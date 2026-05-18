using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>current-status</c> region for a per-phase markdown file.
///
/// Provides a snapshot of the phase's current state, next action, and open
/// blockers so the reader can orient themselves at a glance.
/// </summary>
public sealed class CurrentStatusMdGenerator
{
    public string Generate(
        Phase                      phase,
        NextAction?                nextAction,
        IReadOnlyList<BlockerRecord> openBlockers,
        string? activeOrchestrationSessionId = null,
        string? activeOrchestrationSessionState = null,
        string? activeOrchestrationAttentionOutcome = null)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Current state:** `{phase.State}`  ");

        if (!string.IsNullOrWhiteSpace(activeOrchestrationSessionId))
            sb.AppendLine($"**Active orchestration session:** `{activeOrchestrationSessionId}` ({activeOrchestrationSessionState ?? "unknown"})  ");
        if (!string.IsNullOrWhiteSpace(activeOrchestrationAttentionOutcome))
            sb.AppendLine($"**Attention outcome:** `{activeOrchestrationAttentionOutcome}`  ");

        if (nextAction is not null)
        {
            sb.AppendLine($"**Next action actor:** {nextAction.Actor}  ");
            sb.AppendLine($"**Next action:** {nextAction.Description}  ");
        }
        else
        {
            sb.AppendLine("**Next action:** _not set_  ");
        }

        sb.AppendLine();

        if (openBlockers.Count > 0)
        {
            sb.AppendLine("#### Open blockers");
            sb.AppendLine();
            foreach (var b in openBlockers)
            {
                sb.AppendLine($"- **{b.BlockerId}** — {b.Reason}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("_No open blockers._");
        }

        return sb.ToString().TrimEnd();
    }
}
