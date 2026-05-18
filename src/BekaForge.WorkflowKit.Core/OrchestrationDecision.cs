namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Decisions recorded by orchestration gate handling.
/// </summary>
public enum OrchestrationDecision
{
    Advance = 0,
    Retry = 1,
    RequestFix = 2,
    RequestHuman = 3,
    Block = 4,
    Fail = 5,
    FinishPass = 6,
    FinishPassWithWarnings = 7
}
