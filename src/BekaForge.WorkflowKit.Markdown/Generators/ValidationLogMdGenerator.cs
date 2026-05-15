using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>validation-log</c> region for a per-phase markdown file.
///
/// Validation records are written by any agent performing or requesting validation.
/// Rendered newest-first. Includes evidence, skip reasons, and manual steps.
/// </summary>
public sealed class ValidationLogMdGenerator
{
    public string Generate(IReadOnlyList<ValidationRecord> records)
    {
        if (records.Count == 0)
            return "_No validation log entries yet._";

        var sb = new System.Text.StringBuilder();

        foreach (var r in records.OrderByDescending(x => x.CreatedUtc))
        {
            string verdict = r.ValidationResult switch
            {
                ValidationResult.Passed => "\u2705 PASSED",
                ValidationResult.PassedWithWarnings => "\u26a0\ufe0f PASSED WITH WARNINGS",
                ValidationResult.Failed => "\u274c FAILED",
                ValidationResult.Skipped => "\u23ed\ufe0f SKIPPED",
                ValidationResult.PendingUser => "\u23f3 PENDING USER",
                _ => r.ValidationResult.ToString()
            };

            sb.AppendLine($"### {r.ValidationId} \u2014 {verdict}");
            sb.AppendLine();
            sb.AppendLine($"**Type:** {r.ValidationType}  ");
            sb.AppendLine($"**Actor:** {r.Actor}  ");
            sb.AppendLine($"**Date:** {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC  ");
            sb.AppendLine();
            sb.AppendLine(r.Summary);
            sb.AppendLine();

            // Evidence items
            if (r.EvidenceItems.Count > 0)
            {
                sb.AppendLine("**Evidence:**");
                sb.AppendLine();
                foreach (var ev in r.EvidenceItems)
                {
                    sb.AppendLine($"- {ev.Description} _(source: {ev.Source})_");
                    if (!string.IsNullOrWhiteSpace(ev.Reference))
                        sb.AppendLine($"  Reference: {ev.Reference}");
                }
                sb.AppendLine();
            }

            // Command output
            if (!string.IsNullOrWhiteSpace(r.Command))
            {
                sb.AppendLine($"**Command:** `{r.Command}`  ");
                if (r.ExitCode.HasValue)
                    sb.AppendLine($"**Exit code:** {r.ExitCode}  ");
                sb.AppendLine();
            }

            // Failed checks
            if (r.FailedChecks.Count > 0)
            {
                sb.AppendLine("**Failed checks:**");
                sb.AppendLine();
                foreach (var check in r.FailedChecks)
                    sb.AppendLine($"- {check}");
                sb.AppendLine();
            }

            // Manual steps
            if (r.ManualSteps.Count > 0)
            {
                sb.AppendLine("**Manual test steps:**");
                sb.AppendLine();
                int stepNum = 1;
                foreach (var step in r.ManualSteps)
                {
                    sb.AppendLine($"{stepNum}. {step}");
                    stepNum++;
                }
                sb.AppendLine();
            }

            // Skip reason
            if (!string.IsNullOrWhiteSpace(r.SkipReason))
            {
                sb.AppendLine($"**Skip reason:** {r.SkipReason}  ");
                sb.AppendLine();
            }

            // Approved by
            if (!string.IsNullOrWhiteSpace(r.ApprovedBy))
            {
                sb.AppendLine($"**Approved by:** {r.ApprovedBy}  ");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(r.Notes))
            {
                sb.AppendLine($"**Notes:** {r.Notes}  ");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
