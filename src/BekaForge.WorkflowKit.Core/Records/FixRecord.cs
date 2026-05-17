namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records a fix execution performed after a failed review or blocker.
/// Appended to logs/fix.jsonl.
/// </summary>
public sealed record FixRecord
{
    /// <summary>Unique identifier in the format FIX-NNN.</summary>
    public required string FixId { get; init; }

    /// <summary>The phase this fix log belongs to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The actor who performed the fix.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Summary of what was fixed and how.</summary>
    public required string Summary { get; init; }

    /// <summary>Short title used in generated fix logs.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Review record this fix resolves, if applicable.</summary>
    public string? RelatedReviewId { get; init; }

    /// <summary>Blocker record this fix resolves, if applicable.</summary>
    public string? RelatedBlockerId { get; init; }

    /// <summary>Problem statement that caused the fix.</summary>
    public string Problem { get; init; } = string.Empty;

    /// <summary>List of files modified during the fix.</summary>
    public IReadOnlyList<string> FilesModified { get; init; } = [];

    /// <summary>Additional fix notes.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Verification performed after the fix.</summary>
    public string Verification { get; init; } = string.Empty;

    /// <summary>Phase status after the fix was recorded.</summary>
    public PhaseState StatusAfterFix { get; init; } = PhaseState.FixLogged;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
