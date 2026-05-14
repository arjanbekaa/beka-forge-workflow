namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Status of a sub-phase within a larger phase.
/// Sub-phases decompose complex phases into smaller, trackable units of work
/// that LLM agents can process independently.
/// </summary>
public enum SubPhaseStatus
{
    /// <summary>Sub-phase is planned but not yet started.</summary>
    Planned,

    /// <summary>Sub-phase is currently being worked on.</summary>
    InProgress,

    /// <summary>Sub-phase implementation is complete.</summary>
    Completed,

    /// <summary>Legacy alias preserved for existing workflow state files.</summary>
    Complete = Completed,

    /// <summary>Sub-phase is blocked by an unresolved issue.</summary>
    Blocked,

    /// <summary>Sub-phase has been deferred to a future iteration.</summary>
    Deferred
}

/// <summary>
/// A single sub-phase within a larger phase. Sub-phases decompose complex work
/// into smaller units that can be implemented, audited, and reviewed independently.
/// When a phase has sub-phases, progress is computed from sub-phase completion
/// rather than the parent phase state alone.
/// </summary>
public sealed record SubPhase
{
    /// <summary>Unique identifier within the parent phase (e.g., "PHASE-015-A").</summary>
    public required string SubPhaseId { get; init; }

    /// <summary>Short human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>Current status of this sub-phase.</summary>
    public SubPhaseStatus Status { get; init; } = SubPhaseStatus.Planned;

    /// <summary>Sub-phase IDs this sub-phase depends on (within the same parent phase).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>IDs of implementation records for this sub-phase.</summary>
    public IReadOnlyList<string> ImplementationLogIds { get; init; } = [];

    /// <summary>IDs of audit records for this sub-phase.</summary>
    public IReadOnlyList<string> AuditLogIds { get; init; } = [];

    /// <summary>One-paragraph summary of what this sub-phase delivers.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this sub-phase was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of last status change.</summary>
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
