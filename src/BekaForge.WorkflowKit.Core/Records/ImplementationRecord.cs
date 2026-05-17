namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records a completed implementation effort.
/// Appended to logs/implementation.jsonl.
/// </summary>
public sealed record ImplementationRecord
{
    /// <summary>Unique identifier in the format IMP-NNN.</summary>
    public required string ImplementationId { get; init; }

    /// <summary>The phase this implementation log belongs to, if phase-scoped.</summary>
    public string? PhaseId { get; init; }

    /// <summary>The agent who performed the implementation.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Summary of what was implemented.</summary>
    public required string Summary { get; init; }

    /// <summary>Short title used in generated implementation logs.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The resulting phase state after this log entry.</summary>
    public required PhaseState Status { get; init; }

    /// <summary>List of files created or modified during implementation.</summary>
    public IReadOnlyList<string> FilesModified { get; init; } = [];

    /// <summary>Detailed implementation bullet points.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>Fix records related to this implementation, if any.</summary>
    public IReadOnlyList<string> RelatedFixIds { get; init; } = [];

    /// <summary>Build validation result, e.g. passed, failed, or notRun.</summary>
    public string ValidationBuild { get; init; } = "notRun";

    /// <summary>Test validation result, e.g. passed, failed, or notRun.</summary>
    public string ValidationTests { get; init; } = "notRun";

    /// <summary>Manual validation result, e.g. passed, failed, or notRun.</summary>
    public string ValidationManualCheck { get; init; } = "notRun";

    /// <summary>Additional implementation notes.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
