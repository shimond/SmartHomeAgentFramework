namespace SmartHome.McpServer;

/// <summary>A point-in-time electricity price: the rate, its band, and the hour it applies to.</summary>
public sealed record EnergyPrice(double Cents, string Band, int Hour);

/// <summary>
/// The energy "business logic" the MCP tools expose — deliberately behind an interface so it can
/// be swapped (e.g. for a real tariff feed) and unit-tested with a fake clock. The clock is an
/// injected <see cref="TimeProvider"/> rather than a direct <c>DateTime.Now</c>, so tests are
/// deterministic and the timezone is explicit.
/// </summary>
public interface IEnergyService
{
    EnergyPrice CurrentPrice();
    string Forecast();
}

public sealed class EnergyService(TimeProvider clock) : IEnergyService
{
    public EnergyPrice CurrentPrice()
    {
        var hour = clock.GetLocalNow().Hour;
        var (cents, band) = hour switch
        {
            >= 0 and < 7 => (8.0, "cheap"),
            >= 7 and < 10 => (24.0, "expensive"),
            >= 10 and < 16 => (15.0, "normal"),
            >= 16 and < 21 => (29.0, "expensive"),
            _ => (12.0, "normal")
        };
        return new EnergyPrice(cents, band, hour);
    }

    public string Forecast() => (clock.GetLocalNow().DayOfYear % 3) switch
    {
        0 => "Sunny midday — solar surplus 11:00–15:00, good for heavy loads.",
        1 => "Cold front tonight — heating load rising, pre-warm before the peak.",
        _ => "Mild and overcast — no solar surplus, follow price bands."
    };
}
