// =====================================================================================
// STEP 0b — Raw Ollama client. Same goal as Step 0a, different provider, different
// contract. THIS IS THE PAIN POINT SLIDE: diff this file against Step0a/Program.cs live.
// -------------------------------------------------------------------------------------
// What's different from Step 0a, on purpose:
//   - Construction: OllamaApiClient(Uri, model) vs ChatClient(model, ApiKeyCredential)
//   - Message types: OllamaSharp's own Message/ChatRole vs OpenAI.Chat.ChatMessage
//   - Method shape: ChatAsync(...) streams chunks even for a "single" answer, where
//     Step 0a had a clean CompleteChatAsync vs CompleteChatStreamingAsync split
//   - Structured output: no first-class ResponseFormat.CreateJsonSchemaFormat equivalent
//     here; getting JSON back means prompting for it and hoping (verify current
//     OllamaSharp's "format" support against its docs — Step 2 makes this gap explicit)
//
// =====================================================================================

using System.Text;
using OllamaSharp;
using SmartHome.Shared.Hosting;
using SmartHome.Shared.Web;

// OllamaSharp's request/message types collide by name with our own Shared.Web.ChatRequest,
// so they're aliased explicitly instead of wildcard-importing OllamaSharp.Models.Chat.
using OllamaChatRequest = OllamaSharp.Models.Chat.ChatRequest;
using OllamaMessage = OllamaSharp.Models.Chat.Message;
using OllamaChatRole = OllamaSharp.Models.Chat.ChatRole;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Aspire injects the Ollama container's endpoint via service discovery when this project
// is referenced with .WithReference(chatModel) in the AppHost, setting OLLAMA_CHAT_URI /
// OLLAMA_CHAT_MODEL. Outside Aspire, both must be set manually (e.g. in launchSettings.json
// or the environment) — there is no localhost fallback here; a missing value throws at
// construction. Verify the exact Aspire-injected config/env-var name against the installed
// CommunityToolkit.Aspire.Hosting.Ollama version.

var ollamaClient = new OllamaApiClient(builder.Configuration["OLLAMA_CHAT_URI"], builder.Configuration["OLLAMA_CHAT_MODEL"]);

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Content(
    AdvisorPage.Html("Step 0b — Raw Ollama Client", "Native OllamaSharp SDK. Same goal as 0a, different contract."),
    "text/html"));

// --- "Full response" equivalent: OllamaSharp doesn't separate Response vs Streaming the
// same way Step 0a's two methods did; here we drain the chunk stream into one string. ---
app.MapPost("/api/chat", async (ChatRequest req) =>
{
    var messages = new List<OllamaMessage>
    {
        new() { Role = OllamaChatRole.System, Content = AdvisorInstructions.Text },
        new() { Role = OllamaChatRole.User, Content = req.Message }
    };

    var sb = new StringBuilder();
    await foreach (var chunk in ollamaClient.ChatAsync(new OllamaChatRequest { Messages = messages }))
        sb.Append(chunk?.Message?.Content);

    return Results.Ok(new { reply = sb.ToString() });
});

// --- Streaming: same demo-page contract as Step 0a, different API shape underneath ---
app.MapPost("/api/stream", async (ChatRequest req, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/plain; charset=utf-8";
    var messages = new List<OllamaMessage>
    {
        new() { Role = OllamaChatRole.System, Content = AdvisorInstructions.Text },
        new() { Role = OllamaChatRole.User, Content = req.Message }
    };

    await foreach (var chunk in ollamaClient.ChatAsync(new OllamaChatRequest { Messages = messages }))
    {
        var text = chunk?.Message?.Content;
        if (string.IsNullOrEmpty(text)) continue;
        await response.WriteAsync(text, ct);
        await response.Body.FlushAsync(ct);
    }
});

// --- Structured output attempt: prompt for JSON and hope. No schema enforcement here. ---
// THIS is the line to contrast against Step 0a's ChatResponseFormat.CreateJsonSchemaFormat.
app.MapPost("/api/recommend-structured", async (ChatRequest req) =>
{
    var messages = new List<OllamaMessage>
    {
        new()
        {
            Role = OllamaChatRole.System,
            Content = AdvisorInstructions.Text +
                "\nRespond with ONLY a JSON object: " +
                "{\"recommendedPlatform\":string,\"reason\":string,\"considerations\":string[]}. " +
                "No markdown fences, no prose."
        },
        new() { Role = OllamaChatRole.User, Content = req.Message }
    };

    var sb = new StringBuilder();
    await foreach (var chunk in ollamaClient.ChatAsync(new OllamaChatRequest { Messages = messages }))
        sb.Append(chunk?.Message?.Content);

    // No guarantee this parses as JSON — that's the point of this endpoint. Try parsing
    // it live in the session and show students what happens when it doesn't.
    return Results.Content(sb.ToString(), "application/json");
});

app.Run();
