// =====================================================================================
// STEP 3b — AGENT MIDDLEWARE. Intercepting runs and tool calls at the AGENT layer.
// -------------------------------------------------------------------------------------
// Steps 3 and 7 wrap the IChatClient with middleware (.AsBuilder().UseLogging()
// .UseOpenTelemetry().Build()) — that pipeline sits BELOW the agent and sees raw model
// calls. This step shows the OTHER layer: middleware on the AGENT itself.
//
// AIAgent.AsBuilder() gives you an AIAgentBuilder with the same fluent idiom, but the
// delegates you plug in see agent-level concepts, not raw chat completions:
//
//   - RUN middleware (.Use(...)) wraps every RunAsync / RunStreamingAsync. You get the
//     incoming messages, the AgentSession, the run options, and a `next` callback — so you
//     can log, time, or annotate a whole run and inspect the AgentResponse.
//   - FUNCTION-INVOCATION middleware (.Use((agent, ctx, next, ct) => ...)) wraps EACH tool
//     call the agent makes. You see the FunctionInvocationContext (the tool + its
//     arguments) and can log, time, short-circuit, or post-process the result.
//
// The pipeline below has three middlewares. Two only OBSERVE (log the run, log each tool
// call); the third TRANSFORMS — a PII-redaction middleware that scrubs emails, phones, and
// PINs out of a tool's result before it flows back to the model and the DevUI. That is the
// more important half of middleware: changing the data in flight, not just watching it.
//
// Same Home Assistance agent and tools as Step 3, hosted in DevUI. Try "Turn on the living
// room lights", "What's the home status?", or "Show me the owner contact card" (this one
// triggers the redaction middleware), then open this resource's Logs in the Aspire
// dashboard to watch the run-, tool-, and redaction-middleware fire.
// =====================================================================================

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

builder.Services.AddSingleton<HomeState>();

builder.AddAIAgent("concierge-with-middleware", (sp, key) =>
{
    var state = sp.GetRequiredService<HomeState>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("AgentMiddleware");

    // The base tool-using agent — same instructions and tools as Step 3, built straight
    // on the IChatClient (no chat-client middleware here, so the contrast stays "agent
    // middleware", not "chat-client middleware").
    AIAgent agent = new ChatClientAgent(
        sp.GetRequiredService<IChatClient>(),
        Agents.HomeAssistanceInstructions,
        key,
        description: null,
        tools: Agents.ToolsFor(state),
        loggerFactory: loggerFactory);

    // ---- AGENT MIDDLEWARE: wrap the agent itself via AsBuilder() ----
    //
    // Both interceptors below are spelled `.Use(...)`, but they bind to two DIFFERENT
    // overloads — the compiler picks each one purely from the lambda's parameter types,
    // not from any method name. The parameter types are written out in full here so it's
    // obvious which layer each lambda runs at:
    //   - the RUN overload's lambda takes `IEnumerable<ChatMessage>` (a whole run),
    //   - the FUNCTION-INVOCATION overload's lambda takes `FunctionInvocationContext`
    //     (one tool call) and returns the tool's result.
    return agent.AsBuilder()
        // (1) RUN middleware — AIAgentBuilder.Use(...). Wraps every RunAsync /
        // RunStreamingAsync; this single-delegate overload covers both the non-streaming
        // and streaming run surfaces. Identified by the first parameter being the run's
        // messages, and `next` continuing the whole run (returns Task, no value).
        .Use(async (
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            Func<IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, CancellationToken, Task> next,
            CancellationToken cancellationToken) =>
        {
            var sw = Stopwatch.StartNew();
            logger.LogInformation("▶ Agent run starting ({Count} message(s))", messages.Count());
            await next(messages, session, options, cancellationToken);
            logger.LogInformation("■ Agent run finished in {Ms} ms", sw.ElapsedMilliseconds);
        })
        // (2) FUNCTION-INVOCATION middleware — FunctionInvocationDelegatingAgentBuilderExtensions
        // .Use(...). Wraps EACH tool call the agent makes. Identified by the
        // `FunctionInvocationContext` parameter (the tool + its arguments) — and here
        // `next` invokes the actual tool and RETURNS its result, which we pass back through.
        .Use(async (
            AIAgent innerAgent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken) =>
        {
            var sw = Stopwatch.StartNew();
            logger.LogInformation("🔧 Tool '{Tool}' invoked", context.Function.Name);
            object? result = await next(context, cancellationToken);
            logger.LogInformation("✅ Tool '{Tool}' completed in {Ms} ms", context.Function.Name, sw.ElapsedMilliseconds);
            return result;
        })
        // (3) REDACTION middleware — also a function-invocation .Use(...), but this one
        // TRANSFORMS the tool result instead of just observing it. It scrubs PII (emails,
        // phones, PINs) from any string a tool returns BEFORE that text flows back to the
        // model and the DevUI. This is the more important half of middleware: changing the
        // data in flight, not just watching it. (Deliberately simple regexes for teaching —
        // not a production DLP engine.)
        .Use(async (
            AIAgent innerAgent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken) =>
        {
            object? result = await next(context, cancellationToken);
            if (result is string text)
            {
                string scrubbed = PiiRedactor.Redact(text);
                if (scrubbed != text)
                    logger.LogInformation("🛡 Redacted PII from '{Tool}' result", context.Function.Name);
                return scrubbed;
            }
            return result;
        })
        // Built-in agent middleware composes in the same pipeline — this traces at the AGENT
        // level (runs + tool calls), complementing the chat-client tracing in Steps 3/7.
        .UseOpenTelemetry(sourceName: "SmartHome")
        .Build();
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();

// A tiny, deliberately simple PII scrubber for the redaction middleware above.
// Order matters: email and phone run before the bare-digit PIN rule, so the digits inside
// a phone number aren't swallowed by the PIN pattern. Real DLP needs far more than this.
static class PiiRedactor
{
    private static readonly Regex Email = new(@"\b[\w.+-]+@[\w-]+\.[\w.-]+\b", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"\+?\d[\d\s().-]{7,}\d", RegexOptions.Compiled);
    private static readonly Regex Pin = new(@"\b\d{4,}\b", RegexOptions.Compiled);

    public static string Redact(string text)
    {
        text = Email.Replace(text, "[redacted-email]");
        text = Phone.Replace(text, "[redacted-phone]");
        text = Pin.Replace(text, "[redacted-pin]");
        return text;
    }
}
