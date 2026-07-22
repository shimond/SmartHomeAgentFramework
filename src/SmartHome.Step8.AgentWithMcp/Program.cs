// =====================================================================================
// STEP 8 — Agent + MCP. The energy tool now lives in a SEPARATE process/service
// (SmartHome.McpServer), reached over the Model Context Protocol — not a C# method in
// this codebase. Proves tools don't have to be local to the agent that calls them.
// -------------------------------------------------------------------------------------
// Under the AppHost, the MCP server's location comes from service discovery (it's
// referenced via .WithReference(mcpServer) in Program.cs of the AppHost). Outside Aspire,
// this falls back to http://localhost:5300.
//
// Try: "Is now a cheap time to run the dishwasher?"
// =====================================================================================

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

builder.Services.AddSingleton<IHomeGateway, InMemoryHome>();
builder.Services.AddSingleton<McpToolsProvider>();
builder.Services.AddHttpClient("mcp-energy-server");


builder.AddAIAgent("concierge-with-mcp",  (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<IHomeGateway>();
    var mcpToolsProvider = sp.GetRequiredService<McpToolsProvider>();
    var mcpTools = mcpToolsProvider.Tools;

    var tools = Agents.ToolsFor(state);
    foreach (var t in mcpTools) tools.Add(t);

    return new ChatClientAgent(chat,
        Agents.HomeAssistanceInstructions +
            "\nWhen advising whether now is a good time to run heavy appliances, FIRST call " +
            "GetHomeStatus to read the current house state, THEN call AdviseEnergyUse passing that " +
            "status as 'homeStatus'. Use GetEnergyPriceNow / GetForecast for simple price/forecast questions.",
        key, null, tools);
});

var app = builder.Build();

// Warm the MCP handshake once, before the host starts serving requests, so the
// "concierge-with-mcp" agent factory above can read McpToolsProvider.Tools synchronously
// instead of blocking on the async handshake (AddAIAgent's factory delegate has no async
// overload).
await app.Services.GetRequiredService<McpToolsProvider>().GetTools();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();
