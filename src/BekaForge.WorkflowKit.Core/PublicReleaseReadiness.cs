namespace BekaForge.WorkflowKit.Core;

public enum SupportStatus
{
    Verified,
    Limited,
    Unsupported,
    Unknown
}

public sealed record SupportMatrixEntry
{
    public required string Surface { get; init; }
    public required SupportStatus Status { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed record PackagingCheck
{
    public required string Name { get; init; }
    public required bool Passed { get; init; }
    public required string Message { get; init; }
    public bool IsBlocking { get; init; } = true;
}

public sealed record ReleaseCandidateReport
{
    public string? WorkflowId { get; init; }
    public required WorkflowIntegrityReport IntegrityReport { get; init; }
    public required DocumentationCoverageReport DocumentationCoverage { get; init; }
    public DocumentationPolicyMode DocumentationPolicy { get; init; } = DocumentationPolicyMode.Manual;
    public bool DocumentationCoverageBlocksRelease { get; init; }
    public IReadOnlyList<SupportMatrixEntry> SupportMatrix { get; init; } = [];
    public IReadOnlyList<PackagingCheck> PackagingChecks { get; init; } = [];
    public bool KnownLimitationsPresent { get; init; }
    public bool FinalReviewPresent { get; init; }
    public bool MigrationNotesPresent { get; init; }
    public IReadOnlyList<string> BlockingReasons { get; init; } = [];
    public IReadOnlyList<string> WarningReasons { get; init; } = [];
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PublicReleaseValidationResult
{
    public required ReleaseCandidateReport Report { get; init; }
    public required bool Passed { get; init; }
    public required int BlockingIssueCount { get; init; }
}
