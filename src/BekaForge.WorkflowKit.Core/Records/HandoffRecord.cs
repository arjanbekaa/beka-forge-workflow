namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records a handoff of responsibility from one agent to another.
/// Appended to handoffs/handoffs.jsonl.
/// Agents call workflow.get_handoffs to discover work assigned to them.
/// </summary>
public sealed record HandoffRecord
{
    /// <summary>Unique identifier in the format HANDOFF-NNN.</summary>
    public required string HandoffId { get; init; }

    /// <summary>The phase this handoff relates to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The agent handing off responsibility.</summary>
    public required WorkflowActor FromActor { get; init; }

    /// <summary>The agent receiving responsibility.</summary>
    public required WorkflowActor ToActor { get; init; }

    /// <summary>Summary of what the receiving agent needs to do.</summary>
    public required string Summary { get; init; }

    /// <summary>Suggested WorkflowKit operation the receiving agent should call first.</summary>
    public string? OperationHint { get; init; }

    /// <summary>UTC timestamp when this handoff was recorded.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
