namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Canonical error codes for WorkflowKit operation failures.
/// These string values are part of the external contract and must remain stable.
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>Requested state machine transition is not allowed.</summary>
    public const string InvalidTransition = "InvalidTransition";

    /// <summary>Phase is in a terminal state and cannot transition further.</summary>
    public const string TerminalState = "TerminalState";

    /// <summary>BLOCKED was requested without a blocker reason or ID.</summary>
    public const string BlockerRequired = "BlockerRequired";

    /// <summary>PASS was requested but the validation requirement has not been satisfied.</summary>
    public const string ValidationRequired = "ValidationRequired";

    /// <summary>Validation marked as Passed without evidence.</summary>
    public const string EvidenceRequired = "EvidenceRequired";

    /// <summary>Manual validation type marked Passed by non-human actor.</summary>
    public const string ManualValidationRequiresHuman = "ManualValidationRequiresHuman";

    /// <summary>Skipped validation has no skip reason.</summary>
    public const string SkipReasonRequired = "SkipReasonRequired";

    /// <summary>SkippedByUserOverride has no approvedBy.</summary>
    public const string UserOverrideRequiresApproval = "UserOverrideRequiresApproval";

    /// <summary>General input or state validation failure.</summary>
    public const string ValidationFailed = "ValidationFailed";

    /// <summary>Requested entity was not found.</summary>
    public const string NotFound = "NotFound";

    /// <summary>An unexpected internal error occurred.</summary>
    public const string InternalError = "InternalError";
}
