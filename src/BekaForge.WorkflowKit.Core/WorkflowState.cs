using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Top-level workflow entity. Persisted as workflow.json under .workflowkit/.
/// This is the authoritative record of the overall asset development workflow.
/// </summary>
public sealed record WorkflowState
{
    /// <summary>Schema version for forward-compatibility checks.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Unique workflow identifier.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>Name of the Beka Forge asset being developed.</summary>
    public required string AssetName { get; init; }

    /// <summary>Absolute path to the workflow root directory on disk.</summary>
    public required string RootPath { get; init; }

    /// <summary>UTC timestamp when the workflow was initialized.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of the last state change.</summary>
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>ID of the phase currently active or most recently active (PHASE-NNN).</summary>
    public string? CurrentPhaseId { get; init; }

    /// <summary>Last known phase state, for quick summary reads.</summary>
    public PhaseState? LastStatus { get; init; }

    /// <summary>The recommended next action for agents.</summary>
    public NextAction? NextAction { get; init; }

    /// <summary>Top-level architecture constraints that apply across all phases.</summary>
    public IReadOnlyList<string> ArchitectureConstraints { get; init; } = [];

    /// <summary>Ordered list of all phase IDs in this workflow.</summary>
    public IReadOnlyList<string> PhaseIds { get; init; } = [];

    /// <summary>ID of the most recent implementation record (IMP-NNN).</summary>
    public string? LastImplementationId { get; init; }

    /// <summary>ID of the most recent audit record (AUD-NNN).</summary>
    public string? LastAuditId { get; init; }

    /// <summary>ID of the most recent review record (REV-NNN).</summary>
    public string? LastReviewId { get; init; }

    /// <summary>ID of the most recent validation record (VAL-NNN).</summary>
    [JsonPropertyName("lastValidationId")]
    public string? LastValidationId { get; init; }

    /// <summary>Legacy — ID of the most recent test record (TEST-NNN). Kept for backward compat.</summary>
    [JsonPropertyName("lastTestId")]
    public string? LastTestId { get; init; }

    /// <summary>ID of the most recent fix record (FIX-NNN).</summary>
    public string? LastFixId { get; init; }

    /// <summary>Count of open (unresolved) blockers across all phases.</summary>
    public int OpenBlockerCount { get; init; }
}
