namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Input context required by PhaseTransitionValidator to evaluate a requested state transition.
/// All relevant runtime conditions are provided explicitly so the validator is pure and testable.
/// </summary>
public sealed record TransitionContext
{
    /// <summary>The phase's current state before the transition.</summary>
    public required PhaseState CurrentState { get; init; }

    /// <summary>The state the caller is requesting to transition to.</summary>
    public required PhaseState TargetState { get; init; }

    /// <summary>
    /// Whether this phase requires validation before it can reach PASS.
    /// Sourced from PhaseContract.RequiresValidation.
    /// Defaults to true (conservative — validation required unless explicitly opted out).
    /// </summary>
    public bool RequiresValidation { get; init; } = true;

    /// <summary>
    /// Reason text for a BLOCKED transition. Must be non-empty when TargetState is Blocked
    /// and no BlockerId is provided.
    /// </summary>
    public string? BlockerReason { get; init; }

    /// <summary>
    /// ID of an existing blocker record (BLK-NNN). Satisfies the blocker requirement
    /// for a BLOCKED transition in lieu of a new BlockerReason.
    /// </summary>
    public string? BlockerId { get; init; }

    /// <summary>
    /// True when the phase has at least one SkippedNotPossible validation record.
    /// Blocks clean Pass — only PassWithWarnings is allowed when this is true.
    /// </summary>
    public bool HasSkippedNotPossible { get; init; }
}
