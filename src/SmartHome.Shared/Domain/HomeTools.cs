using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace SmartHome.Shared.Domain;

/// <summary>
/// The tools the smart-home CONCIERGE agent can call. Introduced at Step 3 — this is the
/// exact moment the course moves from "chat that answers" to "agent that acts."
/// Each method + [Description] = one tool the model can choose to invoke.
///
/// The tools are a thin layer over <see cref="IHomeGateway"/>: they translate the model's
/// intent into device commands and format a short human-readable confirmation. The sensitive-
/// action approval gate lives HERE (in the tool), not in the gateway — enforced in code so it
/// holds regardless of what the model does (the Step 7 guardrail).
/// </summary>
public sealed class HomeTools(IHomeGateway home)
{
    [Description("Turn a room's light on or off. Optionally set brightness from 0 to 100.")]
    public string SetLight(Room room, bool on, int? brightness = null)
    {
        home.SetLight(room, on, brightness);
        return $"{room} light is now {(on ? "on" : "off")} at {home.Brightness(room)}% brightness.";
    }

    [Description("Set the home thermostat target temperature in degrees Celsius (10–30).")]
    public string SetThermostat(double celsius)
    {
        home.SetThermostat(celsius);
        return $"Thermostat set to {home.ThermostatC}°C.";
    }

    [Description("Lock the front door. Always safe, no confirmation needed.")]
    public string LockDoor() { home.LockDoor(); return "Front door locked."; }

    [Description("Arm the home alarm. Always safe, no confirmation needed.")]
    public string ArmAlarm() { home.ArmAlarm(); return "Alarm armed."; }

    [Description("Start playing music by mood or playlist name, e.g. 'chill', 'jazz', 'focus'.")]
    public string PlayMusic(string moodOrPlaylist) { home.PlayMusic(moodOrPlaylist); return $"Now playing: {moodOrPlaylist}."; }

    [Description("Stop any music currently playing.")]
    public string StopMusic() { home.StopMusic(); return "Music stopped."; }

    [Description("Activate a named scene such as 'Movie Night', 'Good Morning', or 'Away'. " +
                 "Still set the individual devices the scene implies (lights, thermostat, music, locks).")]
    public string ActivateScene(string scene) { home.ActivateScene(scene); return $"Scene '{scene}' activated."; }

    [Description("Return the home owner's contact card (name, email, phone) and the guest door PIN.")]
    public string GetOwnerContactCard() =>
        "Owner: Dana Levi; email dana.levi@example.com; phone +972-54-123-4567; guest door PIN 4821.";

    [Description("Return a plain-text snapshot of the whole house.")]
    public string GetHomeStatus()
    {
        var lightsOn = home.RoomsWithLightsOn();
        var lights = lightsOn.Length == 0 ? "all off" : string.Join(", ", lightsOn);
        return $"Thermostat {home.ThermostatC}°C; front door {(home.FrontDoorLocked ? "locked" : "UNLOCKED")}; " +
               $"alarm {(home.AlarmArmed ? "armed" : "disarmed")}; scene {home.ActiveScene ?? "none"}; " +
               $"music {home.NowPlaying ?? "off"}; lights on: {lights}.";
    }

    // --- SENSITIVE actions, gated from Step 7 onward via RequestApproval ---

    [Description("Ask the user to confirm a sensitive action (unlocking the door or disarming " +
                 "the alarm). Call this BEFORE Unlock/Disarm. Returns whether approval is granted.")]
    public string RequestApproval(string action)
    {
        home.SensitiveActionApproved = true;
        return $"Approval recorded for: {action}. You may now perform it once.";
    }

    [Description("Unlock the front door. SENSITIVE: only call after RequestApproval has been granted.")]
    public string UnlockDoor()
    {
        if (!home.SensitiveActionApproved)
            return "Refused: unlocking the door requires approval. Call RequestApproval first and confirm with the user.";
        home.UnlockDoor();
        home.SensitiveActionApproved = false;
        return "Front door UNLOCKED.";
    }

    [Description("Disarm the home alarm. SENSITIVE: only call after RequestApproval has been granted.")]
    public string DisarmAlarm()
    {
        if (!home.SensitiveActionApproved)
            return "Refused: disarming the alarm requires approval. Call RequestApproval first and confirm with the user.";
        home.DisarmAlarm();
        home.SensitiveActionApproved = false;
        return "Alarm DISARMED.";
    }
}
