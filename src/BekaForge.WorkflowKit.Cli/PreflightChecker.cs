using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Pure preflight check engine: determines whether a phase is ready for a
/// specific role (Planner, Implementer, Auditor, Reviewer, Validator) to begin work.
///
/// No I/O — all state is provided by the caller.
/// Returns a <see cref="PreflightResult"/> with issues (blocking) and
/// warnings (advisory). Empty issues list means "clear to proceed".
/// </summary>
public static class PreflightChecker
{
    /// <summary>Valid role names (case-insensitive).</summary>
    public static readonly IReadOnlyList<string> KnownRoles =
        ["Planner", "Implementer", "Auditor", "Reviewer", "Validator"];

    /// <summary>Result of a preflight check.</summary>
    public sealed record PreflightResult(
        string PhaseId,
        string Role,
        bool Clear,
        IReadOnlyList<string> Issues,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Checks whether the phase is ready for the given role to begin work.
    /// </summary>
    /// <param name="phase">Phase under inspection.</param>
    /// <param name="role">Role string: Planner | Implementer | Auditor | Reviewer | Validator.</param>
    /// <param name="allPhases">Full phase list for dependency resolution.</param>
    /// <param name="openBlockerCount">Count of unresolved blockers on this phase.</param>
    /// <param name="hasContract">Whether the phase has a contract defined.</param>
    public static PreflightResult Check(
        Phase phase,
        string? role,
        IReadOnlyList<Phase> allPhases,
        int openBlockerCount,
        bool hasContract)
    {
        var issues = new List<string>();
        var warnings = new List<string>();
        var id = phase.PhaseId;
        var roleNorm = (role ?? "").ToLowerInvariant().Trim();

        // --- Universal: terminal states -------------------------------------------
        if (PhaseTransitionValidator.IsTerminal(phase.State))
        {
            issues.Add($"Phase is in terminal state '{phase.State}'. No further lifecycle work is possible on this phase.");
            return new PreflightResult(id, role ?? "", false, issues, warnings);
        }

        // --- Universal: open blockers ---------------------------------------------
        if (openBlockerCount > 0)
            issues.Add($"Phase has {openBlockerCount} open blocker(s). Resolve them before proceeding.");

        // --- Universal: dependency check -----------------------------------------
        foreach (var depId in phase.Dependencies)
        {
            var dep = allPhases.FirstOrDefault(p =>
                string.Equals(p.PhaseId, depId, StringComparison.OrdinalIgnoreCase));

            if (dep is null)
            {
                warnings.Add($"Dependency '{depId}' was not found in the phase list.");
                continue;
            }

            if (dep.State != PhaseState.Pass && dep.State != PhaseState.PassWithWarnings)
                issues.Add($"Dependency '{depId}' ({dep.Title}) is not complete — state: {dep.State}.");
        }

        // --- Role-specific checks -------------------------------------------------
        switch (roleNorm)
        {
            case "planner":
            case "planning":
                if (!hasContract)
                    warnings.Add(
                        "No phase contract is defined. Save one before handing the phase to implementers.");

                if (phase.SubPhases.Count == 0)
                    warnings.Add(
                        "No sub-phases are defined. Break the work into execution-ready sub-phases before implementation.");
                break;

            case "implementer":
            case "implementation":
                if (phase.State is not (PhaseState.ReadyForImplementation
                    or PhaseState.AssignedToImplementation
                    or PhaseState.InImplementation))
                    issues.Add(
                        $"For an Implementer, the phase must be ReadyForImplementation, " +
                        $"AssignedToImplementation, or InImplementation. Current: {phase.State}.");

                if (!hasContract)
                    warnings.Add(
                        "No phase contract is defined. Consider saving one with " +
                        $"`bfwf phase contract save --phase {id} --objective \"...\" --scope \"...\"` " +
                        "to lock in acceptance criteria.");
                break;

            case "auditor":
            case "audit":
                if (phase.State != PhaseState.ImplementationLogged)
                    issues.Add(
                        $"For an Auditor, the phase must be ImplementationLogged. Current: {phase.State}.");

                if (phase.ImplementationLogIds.Count == 0)
                    issues.Add(
                        "No implementation log entries found. Implementation must be logged before auditing.");
                break;

            case "reviewer":
            case "review":
                if (phase.State is not (PhaseState.AuditLogged
                    or PhaseState.ReadyForReview
                    or PhaseState.ReviewInProgress))
                    issues.Add(
                        $"For a Reviewer, the phase must be AuditLogged, ReadyForReview, or ReviewInProgress. " +
                        $"Current: {phase.State}.");

                if (phase.AuditLogIds.Count == 0)
                    issues.Add(
                        "No audit log entries found. The audit must be completed before review can begin.");
                break;

            case "validator":
            case "validation":
                if (phase.State is not (PhaseState.ReviewLogged
                    or PhaseState.ReadyForTest
                    or PhaseState.TestInProgress))
                    issues.Add(
                        $"For a Validator, the phase must be ReviewLogged, ReadyForTest, or TestInProgress. " +
                        $"Current: {phase.State}.");

                if (phase.ReviewLogIds.Count == 0)
                    issues.Add(
                        "No review log entries found. Review must be completed before validation can begin.");
                break;

            default:
                issues.Add(
                    $"Unknown role '{role}'. Valid roles: Planner, Implementer, Auditor, Reviewer, Validator.");
                break;
        }

        return new PreflightResult(id, role ?? "", issues.Count == 0, issues, warnings);
    }
}
