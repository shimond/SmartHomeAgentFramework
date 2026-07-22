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
// The pipeline below has FOUR middlewares. Two only OBSERVE (log the run, log each tool call);
// the other two TRANSFORM the data crossing the LLM boundary — in BOTH directions:
//
//   - REDACT OUT: a PII-redaction middleware scrubs emails, phones, and PINs out of a tool's
//     RESULT before it flows back to the model and the DevUI.
//   - INJECT IN: the client sends the door PIN AS PART OF ITS CHAT MESSAGE, but we do NOT let
//     the model see it. The RUN middleware pulls the PIN out of the incoming message and masks
//     it ("...my PIN is [hidden]") BEFORE the messages reach the model, stashing the real value
//     in an AsyncLocal. The lock tool's `pin` parameter is excluded from the JSON schema (see
//     PinGatedLock.cs), so the model calls a zero-argument UnlockDoor(); the FUNCTION-INVOCATION
//     middleware then injects the stashed PIN into the tool's arguments. Net effect: the secret
//     arrives as text, is scrubbed at the model boundary, and is handed to the tool out-of-band.
//     (If the message carries no PIN, it falls back to a configured demo PIN.)
//
// That is the more important half of middleware: controlling the data in flight, not just
// watching it — and here, controlling what the model is and isn't allowed to see.
//
// Same Home Assistance agent and tools as Step 3, hosted in DevUI. Try:
//   - "Turn on the living room lights" / "What's the home status?"       (observe the loggers)
//   - "Show me the owner contact card"                                   (triggers REDACT OUT)
//   - "Unlock the front door, I confirm — my PIN is 4821"                (triggers INJECT IN)
// then open this resource's Logs in the Aspire dashboard to watch the middlewares fire. Note in
// DevUI that the model's view of your message has the PIN masked, yet the door still unlocks —
// because the tool got the PIN out-of-band.
// =====================================================================================

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;
using SmartHome.Step3b.AgentWithMiddleware;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

builder.Services.AddSingleton<IHomeGateway, InMemoryHome>();

// The guest PIN the lock hardware requires to actuate. A demo value ships in appsettings.json;
// a real deployment would keep it in user-secrets. It is captured by the injection middleware
// below and NEVER placed in the model's context.
var doorPin = builder.Configuration["Lock:Pin"] ?? "4821";

