namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records an architecture review performed by Codex on a completed implementation.
/// This is a Codex gate decision — distinct from the DeepSeek self-audit (AuditRecord).
/// Appended to logs/review.jsonl.
/// </summary>
public sealed record ReviewRecord
{
    /// <summary>Unique identifier in the format REV-NNN.</summary>
    public required string ReviewId { get; init; }

    /// <summary>The phase this review log belongs to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The agent who performed the review (typically Codex).</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Summary of the review findings and decision.</summary>
    public required string Summary { get; init; }

    /// <summary>Whether the implementation passed the Codex review gate.</summary>
    public required bool Passed { get; init; }

    /// <summary>List of architecture or quality issues identified during review.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>Whether fixes are required before the phase can proceed.</summary>
    public bool RequiresFix { get; init; }

    /// <summary>Additional notes from the reviewer.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
