namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Structured planning lane for work that can run independently or after
/// explicit lane dependencies are satisfied.
/// </summary>
public sealed record ExecutionLane
{
    /// <summary>Stable identifier for the lane, for example "LANE-A".</summary>
    public required string LaneId { get; init; }

    /// <summary>Short human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>One-paragraph summary of the lane goal.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Phase IDs owned by this lane.</summary>
    public IReadOnlyList<string> PhaseIds { get; init; } = [];

    /// <summary>Sub-phase IDs owned by this lane.</summary>
    public IReadOnlyList<string> SubPhaseIds { get; init; } = [];

    /// <summary>Lane IDs that must finish before this lane can start.</summary>
    public IReadOnlyList<string> DependsOnLaneIds { get; init; } = [];

    /// <summary>Files, modules, or areas this lane owns.</summary>
    public IReadOnlyList<string> OwnedAreas { get; init; } = [];

    /// <summary>Coordination risks or handoff notes for this lane.</summary>
    public string CoordinationNotes { get; init; } = string.Empty;
}
