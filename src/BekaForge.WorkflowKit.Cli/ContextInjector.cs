using System.Text;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Generates a role-specific context injection block for AI agents.
///
/// The output is a structured Markdown document that an agent can use as its
/// system prompt or context prefix. It contains only the information that is
/// relevant to the given role on the given phase, not the entire workflow.
///
/// No I/O - all data is provided by the caller.
/// </summary>
public static class ContextInjector
{
    /// <summary>
    /// Generates a Markdown context injection block for the given role and phase.
    /// </summary>
    public static string GenerateMarkdown(
        Phase phase,
        string role,
        IReadOnlyList<Phase> allPhases,
        IReadOnlyList<WorkflowEvent> recentEvents,
        IReadOnlyList<ValidationRecord> validationRecords,
        DateTimeOffset now)
    {
        var sb = new StringBuilder();
        var roleNorm = (role ?? string.Empty).Trim().ToLowerInvariant();

        sb.AppendLine("# Beka Forge Workflow - Agent Context Injection");
        sb.AppendLine();
        sb.AppendLine($"**Phase:** {phase.PhaseId} - {phase.Title}");
        sb.AppendLine($"**Role:**  {role}");
        sb.AppendLine($"**State:** {phase.State}");
        sb.AppendLine($"**Generated:** {now:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        sb.AppendLine("## Workflow Gate");
        sb.AppendLine();
        sb.AppendLine("- Read `.workflowkit/workflow/Rules.md` before doing any work.");
        sb.AppendLine("- If you cannot read the Rules file, or you cannot use the required workflow tool calls, stop and tell the user exactly what is blocked.");
        sb.AppendLine("- Do not claim validation, audit, review, or phase progress that was not actually logged.");
        sb.AppendLine();

        if (AttentionFlagRules.HasAny(phase.AttentionFlags))
        {
            sb.AppendLine("## Attention State");
            sb.AppendLine();
            sb.AppendLine($"**Derived outcome:** {AttentionFlagRules.DeriveOutcome(phase.AttentionFlags)}");
            sb.AppendLine("**Active flags:** " + string.Join(", ", DescribeAttentionFlags(phase.AttentionFlags)));
            sb.AppendLine();
        }

        if (phase.Contract is { } contract)
        {
            sb.AppendLine("## Phase Contract");
            sb.AppendLine();
            sb.AppendLine($"**Objective:** {contract.Objective}");
            sb.AppendLine();
            sb.AppendLine($"**Scope:** {contract.Scope}");

            if (!string.IsNullOrWhiteSpace(contract.OutOfScope))
                sb.AppendLine($"\n**Out of scope:** {contract.OutOfScope}");

            if (contract.AcceptanceCriteria.Count > 0)
            {
                sb.AppendLine("\n**Acceptance criteria:**");
                foreach (var criterion in contract.AcceptanceCriteria)
                    sb.AppendLine($"- {criterion}");
            }

            if (contract.ArchitectureConstraints.Count > 0)
            {
                sb.AppendLine("\n**Architecture constraints:**");
                foreach (var constraint in contract.ArchitectureConstraints)
                    sb.AppendLine($"- {constraint}");
            }

            if (!string.IsNullOrWhiteSpace(contract.ImplementationNotes))
            {
                sb.AppendLine("\n**Implementation notes:**");
                sb.AppendLine(contract.ImplementationNotes);
            }

            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("> No contract defined for this phase. Consider adding one for clearer acceptance criteria.");
            sb.AppendLine();
        }

        sb.AppendLine("## Your Role");
        sb.AppendLine();
        sb.AppendLine("When you produce output, keep it concrete: reference the records, files, risks, evidence, and next gate implications instead of vague acknowledgements.");
        sb.AppendLine();
        switch (roleNorm)
        {
            case "planner":
            case "planning":
                sb.AppendLine("You are the **Planner**. Your job:");
                sb.AppendLine("1. Break the work into useful sub-phases before implementation starts.");
                sb.AppendLine("2. Prefer many clear, execution-ready sub-phases over a few vague ones, but do not split work mechanically.");
                sb.AppendLine("3. Record phase dependencies, sub-phase dependencies, and what can run in parallel.");
                sb.AppendLine("4. If multi-agent execution is possible, define structured execution lanes with clear ownership boundaries and note shared write-area risks.");
                sb.AppendLine("5. Save planning updates through workflow fields only: phase dependencies, contract dependency notes, parallelization notes, execution lanes, and sub-phases.");
                sb.AppendLine($"6. Use `bfwf phase update --phase {phase.PhaseId} --sub-phases-json \"[...]\" --execution-lanes-json \"[...]\"` to save the breakdown when the structure changes.");
                break;

            case "implementer":
            case "implementation":
                sb.AppendLine("You are the **Implementer**. Your job:");
                sb.AppendLine("1. Implement the phase according to the contract above.");
                sb.AppendLine($"2. When done, log your implementation: `bfwf log implementation --phase {phase.PhaseId} --summary \"...\"`.");
                sb.AppendLine($"3. Self-audit your work with real findings: `bfwf log audit --phase {phase.PhaseId} --summary \"...\" --passed true|false --issues \"issue 1; issue 2\" --recommendations \"rec 1; rec 2\" --notes \"critical parts and risks\"`.");
                sb.AppendLine($"4. If validation is required and you cannot run it, use `bfwf validation request-user --phase {phase.PhaseId}` instead of claiming a pass.");
                if (phase.Contract?.RequiredFilesOrAreas.Count > 0)
                {
                    sb.AppendLine("\n**Files/areas you are expected to modify:**");
                    foreach (var area in phase.Contract.RequiredFilesOrAreas)
                        sb.AppendLine($"- `{area}`");
                }
                break;

            case "auditor":
            case "audit":
                sb.AppendLine("You are the **Auditor**. Your job:");
                sb.AppendLine("1. Review the implementation against the contract and acceptance criteria for correctness and completeness.");
                sb.AppendLine("2. Record the critical parts you inspected, the main risks you see, any concrete issues, and any recommendations.");
                sb.AppendLine("3. Ask whether unresolved findings should be fixed now or explicitly accepted before the phase proceeds.");
                sb.AppendLine($"4. Log the audit with real findings: `bfwf log audit --phase {phase.PhaseId} --summary \"...\" --passed true|false --issues \"issue 1; issue 2\" --recommendations \"rec 1; rec 2\" --notes \"critical parts and risks\"`.");
                if (phase.Contract?.AuditRequirements is { Length: > 0 } auditRequirements)
                {
                    sb.AppendLine("\n**Specific audit requirements:**");
                    sb.AppendLine(auditRequirements);
                }
                break;

            case "reviewer":
            case "review":
                sb.AppendLine("You are the **Reviewer**. Your job:");
                sb.AppendLine("1. Independently review this phase for architectural soundness, safety, and completeness.");
                sb.AppendLine("2. Record the critical parts you inspected, the main risks you see, any concrete issues, and any recommendations.");
                sb.AppendLine("3. Check the audit log to understand what the implementer verified.");
                sb.AppendLine("4. Make an explicit gate decision: pass, or requires fix.");
                sb.AppendLine("5. If unresolved findings remain, ask whether they should be fixed now or accepted and passed with those findings recorded.");
                sb.AppendLine($"6. Log the review with real findings: `bfwf log review --phase {phase.PhaseId} --summary \"...\" --passed true|false --requires-fix true|false --issues \"issue 1; issue 2\" --recommendations \"rec 1; rec 2\" --notes \"critical parts and risks\"`.");
                break;

            case "validator":
            case "validation":
                sb.AppendLine("You are the **Validator**. Your job:");
                sb.AppendLine("1. Run automated and/or manual validation against the phase's acceptance criteria.");
                sb.AppendLine($"2. Use `bfwf validation run --phase {phase.PhaseId} --command \"<test command>\"` for automated checks.");
                sb.AppendLine($"3. Log each validation: `bfwf validation log --phase {phase.PhaseId} --type <type> --result <result> --summary \"...\" --evidence '[...]'`.");
                sb.AppendLine($"4. If the validation requires a human or cannot be run here, use `bfwf validation request-user --phase {phase.PhaseId}` instead of claiming success.");
                if (phase.Contract?.ValidationRequirements is { Length: > 0 } validationRequirementsText)
                {
                    sb.AppendLine("\n**Validation requirements:**");
                    sb.AppendLine(validationRequirementsText);
                }
                break;
        }
        sb.AppendLine();

        var hasPlanningStructure = roleNorm is "planner" or "planning"
            || phase.SubPhases.Count > 0
            || phase.Dependencies.Count > 0
            || phase.Contract is { } plannerContract &&
               (!string.IsNullOrWhiteSpace(plannerContract.ParallelizationNotes)
                || plannerContract.DependsOnPhaseIds.Count > 0
                || plannerContract.ExecutionLanes.Count > 0);

        if (hasPlanningStructure)
        {
            sb.AppendLine("## Planning Structure");
            sb.AppendLine();

            if (phase.Dependencies.Count > 0)
            {
                sb.AppendLine("**Phase dependencies:**");
                foreach (var depId in phase.Dependencies)
                    sb.AppendLine($"- {DescribeDependency(depId, allPhases)}");
                sb.AppendLine();
            }

            if (phase.Contract?.DependsOnPhaseIds.Count > 0)
            {
                sb.AppendLine("**Contract dependency order:**");
                foreach (var depId in phase.Contract.DependsOnPhaseIds)
                    sb.AppendLine($"- {DescribeDependency(depId, allPhases)}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(phase.Contract?.ParallelizationNotes))
            {
                sb.AppendLine("**Parallelization notes:**");
                sb.AppendLine(phase.Contract.ParallelizationNotes);
                sb.AppendLine();
            }

            if (phase.Contract?.ExecutionLanes.Count > 0)
            {
                sb.AppendLine("**Execution lanes:**");
                foreach (var lane in phase.Contract.ExecutionLanes)
                {
                    sb.AppendLine($"- `{lane.LaneId}` {lane.Title}");
                    if (!string.IsNullOrWhiteSpace(lane.Summary))
                        sb.AppendLine($"  Summary: {lane.Summary}");
                    if (lane.PhaseIds.Count > 0)
                        sb.AppendLine($"  Phases: {string.Join(", ", lane.PhaseIds.Select(id => $"`{id}`"))}");
                    if (lane.SubPhaseIds.Count > 0)
                        sb.AppendLine($"  Sub-phases: {string.Join(", ", lane.SubPhaseIds.Select(id => $"`{id}`"))}");
                    if (lane.DependsOnLaneIds.Count > 0)
                        sb.AppendLine($"  Depends on lanes: {string.Join(", ", lane.DependsOnLaneIds.Select(id => $"`{id}`"))}");
                    if (lane.OwnedAreas.Count > 0)
                        sb.AppendLine($"  Owned areas: {string.Join(", ", lane.OwnedAreas.Select(area => $"`{area}`"))}");
                    if (!string.IsNullOrWhiteSpace(lane.CoordinationNotes))
                        sb.AppendLine($"  Coordination: {lane.CoordinationNotes}");
                }
                sb.AppendLine();
            }
            else if (roleNorm is "planner" or "planning")
            {
                sb.AppendLine("- No execution lanes are recorded yet. Add them when work can be split into safe parallel ownership lanes.");
                sb.AppendLine();
            }

            if (phase.SubPhases.Count > 0)
            {
                sb.AppendLine("**Current sub-phases:**");
                foreach (var subPhase in phase.SubPhases)
                {
                    var deps = subPhase.DependsOn.Count == 0
                        ? "no sub-phase dependencies"
                        : $"depends on {string.Join(", ", subPhase.DependsOn)}";
                    sb.AppendLine($"- `{subPhase.SubPhaseId}` {subPhase.Title} [{subPhase.Status}] - {deps}");
                }
                sb.AppendLine();
            }
            else if (roleNorm is "planner" or "planning")
            {
                sb.AppendLine("- No sub-phases exist yet. Create them before implementation starts.");
                sb.AppendLine();
            }
        }

        var advice = NextActionAdvisor.Advise(phase);
        sb.AppendLine("## Next Action");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(advice.Command);
        sb.AppendLine("```");
        sb.AppendLine($"_{advice.Explanation}_");
        sb.AppendLine();

        var phaseEvents = recentEvents
            .Where(e => string.Equals(e.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .Take(5)
            .ToList();

        if (phaseEvents.Count > 0)
        {
            sb.AppendLine("## Recent Activity");
            sb.AppendLine();
            foreach (var evt in phaseEvents)
                sb.AppendLine($"- `{evt.Timestamp:MM-dd HH:mm}` [{evt.Actor}] {evt.Summary}");
            sb.AppendLine();
        }

        var phaseValidations = validationRecords
            .Where(v => string.Equals(v.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.CreatedUtc)
            .Take(5)
            .ToList();

        if (phaseValidations.Count > 0 && roleNorm is "validator" or "validation")
        {
            sb.AppendLine("## Previous Validation Records");
            sb.AppendLine();
            foreach (var validation in phaseValidations)
                sb.AppendLine($"- `{validation.ValidationId}` {validation.ValidationType}: {validation.ValidationResult} - {validation.Summary}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("_This context block was generated by `bfwf context inject`. Do not modify this section manually._");

        return sb.ToString();
    }

    private static string DescribeDependency(string depId, IReadOnlyList<Phase> allPhases)
    {
        var dep = allPhases.FirstOrDefault(p =>
            string.Equals(p.PhaseId, depId, StringComparison.OrdinalIgnoreCase));

        if (dep is null)
            return $"`{depId}` - missing from workflow";

        return $"`{depId}` - {dep.Title} ({dep.State})";
    }

    private static IEnumerable<string> DescribeAttentionFlags(AttentionFlagsSnapshot flags)
    {
        if (flags.HumanValidationRequired) yield return "HumanValidationRequired";
        if (flags.TestsNotRunnable) yield return "TestsNotRunnable";
        if (flags.ManualReviewRequired) yield return "ManualReviewRequired";
        if (flags.ExternalToolRequired) yield return "ExternalToolRequired";
        if (flags.MaxAgentAttemptsReached) yield return "MaxAgentAttemptsReached";
        if (flags.UnresolvedRisk) yield return "UnresolvedRisk";
        if (flags.BlockedByUser) yield return "BlockedByUser";
        if (flags.BlockedByEnvironment) yield return "BlockedByEnvironment";
    }
}
