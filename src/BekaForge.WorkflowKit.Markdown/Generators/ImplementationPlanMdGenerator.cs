using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Markdown.Generators;

public sealed class ImplementationPlanMdGenerator
{
    public string Generate(WorkflowDashboardSummary summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Project:** {summary.AssetName}  ");
        sb.AppendLine($"**Overall progress:** {summary.OverallProgressPercent}%  ");
        sb.AppendLine($"**Phases:** {summary.CompletedPhases}/{summary.TotalPhases} complete  ");
        sb.AppendLine();

        if (summary.Phases.Count == 0)
        {
            sb.AppendLine("_No phases created yet._");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("| Phase | Title | State | Progress | Agent |");
        sb.AppendLine("|---|---|---|---:|---|");

        foreach (var phase in summary.Phases)
        {
            sb.AppendLine($"| {phase.PhaseId} | {phase.Title} | {phase.StateDisplay} | {phase.ProgressPercent}% | {phase.AssignedAgent} |");
        }

        return sb.ToString().TrimEnd();
    }
}
