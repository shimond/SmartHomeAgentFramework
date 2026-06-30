
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

// McpToolsProvider does the async MCP handshake LAZILY (on first request) and caches the
// result. An IHostedService that populated a .Tools property eagerly would race the agent:
// the energy agent's factory reads the tool list when the agent is built, which can happen
// before startup finished the handshake — leaving it with 0 tools. Fetching on demand and
// awaiting the result removes that ordering dependency entirely.
builder.Services.AddSingleton<McpToolsProvider>();

static ChatClientAgent Specialist(IServiceProvider sp, string name, string instructions, IList<McpClientTool>? energyTools = null)
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<HomeState>();
    var tools = Agents.ToolsFor(state);
    if (energyTools is not null)
        foreach (var t in energyTools) tools.Add(t);
    return new ChatClientAgent(chat, instructions, name, null, tools);
}

// Three specialists, each reporting on ONE domain. In the concurrent briefing below they all
// receive the SAME prompt at the same time, so each MUST answer only its own slice — otherwise
// (as a live run showed) they each summarize the whole house and the merged output is redundant
// and garbled. The instructions are deliberately strict: exactly one short line, prefixed with
// the domain, mentioning ONLY that domain's devices and nothing else.
builder.AddAIAgent("security", (sp, key) => Specialist(sp, key,
    "You are the SECURITY reporter. Look ONLY at the door lock and the alarm. " +
    "Call GetHomeStatus, then reply with EXACTLY ONE short line, starting with 'Security:'. " +
    "Mention ONLY the lock and alarm state. Do NOT mention lights, thermostat, music, scenes, " +
    "energy or price — those belong to other reporters. No preamble, no extra sentences."));

builder.AddAIAgent("comfort", (sp, key) => Specialist(sp, key,
    "You are the COMFORT reporter. Look ONLY at lights, thermostat, scene and music. " +
    "Call GetHomeStatus, then reply with EXACTLY ONE short line, starting with 'Comfort:'. " +
    "Mention ONLY lights/thermostat/scene/music. Do NOT mention the lock, alarm, energy or " +
    "price — those belong to other reporters. No preamble, no extra sentences."));

builder.AddAIAgent("energy", (sp, key) => Specialist(sp, key,
    "You are the ENERGY reporter. Use GetEnergyPriceNow and GetForecast ONLY. " +
    "Reply with EXACTLY ONE short line, starting with 'Energy:', giving the current price band " +
    "and whether now is a good time for heavy appliances. Do NOT mention the lock, alarm, " +
    "lights, thermostat or music — those belong to other reporters. No preamble, no extra sentences.",
    sp.GetRequiredService<McpToolsProvider>().GetTools().Result));

// --- CONCURRENT WORKFLOW — the one shape a for-loop CANNOT replicate. All three specialists
// run AT THE SAME TIME on the same "home briefing" prompt; the aggregator merges their replies
// into one report. A for-loop is serial (security THEN comfort THEN energy ≈ 3× the latency);
// here the wall-clock time is roughly that of the slowest single agent. To match this by hand
// you'd be writing Task.WhenAll + result-collection + failure isolation yourself — BuildConcurrent
// is that, declaratively, in one call.
// Aggregator (fan-in): stitch each specialist's final line into a single briefing message.
static List<ChatMessage> Merge(IList<List<ChatMessage>> perAgent)
{
    var lines = perAgent
        .Select(messages => messages.LastOrDefault()?.Text?.Trim())
        .Where(text => !string.IsNullOrWhiteSpace(text));
    return [new ChatMessage(ChatRole.Assistant, "🏠 Home briefing:\n- " + string.Join("\n- ", lines))];
}

// We register the workflow-as-agent MANUALLY (not via .AddAsAIAgent()) for one reason: the
// hosting shortcut hard-codes includeWorkflowOutputsInResponse = false, so the aggregated
// briefing is produced but never forwarded to the response — DevUI then shows the executor
// graph with "(no output)" on every node. Calling Workflow.AsAIAgent ourselves lets us pass
// includeWorkflowOutputsInResponse: true, which surfaces the merged "🏠 Home briefing" as the
// agent's actual reply. Registered as a keyed AIAgent, exactly like AddAIAgent does, so DevUI
// still discovers it.
builder.Services.AddKeyedSingleton<AIAgent>("home-briefing", (sp, key) =>
{
    var security = sp.GetRequiredKeyedService<AIAgent>("security");
    var comfort = sp.GetRequiredKeyedService<AIAgent>("comfort");
    var energy = sp.GetRequiredKeyedService<AIAgent>("energy");

    var workflow = AgentWorkflowBuilder.BuildConcurrent((string)key, [security, comfort, energy], Merge);
    return workflow.AsAIAgent(name: (string)key, includeWorkflowOutputsInResponse: true);
});

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

app.Run();

/// <summary>
/// Performs the async MCP handshake LAZILY on first use and caches the resulting tool list,
/// so every agent that asks gets the same fully-populated list. The handshake runs at most
/// once (guarded by a SemaphoreSlim) however many specialists request the tools. This avoids
/// the startup race an eager IHostedService has: an agent can be built before startup
/// finishes the handshake, and would then capture an empty tool list.
/// </summary>
sealed class McpToolsProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _mcpClient;
    private IList<McpClientTool>? _tools;

    public async Task<IList<McpClientTool>> GetTools(CancellationToken cancellationToken = default)
    {
        if (_tools is not null) return _tools;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_tools is not null) return _tools;

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

            _tools = await _mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_mcpClient is not null)
            await _mcpClient.DisposeAsync();
        _gate.Dispose();
    }
}

