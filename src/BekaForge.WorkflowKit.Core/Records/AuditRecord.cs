namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records a self-audit or independent audit performed on implementation output.
/// This is distinct from a ReviewRecord — it is the audit stage of the workflow.
/// Appended to logs/audit.jsonl.
/// </summary>
public sealed record AuditRecord
{
    /// <summary>Unique identifier in the format AUD-NNN.</summary>
    public required string AuditId { get; init; }

    /// <summary>The phase this audit log belongs to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The actor who performed the audit.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Summary of what was audited and the findings.</summary>
    public required string Summary { get; init; }

    /// <summary>Whether the self-audit concluded the implementation is acceptable.</summary>
    public required bool Passed { get; init; }

    /// <summary>List of issues found during the self-audit.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>Quality improvement recommendations — architecture alternatives, simplifications, or better patterns.
    /// These are non-blocking suggestions logged even when the audit passes.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>Additional notes about the audit.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
