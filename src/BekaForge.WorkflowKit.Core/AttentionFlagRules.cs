namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Shared structural rules and derived-outcome logic for attention flags.
/// Context-specific clear rules are enforced by handlers that know the linked records.
/// </summary>
public static class AttentionFlagRules
{
    public static bool TryValidate(AttentionFlagsSnapshot snapshot, out string? error)
    {
        error = null;

        if (snapshot.BlockedByUser && snapshot.BlockedByEnvironment && snapshot.ReasonRecordIds.Count < 2)
        {
            error = "blockedByUser and blockedByEnvironment require separate evidence references.";
            return false;
        }

        if (snapshot.MaxAgentAttemptsReached && snapshot.ReasonRecordIds.Count == 0)
        {
            error = "maxAgentAttemptsReached requires at least one linked reason record.";
            return false;
        }

        if (snapshot.TestsNotRunnable && snapshot.ReasonRecordIds.Count == 0)
        {
            error = "testsNotRunnable requires at least one linked reason record.";
            return false;
        }

        if (snapshot.ExternalToolRequired && !snapshot.BlockedByEnvironment && !snapshot.ManualReviewRequired)
        {
            error = "externalToolRequired requires blockedByEnvironment or manualReviewRequired.";
            return false;
        }

        if (snapshot.TestsNotRunnable
            && !snapshot.HumanValidationRequired
            && !snapshot.ManualReviewRequired
            && !snapshot.BlockedByEnvironment
            && !snapshot.UnresolvedRisk)
        {
            error = "testsNotRunnable must be accompanied by humanValidationRequired, manualReviewRequired, blockedByEnvironment, or unresolvedRisk.";
            return false;
        }

        if (snapshot.MaxAgentAttemptsReached
            && !snapshot.ManualReviewRequired
            && !snapshot.BlockedByUser
            && !snapshot.UnresolvedRisk)
        {
            error = "maxAgentAttemptsReached must imply manualReviewRequired, blockedByUser, or unresolvedRisk.";
            return false;
        }

        return true;
    }

    public static AttentionDerivedOutcome DeriveOutcome(AttentionFlagsSnapshot snapshot)
    {
        if (snapshot.MaxAgentAttemptsReached)
            return AttentionDerivedOutcome.AttemptBudgetExceeded;
        if (snapshot.BlockedByUser)
            return AttentionDerivedOutcome.BlockedByUser;
        if (snapshot.BlockedByEnvironment)
            return AttentionDerivedOutcome.BlockedByEnvironment;
        if (snapshot.HumanValidationRequired)
            return AttentionDerivedOutcome.NeedsHumanValidation;
        if (snapshot.ManualReviewRequired)
            return AttentionDerivedOutcome.NeedsManualReview;
        if (snapshot.ExternalToolRequired)
            return AttentionDerivedOutcome.NeedsExternalTool;
        if (snapshot.TestsNotRunnable)
            return AttentionDerivedOutcome.TestsNotRunnable;
        if (snapshot.UnresolvedRisk)
            return AttentionDerivedOutcome.WarningOpen;
        return AttentionDerivedOutcome.ReadyToContinue;
    }

    public static bool HasAny(AttentionFlagsSnapshot snapshot) =>
        snapshot.HumanValidationRequired
        || snapshot.TestsNotRunnable
        || snapshot.ManualReviewRequired
        || snapshot.ExternalToolRequired
        || snapshot.MaxAgentAttemptsReached
        || snapshot.UnresolvedRisk
        || snapshot.BlockedByUser
        || snapshot.BlockedByEnvironment;
}
