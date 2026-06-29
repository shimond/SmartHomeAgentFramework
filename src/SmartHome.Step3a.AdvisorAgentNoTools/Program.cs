// =====================================================================================
// STEP 3a — The ADVISOR, now wrapped in an AIAgent (NO tools).
// -------------------------------------------------------------------------------------
// This is the missing rung between Step 1 and Step 3:
//
//   - Step 1 (AdvisorAbstracted) talks to the provider-agnostic IChatClient directly:
//     it builds a List<ChatMessage> (System + User) by hand and calls GetResponseAsync.
//   - Step 3 (ConciergeAgent) jumps straight to a tool-using AIAgent hosted in DevUI.
//
// Here we keep EXACTLY Step 1's scenario — same advisor instructions, same HTML page,
// same /api/chat + /api/stream contract — but route through an AIAgent built with NO
// tools. The teaching point: wrapping IChatClient in an agent gives you a run surface
// (RunAsync / RunStreamingAsync) and lets the agent own the system prompt as its
// Instructions, so each request passes only the user's message. No tools, no DevUI yet —
// just the agent abstraction itself, so the contrast with Step 1 stays sharp.
// =====================================================================================

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Hosting;
using SmartHome.Shared.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

// Register the advisor as a keyed AIAgent. It wraps the same IChatClient Step 1 uses, but
// carries the advisor instructions itself and is given NO tools — it can only give advice.
builder.AddAIAgent("advisor-no-tools", (sp, key) =>
    new ChatClientAgent(sp.GetRequiredService<IChatClient>(), AdvisorInstructions.Text, name: key));

var app = builder.Build();
app.MapDefaultEndpoints();

var providerName = builder.Configuration["SmartHome:Provider"] ?? "Ollama";
app.MapGet("/", () => Results.Content(
    AdvisorPage.Html("Step 3a — Advisor as an Agent (no tools)",
        $"Same advisor as Step 1, but through the AIAgent abstraction. Currently running: {providerName}."),
    "text/html"));

// Full response: hand the user message to the agent — the agent supplies the system prompt
// from its Instructions, so (unlike Step 1) we don't build a System+User message list here.
app.MapPost("/api/chat", async (ChatRequest req, [FromKeyedServices("advisor-no-tools")] AIAgent agent) =>
{
    var response = await agent.RunAsync(req.Message);
    return Results.Ok(new { reply = response.Text });
});

// Streaming: the agent equivalent of Step 1's StreamingWriter — stream the run's text
// updates straight to the response body, flushing per chunk.
app.MapPost("/api/stream", async (ChatRequest req, [FromKeyedServices("advisor-no-tools")] AIAgent agent,
    HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/plain; charset=utf-8";
    await foreach (var update in agent.RunStreamingAsync(req.Message, cancellationToken: ct))
    {
        if (string.IsNullOrEmpty(update.Text)) continue;
        await response.WriteAsync(update.Text, ct);
        await response.Body.FlushAsync(ct);
    }
});

app.Run();
