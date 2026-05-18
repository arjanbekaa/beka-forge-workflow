namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Derived operator-visible attention outcome computed from the active flag set.
/// </summary>
public enum AttentionDerivedOutcome
{
    AttemptBudgetExceeded = 0,
    BlockedByUser = 1,
    BlockedByEnvironment = 2,
    NeedsHumanValidation = 3,
    NeedsManualReview = 4,
    NeedsExternalTool = 5,
    TestsNotRunnable = 6,
    WarningOpen = 7,
    ReadyToContinue = 8
}
