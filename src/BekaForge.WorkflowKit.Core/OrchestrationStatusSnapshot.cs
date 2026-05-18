namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Shared orchestration status view returned by read operations and runtime handlers.
/// </summary>
public sealed record OrchestrationStatusSnapshot
{
    public required string SessionId { get; init; }
    public required string PhaseId { get; init; }
    public required OrchestrationSessionState SessionState { get; init; }
    public string? ActiveRunId { get; init; }
    public OrchestrationRunRole? ActiveRunRole { get; init; }
    public OrchestrationRunState? ActiveRunState { get; init; }
    public string? LatestGateDecisionId { get; init; }
    public AttentionFlagsSnapshot AttentionFlags { get; init; } = new();
    public AttentionDerivedOutcome DerivedAttentionOutcome { get; init; } = AttentionDerivedOutcome.ReadyToContinue;
    public OrchestrationAttemptCounters Attempts { get; init; } = new();
    public OrchestrationAttemptPolicy AttemptPolicy { get; init; } = new();
}
