using OpenAI.Chat;
using SmartHome.Shared.Hosting;
using SmartHome.Shared.Web;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var apiKey = builder.Configuration["OPENAI_KEY"]
             ?? throw new InvalidOperationException("Set the 'openai' connection string in configuration to run Step 0a.");
var model = builder.Configuration["SmartHome:Model"] ?? "gpt-4o-mini";

var openAiClient = new ChatClient(model, new ApiKeyCredential(apiKey));

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Content(
    AdvisorPage.Html("Step 0a — Raw OpenAI ChatClient", "Native OpenAI SDK. No abstraction yet."),
    "text/html"));

// --- Responses API: one call, full answer back ---
app.MapPost("/api/chat", async (ChatRequest req) =>
{
    var messages = new List<OpenAI.Chat.ChatMessage>
    {
        OpenAI.Chat.ChatMessage.CreateSystemMessage(AdvisorInstructions.Text),
        OpenAI.Chat.ChatMessage.CreateUserMessage(req.Message)
    };

    ChatCompletion completion = await openAiClient.CompleteChatAsync(messages);
    return Results.Ok(new { reply = completion.Content[0].Text });
});

// --- Streaming API: tokens arrive as they're generated ---
app.MapPost("/api/stream", async (ChatRequest req, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/plain; charset=utf-8";
    var messages = new List<OpenAI.Chat.ChatMessage>
    {
        OpenAI.Chat.ChatMessage.CreateSystemMessage(AdvisorInstructions.Text),
        OpenAI.Chat.ChatMessage.CreateUserMessage(req.Message)
    };

    await foreach (StreamingChatCompletionUpdate update in openAiClient.CompleteChatStreamingAsync(messages, cancellationToken: ct))
    {
        foreach (var part in update.ContentUpdate)
        {
            if (string.IsNullOrEmpty(part.Text)) continue;
            await response.WriteAsync(part.Text, ct);
            await response.Body.FlushAsync(ct);
        }
    }
});

// --- Structured output: OpenAI's NATIVE json-schema response format ---
// This works smoothly because OpenAI enforces the schema server-side. Step 0b will not
// have an equivalent of this; Step 2 is where we compare the two head-to-head.
app.MapPost("/api/recommend-structured", async (ChatRequest req) =>
{
    var options = new ChatCompletionOptions
    {
        ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            "smart_home_recommendation",
            BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "recommendedPlatform": { "type": "string" },
                "reason": { "type": "string" },
                "considerations": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["recommendedPlatform", "reason", "considerations"],
              "additionalProperties": false
            }
            """))
    };
    var messages = new List<OpenAI.Chat.ChatMessage>
    {
        OpenAI.Chat.ChatMessage.CreateSystemMessage(AdvisorInstructions.Text),
        OpenAI.Chat.ChatMessage.CreateUserMessage(req.Message)
    };
    ChatCompletion completion = await openAiClient.CompleteChatAsync(messages, options);
    return Results.Content(completion.Content[0].Text, "application/json");
});

app.Run();
