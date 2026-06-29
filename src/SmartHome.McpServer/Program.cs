// =====================================================================================
// SmartHome.McpServer — an MCP server (HTTP/SSE) exposing energy/weather tools.
// -------------------------------------------------------------------------------------
// Runs as a plain ASP.NET web app so Aspire can register it as a named resource and
// inject its URL into consumers (Step 8, Step 9) via service discovery — no subprocess
// path-hacking needed. Clients connect with SseClientTransport using the discovered URL.
// =====================================================================================

using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp();

app.Run();

[McpServerToolType]
public static class EnergyMcpTools
{
    [McpServerTool, Description("Current electricity price (cents/kWh) and band: cheap/normal/expensive.")]
    public static string GetEnergyPriceNow()
    {
        var hour = DateTime.Now.Hour;
        var (cents, band) = hour switch
        {
            >= 0 and < 7 => (8.0, "cheap"),
            >= 7 and < 10 => (24.0, "expensive"),
            >= 10 and < 16 => (15.0, "normal"),
            >= 16 and < 21 => (29.0, "expensive"),
            _ => (12.0, "normal")
        };
        return $"Electricity is currently {cents} c/kWh ({band}) at {hour:00}:00.";
    }

    [McpServerTool, Description("Short weather/forecast hint for energy decisions.")]
    public static string GetForecast() => (DateTime.Now.DayOfYear % 3) switch
    {
        0 => "Sunny midday — solar surplus 11:00–15:00, good for heavy loads.",
        1 => "Cold front tonight — heating load rising, pre-warm before the peak.",
        _ => "Mild and overcast — no solar surplus, follow price bands."
    };
}
