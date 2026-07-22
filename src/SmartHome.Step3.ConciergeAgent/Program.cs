using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting; 

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

builder.Services.AddSingleton<IHomeGateway, InMemoryHome>();



builder.AddAIAgent("concierge-with-tools", (sp, key) =>
{
    var state = sp.GetRequiredService<IHomeGateway>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    var client = sp.GetRequiredService<IChatClient>()
                .AsBuilder()
                .UseLogging(loggerFactory)
                .UseOpenTelemetry(sourceName: "SmartHome", configure: o => o.EnableSensitiveData = true)
                .BuildAIAgent(Agents.HomeAssistanceInstructions, tools: Agents.ToolsFor(state), name: key, loggerFactory: loggerFactory);
    return client;
});



var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();
