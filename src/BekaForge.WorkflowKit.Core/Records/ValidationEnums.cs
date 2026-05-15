namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// The method used to validate a phase. Determines what evidence is required
/// and whether a human must confirm the result.
/// </summary>
public enum ValidationType
{
    /// <summary>Validation performed by running an automated command (e.g. dotnet test, npm test).</summary>
    AutomatedCommand,

    /// <summary>Validation performed by static inspection of code, logs, or build output.</summary>
    StaticInspection,

    /// <summary>Validation requires manual testing in a browser. Cannot be marked Passed by LLM alone.</summary>
    BrowserManual,

    /// <summary>Validation requires manual testing in the Unity Editor. Cannot be marked Passed by LLM alone.</summary>
    UnityManual,

    /// <summary>Validation performed by an automated Unity test runner.</summary>
    UnityAutomated,

    /// <summary>Validation requires a human to perform steps and confirm. Cannot be marked Passed by LLM alone.</summary>
    HumanValidationRequired,

    /// <summary>No validation is needed for this phase.</summary>
    SkippedNotNeeded,

    /// <summary>Validation cannot be performed in this environment (e.g. no browser, no Unity). Requires risk note.</summary>
    SkippedNotPossible,

    /// <summary>Validation was skipped by explicit user override.</summary>
    SkippedByUserOverride,

    /// <summary>Represents a legacy TestRecord converted to a validation entry for reporting purposes.</summary>
    LegacyTest
}

/// <summary>
/// The outcome of a validation run.
/// </summary>
public enum ValidationResult
{
    /// <summary>All validation checks passed without issues.</summary>
    Passed,

    /// <summary>Validation passed but with non-blocking warnings.</summary>
    PassedWithWarnings,

    /// <summary>Validation failed — issues must be addressed.</summary>
    Failed,

    /// <summary>Validation was intentionally skipped.</summary>
    Skipped,

    /// <summary>Validation is pending human action (e.g. manual test steps outstanding). Blocks Pass.</summary>
    PendingHumanValidation,

    /// <summary>Backward-compat alias for PendingHumanValidation. Do not use in new code.</summary>
    PendingUser = PendingHumanValidation
}

/// <summary>
/// Who or what provided the validation evidence.
/// </summary>
public enum EvidenceSource
{
    /// <summary>The AI agent performed the validation.</summary>
    Agent,

    /// <summary>A human owner confirmed the result.</summary>
    HumanOwner,

    /// <summary>The evidence came from a command execution.</summary>
    Command,

    /// <summary>The evidence came from an external tool.</summary>
    Tool,

    /// <summary>The evidence came from a CI/CD pipeline.</summary>
    CI
}
