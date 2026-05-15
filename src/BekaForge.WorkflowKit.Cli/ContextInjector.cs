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
/// relevant to the given role on the given phase — not the entire workflow.
///
/// No I/O — all data is provided by the caller.
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
        var roleNorm = (role ?? "").ToLowerInvariant();

        sb.AppendLine("# Beka Forge Workflow — Agent Context Injection");
        sb.AppendLine();
        sb.AppendLine($"**Phase:** {phase.PhaseId} — {phase.Title}");
        sb.AppendLine($"**Role:**  {role}");
        sb.AppendLine($"**State:** {phase.State}");
        sb.AppendLine($"**Generated:** {now:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        // --- Phase contract ---
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
                foreach (var c in contract.AcceptanceCriteria)
                    sb.AppendLine($"- {c}");
            }

            if (contract.ArchitectureConstraints.Count > 0)
            {
                sb.AppendLine("\n**Architecture constraints:**");
                foreach (var c in contract.ArchitectureConstraints)
                    sb.AppendLine($"- {c}");
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
            sb.AppendLine("> ⚠️  No contract defined for this phase. Consider adding one for clearer acceptance criteria.");
            sb.AppendLine();
        }

        // --- Role-specific section ---
        sb.AppendLine("## Your Role");
        sb.AppendLine();
        switch (roleNorm)
        {
            case "implementer":
            case "implementation":
                sb.AppendLine("You are the **Implementer**. Your job:");
                sb.AppendLine("1. Implement the phase according to the contract above.");
                sb.AppendLine("2. When done, log your implementation: `bfwf log implementation --phase " + phase.PhaseId + " --summary \"...\"` (phase advances to ImplementationLogged).");
                sb.AppendLine("3. Self-audit your work: `bfwf log audit --phase " + phase.PhaseId + " --summary \"...\" --passed true`.");
                if (phase.Contract?.RequiredFilesOrAreas.Count > 0)
                {
                    sb.AppendLine("\n**Files/areas you are expected to modify:**");
                    foreach (var f in phase.Contract.RequiredFilesOrAreas)
                        sb.AppendLine($"- `{f}`");
                }
                break;

            case "auditor":
            case "audit":
                sb.AppendLine("You are the **Auditor**. Your job:");
                sb.AppendLine("1. Review the implementation against the contract and acceptance criteria for **correctness and completeness**.");
                sb.AppendLine("2. Ask: \"Is this the smartest way to implement this?\" Look for simpler alternatives, missed edge cases, or patterns that could be improved. These are **quality recommendations** — they do not block the audit from passing.");
                sb.AppendLine("3. Log the audit including any recommendations: `bfwf log audit --phase " + phase.PhaseId + " --summary \"...\" --passed true|false --recommendations \"suggestion 1; suggestion 2\"`.");
                sb.AppendLine("   > Even when passing, include at least one recommendation if you spotted anything worth improving.");
                if (phase.Contract?.AuditRequirements is { Length: > 0 } auditReqs)
                {
                    sb.AppendLine("\n**Specific audit requirements:**");
                    sb.AppendLine(auditReqs);
                }
                break;

            case "reviewer":
            case "review":
                sb.AppendLine("You are the **Reviewer**. Your job:");
                sb.AppendLine("1. Independently review this phase for **architectural soundness and completeness**.");
                sb.AppendLine("2. Ask: \"If I were building this from scratch, would I make the same design choices?\" Surface alternatives, simplifications, or quality improvements as **recommendations** — these do not block the review from passing.");
                sb.AppendLine("3. Check the audit log to understand what the implementer verified.");
                sb.AppendLine("4. Log the review including any recommendations: `bfwf log review --phase " + phase.PhaseId + " --summary \"...\" --passed true|false --recommendations \"suggestion 1; suggestion 2\"`.");
                sb.AppendLine("   > Even when passing, include at least one recommendation if you spotted a better approach.");
                break;

            case "validator":
            case "validation":
                sb.AppendLine("You are the **Validator**. Your job:");
                sb.AppendLine("1. Run automated and/or manual validation against the phase's acceptance criteria.");
                sb.AppendLine("2. Use `bfwf validation run --phase " + phase.PhaseId + " --command \"<test command>\"` for automated checks.");
                sb.AppendLine("3. Log each validation: `bfwf validation log --phase " + phase.PhaseId + " --type <type> --result <result> --summary \"...\" --evidence '[...]'`.");
                if (phase.Contract?.ValidationRequirements is { Length: > 0 } valReqs)
                {
                    sb.AppendLine("\n**Validation requirements:**");
                    sb.AppendLine(valReqs);
                }
                break;
        }
        sb.AppendLine();

        // --- Next action ---
        var advice = NextActionAdvisor.Advise(phase);
        sb.AppendLine("## Next Action");
        sb.AppendLine();
        sb.AppendLine($"```");
        sb.AppendLine(advice.Command);
        sb.AppendLine("```");
        sb.AppendLine($"_{advice.Explanation}_");
        sb.AppendLine();

        // --- Dependencies ---
        if (phase.Dependencies.Count > 0)
        {
            sb.AppendLine("## Dependencies");
            sb.AppendLine();
            foreach (var depId in phase.Dependencies)
            {
                var dep = allPhases.FirstOrDefault(p =>
                    string.Equals(p.PhaseId, depId, StringComparison.OrdinalIgnoreCase));
                var depState = dep?.State.ToString() ?? "unknown";
                var icon = dep?.State is PhaseState.Pass or PhaseState.PassWithWarnings ? "✓" : "○";
                sb.AppendLine($"- {icon} `{depId}` — {dep?.Title ?? "?"} ({depState})");
            }
            sb.AppendLine();
        }

        // --- Recent activity ---
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

        // --- Validation summary (for Validator role) ---
        var phaseValidations = validationRecords
            .Where(v => string.Equals(v.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (phaseValidations.Count > 0 && roleNorm is "validator" or "validation")
        {
            sb.AppendLine("## Previous Validation Records");
            sb.AppendLine();
            foreach (var v in phaseValidations.OrderByDescending(x => x.CreatedUtc).Take(5))
                sb.AppendLine($"- `{v.ValidationId}` {v.ValidationType}: {v.ValidationResult} — {v.Summary}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("_This context block was generated by `bfwf context inject`. Do not modify this section manually._");

        return sb.ToString();
    }
}
