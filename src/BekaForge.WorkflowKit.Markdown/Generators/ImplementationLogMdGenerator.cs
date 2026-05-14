using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the implementation-log region in a stable dashboard-readable format.
/// </summary>
public sealed class ImplementationLogMdGenerator
{
    public string Generate(IReadOnlyList<ImplementationRecord> records)
    {
        if (records.Count == 0)
            return "_No implementation log entries yet._";

        var sb = new System.Text.StringBuilder();

        foreach (var r in records.OrderByDescending(x => x.CreatedUtc))
        {
            var title = string.IsNullOrWhiteSpace(r.Title) ? r.Summary : r.Title;
            var phaseLabel = string.IsNullOrWhiteSpace(r.PhaseId) ? "(workflow)" : r.PhaseId;

            sb.AppendLine($"## {r.ImplementationId} - {phaseLabel} - {title}");
            sb.AppendLine();
            sb.AppendLine($"**Date:** {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC  ");
            sb.AppendLine($"**Actor:** {r.Actor}  ");
            sb.AppendLine($"**Phase:** {phaseLabel}  ");
            sb.AppendLine($"**Status:** {r.Status}  ");
            sb.AppendLine($"**Related fixes:** {FormatList(r.RelatedFixIds)}  ");
            sb.AppendLine($"**Files changed:** {FormatList(r.FilesModified)}  ");
            sb.AppendLine();
            sb.AppendLine("### Summary");
            sb.AppendLine(r.Summary);
            sb.AppendLine();
            sb.AppendLine("### Details");
            AppendBullets(sb, r.Details);
            sb.AppendLine();
            sb.AppendLine("### Validation");
            sb.AppendLine($"- Build: {r.ValidationBuild}");
            sb.AppendLine($"- Tests: {r.ValidationTests}");
            sb.AppendLine($"- Manual check: {r.ValidationManualCheck}");
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

    private static void AppendBullets(System.Text.StringBuilder sb, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            sb.AppendLine("- No detailed bullets recorded.");
            return;
        }

        foreach (var value in values)
            sb.AppendLine($"- {value}");
    }
}
