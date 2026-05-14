using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the <c>testing-log</c> region for a per-phase markdown file.
///
/// Test records are written by UnityAssistant after running Unity integration
/// tests. Rendered newest-first.
/// </summary>
public sealed class TestingLogMdGenerator
{
    public string Generate(IReadOnlyList<TestRecord> records)
    {
        if (records.Count == 0)
            return "_No test log entries yet._";

        var sb = new System.Text.StringBuilder();

        foreach (var r in records.OrderByDescending(x => x.CreatedUtc))
        {
            string verdict = r.Passed ? "✅ PASSED" : "❌ FAILED";
            if (r.Passed && r.HasWarnings)
                verdict = "⚠️ PASSED WITH WARNINGS";

            sb.AppendLine($"### {r.TestId} — {verdict}");
            sb.AppendLine();
            sb.AppendLine($"**Actor:** {r.Actor}  ");
            sb.AppendLine($"**Date:** {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC  ");
            sb.AppendLine();
            sb.AppendLine(r.Summary);
            sb.AppendLine();

            if (r.FailedTests.Count > 0)
            {
                sb.AppendLine("**Failed tests:**");
                sb.AppendLine();
                foreach (var test in r.FailedTests)
                    sb.AppendLine($"- {test}");
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
