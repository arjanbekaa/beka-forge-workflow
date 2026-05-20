using BekaForge.WorkflowKit.Core;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class WorkflowIntegrityReportTests
{
    [Fact]
    public void WorkflowIntegritySummary_FromIssues_EmptyCollection_ReturnsZeroCounts()
    {
        var summary = WorkflowIntegritySummary.FromIssues([]);

        Assert.Equal(0, summary.TotalIssues);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.WarningCount);
        Assert.Equal(0, summary.InfoCount);
        Assert.Equal(0, summary.ReleaseBlockingCount);
        Assert.Equal(0, summary.AuthoritativeIssueCount);
        Assert.Equal(0, summary.MirrorIssueCount);
    }

    [Fact]
    public void WorkflowIntegritySummary_FromIssues_MixedSourceKinds_ReturnsExpectedCounts()
    {
        var issues = new[]
        {
            new WorkflowIntegrityIssue
            {
                Code = "MalformedJsonlLine",
                Severity = WorkflowIntegritySeverity.Error,
                Category = WorkflowIntegrityCategory.Log,
                Message = "Malformed JSONL line detected.",
                Path = ".workflowkit/logs/implementation.jsonl",
                LineNumber = 4,
                RecordId = "IMP-123",
                IsReleaseBlocking = true,
                SourceKind = WorkflowIntegritySourceKind.Authoritative
            },
            new WorkflowIntegrityIssue
            {
                Code = "MarkdownOutOfSync",
                Severity = WorkflowIntegritySeverity.Warning,
                Category = WorkflowIntegrityCategory.MarkdownMirror,
                Message = "CurrentStatus.md is stale.",
                Path = ".workflowkit/workflow/07_Status/CurrentStatus.md",
                SuggestedFix = "Run bfwf sync-markdown.",
                SourceKind = WorkflowIntegritySourceKind.RebuildableMirror
            },
            new WorkflowIntegrityIssue
            {
                Code = "ReadModelRefreshSuggested",
                Severity = WorkflowIntegritySeverity.Info,
                Category = WorkflowIntegrityCategory.ReadModel,
                Message = "Index can be rebuilt from authoritative state.",
                Path = ".workflowkit/index/workflow.db",
                SourceKind = WorkflowIntegritySourceKind.RebuildableMirror
            }
        };

        var summary = WorkflowIntegritySummary.FromIssues(issues);

        Assert.Equal(3, summary.TotalIssues);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(1, summary.InfoCount);
        Assert.Equal(1, summary.ReleaseBlockingCount);
        Assert.Equal(1, summary.AuthoritativeIssueCount);
        Assert.Equal(2, summary.MirrorIssueCount);
    }

    [Fact]
    public void WorkflowIntegrityReport_Create_ComputesSummaryAndPreservesDefaults()
    {
        var before = DateTimeOffset.UtcNow;

        var report = WorkflowIntegrityReport.Create("wf-123", []);

        var after = DateTimeOffset.UtcNow;

        Assert.Equal("wf-123", report.WorkflowId);
        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
        Assert.InRange(report.GeneratedUtc, before, after);
    }
}
