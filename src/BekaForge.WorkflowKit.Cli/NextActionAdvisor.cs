using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Pure static advisor: maps a phase's current state to the single most
/// actionable next CLI command an agent should run.
/// No I/O — all inputs come from the caller.
/// </summary>
public static class NextActionAdvisor
{
    /// <summary>Advice record returned for a single phase.</summary>
    public sealed record Advice(
        string PhaseId,
        string Title,
        string State,
        string Command,
        string Explanation,
        IReadOnlyList<string> Blockers,
        bool IsTerminal);

    /// <summary>
    /// Returns the next-action advice for a single phase.
    /// </summary>
    /// <param name="phase">The phase to advise on.</param>
    /// <param name="blockerDescriptions">Human-readable descriptions of open blockers, if any.</param>
    public static Advice Advise(Phase phase, IReadOnlyList<string>? blockerDescriptions = null)
    {
        var id = phase.PhaseId;
        var blockers = blockerDescriptions ?? Array.Empty<string>();

        // Open blockers override state-based advice.
        if (blockers.Count > 0)
            return new Advice(id, phase.Title, phase.State.ToString(),
                $"bfwf blocker resolve --blocker-id <BLK-NNN>",
                "Phase has open blockers. Resolve all blockers before continuing.",
                blockers,
                IsTerminal: false);

        var (cmd, explanation) = phase.State switch
        {
            PhaseState.Planned =>
                ($"bfwf phase status --phase {id} --state ReadyForImplementation",
                 "Advance to ReadyForImplementation once the phase definition is confirmed and dependencies are met."),

            PhaseState.ReadyForImplementation =>
                ($"bfwf phase status --phase {id} --state AssignedToImplementation",
                 "Assign the phase to an implementation agent, then advance to InImplementation."),

            PhaseState.AssignedToImplementation =>
                ($"bfwf phase status --phase {id} --state InImplementation",
                 "Start implementation (advance to InImplementation to begin work)."),

            PhaseState.InImplementation =>
                ($"bfwf log implementation --phase {id} --summary \"<implementation summary>\"",
                 "Implementation is in progress. Log the completed implementation to advance to ImplementationLogged."),

            PhaseState.ImplementationLogged =>
                ($"bfwf log audit --phase {id} --summary \"<audit findings>\" --passed true",
                 "Self-audit the implementation. Log the audit result to advance to AuditLogged."),

            PhaseState.AuditLogged =>
                ($"bfwf phase status --phase {id} --state ReadyForReview",
                 "Audit complete. Advance to ReadyForReview so an independent reviewer can begin."),

            PhaseState.ReadyForReview =>
                ($"bfwf phase status --phase {id} --state ReviewInProgress",
                 "Review is queued. Start the review by advancing to ReviewInProgress."),

            PhaseState.ReviewInProgress =>
                ($"bfwf log review --phase {id} --summary \"<review findings>\" --passed true",
                 "Review is in progress. Log the review result to advance to ReviewLogged."),

            PhaseState.ReviewLogged =>
                ($"bfwf phase status --phase {id} --state ReadyForTest",
                 "Review passed. Advance to ReadyForTest for validation."),

            PhaseState.ReadyForTest =>
                ($"bfwf phase status --phase {id} --state TestInProgress",
                 "Validation queued. Advance to TestInProgress and run the validation suite."),

            PhaseState.TestInProgress =>
                ($"bfwf validation log --phase {id} --type Automated --result Passed --summary \"<findings>\" --evidence '[]'",
                 "Validation in progress. Log the validation result to advance to TestLogged."),

            PhaseState.TestLogged =>
                ($"bfwf phase status --phase {id} --state Pass",
                 "Validation logged. Advance to Pass (or PassWithWarnings if SkippedNotPossible validations exist)."),

            PhaseState.RequiresFix =>
                ($"bfwf phase status --phase {id} --state FixInProgress",
                 "Reviewer requested fixes. Start the fix cycle by advancing to FixInProgress."),

            PhaseState.FixInProgress =>
                ($"bfwf log fix --phase {id} --summary \"<fix summary>\"",
                 "Fix in progress. Log the fix to advance to FixLogged."),

            PhaseState.FixLogged =>
                ($"bfwf phase status --phase {id} --state ReadyForReview",
                 "Fix complete. Re-enter the review cycle by advancing to ReadyForReview."),

            PhaseState.Blocked =>
                ($"bfwf blocker resolve --blocker-id <BLK-NNN>",
                 "Phase is explicitly blocked. Resolve the blocker to resume progress."),

            PhaseState.Pass =>
                ("(none — phase complete)",
                 "Phase has passed all gates. No further action required."),

            PhaseState.PassWithWarnings =>
                ("(none — phase complete with warnings)",
                 "Phase passed with non-blocking warnings. Review them if needed, but no gate action is required."),

            PhaseState.FailedArchitecture =>
                ("(terminal — architecture failure)",
                 "Phase failed the architecture review. Create a new phase to address the architecture concerns."),

            PhaseState.FailedCompile =>
                ("(terminal — compile failure)",
                 "Phase failed due to compile errors. Create a new fix phase or resolve compilation issues."),

            PhaseState.FailedValidation =>
                ("(terminal — validation failure)",
                 "Phase failed validation. Create a new fix phase or reopen via recovery."),

            _ =>
                ($"bfwf phase show --phase {id}",
                 "Unknown or unexpected state. Inspect the phase for details.")
        };

        bool terminal = PhaseTransitionValidator.IsTerminal(phase.State);
        return new Advice(id, phase.Title, phase.State.ToString(), cmd, explanation, Array.Empty<string>(), terminal);
    }
}
