using System.Linq;

namespace BekaForge.WorkflowKit.Core;

public enum WorkflowIntegritySeverity
{
    Error,
    Warning,
    Info
}

public enum WorkflowIntegrityCategory
{
    Registry,
    Log,
    EvidenceReference,
    MarkdownMirror,
    ReadModel,
    OperationMetadata,
    Other
}

public enum WorkflowIntegritySourceKind
{
    Authoritative,
    RebuildableMirror
}

public sealed record WorkflowIntegrityIssue
{
    public required string Code { get; init; }
    public WorkflowIntegritySeverity Severity { get; init; } = WorkflowIntegritySeverity.Error;
    public WorkflowIntegrityCategory Category { get; init; } = WorkflowIntegrityCategory.Other;
    public required string Message { get; init; }
    public string? Path { get; init; }
    public int? LineNumber { get; init; }
    public string? PhaseId { get; init; }
    public string? EntityId { get; init; }
    public string? RecordId { get; init; }
    public string? SuggestedFix { get; init; }
    public bool IsReleaseBlocking { get; init; }
    public WorkflowIntegritySourceKind SourceKind { get; init; } = WorkflowIntegritySourceKind.Authoritative;
}

public sealed record WorkflowIntegritySummary
{
    public int TotalIssues { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public int ReleaseBlockingCount { get; init; }
    public int AuthoritativeIssueCount { get; init; }
    public int MirrorIssueCount { get; init; }

    public static WorkflowIntegritySummary FromIssues(IEnumerable<WorkflowIntegrityIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var materialized = issues as IReadOnlyCollection<WorkflowIntegrityIssue> ?? issues.ToArray();
        return new WorkflowIntegritySummary
        {
            TotalIssues = materialized.Count,
            ErrorCount = materialized.Count(issue => issue.Severity == WorkflowIntegritySeverity.Error),
            WarningCount = materialized.Count(issue => issue.Severity == WorkflowIntegritySeverity.Warning),
            InfoCount = materialized.Count(issue => issue.Severity == WorkflowIntegritySeverity.Info),
            ReleaseBlockingCount = materialized.Count(issue => issue.IsReleaseBlocking),
            AuthoritativeIssueCount = materialized.Count(issue => issue.SourceKind == WorkflowIntegritySourceKind.Authoritative),
            MirrorIssueCount = materialized.Count(issue => issue.SourceKind == WorkflowIntegritySourceKind.RebuildableMirror)
        };
    }
}

public sealed record WorkflowIntegrityReport
{
    public string? WorkflowId { get; init; }
    public IReadOnlyList<WorkflowIntegrityIssue> Issues { get; init; } = [];
    public WorkflowIntegritySummary Summary { get; init; } = new();
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    public static WorkflowIntegrityReport Create(
        string? workflowId,
        IEnumerable<WorkflowIntegrityIssue> issues,
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var materialized = issues as IReadOnlyList<WorkflowIntegrityIssue> ?? issues.ToArray();
        return new WorkflowIntegrityReport
        {
            WorkflowId = workflowId,
            Issues = materialized,
            Summary = WorkflowIntegritySummary.FromIssues(materialized),
            GeneratedUtc = generatedUtc ?? DateTimeOffset.UtcNow
        };
    }
}

public sealed record WorkflowReleaseGateResult
{
    public required WorkflowIntegrityReport Report { get; init; }
    public required bool Passed { get; init; }
    public required int BlockingIssueCount { get; init; }
}
