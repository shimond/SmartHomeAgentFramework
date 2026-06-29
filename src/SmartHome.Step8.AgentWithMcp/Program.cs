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
using ModelContextProtocol.Client;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

ChatClientSetup.RegisterAIClient(builder);

builder.Services.AddSingleton<HomeState>();

builder.Services.AddHttpClient("mcp-energy-server");

// McpToolsProvider does the async MCP handshake in StartAsync (proper IHostedService
// lifecycle) and makes the tool list available as a singleton before the agent runs.
builder.Services.AddSingleton<McpToolsProvider>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<McpToolsProvider>());

builder.AddAIAgent("concierge", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<HomeState>();
    var mcpTools = sp.GetRequiredService<McpToolsProvider>().Tools;

    var tools = Agents.ToolsFor(state);
    foreach (var t in mcpTools) tools.Add(t);

    return new ChatClientAgent(chat,
        Agents.HomeAssistanceInstructions +
            "\nUse GetEnergyPriceNow / GetForecast (from the energy server) before advising " +
            "whether now is a good time to run heavy appliances.",
        key, null, tools);
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();

/// <summary>
/// Performs the async MCP handshake during host startup and exposes the tool list.
/// Using IHostedService ensures proper async lifecycle without blocking or sync-over-async.
/// </summary>
sealed class McpToolsProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IHostedService, IAsyncDisposable
{
    private McpClient? _mcpClient;
    public IList<McpClientTool> Tools { get; private set; } = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Dump all Aspire-injected service keys so we can see the exact format.
        var mcpKeys = configuration.AsEnumerable()
            .Where(kv => kv.Key.Contains("mcp", StringComparison.OrdinalIgnoreCase) ||
                         kv.Key.StartsWith("services", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var kv in mcpKeys)
            Console.WriteLine($"[MCP-DIAG] {kv.Key} = {kv.Value}");

        var endpoint =
            configuration["services:mcp-energy-server:https:0"] ??
            configuration["services:mcp-energy-server:http:0"] ??
            "http://localhost:5300";

        var httpClient = httpClientFactory.CreateClient("mcp-energy-server");
        httpClient.BaseAddress = new Uri(endpoint);

        _mcpClient = await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions { Endpoint = new Uri(endpoint) },
                httpClient,
                loggerFactory: null,
                ownsHttpClient: true),
            cancellationToken: cancellationToken);

        Tools = await _mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_mcpClient is not null)
            await _mcpClient.DisposeAsync();
    }
}
