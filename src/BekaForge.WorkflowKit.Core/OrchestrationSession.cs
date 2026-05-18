namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Top-level runtime record for one coordinated orchestration session.
/// Persisted as authoritative JSON under .workflowkit/orchestration/sessions/.
/// </summary>
public sealed record OrchestrationSession
{
    public required string SessionId { get; init; }
    public required string PhaseId { get; init; }
    public required string WorkflowId { get; init; }
    public required WorkflowActor ManagerActor { get; init; }
    public OrchestrationSessionState SessionState { get; init; } = OrchestrationSessionState.Planned;
    public string ObjectiveSnapshot { get; init; } = string.Empty;
    public string ScopeSnapshot { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOnPhaseIds { get; init; } = [];
    public IReadOnlyList<string> ExecutionLaneIds { get; init; } = [];
    public string? ActiveRunId { get; init; }
    public string? LatestGateDecisionId { get; init; }
    public OrchestrationAttemptCounters Attempts { get; init; } = new();
    public OrchestrationAttemptPolicy AttemptPolicy { get; init; } = new();
    public AttentionFlagsSnapshot AttentionFlags { get; init; } = new();
    public OrchestrationContractSnapshotRef ContractSnapshot { get; init; } = new();
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; init; }
}
