namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Deterministic progress mapping used by markdown, server summaries, and dashboard UI.
/// </summary>
public static class PhaseProgress
{
    public static int ForPhase(Phase phase)
    {
        if (IsSuccessfulTerminal(phase.State))
            return 100;

        var stateProgress = ForState(phase.State);

        // If phase has sub-phases, compute progress from sub-phase completion
        if (phase.SubPhases is { Count: > 0 })
        {
            var total = phase.SubPhases.Count;
            var completed = phase.SubPhases.Count(sp =>
                sp.Status is SubPhaseStatus.Completed or SubPhaseStatus.Deferred);
            var inProgress = phase.SubPhases.Count(sp =>
                sp.Status is SubPhaseStatus.InProgress);

            // Base progress: each completed sub-phase contributes equally
            var baseProgress = (int)((double)completed / total * 90); // 0-90%

            // In-progress sub-phases add partial credit (up to 5% per in-progress)
            var partialCredit = Math.Min(inProgress * 5, 10); // cap at 10%

            var subPhaseProgress = Math.Min(baseProgress + partialCredit, 100);

            // Planned phases should not look nearly complete just because their
            // internal checklist was filled out before implementation started.
            if (phase.State == PhaseState.Planned)
                return stateProgress;

            return Math.Max(stateProgress, subPhaseProgress);
        }

        if (phase.State == PhaseState.Blocked)
        {
            if ((phase.ValidationLogIds.Count + phase.TestLogIds.Count) > 0)
                return 95;
            if (phase.ReviewLogIds.Count > 0)
                return 75;
            if (phase.AuditLogIds.Count > 0)
                return 55;
            if (phase.ImplementationLogIds.Count > 0)
                return 45;
        }

        return stateProgress;
    }

    public static int ForState(PhaseState state) => state switch
    {
        PhaseState.Planned => 0,
        PhaseState.ReadyForImplementation => 10,
        PhaseState.AssignedToImplementation => 20,
        PhaseState.InImplementation => 35,
        PhaseState.ImplementationLogged => 45,
        PhaseState.AuditLogged => 55,
        PhaseState.ReadyForReview => 60,
        PhaseState.ReviewInProgress => 65,
        PhaseState.ReviewLogged => 75,
        PhaseState.RequiresFix => 65,
        PhaseState.FixInProgress => 65,
        PhaseState.FixLogged => 65,
        PhaseState.ReadyForTest => 80,
        PhaseState.TestInProgress => 90,
        PhaseState.TestLogged => 95,
        PhaseState.Pass => 100,
        PhaseState.PassWithWarnings => 100,
        PhaseState.Blocked => 0,
        PhaseState.FailedArchitecture => 0,
        PhaseState.FailedCompile => 0,
        PhaseState.FailedValidation => 0,
        _ => 0
    };

    public static bool IsSuccessfulTerminal(PhaseState state) =>
        state is PhaseState.Pass or PhaseState.PassWithWarnings;

    public static bool IsFailedTerminal(PhaseState state) =>
        state is PhaseState.FailedArchitecture or PhaseState.FailedCompile or PhaseState.FailedValidation;
}
