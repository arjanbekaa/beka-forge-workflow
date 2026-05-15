namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records a validation run for a phase. Replaces the legacy TestRecord.
/// Appended to logs/validation.jsonl.
///
/// Honesty rules (enforced by CreateValidationLogHandler):
/// - Passed/PassedWithWarnings requires at least one evidence item.
/// - BrowserManual, UnityManual, HumanValidationRequired cannot be marked
///   Passed by an LLM without human confirmation. Use PendingUser instead.
/// - Skipped validation requires a non-empty skipReason.
/// - SkippedByUserOverride requires an approvedBy field.
/// </summary>
public sealed record ValidationRecord
{
    /// <summary>Unique identifier in the format VAL-NNN.</summary>
    public required string ValidationId { get; init; }

    /// <summary>The phase this validation log belongs to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The agent who performed or requested the validation.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>The method of validation used.</summary>
    public required ValidationType ValidationType { get; init; }

    /// <summary>The outcome of the validation.</summary>
    public required ValidationResult ValidationResult { get; init; }

    /// <summary>Summary of the validation run and results.</summary>
    public required string Summary { get; init; }

    /// <summary>Whether there were non-blocking warnings.</summary>
    public bool HasWarnings { get; init; }

    /// <summary>Names of specific checks that failed, if any.</summary>
    public IReadOnlyList<string> FailedChecks { get; init; } = [];

    /// <summary>Evidence items supporting the validation result.</summary>
    public IReadOnlyList<ValidationEvidence> EvidenceItems { get; init; } = [];

    /// <summary>The command that was executed, if validation type is AutomatedCommand.</summary>
    public string? Command { get; init; }

    /// <summary>Exit code of the executed command, if applicable.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Manual test steps the user needs to perform, if applicable.</summary>
    public IReadOnlyList<string> ManualSteps { get; init; } = [];

    /// <summary>Reason why validation was skipped, if applicable.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Risk note required when ValidationType is SkippedNotPossible.</summary>
    public string? RiskNote { get; init; }

    /// <summary>Who approved skipping validation or a user override.</summary>
    public string? ApprovedBy { get; init; }

    /// <summary>Additional notes about the validation.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A single piece of evidence supporting a validation result.
/// </summary>
public sealed record ValidationEvidence
{
    /// <summary>Human-readable description of what this evidence proves.</summary>
    public required string Description { get; init; }

    /// <summary>Who or what provided this evidence.</summary>
    public required EvidenceSource Source { get; init; }

    /// <summary>Optional reference (file path, URL, log line, screenshot path).</summary>
    public string? Reference { get; init; }

    /// <summary>UTC timestamp when this evidence was collected.</summary>
    public DateTimeOffset CollectedUtc { get; init; } = DateTimeOffset.UtcNow;
}
