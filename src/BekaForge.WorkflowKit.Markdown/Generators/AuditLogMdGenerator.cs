using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>audit-log</c> region for a per-phase markdown file.
///
/// Audit records are written by DeepSeek as self-audits before requesting
/// Codex review. Rendered newest-first.
/// </summary>
public sealed class AuditLogMdGenerator
{
    public string Generate(IReadOnlyList<AuditRecord> records)
    {
        if (records.Count == 0)
            return "_No audit log entries yet._";

        var sb = new System.Text.StringBuilder();

        foreach (var r in records.OrderByDescending(x => x.CreatedUtc))
        {
            string verdict = r.Passed ? "✅ PASSED" : "❌ FAILED";

            sb.AppendLine($"### {r.AuditId} — {verdict}");
            sb.AppendLine();
            sb.AppendLine($"**Actor:** {r.Actor}  ");
            sb.AppendLine($"**Date:** {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC  ");
            sb.AppendLine();
            sb.AppendLine(r.Summary);
            sb.AppendLine();

            if (r.Issues.Count > 0)
            {
                sb.AppendLine("**Issues found:**");
                sb.AppendLine();
                foreach (var issue in r.Issues)
                    sb.AppendLine($"- {issue}");
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
