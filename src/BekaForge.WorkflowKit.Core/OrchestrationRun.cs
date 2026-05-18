namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Durable record for one delegated unit of work under an orchestration session.
/// Persisted as authoritative JSON under .workflowkit/orchestration/runs/.
/// </summary>
public sealed record OrchestrationRun
{
    public required string RunId { get; init; }
    public required string SessionId { get; init; }
    public required string PhaseId { get; init; }
    public string? ParentRunId { get; init; }
    public required string RootRunId { get; init; }
    public string? LaneId { get; init; }
    public required OrchestrationRunRole Role { get; init; }
    public required WorkflowActor AssignedActor { get; init; }
    public OrchestrationRunState RunState { get; init; } = OrchestrationRunState.Queued;
    public string Purpose { get; init; } = string.Empty;
    public OrchestrationContractSnapshotRef InputContractRef { get; init; } = new();
    public int AttemptNumber { get; init; } = 1;
    public string? RequestedByGate { get; init; }
    public string ResultSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ProducedRecordIds { get; init; } = [];
    public AttentionFlagsSnapshot AttentionFlags { get; init; } = new();
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; init; }
}
