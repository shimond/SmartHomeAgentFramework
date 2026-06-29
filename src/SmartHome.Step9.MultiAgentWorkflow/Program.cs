
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
ChatClientSetup.RegisterAIClient(builder);
builder.Services.AddSingleton<HomeState>();

// Register a named HttpClient. AddServiceDefaults wires Aspire service discovery onto
// every HttpClient, so requests to "https+http://mcp-energy-server" are resolved at
// runtime — but the Endpoint in HttpClientTransportOptions must be a plain http URI.
builder.Services.AddHttpClient("mcp-energy-server");

// McpToolsProvider does the async MCP handshake in StartAsync (proper IHostedService
// lifecycle) and makes the tool list available as a singleton before the agent runs.
builder.Services.AddSingleton<McpToolsProvider>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<McpToolsProvider>());

static ChatClientAgent Specialist(IServiceProvider sp, string name, string instructions, IList<McpClientTool>? energyTools = null)
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<HomeState>();
    var tools = Agents.ToolsFor(state);
    if (energyTools is not null)
        foreach (var t in energyTools) tools.Add(t);
    return new ChatClientAgent(chat, instructions, name, null, tools);
}

// Wraps an AIAgent as a single AITool another agent can call: the orchestrator passes a
// natural-language instruction, the specialist runs its own full reasoning/tool loop, and
// only the final text comes back. This manual wrap always compiles regardless of package
// version; SOME Agent Framework versions also ship a built-in "agent as tool" helper
// (look for something like `agent.AsAIFunction()`/`AsAgentTool()` on AIAgent) — if yours
// has one, prefer it, but this is the guaranteed-to-work fallback for the workshop.
static AIFunction AsAgentTool(AIAgent agent, string toolName, string description)
{
    async Task<string> Invoke(string instruction)
    {
        var response = await agent.RunAsync(instruction);
        return response.Text;
    }
    return AIFunctionFactory.Create(Invoke, toolName, description);
}

builder.AddAIAgent("security", (sp, key) => Specialist(sp, key,
    "You handle home SECURITY only: locking the door and arming/disarming the alarm. " +
    "Unlocking or disarming is sensitive — confirm, call RequestApproval, then act. " +
    "Ignore comfort and energy requests; leave those to other agents."));

builder.AddAIAgent("comfort", (sp, key) => Specialist(sp, key,
    "You handle COMFORT only: lights, brightness, thermostat, scenes and music. " +
    "Do not touch locks or the alarm."));

builder.AddAIAgent("energy", (sp, key) => Specialist(sp, key,
    "You handle ENERGY only. Use GetEnergyPriceNow and GetForecast to advise whether now " +
    "is a good time to run heavy appliances, deferring past evening peaks. You may adjust " +
    "the thermostat to save energy, but do not change locks or the alarm.",
    sp.GetRequiredService<McpToolsProvider>().Tools));

// --- Variant 1: WORKFLOW — fixed graph, security → comfort → energy, always all three ---
builder.AddWorkflow("home-ops", (sp, key) =>
{
    var security = sp.GetRequiredKeyedService<AIAgent>("security");
    var comfort = sp.GetRequiredKeyedService<AIAgent>("comfort");
    var energy = sp.GetRequiredKeyedService<AIAgent>("energy");
    return AgentWorkflowBuilder.BuildSequential(key, [security, comfort, energy]);
}).AddAsAIAgent();

// --- Variant 2: ORCHESTRATOR AGENT — no graph; the model decides which specialist(s)
// to call, in what order, and can skip any of them based on what the request actually needs.
builder.AddAIAgent("home-ops-orchestrator", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var security = sp.GetRequiredKeyedService<AIAgent>("security");
    var comfort = sp.GetRequiredKeyedService<AIAgent>("comfort");
    var energy = sp.GetRequiredKeyedService<AIAgent>("energy");

    return new ChatClientAgent(chat,
        """
        You coordinate three specialists: security, comfort, and energy. For each user
        request, decide WHICH specialist(s) are actually relevant and call only those —
        do not call a specialist whose domain the request doesn't touch. You may call
        them in any order, and more than once if needed. Summarize what happened.
        """,
        key, null,
        [
            AsAgentTool(security, "AskSecuritySpecialist", "Delegate a security-related instruction (locks, alarm) to the security specialist."),
            AsAgentTool(comfort, "AskComfortSpecialist", "Delegate a comfort-related instruction (lights, thermostat, scenes, music) to the comfort specialist."),
            AsAgentTool(energy, "AskEnergySpecialist", "Delegate an energy-related instruction (pricing, scheduling heavy appliances) to the energy specialist."),
        ]);
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
        // Read the real URL Aspire injects via .WithReference(mcpServer) in the AppHost.
        // Falls back to localhost for running without Aspire.
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

