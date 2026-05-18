namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Runtime state of an orchestration session.
/// This augments phase lifecycle state; it does not replace it.
/// </summary>
public enum OrchestrationSessionState
{
    Planned = 0,
    Running = 1,
    WaitingForAgent = 2,
    WaitingForHuman = 3,
    Blocked = 4,
    CompletedPass = 5,
    CompletedPassWithWarnings = 6,
    CompletedFailure = 7,
    Cancelled = 8
}
