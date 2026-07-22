using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SmartHome.McpServer;

/// <summary>
/// The MCP tools, as a NON-static instance class. The SDK builds a fresh instance for every tool
/// call, activated from a per-request DI scope, so <see cref="IEnergyService"/> is constructor-
/// injected here — no static state, no <c>DateTime.Now</c>. These methods are thin adapters that
/// format the injected service's data into the plain-text the model reads.
/// </summary>
[McpServerToolType]
public sealed class EnergyMcpTools(IEnergyService energy)
{
    [McpServerTool, Description("Current electricity price (cents/kWh) and band: cheap/normal/expensive.")]
    public string GetEnergyPriceNow()
    {
        var p = energy.CurrentPrice();
        return $"Electricity is currently {p.Cents} c/kWh ({p.Band}) at {p.Hour:00}:00.";
    }

    [McpServerTool, Description("Short weather/forecast hint for energy decisions.")]
    public string GetForecast() => energy.Forecast();

    [McpServerTool, Description(
        "Advise whether NOW is a good time to run heavy appliances, given the current home " +
        "status. Pass the home status snapshot (from GetHomeStatus) as 'homeStatus'.")]
    public string AdviseEnergyUse(
        [Description("Plain-text snapshot of the current home status, e.g. the output of GetHomeStatus.")]
        string homeStatus)
    {
        var p = energy.CurrentPrice();
        var recommendation = p.Band switch
        {
            "cheap" => "good time for heavy loads",
            "expensive" => "defer heavy loads past the peak",
            _ => "acceptable, but cheaper windows exist"
        };
        return $"At {p.Hour:00}:00 electricity is {p.Cents} c/kWh ({p.Band}). {energy.Forecast()} " +
               $"Current home status: {homeStatus}. " +
               $"Recommendation: {recommendation}.";
    }
}
