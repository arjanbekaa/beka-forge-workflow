using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>review-log</c> region for a per-phase markdown file.
///
/// Review records are written after completing an independent review.
/// Rendered newest-first.
/// </summary>
public sealed class ReviewLogMdGenerator
{
    public string Generate(IReadOnlyList<ReviewRecord> records)
    {
        if (records.Count == 0)
            return "_No review log entries yet._";

        var sb = new System.Text.StringBuilder();

        foreach (var r in records.OrderByDescending(x => x.CreatedUtc))
        {
            string verdict = r.Passed ? "✅ APPROVED" : "❌ CHANGES REQUESTED";

            sb.AppendLine($"### {r.ReviewId} — {verdict}");
            sb.AppendLine();
            sb.AppendLine($"**Reviewer:** {r.Actor}  ");
            sb.AppendLine($"**Date:** {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC  ");

            if (r.RequiresFix)
                sb.AppendLine("**Requires fix:** Yes  ");

            sb.AppendLine();
            sb.AppendLine(r.Summary);
            sb.AppendLine();

            if (r.Issues.Count > 0)
            {
                sb.AppendLine("**Issues:**");
                sb.AppendLine();
                foreach (var issue in r.Issues)
                    sb.AppendLine($"- {issue}");
                sb.AppendLine();
            }

            if (r.Recommendations.Count > 0)
            {
                sb.AppendLine("**Recommendations:**");
                sb.AppendLine();
                foreach (var recommendation in r.Recommendations)
                    sb.AppendLine($"- {recommendation}");
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
