using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>workflow-overview</c> region for workflow.md (the top-level doc).
///
/// Produces a phase summary table showing every phase with its current state,
/// assigned agent, and short title.
/// </summary>
public sealed class WorkflowMdGenerator
{
    public string Generate(WorkflowState workflow, IReadOnlyList<Phase> phases)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Asset:** {workflow.AssetName}  ");
        sb.AppendLine($"**Workflow ID:** {workflow.WorkflowId}  ");
        sb.AppendLine($"**Open blockers:** {workflow.OpenBlockerCount}  ");
        sb.AppendLine($"**Schema version:** {workflow.SchemaVersion}");
        sb.AppendLine();

        if (phases.Count == 0)
        {
            sb.Append("_No phases created yet._");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("| Phase | Title | State | Progress | Agent |");
        sb.AppendLine("|---|---|---|---:|---|");

        foreach (var phase in phases)
        {
            string agent = phase.AssignedAgent?.ToString() ?? "—";
            sb.AppendLine($"| {phase.PhaseId} | {phase.Title} | {phase.State} | {PhaseProgress.ForPhase(phase)}% | {agent} |");
        }

        // Remove trailing newline — Wrap() adds its own newline handling.
        return sb.ToString().TrimEnd();
    }
}
