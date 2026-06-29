// =====================================================================================
// STEP 2 — The ceiling: IChatClient unifies the CALL SITE, not the underlying GUARANTEE.
// -------------------------------------------------------------------------------------
// Both providers now go through the SAME ChatOptions.ResponseFormat call. But:
//   - With OpenAI, the schema is enforced server-side — the JSON WILL match the shape.
//   - With Ollama, adherence depends on the model and the installed OllamaSharp version's
//     support for structured/JSON output — IChatClient smooths the call site, but cannot
//     manufacture a guarantee the underlying provider doesn't make. (Ollama has gained
//     JSON/schema support over time — verify the current behavior for your installed
//     version/model rather than assuming either "it works" or "it doesn't.")
//
// This is the deliberate ceiling: the abstraction solves the API-shape problem (Step 1),
// but not the capability-parity problem. That gap is exactly why Step 3 introduces the
// AGENT — it's the layer that can compensate (retry, repair, or fall back) regardless of
// what the underlying provider actually guarantees.
// =====================================================================================

using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;
using SmartHome.Shared.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

var app = builder.Build();
app.MapDefaultEndpoints();

var providerName = builder.Configuration["SmartHome:Provider"] ?? "Ollama";

// Step 2 adds its own tester (a textarea + button that POSTs to /api/recommend-structured
// and renders the parsed object) so the structured-output endpoint can be demoed straight
// from the browser — no curl/Postman during the talk. The markup lives in
// AdvisorPage.StructuredTester and rides in via the optional extraBody parameter, so it
// does NOT affect the other steps' pages.
app.MapGet("/", () => Results.Content(
    AdvisorPage.Html("Step 2 — Structured Output Ceiling",
        $"Same call site as Step 1. Provider: {providerName}. Try /api/recommend-structured.",
        AdvisorPage.StructuredTester),
    "text/html"));

app.MapPost("/api/chat", async (ChatRequest req, IChatClient chatClient) =>
{
    var response = await chatClient.GetResponseAsync(
        [new ChatMessage(ChatRole.System, AdvisorInstructions.Text), new ChatMessage(ChatRole.User, req.Message)]);
    return Results.Ok(new { reply = response.Text });
});

app.MapPost("/api/stream", async (ChatRequest req, IChatClient chatClient, HttpResponse response, CancellationToken ct) =>
    await StreamingWriter.WriteAsync(response, chatClient, req.Message, AdvisorInstructions.Text, ct));

// THE CEILING DEMO: same generic call regardless of provider; the RESULT's reliability is
// what differs. GetResponseAsync<T> does TWO things for us in one call:
//   1. derives the JSON schema from T and sets the ResponseFormat (no manual ChatOptions /
//      AIJsonUtilities.CreateJsonSchema — the generic overload builds it from the type), and
//   2. deserializes the reply into T (no manual JsonSerializer.Deserialize).
// TryGetResult is the graceful path: with a weaker provider guarantee it returns false
// instead of throwing, so the failure shows up live in the room rather than as a slide claim.
app.MapPost("/api/recommend-structured", async (ChatRequest req, IChatClient chatClient) =>
{
    var response = await chatClient.GetResponseAsync<SmartHomeRecommendation>(
        [new ChatMessage(ChatRole.System, AdvisorInstructions.Text), new ChatMessage(ChatRole.User, req.Message)]);

    if (response.TryGetResult(out var parsed))
        return Results.Ok(new { provider = providerName, parsed, raw = response.Text });

    return Results.Ok(new { provider = providerName, parsed = (SmartHomeRecommendation?)null, raw = response.Text,
        parseError = "The provider's reply did not deserialize into SmartHomeRecommendation (TryGetResult returned false)." });
});

app.Run();
