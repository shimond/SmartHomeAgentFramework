using Microsoft.Extensions.AI;
using SmartHome.Shared.Hosting;
using SmartHome.Shared.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);


var app = builder.Build();
app.MapDefaultEndpoints();

var providerName = builder.Configuration["SmartHome:Provider"] ?? "Ollama";
app.MapGet("/", () => Results.Content(
    AdvisorPage.Html("Step 1 — IChatClient Abstraction", $"Provider-agnostic. Currently running: {providerName}."),
    "text/html"));

app.MapPost("/api/chat", async (ChatRequest req, IChatClient chatClient) =>
{
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, AdvisorInstructions.Text),
        new(ChatRole.User, req.Message)
    };
    var response = await chatClient.GetResponseAsync(messages);
    return Results.Ok(new { reply = response.Text });
});

app.MapPost("/api/stream", async (ChatRequest req, IChatClient chatClient, HttpResponse response, CancellationToken ct) =>
    await StreamingWriter.WriteAsync(response, chatClient, req.Message, AdvisorInstructions.Text, ct));

app.Run();
