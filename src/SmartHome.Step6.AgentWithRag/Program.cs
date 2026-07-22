// =====================================================================================
// STEP 6 — Agent + RAG. Ground answers in the appliance manuals instead of guesses.
// -------------------------------------------------------------------------------------
// Teaching beat: ask a maintenance question, then temporarily remove SearchManuals from
// the tool list and ask again — watch it hallucinate a plausible-but-wrong procedure.
// Add the tool back and the answer is grounded in the actual manual text.
//
// Try: "How do I descale the coffee machine?"
// =====================================================================================

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;
using SmartHome.Shared.Rag;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);
builder.Services.AddSingleton<IHomeGateway, InMemoryHome>();

var manualsPath = Path.Combine(AppContext.BaseDirectory, "Rag", "Manuals");

// SmartHome:RagStore picks the retriever: "Keyword" (dependency-free overlap) or
// "Embedding" (OpenAI embeddings + in-memory cosine similarity). Only the selected
// implementation is registered, behind the shared IManualStore — the agent below never
// knows which one it got; it just calls SearchManuals.
var ragStore = (builder.Configuration["SmartHome:RagStore"] ?? "Embedding").Trim();

if (ragStore.Equals("Embedding", StringComparison.OrdinalIgnoreCase))
{
    ChatClientSetup.RegisterEmbeddingGenerator(builder);
    builder.Services.AddSingleton<IManualStore>(sp => new EmbeddingManualStore(
        manualsPath,
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()));
}
else
{
    builder.Services.AddSingleton<IManualStore>(new ManualStore(manualsPath));
}

builder.AddAIAgent("concierge-with-rag", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<IHomeGateway>();

    var tools = Agents.ToolsFor(state);
    tools.Add(AIFunctionFactory.Create(sp.GetRequiredService<IManualStore>().SearchManuals));

    return new ChatClientAgent(chat,
        Agents.HomeAssistanceInstructions +
            "\nFor any maintenance or 'how do I…' question about an appliance, call " +
            "SearchManuals first and ground your answer ONLY in the returned passages.",
        key, null, tools);
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();
