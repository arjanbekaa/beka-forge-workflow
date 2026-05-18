namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Runtime state of one delegated run inside an orchestration session.
/// </summary>
public enum OrchestrationRunState
{
    Queued = 0,
    Dispatched = 1,
    InProgress = 2,
    Reported = 3,
    Accepted = 4,
    Rejected = 5,
    Blocked = 6,
    Cancelled = 7
}
