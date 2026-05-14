namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Records the current recommended next step for agents.
/// Set by the reviewer or planner after each review gate or planning decision.
/// Agents call workflow.get_next_action before acting.
/// </summary>
public sealed record NextAction
{
    /// <summary>Unique identifier in the format TIME-NNN (reuses the action sequence).</summary>
    public required string ActionId { get; init; }

    /// <summary>The agent who should perform the next action.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Human-readable description of what the actor should do next.</summary>
    public required string Description { get; init; }

    /// <summary>The phase this action targets, if applicable.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Suggested WorkflowKit operation name to call first (optional hint).</summary>
    public string? OperationHint { get; init; }

    /// <summary>Urgency of this next action.</summary>
    public Urgency Urgency { get; init; } = Urgency.Medium;

    /// <summary>Optional target due date for this next action.</summary>
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>
    /// When true, signals that this action should be finished as soon as possible — 
    /// the dashboard shows a prominent "Finish Now" indicator.
    /// </summary>
    public bool PinnedFinishNow { get; init; }

    /// <summary>UTC timestamp when this next action was recorded.</summary>
    public DateTimeOffset SetUtc { get; init; } = DateTimeOffset.UtcNow;
}
