// =====================================================================================
// STEP 7 — Agent + APPROVAL gate + OBSERVABILITY. The production-readiness module.
// -------------------------------------------------------------------------------------
// Two things bolted on:
//   1. Sensitive tools (UnlockDoor, DisarmAlarm) refuse until RequestApproval has been
//      called — enforced in HomeTools itself, so it holds regardless of model behavior.
//   2. The chat client is wrapped with logging + OpenTelemetry; because this project
//      called builder.AddServiceDefaults(), traces already export to the Aspire dashboard
//      with zero extra wiring when run via the AppHost — that's the whole point of
//      ServiceDefaults existing.
//
// Try: "Unlock the front door." → it asks to confirm. "Yes, unlock it." → it does.
// Then open the Aspire dashboard and find this run's trace.
// =====================================================================================

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); 

const string ActivitySourceName = "SmartHome";
ChatClientSetup.RegisterAIClient(builder);
builder.Services.AddKeyedChatClient("telemetry",sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var currentClient = sp.GetRequiredService<IChatClient>();
    return currentClient
        .AsBuilder()
        .UseLogging(loggerFactory)
        .UseOpenTelemetry(sourceName: ActivitySourceName, configure: o => o.EnableSensitiveData = true)
        .Build();
});

builder.Services.AddSingleton<IHomeGateway, InMemoryHome>();

builder.AddAIAgent("concierge-with-approval", (sp, key) =>
{
    var chat = sp.GetRequiredKeyedService<IChatClient>("telemetry");
    var state = sp.GetRequiredService<IHomeGateway>();
    return new ChatClientAgent(chat, Agents.HomeAssistanceInstructions, key, null, Agents.ToolsFor(state));
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();
