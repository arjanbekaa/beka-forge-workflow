using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>phase-contract</c> region for a per-phase markdown file.
///
/// Renders the structured PhaseContract fields so they are always in sync
/// with what is stored in workflow state, while the human author can annotate
/// everything outside the markers.
/// </summary>
public sealed class PhaseContractMdGenerator
{
    public string Generate(Phase phase, PhaseContract? contract)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Phase:** {phase.PhaseId} — {phase.Title}  ");
        sb.AppendLine($"**State:** {phase.State}  ");

        if (phase.AssignedAgent.HasValue)
            sb.AppendLine($"**Assigned agent:** {phase.AssignedAgent.Value}  ");

        if (phase.StartedUtc.HasValue)
            sb.AppendLine($"**Started:** {phase.StartedUtc.Value:yyyy-MM-dd HH:mm} UTC  ");

        if (phase.CompletedUtc.HasValue)
            sb.AppendLine($"**Completed:** {phase.CompletedUtc.Value:yyyy-MM-dd HH:mm} UTC  ");

        sb.AppendLine();

        if (contract is null)
        {
            sb.Append("_Contract not yet written._");
            return sb.ToString().TrimEnd();
        }

        AppendSection(sb, "Objective",  contract.Objective);
        AppendSection(sb, "Scope",      contract.Scope);
        AppendSection(sb, "Out of scope", contract.OutOfScope);

        AppendList(sb, "Acceptance criteria",     contract.AcceptanceCriteria);
        AppendList(sb, "Architecture constraints", contract.ArchitectureConstraints);
        AppendList(sb, "Required files / areas",  contract.RequiredFilesOrAreas);

        AppendSection(sb, "Implementation notes",   contract.ImplementationNotes);
        AppendSection(sb, "Audit requirements",     contract.AuditRequirements);
        AppendSection(sb, "Unity test requirements", contract.UnityTestRequirements);
        AppendSection(sb, "Parallelization notes",  contract.ParallelizationNotes);

        if (contract.DependsOnPhaseIds.Count > 0)
            AppendList(sb, "Depends on phases", contract.DependsOnPhaseIds);

        sb.AppendLine($"**Requires Unity test:** {(contract.RequiresUnityTest ? "Yes" : "No")}");

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(System.Text.StringBuilder sb, string heading, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        sb.AppendLine($"### {heading}");
        sb.AppendLine();
        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static void AppendList(System.Text.StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine($"### {heading}");
        sb.AppendLine();
        foreach (var item in items)
            sb.AppendLine($"- {item}");
        sb.AppendLine();
    }
}
