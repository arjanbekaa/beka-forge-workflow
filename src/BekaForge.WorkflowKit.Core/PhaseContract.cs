namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Defines what must be built, validated, and audited for a phase to pass.
/// PhaseContract is the authoritative specification given to implementing agents.
/// </summary>
public sealed record PhaseContract
{
    /// <summary>What the phase must accomplish.</summary>
    public required string Objective { get; init; }

    /// <summary>What is in scope for this phase.</summary>
    public required string Scope { get; init; }

    /// <summary>What is explicitly not in scope for this phase.</summary>
    public string OutOfScope { get; init; } = string.Empty;

    /// <summary>Architecture rules the implementing agent must not violate.</summary>
    public IReadOnlyList<string> ArchitectureConstraints { get; init; } = [];

    /// <summary>Files, areas, or systems that must be touched or created.</summary>
    public IReadOnlyList<string> RequiredFilesOrAreas { get; init; } = [];

    /// <summary>Conditions that must be true for the phase to be considered passing.</summary>
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    /// <summary>Implementation notes for the implementing agent.</summary>
    public string ImplementationNotes { get; init; } = string.Empty;

    /// <summary>What the self-audit must verify.</summary>
    public string AuditRequirements { get; init; } = string.Empty;

    /// <summary>What validation must cover, if applicable.</summary>
    public string ValidationRequirements { get; init; } = string.Empty;

    /// <summary>Notes on whether this phase can be parallelized with other phases.</summary>
    public string ParallelizationNotes { get; init; } = string.Empty;

    /// <summary>Phase IDs that must be in a passing state before this phase can begin.</summary>
    public IReadOnlyList<string> DependsOnPhaseIds { get; init; } = [];

    /// <summary>Structured execution lanes for multi-agent planning and coordination.</summary>
    public IReadOnlyList<ExecutionLane> ExecutionLanes { get; init; } = [];

    /// <summary>
    /// Whether validation is required for this phase to reach PASS.
    /// When false, PASS can be reached directly from REVIEW_LOGGED.
    /// </summary>
    public bool RequiresValidation { get; init; } = true;
}
