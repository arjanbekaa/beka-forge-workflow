namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Append-only record describing how an orchestration gate advanced, retried,
/// blocked, or terminated a session.
/// </summary>
public sealed record OrchestrationGateDecisionRecord
{
    public required string GateDecisionId { get; init; }
    public required string SessionId { get; init; }
    public required string PhaseId { get; init; }
    public required string RunId { get; init; }
    public required OrchestrationGateKind GateKind { get; init; }
    public int GateAttempt { get; init; } = 1;
    public required OrchestrationDecision Decision { get; init; }
    public required WorkflowActor DecisionActor { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<string> InputRecordIds { get; init; } = [];
    public PhaseState? ResultingPhaseState { get; init; }
    public string? NextActionKind { get; init; }
    public AttentionFlagsSnapshot AttentionFlags { get; init; } = new();
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
