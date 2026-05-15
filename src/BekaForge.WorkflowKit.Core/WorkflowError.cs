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

    /// <summary>Creates an error when PASS is requested but the validation requirement is not satisfied.</summary>
    public static WorkflowError ValidationRequired() =>
        new("ValidationRequired",
            "PASS requires TEST_LOGGED when PhaseContract.RequiresValidation is true.");

    /// <summary>Creates an error when passed=true is set without evidence.</summary>
    public static WorkflowError EvidenceRequired() =>
        new("EvidenceRequired",
            "Validation cannot be marked as Passed without at least one evidence item.");

    /// <summary>Creates an error when a manual validation type is marked Passed by a non-human actor.</summary>
    public static WorkflowError ManualValidationRequiresHuman() =>
        new("ManualValidationRequiresHuman",
            "Manual validation types (BrowserManual, UnityManual, HumanValidationRequired) " +
            "cannot be marked Passed without human confirmation. Use PendingUser result instead.");

    /// <summary>Creates an error when validation is skipped without a reason.</summary>
    public static WorkflowError SkipReasonRequired() =>
        new("SkipReasonRequired",
            "Skipped validation requires a non-empty skipReason.");

    /// <summary>Creates an error when SkippedByUserOverride is used without approvedBy.</summary>
    public static WorkflowError UserOverrideRequiresApproval() =>
        new("UserOverrideRequiresApproval",
            "SkippedByUserOverride requires an approvedBy field identifying the human owner.");

    /// <summary>Creates a general validation error.</summary>
    public static WorkflowError ValidationFailed(string message) =>
        new("ValidationFailed", message);
}
