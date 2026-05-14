namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// Configuration for the developer trace system.
/// Stored in-memory; can be persisted in the future.
/// </summary>
public sealed record TraceOptions
{
    /// <summary>Trace recording mode.</summary>
    public TraceMode Mode { get; init; } = TraceMode.Basic;

    /// <summary>Maximum number of days to retain trace files.</summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>Maximum size of the traces directory in bytes before cleanup triggers.</summary>
    public long MaxDirectorySizeBytes { get; init; } = 100 * 1024 * 1024; // 100 MB

    /// <summary>Maximum length for any metadata string value before truncation.</summary>
    public int MaxMetadataValueLength { get; init; } = 512;

    /// <summary>Whether to record full operation input summaries (may contain prompt-like text).</summary>
    public bool IncludeInputSummaries { get; init; } = true;

    /// <summary>Whether to record file paths touched by operations.</summary>
    public bool IncludeSourcePaths { get; init; } = true;

    public static TraceOptions Default => new();
}

/// <summary>
/// Trace verbosity level.
/// </summary>
public enum TraceMode
{
    /// <summary>No traces are recorded.</summary>
    Off,

    /// <summary>Logs operation start/end, duration, success/failure, cache hit/miss, writes, and errors.</summary>
    Basic,

    /// <summary>Additionally logs internal context decisions, index query details, file slice pointers, invalidation decisions.</summary>
    Verbose
}
