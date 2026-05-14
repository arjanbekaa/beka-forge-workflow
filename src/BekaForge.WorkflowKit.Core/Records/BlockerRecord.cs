namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records an active blocker that is preventing a phase from progressing.
/// Appended to blockers/blockers.jsonl.
/// A blocker must be recorded before a phase can transition to BLOCKED.
/// </summary>
public sealed record BlockerRecord
{
    /// <summary>Unique identifier in the format BLK-NNN.</summary>
    public required string BlockerId { get; init; }

    /// <summary>The phase that is blocked.</summary>
    public required string PhaseId { get; init; }

    /// <summary>Human-readable description of why this phase is blocked.</summary>
    public required string Reason { get; init; }

    /// <summary>The agent who reported the blocker.</summary>
    public required WorkflowActor ReportedBy { get; init; }

    /// <summary>Whether this blocker has been resolved.</summary>
    public bool IsResolved { get; init; }

    /// <summary>Human-readable description of how the blocker was resolved, if applicable.</summary>
    public string? Resolution { get; init; }

    /// <summary>UTC timestamp when the blocker was recorded.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when the blocker was resolved, if applicable.</summary>
    public DateTimeOffset? ResolvedUtc { get; init; }
}
