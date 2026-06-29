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
        - SENSITIVE actions (unlocking the door, disarming the alarm) are guarded: when you
          call them the runtime will pause and ask the user to approve before they run. You
          do not need a separate confirmation tool — just call the action, and the approval
          gate handles the human-in-the-loop check.
        - If asked for status, call GetHomeStatus rather than guessing.
        """;

    public static IList<AITool> ToolsFor(HomeState state)
    {
        var t = new HomeTools(state);
        return
        [
            AIFunctionFactory.Create(t.SetLight),
            AIFunctionFactory.Create(t.SetThermostat),
            AIFunctionFactory.Create(t.LockDoor),
            AIFunctionFactory.Create(t.ArmAlarm),
            AIFunctionFactory.Create(t.PlayMusic),
            AIFunctionFactory.Create(t.StopMusic),
            AIFunctionFactory.Create(t.ActivateScene),
            AIFunctionFactory.Create(t.GetHomeStatus),
            // SENSITIVE actions: wrapped so the runtime enforces a human-in-the-loop approval
            // gate before they execute. The caller surfaces the FunctionApprovalRequestContent
            // to the user, then resumes the run with their decision (see the explicit endpoint).
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(t.UnlockDoor)),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(t.DisarmAlarm)),
        ];
    }
}

/// <summary>Instructions for the ADVISOR (Steps 0a–2) — pure chat, no tools, no actions.</summary>
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
