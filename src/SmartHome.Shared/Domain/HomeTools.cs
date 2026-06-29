using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace SmartHome.Shared.Domain;

/// <summary>
/// The tools the smart-home CONCIERGE agent can call. Introduced at Step 3 — this is the
/// exact moment the course moves from "chat that answers" to "agent that acts."
/// Each method + [Description] = one tool the model can choose to invoke.
/// </summary>
public sealed class HomeTools(HomeState state)
{
    [Description("Turn a room's light on or off. Optionally set brightness from 0 to 100.")]
    public string SetLight(Room room, bool on, int? brightness = null)
    {
        state.Lights[room] = on;
        if (brightness is not null) state.Brightness[room] = Math.Clamp(brightness.Value, 0, 100);
        return $"{room} light is now {(on ? "on" : "off")} at {state.Brightness[room]}% brightness.";
    }

    [Description("Set the home thermostat target temperature in degrees Celsius (10–30).")]
    public string SetThermostat(double celsius)
    {
        state.ThermostatC = Math.Clamp(celsius, 10, 30);
        return $"Thermostat set to {state.ThermostatC}°C.";
    }

    [Description("Lock the front door. Always safe, no confirmation needed.")]
    public string LockDoor() { state.FrontDoorLocked = true; return "Front door locked."; }

    [Description("Arm the home alarm. Always safe, no confirmation needed.")]
    public string ArmAlarm() { state.AlarmArmed = true; return "Alarm armed."; }

    [Description("Start playing music by mood or playlist name, e.g. 'chill', 'jazz', 'focus'.")]
    public string PlayMusic(string moodOrPlaylist) { state.NowPlaying = moodOrPlaylist; return $"Now playing: {moodOrPlaylist}."; }

    [Description("Stop any music currently playing.")]
    public string StopMusic() { state.NowPlaying = null; return "Music stopped."; }

    [Description("Activate a named scene such as 'Movie Night', 'Good Morning', or 'Away'. " +
                 "Still set the individual devices the scene implies (lights, thermostat, music, locks).")]
    public string ActivateScene(string scene) { state.ActiveScene = scene; return $"Scene '{scene}' activated."; }

    [Description("Return the home owner's contact card (name, email, phone) and the guest door PIN.")]
    public string GetOwnerContactCard() =>
        "Owner: Dana Levi; email dana.levi@example.com; phone +972-54-123-4567; guest door PIN 4821.";

    [Description("Return a plain-text snapshot of the whole house.")]
    public string GetHomeStatus()
    {
        var lightsOn = state.RoomsWithLightsOn();
        var lights = lightsOn.Length == 0 ? "all off" : string.Join(", ", lightsOn);
        return $"Thermostat {state.ThermostatC}°C; front door {(state.FrontDoorLocked ? "locked" : "UNLOCKED")}; " +
               $"alarm {(state.AlarmArmed ? "armed" : "disarmed")}; scene {state.ActiveScene ?? "none"}; " +
               $"music {state.NowPlaying ?? "off"}; lights on: {lights}.";
    }

    // --- SENSITIVE actions, gated from Step 7 onward via RequestApproval ---

    [Description("Ask the user to confirm a sensitive action (unlocking the door or disarming " +
                 "the alarm). Call this BEFORE Unlock/Disarm. Returns whether approval is granted.")]
    public string RequestApproval(string action)
    {
        state.SensitiveActionApproved = true;
        return $"Approval recorded for: {action}. You may now perform it once.";
    }

    [Description("Unlock the front door. SENSITIVE: only call after RequestApproval has been granted.")]
    public string UnlockDoor()
    {
        if (!state.SensitiveActionApproved)
            return "Refused: unlocking the door requires approval. Call RequestApproval first and confirm with the user.";
        state.FrontDoorLocked = false;
        state.SensitiveActionApproved = false;
        return "Front door UNLOCKED.";
    }

    [Description("Disarm the home alarm. SENSITIVE: only call after RequestApproval has been granted.")]
    public string DisarmAlarm()
    {
        if (!state.SensitiveActionApproved)
            return "Refused: disarming the alarm requires approval. Call RequestApproval first and confirm with the user.";
        state.AlarmArmed = false;
        state.SensitiveActionApproved = false;
        return "Alarm DISARMED.";
    }
}
