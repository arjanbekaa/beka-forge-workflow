namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Machine-readable attention snapshot attached to orchestration records.
/// The full lifecycle semantics are implemented in later phases.
/// </summary>
public sealed record AttentionFlagsSnapshot
{
    public bool HumanValidationRequired { get; init; }
    public bool TestsNotRunnable { get; init; }
    public bool ManualReviewRequired { get; init; }
    public bool ExternalToolRequired { get; init; }
    public bool MaxAgentAttemptsReached { get; init; }
    public bool UnresolvedRisk { get; init; }
    public bool BlockedByUser { get; init; }
    public bool BlockedByEnvironment { get; init; }
    public IReadOnlyList<string> ReasonRecordIds { get; init; } = [];
}
