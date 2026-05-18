using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Markdown.Generators;

public sealed class DashboardStatusMdGenerator
{
    public string Generate(WorkflowDashboardSummary summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Project:** {summary.AssetName}  ");
        sb.AppendLine($"**Current phase:** {summary.CurrentPhaseId ?? "(none)"}  ");
        sb.AppendLine($"**Last status:** {summary.LastStatusDisplay}  ");
        sb.AppendLine($"**Overall progress:** {summary.OverallProgressPercent}%  ");
        sb.AppendLine($"**Completed phases:** {summary.CompletedPhases}/{summary.TotalPhases}  ");
        sb.AppendLine($"**Blocked phases:** {summary.BlockedPhases}  ");
        sb.AppendLine($"**Failed phases:** {summary.FailedPhases}  ");
        sb.AppendLine($"**Active orchestration sessions:** {summary.ActiveOrchestrationSessionCount}  ");
        sb.AppendLine($"**Updated:** {summary.UpdatedUtc:yyyy-MM-dd HH:mm} UTC  ");
        sb.AppendLine();
        sb.AppendLine($"**Next action:** {summary.NextActionDescription}");
        sb.AppendLine();

        sb.AppendLine("## Phase Progress");
        sb.AppendLine();
        if (summary.Phases.Count == 0)
        {
            sb.AppendLine("_No phases created yet._");
        }
        else
        {
            sb.AppendLine("| Phase | Title | State | Orchestration | Progress |");
            sb.AppendLine("|---|---|---|---|---:|");
            foreach (var phase in summary.Phases)
            {
                var orchestration = string.IsNullOrWhiteSpace(phase.ActiveOrchestrationSessionId)
                    ? "-"
                    : $"{phase.ActiveOrchestrationSessionId} / {phase.ActiveOrchestrationAttentionOutcome ?? phase.ActiveOrchestrationSessionState ?? "active"}";
                sb.AppendLine($"| {phase.PhaseId} | {phase.Title} | {phase.StateDisplay} | {orchestration} | {phase.ProgressPercent}% |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Open Blockers");
        sb.AppendLine();
        if (summary.OpenBlockers.Count == 0)
        {
            sb.AppendLine("_No open blockers._");
        }
        else
        {
            foreach (var blocker in summary.OpenBlockers)
                sb.AppendLine($"- **{blocker.BlockerId}** ({blocker.PhaseId}) - {blocker.Reason}");
        }

        sb.AppendLine();
        sb.AppendLine("## Recent Activity");
        sb.AppendLine();
        var activity = summary.RecentEvents.Concat(summary.RecentLogs)
            .OrderByDescending(i => i.CreatedUtc)
            .Take(10)
            .ToList();
        if (activity.Count == 0)
        {
            sb.AppendLine("_No recent activity._");
        }
        else
        {
            foreach (var item in activity)
                sb.AppendLine($"- {item.CreatedUtc:yyyy-MM-dd HH:mm} UTC - {item.DisplayText}");
        }

        return sb.ToString().TrimEnd();
    }
}
