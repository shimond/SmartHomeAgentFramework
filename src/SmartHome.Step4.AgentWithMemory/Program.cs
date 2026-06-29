// =====================================================================================
// STEP 4 — Agent + MEMORY. Survives a restart of THIS container, dies across multiple.
// -------------------------------------------------------------------------------------
// PreferenceStore writes to a local JSON file, so "remembering" outlives one run AND one
// process restart. What it can't do: be seen by a second container. That gap — not a bug,
// a deliberate limitation — is exactly what motivates Step 5's externalized, DB-backed
// conversation store. Keep this file open side-by-side with Step 5 later.
//
// Try: "My movie-night preference is 20°C and 15% brightness." then restart the app, then
// "Set up movie night." — it still knows.
// =====================================================================================

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

var prefPath = Path.Combine(AppContext.BaseDirectory, "preferences.json");
builder.Services.AddSingleton(new PreferenceStore(prefPath));

builder.AddAIAgent("concierge", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<HomeState>();
    var prefs = new PreferenceTools(sp.GetRequiredService<PreferenceStore>());

    var tools = Agents.ToolsFor(state);
    tools.Add(AIFunctionFactory.Create(prefs.RememberPreference));
    tools.Add(AIFunctionFactory.Create(prefs.RecallPreference));
    tools.Add(AIFunctionFactory.Create(prefs.ListPreferences));

    return new ChatClientAgent(chat,
        Agents.HomeAssistanceInstructions+
            "\nWhen the user states a lasting preference, call RememberPreference. " +
            "Before activating a scene, call RecallPreference to honor saved settings.",
        key, null, tools);
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();
