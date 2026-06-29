namespace SmartHome.Shared.Domain;

public enum Room { LivingRoom, Bedroom, Kitchen, Office, Hallway }

/// <summary>
/// The simulated house. Used from Step 3 onward, once the concierge can actually DO things.
/// Registered as a singleton per process so every tool call mutates the same house.
/// </summary>
public sealed class HomeState
{
    public Dictionary<Room, bool> Lights { get; } =
        Enum.GetValues<Room>().ToDictionary(r => r, _ => false);

    public Dictionary<Room, int> Brightness { get; } =
        Enum.GetValues<Room>().ToDictionary(r => r, _ => 0); // 0–100

    public double ThermostatC { get; set; } = 21;
    public bool FrontDoorLocked { get; set; } = true;
    public bool AlarmArmed { get; set; }
    public string? NowPlaying { get; set; }
    public string? ActiveScene { get; set; }

    public Room[] RoomsWithLightsOn() => Lights.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
}

/// <summary>Step 2/Step1 — structured output target. Same shape, used to show the ceiling.</summary>
public record HomeStatusReport(
    double ThermostatC,
    bool FrontDoorLocked,
    bool AlarmArmed,
    string? ActiveScene,
    string? NowPlaying,
    string[] RoomsWithLightsOn);

/// <summary>Step 0a/0b/1/2 — the ADVISOR has no HomeState; it just recommends a system.</summary>
public record SmartHomeRecommendation(
    string RecommendedPlatform,
    string Reason,
    string[] Considerations);
