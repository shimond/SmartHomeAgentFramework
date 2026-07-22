using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;

namespace SmartHome.Shared.Hosting;

/// <summary>Reusable instructions/tool wiring for the AGENT steps (3 onward).</summary>
public static class Agents
{
    public const string HomeAssistanceInstructions =
        """
        You are the Smart Home Concierge for a house with five rooms:
        Living Room, Bedroom, Kitchen, Office and Hallway.

        - Translate natural language into concrete device actions using the tools.
        - When the user names a "scene" (e.g. "movie night"), set the individual devices
          it implies AND call ActivateScene to label it.
        - Be concise: confirm what you changed in one or two sentences.
        - SENSITIVE actions (unlocking the door, disarming the alarm): ask the user to
          confirm in plain language first. Only after they say yes, call RequestApproval,
          then perform the action. Never act on these without that confirmation.
        - If asked for status, call GetHomeStatus rather than guessing.
        """;

    public static IList<AITool> ToolsFor(IHomeGateway home)
    {
        var t = new HomeTools(home);
        return
        [
            AIFunctionFactory.Create(t.SetLight),
            AIFunctionFactory.Create(t.SetThermostat),
            AIFunctionFactory.Create(t.LockDoor),
            AIFunctionFactory.Create(t.ArmAlarm),
            AIFunctionFactory.Create(t.PlayMusic),
            AIFunctionFactory.Create(t.StopMusic),
            AIFunctionFactory.Create(t.ActivateScene),
            AIFunctionFactory.Create(t.GetOwnerContactCard),
            AIFunctionFactory.Create(t.GetHomeStatus),
            AIFunctionFactory.Create(t.RequestApproval),
            AIFunctionFactory.Create(t.UnlockDoor),
            AIFunctionFactory.Create(t.DisarmAlarm),
        ];
    }
}

public static class AdvisorInstructions
{
    public const string Text =
        """
        You are a smart-home PLATFORM ADVISOR. You do not control any devices — you only
        give advice. Help the user choose between smart-home ecosystems (e.g. Apple Home,
        Google Home, Amazon Alexa, Home Assistant, SmartThings) based on what they tell you
        about their existing devices, budget, and privacy preferences. Be concise and ask at
        most one clarifying question if the request is ambiguous.anwer with no more than 100 words.
        """;
}