builder.AddAIAgent("concierge-with-middleware", (sp, key) =>
{
    var state = sp.GetRequiredService<IHomeGateway>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("AgentMiddleware");

    // Out-of-band channel between the two middlewares below. The RUN middleware pulls the PIN
    // out of the client's chat message and stashes it here; the FUNCTION-INVOCATION middleware
    // reads it back and injects it into the tool call. AsyncLocal flows down the same run and is
    // isolated per concurrent request, so it never crosses between users.
    var pinFromMessage = new AsyncLocal<string?>();

    // Same instructions/tools as Step 3, EXCEPT the stock UnlockDoor is swapped for a
    // PIN-gated variant whose `pin` parameter is hidden from the model's schema (PinGatedLock).
    // The model still sees a tool called "UnlockDoor"; it just can't see or fill the PIN.
    var tools = Agents.ToolsFor(state)
        .Where(t => t is not AIFunction f || f.Name != "UnlockDoor")
        .Append(PinGatedLock.CreateTool(state, doorPin))
        .ToList();

    // The base tool-using agent — built straight on the IChatClient (no chat-client middleware
    // here, so the contrast stays "agent middleware", not "chat-client middleware").
    AIAgent agent = new ChatClientAgent(
        sp.GetRequiredService<IChatClient>(),
        Agents.HomeAssistanceInstructions,
        key,
        description: null,
        tools: tools,
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

            // The client sends the PIN AS PART OF THE CHAT MESSAGE (e.g. "unlock the door, my
            // PIN is 4821"). We must NOT let that reach the model. Rewrite every user message
            // with the PIN masked BEFORE calling next (which is what the LLM sees), and stash the
            // real PIN in the AsyncLocal for the tool-injection middleware below. This is the
            // whole trick: the secret arrives as text, but is scrubbed at the model boundary and
            // handed to the tool out-of-band instead.
            string? captured = null;
            var scrubbed = messages.Select(m =>
            {
                if (m.Role == ChatRole.User && !string.IsNullOrEmpty(m.Text))
                {
                    var masked = PinExtractor.Strip(m.Text, out var found);
                    if (found is not null)
                    {
                        captured = found;
                        return new ChatMessage(m.Role, masked);
                    }
                }
                return m;
            }).ToList();

            if (captured is not null)
            {
                pinFromMessage.Value = captured;
                logger.LogInformation("🔑 Captured PIN from the client's message and masked it before the model sees it");
            }

            await next(scrubbed, session, options, cancellationToken);
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
        // (3) SECRET-INJECTION middleware — the realistic INBOUND counterpart to redaction.
        // The model calls UnlockDoor() with NO pin (it's excluded from the tool's schema, see
        // PinGatedLock.cs). Here we inject the PIN into the argument dictionary just before the
        // tool runs, so the secret reaches the TOOL but never the model — not in the schema, not
        // in the tool-call arguments DevUI shows, not in the result. The PIN comes from the
        // client's chat message (captured + masked by the run middleware above); if the message
        // carried none, we fall back to the configured demo PIN so the tool still works.
        .Use(async (
            AIAgent innerAgent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken) =>
        {
            if (context.Function.Name == "UnlockDoor")
            {
                var fromClient = pinFromMessage.Value;
                context.Arguments["pin"] = fromClient ?? doorPin;
                logger.LogInformation("🔐 Injected door PIN into '{Tool}' (source: {Source}) — never shown to the model",
                    context.Function.Name, fromClient is not null ? "client message" : "config fallback");
            }
            return await next(context, cancellationToken);
        })
        // (4) REDACTION middleware — also a function-invocation .Use(...), but this one
        // TRANSFORMS the tool result instead of just observing it. It scrubs PII (emails,
        // phones, PINs) from any string a tool returns BEFORE that text flows back to the
        // model and the DevUI — the OUTBOUND counterpart to the injection above.
        // (Deliberately simple regexes for teaching — not a production DLP engine.)
        .Use(async (
            AIAgent innerAgent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken) =>
        {
            object? result = await next(context, cancellationToken);

            // IMPORTANT: the framework hands tool results back SERIALIZED — a string tool
            // returns as a System.Text.Json.JsonElement here, NOT a .NET string. A naive
            // `result is string` check silently skips every result (the bug this step warns
            // about). Match both the raw-string and JSON-string shapes; leave structured
            // (non-string) JSON untouched so we don't mangle tools that return objects.
            string? text = result switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
                _ => null
            };

            if (text is not null)
            {
                string scrubbed = PiiRedactor.Redact(text);
                if (scrubbed != text)
                {
                    logger.LogInformation("🛡 Redacted PII from '{Tool}' result", context.Function.Name);
                    return scrubbed; // scrubbed text replaces the result the model sees
                }
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

// Pulls a PIN the client typed into its chat message (e.g. "my PIN is 4821") and masks it in
// the text handed to the model. The run middleware uses this so the secret never crosses the
// model boundary, while the tool-injection middleware forwards the captured value to the tool.
static class PinExtractor
{
    // "pin 4821", "pin: 4821", "my pin is 4821" — captures the 3+ digit code after the word PIN.
    private static readonly Regex Pin = new(@"(?i)\bpin\b\D{0,12}(\d{3,})", RegexOptions.Compiled);

    public static string Strip(string text, out string? pin)
    {
        var m = Pin.Match(text);
        if (!m.Success)
        {
            pin = null;
            return text;
        }

        pin = m.Groups[1].Value;
        // Mask just the digits so the sentence the model reads stays coherent ("...is [hidden]").
        return text.Remove(m.Groups[1].Index, m.Groups[1].Length)
                   .Insert(m.Groups[1].Index, "[hidden]");
    }
}
