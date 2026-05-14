using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

public sealed class FixLogMdGenerator
{
    public string Generate(IReadOnlyList<FixRecord> records)
    {
        if (records.Count == 0)
            return "_No fix log entries yet._";

        var sb = new System.Text.StringBuilder();

        foreach (var r in records.OrderByDescending(x => x.CreatedUtc))
        {
            var title = string.IsNullOrWhiteSpace(r.Title) ? r.Summary : r.Title;

            sb.AppendLine($"## {r.FixId} - {r.PhaseId} - {title}");
            sb.AppendLine();
            sb.AppendLine($"**Date:** {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC  ");
            sb.AppendLine($"**Actor:** {r.Actor}  ");
            sb.AppendLine($"**Phase:** {r.PhaseId}  ");
            sb.AppendLine($"**Fixes review:** {r.RelatedReviewId ?? "-"}  ");
            sb.AppendLine($"**Fixes blocker:** {r.RelatedBlockerId ?? "-"}  ");
            sb.AppendLine($"**Status after fix:** {r.StatusAfterFix}  ");
            sb.AppendLine($"**Files changed:** {FormatList(r.FilesModified)}  ");
            sb.AppendLine();
            sb.AppendLine("### Problem");
            sb.AppendLine(string.IsNullOrWhiteSpace(r.Problem) ? r.Summary : r.Problem);
            sb.AppendLine();
            sb.AppendLine("### Fix");
            sb.AppendLine(r.Summary);
            sb.AppendLine();
            sb.AppendLine("### Verification");
            sb.AppendLine(string.IsNullOrWhiteSpace(r.Verification) ? "_No verification recorded._" : r.Verification);
            sb.AppendLine();
            sb.AppendLine("### Notes");
            sb.AppendLine(string.IsNullOrWhiteSpace(r.Notes) ? "_None._" : r.Notes);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "-" : string.Join(", ", values);
}
