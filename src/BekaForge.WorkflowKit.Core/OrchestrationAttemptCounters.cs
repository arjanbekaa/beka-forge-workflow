namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Bounded attempt counters tracked by an orchestration session.
/// </summary>
public sealed record OrchestrationAttemptCounters
{
    public int ImplementationAttempts { get; init; }
    public int AuditAttempts { get; init; }
    public int ReviewAttempts { get; init; }
    public int ValidationAttempts { get; init; }
    public int FixAttempts { get; init; }
    public int HumanRequestCount { get; init; }
}
