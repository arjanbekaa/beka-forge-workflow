namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Append-only event kinds for orchestration run history.
/// </summary>
public enum OrchestrationEventKind
{
    SessionCreated = 0,
    SessionUpdated = 1,
    RunCreated = 2,
    RunStarted = 3,
    RunReported = 4,
    RunAccepted = 5,
    RunRejected = 6,
    SessionCancelled = 7,
    AttentionChanged = 8
}
