using System.Linq;

namespace BekaForge.WorkflowKit.Core;

public enum DocumentationClaimStatus
{
    Planned,
    Implemented,
    Verified,
    Limited,
    Deprecated
}

public sealed record DocumentationLedgerRecord
{
    public required string RecordId { get; init; }
    public required string Title { get; init; }
    public string Summary { get; init; } = string.Empty;
    public DocumentationClaimStatus Status { get; init; } = DocumentationClaimStatus.Implemented;
    public IReadOnlyList<string> Claims { get; init; } = [];
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public IReadOnlyList<string> RelatedPhaseIds { get; init; } = [];
    public IReadOnlyList<string> RelatedOperationNames { get; init; } = [];
    public IReadOnlyList<string> RelatedCommands { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? VerifiedUtc { get; init; }
}

public sealed record DocumentationLedgerResult
{
    public IReadOnlyList<DocumentationLedgerRecord> Records { get; init; } = [];
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DocumentationDraftSection
{
    public required string Heading { get; init; }
    public required DocumentationClaimStatus Status { get; init; }
    public IReadOnlyList<DocumentationLedgerRecord> Records { get; init; } = [];
}

public sealed record DocumentationDraftResult
{
    public string Markdown { get; init; } = string.Empty;
    public IReadOnlyList<DocumentationDraftSection> Sections { get; init; } = [];
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum DocumentationCoverageSeverity
{
    Error,
    Warning,
    Info
}

public sealed record DocumentationCoverageIssue
{
    public required string Code { get; init; }
    public DocumentationCoverageSeverity Severity { get; init; } = DocumentationCoverageSeverity.Error;
    public required string Message { get; init; }
    public string? RecordId { get; init; }
    public string? OperationName { get; init; }
    public string? CommandName { get; init; }
    public bool IsBlocking { get; init; }
}

public sealed record DocumentationCoverageSummary
{
    public int TotalIssues { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public int BlockingIssueCount { get; init; }

    public static DocumentationCoverageSummary FromIssues(IEnumerable<DocumentationCoverageIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var materialized = issues as IReadOnlyCollection<DocumentationCoverageIssue> ?? issues.ToArray();
        return new DocumentationCoverageSummary
        {
            TotalIssues = materialized.Count,
            ErrorCount = materialized.Count(issue => issue.Severity == DocumentationCoverageSeverity.Error),
            WarningCount = materialized.Count(issue => issue.Severity == DocumentationCoverageSeverity.Warning),
            InfoCount = materialized.Count(issue => issue.Severity == DocumentationCoverageSeverity.Info),
            BlockingIssueCount = materialized.Count(issue => issue.IsBlocking)
        };
    }
}

public sealed record DocumentationCoverageReport
{
    public IReadOnlyList<DocumentationCoverageIssue> Issues { get; init; } = [];
    public DocumentationCoverageSummary Summary { get; init; } = new();
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    public static DocumentationCoverageReport Create(
        IEnumerable<DocumentationCoverageIssue> issues,
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var materialized = issues as IReadOnlyList<DocumentationCoverageIssue> ?? issues.ToArray();
        return new DocumentationCoverageReport
        {
            Issues = materialized,
            Summary = DocumentationCoverageSummary.FromIssues(materialized),
            GeneratedUtc = generatedUtc ?? DateTimeOffset.UtcNow
        };
    }
}
