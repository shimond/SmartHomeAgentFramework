using System.ComponentModel;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;

namespace SmartHome.Step3b.AgentWithMiddleware;

/// <summary>
/// Step 3b — a front-door lock whose <c>UnlockDoor</c> tool requires the physical guest PIN to
/// actuate, but HIDES that PIN parameter from the model's JSON schema
/// (<see cref="Microsoft.Extensions.AI.AIFunctionFactoryOptions.ParameterBindingOptions.ExcludeFromSchema"/>).
///
/// The model only ever sees a zero-argument <c>UnlockDoor()</c>. The secret-injection middleware
/// in <c>Program.cs</c> augments the argument dictionary with the real PIN just before the tool
/// runs — so the PIN reaches the TOOL but never the LLM: not in the prompt, not in the tool-call
/// arguments DevUI shows, and not in the result the model reads back. This is the realistic
/// "critical data goes to the tool, not to the model" pattern.
///
/// It mirrors the shared <see cref="SmartHome.Shared.Domain.HomeTools.UnlockDoor"/> approval
/// guard and layers the PIN check on top, so Step 3b's unlock stays consistent with the rest of
/// the course (the approval gate is the pre-existing Step 7 guardrail; the injected PIN is the
/// new concept here).
/// </summary>
internal sealed class PinGatedLock(IHomeGateway home, string requiredPin)
{
    // `pin` is optional so that if the injection middleware is absent it binds to "" and the
    // method returns a clean refusal, rather than the argument marshaller throwing.
    [Description("Unlock the front door. SENSITIVE: only call after RequestApproval has been granted.")]
    public string UnlockDoor(string pin = "")
    {
        // Same approval guardrail as the shared tool (Step 7) — enforced in code, not the prompt.
        if (!home.SensitiveActionApproved)
            return "Refused: unlocking the door requires approval. Call RequestApproval first and confirm with the user.";

        // The lock hardware refuses to actuate without the correct PIN. The model never sees or
        // supplies this — the injection middleware does. If that middleware is absent, `pin`
        // arrives empty and the unlock is (correctly) rejected.
        if (string.IsNullOrEmpty(pin) || pin != requiredPin)
            return "Refused: the lock hardware rejected the PIN.";

        home.UnlockDoor();
        home.SensitiveActionApproved = false;    // one-shot, same as the shared tool
        return "Front door UNLOCKED.";
    }

    /// <summary>
    /// Builds the <c>UnlockDoor</c> tool with the <c>pin</c> parameter EXCLUDED from the JSON
    /// schema, so the model sees a zero-argument tool. The value is supplied at invocation time
    /// by the injection middleware augmenting the argument dictionary (see <c>Program.cs</c>).
    /// </summary>
    public static AIFunction CreateTool(IHomeGateway home, string requiredPin)
    {
        var lockDevice = new PinGatedLock(home, requiredPin);
        return AIFunctionFactory.Create(lockDevice.UnlockDoor, new AIFunctionFactoryOptions
        {
            Name = "UnlockDoor",
            ConfigureParameterBinding = p =>
                p.Name == "pin"
                    ? new AIFunctionFactoryOptions.ParameterBindingOptions { ExcludeFromSchema = true }
                    : default
        });
    }
}
