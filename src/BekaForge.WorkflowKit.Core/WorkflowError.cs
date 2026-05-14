namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// A structured error returned by WorkflowKit operations.
/// Invalid transitions must not mutate state — errors are returned, not thrown.
/// </summary>
public sealed record WorkflowError(string Code, string Message)
{
    /// <summary>Creates an error for a disallowed state machine transition.</summary>
    public static WorkflowError InvalidTransition(PhaseState from, PhaseState to, string reason) =>
        new("InvalidTransition", $"Cannot transition from {from} to {to}: {reason}");

    /// <summary>Creates an error when a terminal state is asked to transition further.</summary>
    public static WorkflowError TerminalState(PhaseState state) =>
        new("TerminalState", $"Phase is in terminal state {state} and cannot transition.");

    /// <summary>Creates an error when BLOCKED is requested without a blocker reason or ID.</summary>
    public static WorkflowError BlockerRequired() =>
        new("BlockerRequired", "Transitioning to BLOCKED requires a non-empty blocker reason or blocker ID.");

    /// <summary>Creates an error when PASS is requested but the Unity test requirement is not satisfied.</summary>
    public static WorkflowError UnityTestRequired() =>
        new("UnityTestRequired",
            "PASS requires UNITY_TEST_LOGGED when PhaseContract.RequiresUnityTest is true.");

    /// <summary>Creates a general validation error.</summary>
    public static WorkflowError ValidationFailed(string message) =>
        new("ValidationFailed", message);
}
