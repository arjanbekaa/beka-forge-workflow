namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// A child span within a trace. Represents an internal step like a cache lookup,
/// index query, or file slice read.
/// </summary>
public sealed record TraceSpanRecord
{
    /// <summary>Unique span ID within the trace.</summary>
    public required string SpanId { get; init; }

    /// <summary>Parent trace or span ID.</summary>
    public required string ParentId { get; init; }

    /// <summary>Operation or step name (e.g. "cache.lookup", "index.query", "file.slice.read").</summary>
    public required string Operation { get; init; }

    /// <summary>Category of this span.</summary>
    public string Category { get; init; } = "internal";

    /// <summary>Human-readable summary of what this span did.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Duration in milliseconds. Set when the span completes.</summary>
    public long DurationMs { get; init; }

    /// <summary>Final status of this span.</summary>
    public TraceStatus Status { get; init; } = TraceStatus.Success;

    /// <summary>Error code if status is Failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Error message if status is Failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Key-value metadata for this span (truncated).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>UTC timestamp when the span started.</summary>
    public DateTimeOffset StartedUtc { get; init; }

    /// <summary>UTC timestamp when the span ended.</summary>
    public DateTimeOffset EndedUtc { get; init; }

    /// <summary>Well-known span categories.</summary>
    public static class Categories
    {
        public const string ContextResolution = "context.resolution";
        public const string CacheLookup = "cache.lookup";
        public const string IndexQuery = "index.query";
        public const string FileSliceRead = "file.slice.read";
        public const string JsonPointerRead = "json.pointer.read";
        public const string MarkdownRegionRead = "markdown.region.read";
        public const string OperationDispatch = "operation.dispatch";
        public const string RecordAppend = "record.append";
        public const string PhaseUpdate = "phase.update";
        public const string MarkdownSync = "markdown.sync";
        public const string CacheInvalidation = "cache.invalidation";
    }
}
