using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Represents a single development phase with its full lifecycle state.
/// A Phase is the unit of work that agents implement, audit, review, and validate.
/// </summary>
public sealed record Phase
{
    /// <summary>Unique identifier in the format PHASE-NNN.</summary>
    public required string PhaseId { get; init; }

    /// <summary>Sequence number (1-based). Drives the PHASE-NNN ID.</summary>
    public required int PhaseNumber { get; init; }

    /// <summary>Short human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>One-paragraph summary of the phase purpose.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Current lifecycle state of this phase.</summary>
    public PhaseState State { get; init; } = PhaseState.Planned;

    /// <summary>Agent currently assigned to implement this phase, if any.</summary>
    public WorkflowActor? AssignedAgent { get; init; }

    /// <summary>Phase IDs this phase depends on.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>Phase contract specifying acceptance criteria and constraints.</summary>
    public PhaseContract? Contract { get; init; }

    /// <summary>IDs of all implementation log entries for this phase (IMP-NNN).</summary>
    public IReadOnlyList<string> ImplementationLogIds { get; init; } = [];

    /// <summary>IDs of all self-audit log entries for this phase (AUD-NNN).</summary>
    public IReadOnlyList<string> AuditLogIds { get; init; } = [];

    /// <summary>IDs of all review gate log entries for this phase (REV-NNN).</summary>
    public IReadOnlyList<string> ReviewLogIds { get; init; } = [];

    /// <summary>IDs of all validation log entries for this phase (VAL-NNN).</summary>
    [JsonPropertyName("validationLogIds")]
    public IReadOnlyList<string> ValidationLogIds { get; init; } = [];

    /// <summary>Legacy — IDs of all test log entries (TEST-NNN). Kept for backward compat with old workflow files. Now aliased to ValidationLogIds.</summary>
    [JsonPropertyName("testLogIds")]
    public IReadOnlyList<string> TestLogIds { get; init; } = [];

    /// <summary>IDs of all fix log entries for this phase (FIX-NNN).</summary>
    public IReadOnlyList<string> FixLogIds { get; init; } = [];

    /// <summary>IDs of all blocker records for this phase (BLK-NNN).</summary>
    public IReadOnlyList<string> BlockerIds { get; init; } = [];

    /// <summary>IDs of all handoff records involving this phase (HANDOFF-NNN).</summary>
    public IReadOnlyList<string> HandoffIds { get; init; } = [];

    /// <summary>Optional sub-phase breakdown for complex phases. When present, progress is computed from sub-phases.</summary>
    public IReadOnlyList<SubPhase> SubPhases { get; init; } = [];

    /// <summary>UTC timestamp when this phase was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when implementation began (first transition to IN_IMPLEMENTATION).</summary>
    public DateTimeOffset? StartedUtc { get; init; }

    /// <summary>UTC timestamp when the phase reached a terminal state.</summary>
    public DateTimeOffset? CompletedUtc { get; init; }

    /// <summary>UTC timestamp of the last state change.</summary>
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
