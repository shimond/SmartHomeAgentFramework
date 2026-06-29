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
builder.Services.AddSingleton<HomeState>();

var manualsPath = Path.Combine(AppContext.BaseDirectory, "Rag", "Manuals");
builder.Services.AddSingleton(new ManualStore(manualsPath));

builder.AddAIAgent("concierge", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<HomeState>();
    var manuals = sp.GetRequiredService<ManualStore>();

    var tools = Agents.ToolsFor(state);
    tools.Add(AIFunctionFactory.Create(manuals.SearchManuals));

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
