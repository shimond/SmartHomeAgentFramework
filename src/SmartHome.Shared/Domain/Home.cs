namespace SmartHome.Shared.Domain;

public enum Room { LivingRoom, Bedroom, Kitchen, Office, Hallway }

/// <summary>
/// The boundary between the concierge's tools and the actual house. Introduced at Step 3, when
/// the agent starts to DO things. Tools depend on this interface, never on a concrete house — so
/// the same tools run against the offline <see cref="InMemoryHome"/> in the demo, or against a
/// real device hub (Home Assistant / SmartThings / Matter) in production. This mirrors the
/// swap-the-implementation pattern the course already uses for RAG (<c>IManualStore</c>) and
/// conversation history (<c>IConversationStore</c>).
///
/// To go real, add a <c>DeviceHubHome : IHomeGateway</c> that calls the hub's HTTP API (inject an
/// <c>HttpClient</c>), then swap the DI registration — the tools and every agent step are
/// unchanged:
/// <code>
///   builder.Services.AddSingleton&lt;IHomeGateway, DeviceHubHome&gt;();   // instead of InMemoryHome
/// </code>
/// (Commands are synchronous here to keep the teaching demo simple; a real hub would make them
/// async and fallible.)
/// </summary>
public interface IHomeGateway
{
    // --- device state (reads) ---
    double ThermostatC { get; }
    bool FrontDoorLocked { get; }
    bool AlarmArmed { get; }
    string? NowPlaying { get; }
    string? ActiveScene { get; }
    int Brightness(Room room);
    Room[] RoomsWithLightsOn();

    // --- app policy: one-shot approval for sensitive actions (Step 7 guardrail) ---
    bool SensitiveActionApproved { get; set; }

    // --- device commands ---
    void SetLight(Room room, bool on, int? brightness = null);
    void SetThermostat(double celsius);
    void LockDoor();
    void UnlockDoor();
    void ArmAlarm();
    void DisarmAlarm();
    void PlayMusic(string moodOrPlaylist);
    void StopMusic();
    void ActivateScene(string scene);
}

/// <summary>
/// The offline, in-memory house — the demo/dev implementation of <see cref="IHomeGateway"/>.
/// State lives for the process lifetime and starts from sensible defaults. Registered as a
/// singleton so every tool call sees the same house. (Not real-life: one house per process, no
/// persistence, commands never fail — swap for a hub-backed implementation for production.)
/// </summary>
public sealed class InMemoryHome : IHomeGateway
{
    private readonly Dictionary<Room, bool> _lights =
        Enum.GetValues<Room>().ToDictionary(r => r, _ => false);
    private readonly Dictionary<Room, int> _brightness =
        Enum.GetValues<Room>().ToDictionary(r => r, _ => 0); // 0–100

    public double ThermostatC { get; private set; } = 21;
    public bool FrontDoorLocked { get; private set; } = true;
    public bool AlarmArmed { get; private set; }
    public string? NowPlaying { get; private set; }
    public string? ActiveScene { get; private set; }
    public bool SensitiveActionApproved { get; set; }

    public int Brightness(Room room) => _brightness[room];
    public Room[] RoomsWithLightsOn() => _lights.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();

    public void SetLight(Room room, bool on, int? brightness = null)
    {
        _lights[room] = on;
        if (brightness is not null) _brightness[room] = Math.Clamp(brightness.Value, 0, 100);
    }

    public void SetThermostat(double celsius) => ThermostatC = Math.Clamp(celsius, 10, 30);
    public void LockDoor() => FrontDoorLocked = true;
    public void UnlockDoor() => FrontDoorLocked = false;
    public void ArmAlarm() => AlarmArmed = true;
    public void DisarmAlarm() => AlarmArmed = false;
    public void PlayMusic(string moodOrPlaylist) => NowPlaying = moodOrPlaylist;
    public void StopMusic() => NowPlaying = null;
    public void ActivateScene(string scene) => ActiveScene = scene;
}

/// <summary>Step 2/Step1 — structured output target. Same shape, used to show the ceiling.</summary>
public record HomeStatusReport(
    double ThermostatC,
    bool FrontDoorLocked,
    bool AlarmArmed,
    string? ActiveScene,
    string? NowPlaying,
    string[] RoomsWithLightsOn);

/// <summary>Step 0a/0b/1/2 — the ADVISOR has no house; it just recommends a system.</summary>
public record SmartHomeRecommendation(
    string RecommendedPlatform,
    string Reason,
    string[] Considerations);
