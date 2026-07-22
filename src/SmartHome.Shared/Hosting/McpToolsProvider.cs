using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace SmartHome.Shared.Hosting;

/// <summary>
/// Performs the async MCP handshake LAZILY on first use and caches the resulting tool list,
/// so every agent that asks gets the same fully-populated list. The handshake runs at most
/// once (guarded by a SemaphoreSlim) however many callers ask. This avoids the startup race
/// an eager IHostedService has: an agent can be built before startup finishes the handshake,
/// and would then capture an empty tool list. Program.cs awaits GetTools() once during
/// startup so agent factories — which run synchronously, since AddAIAgent has no async
/// factory overload — can read the cached Tools property instead of blocking on the
/// handshake. Shared by Step 8 and Step 9, the two steps that talk to SmartHome.McpServer.
/// </summary>
public sealed class McpToolsProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<McpToolsProvider> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _mcpClient;
    private IList<McpClientTool>? _tools;

    /// <summary>
    /// The cached tool list. Only safe to read after <see cref="GetTools"/> has completed at
    /// least once — Program.cs awaits it during startup, before any agent can be resolved.
    /// </summary>
    public IList<McpClientTool> Tools => _tools
        ?? throw new InvalidOperationException(
            "McpToolsProvider was not warmed up during startup. Call GetTools() once before " +
            "the host starts serving requests.");

    public async Task<IList<McpClientTool>> GetTools(CancellationToken cancellationToken = default)
    {
        if (_tools is not null) return _tools;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_tools is not null) return _tools;

            foreach (var kv in configuration.AsEnumerable())
                if (kv.Key.Contains("mcp", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.StartsWith("services", StringComparison.OrdinalIgnoreCase))
                    logger.LogDebug("[MCP-DIAG] {Key} = {Value}", kv.Key, kv.Value);

            // Read the real URL Aspire injects via .WithReference(mcpServer) in the AppHost.
            // Falls back to Mcp:EnergyServerUrl (appsettings.json), then localhost, for
            // running without Aspire.
            var endpoint =
                configuration["services:mcp-energy-server:https:0"] ??
                configuration["services:mcp-energy-server:http:0"] ??
                configuration["Mcp:EnergyServerUrl"] ??
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
