using System.ComponentModel;

namespace SmartHome.Step9.MultiAgentWorkflow;

/// <summary>
/// Local fallback so Step 9 is self-contained even without the MCP server running.
/// Mirrors SmartHome.McpServer's tools exactly — swap these for the real MCP client tools
/// (as Step 8 does) to prove the same workflow runs with a local OR an external provider.
/// </summary>
public sealed class EnergyTools
{
    [Description("Current electricity price (cents/kWh) and band: cheap/normal/expensive.")]
    public string GetEnergyPriceNow()
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

    [Description("Short weather/forecast hint for energy decisions.")]
    public string GetForecast() => (DateTime.Now.DayOfYear % 3) switch
    {
        0 => "Sunny midday — solar surplus 11:00–15:00, good for heavy loads.",
        1 => "Cold front tonight — heating load rising, pre-warm before the peak.",
        _ => "Mild and overcast — no solar surplus, follow price bands."
    };
}
