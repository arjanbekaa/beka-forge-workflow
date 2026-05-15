using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// Root record for a developer trace. Represents one complete operation execution.
/// Stored as one JSON line in traces/trace-YYYY-MM-DD.jsonl.
/// Traces are diagnostics only — they are NOT source of truth.
/// </summary>
public sealed record TraceRecord
{
    /// <summary>Unique trace identifier (e.g. "TRC-001").</summary>
    public required string TraceId { get; init; }

    /// <summary>Parent trace ID for nested operations, if any.</summary>
    public string? ParentTraceId { get; init; }

    /// <summary>Root span ID for this trace.</summary>
    public required string SpanId { get; init; }

    /// <summary>Project / workflow identifier.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Workflow ID from workflow.json, if available.</summary>
    public string? WorkflowId { get; init; }

    /// <summary>Phase ID targeted by this operation, if applicable.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Sub-phase ID, if applicable.</summary>
    public string? SubPhaseId { get; init; }

    /// <summary>The workflow.* operation name (e.g. "workflow.get_relevant_context").</summary>
    public required string OperationName { get; init; }

    /// <summary>Broad category of the operation.</summary>
    public string OperationCategory { get; init; } = "unknown";

    /// <summary>The actor who initiated this operation.</summary>
    public WorkflowActor PerformedBy { get; init; } = WorkflowActor.WorkflowKit;

    /// <summary>High-level task type or intent (e.g. "implementation", "self-audit").</summary>
    public string? TaskType { get; init; }

    /// <summary>Short summary of the request intent.</summary>
    public string RequestSummary { get; init; } = string.Empty;

    /// <summary>Summary of tool inputs (truncated, no full content).</summary>
    public string? ToolInputsSummary { get; init; }

    // -- Cache ------------------------------------------------------------------

    /// <summary>Whether the operation served from the context package cache.</summary>
    public bool CacheHit { get; init; }

    /// <summary>Which cache layer was used (e.g. "phase-package", "record-summary").</summary>
    public string? CacheLayer { get; init; }

    // -- Index / slices ---------------------------------------------------------

    /// <summary>Number of SQLite index queries performed during this operation.</summary>
    public int IndexQueryCount { get; init; }

    /// <summary>Number of file slices read.</summary>
    public int FileSliceCount { get; init; }

    // -- Records ----------------------------------------------------------------

    /// <summary>Number of existing records read during the operation.</summary>
    public int RecordsRead { get; init; }

    /// <summary>Number of new records written during the operation.</summary>
    public int RecordsWritten { get; init; }

    /// <summary>Source paths touched by the operation (no content, just paths).</summary>
    public IReadOnlyList<string> SourcePathsTouched { get; init; } = [];

    // -- Timing -----------------------------------------------------------------

    /// <summary>Total duration of the operation in milliseconds.</summary>
    public long DurationMs { get; init; }

    // -- Status -----------------------------------------------------------------

    /// <summary>Final status of the operation.</summary>
    public TraceStatus Status { get; init; } = TraceStatus.Success;

    /// <summary>Error code if status is Failed (from WorkflowErrorCodes).</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Error message if status is Failed.</summary>
    public string? ErrorMessage { get; init; }

    // -- Spans ------------------------------------------------------------------

    /// <summary>Child spans for internal steps in verbose mode.</summary>
    public IReadOnlyList<TraceSpanRecord> Spans { get; init; } = [];

    // -- Timestamps -------------------------------------------------------------

    /// <summary>UTC timestamp when the trace was created.</summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Git commit SHA associated with this trace, if the operation was a git activity.</summary>
    public string? CommitSha { get; init; }

    /// <summary>Git branch name at the time this trace was recorded.</summary>
    public string? BranchName { get; init; }

    /// <summary>Key-value metadata (truncated, no full content).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    // -- Categories -------------------------------------------------------------

    public static class Categories
    {
        public const string StateRead = "state-read";
        public const string PhaseManagement = "phase-management";
        public const string RecordCreation = "record-creation";
        public const string ContextResolution = "context-resolution";
        public const string FileSlice = "file-slice";
        public const string MarkdownSync = "markdown-sync";
        public const string CacheManagement = "cache-management";
        public const string BlockerManagement = "blocker-management";
        public const string Validation = "validation";
        public const string Unknown = "unknown";
    }
}
