using System.Diagnostics;

namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// Disposable trace scope for a single operation.
/// On dispose, automatically finalizes the trace with duration and status.
/// If not explicitly marked, defaults to Success on dispose without exception,
/// or Failed if an exception was observed.
///
/// Usage:
/// <code>
/// using var trace = traceService.StartOperation("workflow.get_relevant_context", ...);
/// trace.AddSpan("cache.lookup", ...);
/// trace.MarkSuccess();
/// </code>
/// </summary>
public sealed class WorkflowTraceScope : IDisposable
{
    private readonly WorkflowTraceService _service;
    private TraceRecord _record;
    private readonly Stopwatch _stopwatch;
    private readonly List<TraceSpanRecord> _spans = [];
    private bool _finalized;
    private bool _disposed;

    internal WorkflowTraceScope(WorkflowTraceService service, TraceRecord record)
    {
        _service = service;
        _record = record;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>The trace record being built. Updated on finalize.</summary>
    public TraceRecord Record => _record;

    /// <summary>Adds a child span to this trace.</summary>
    public TraceSpanRecord AddSpan(string operation, string category, string summary = "",
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (_service.Options.Mode != TraceMode.Verbose)
            return null!; // Spans only recorded in verbose mode

        var span = new TraceSpanRecord
        {
            SpanId = _service.NextSpanId(),
            ParentId = _record.SpanId,
            Operation = operation,
            Category = category,
            Summary = summary,
            Status = TraceStatus.Success,
            Metadata = metadata ?? new Dictionary<string, string>(),
            StartedUtc = DateTimeOffset.UtcNow
        };
        _spans.Add(span);
        return span;
    }

    /// <summary>Completes a span with success status and duration.</summary>
    public void CompleteSpan(TraceSpanRecord span, TraceStatus status = TraceStatus.Success,
        string? errorCode = null, string? errorMessage = null)
    {
        if (_service.Options.Mode != TraceMode.Verbose || span is null)
            return;

        var idx = _spans.IndexOf(span);
        if (idx < 0) return;

        _spans[idx] = span with
        {
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            EndedUtc = DateTimeOffset.UtcNow,
            DurationMs = (long)(DateTimeOffset.UtcNow - span.StartedUtc).TotalMilliseconds
        };
    }

    /// <summary>Marks the operation as successful.</summary>
    public void MarkSuccess()
    {
        if (_finalized) return;
        Finalize(TraceStatus.Success);
    }

    /// <summary>Marks the operation as successful with warnings.</summary>
    public void MarkWarning(string? message = null)
    {
        if (_finalized) return;
        Finalize(TraceStatus.Warning, errorMessage: message);
    }

    /// <summary>Marks the operation as failed.</summary>
    public void MarkFailed(string? errorCode = null, string? errorMessage = null)
    {
        if (_finalized) return;
        Finalize(TraceStatus.Failed, errorCode, errorMessage);
    }

    /// <summary>Updates the trace with cache hit information.</summary>
    public void SetCacheHit(bool hit, string? layer = null)
    {
        _record = _record with { CacheHit = hit, CacheLayer = layer };
    }

    /// <summary>Increments index query count.</summary>
    public void IncrementIndexQueries(int count = 1)
    {
        _record = _record with { IndexQueryCount = _record.IndexQueryCount + count };
    }

    /// <summary>Increments file slice count.</summary>
    public void IncrementFileSlices(int count = 1)
    {
        _record = _record with { FileSliceCount = _record.FileSliceCount + count };
    }

    /// <summary>Sets records read/written counts.</summary>
    public void SetRecordCounts(int read, int written)
    {
        _record = _record with { RecordsRead = read, RecordsWritten = written };
    }

    /// <summary>Adds source paths touched.</summary>
    public void AddSourcePaths(IEnumerable<string> paths)
    {
        var merged = new HashSet<string>(_record.SourcePathsTouched, StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths) merged.Add(p);
        _record = _record with { SourcePathsTouched = [..merged] };
    }

    private void Finalize(TraceStatus status, string? errorCode = null, string? errorMessage = null)
    {
        _finalized = true;
        _stopwatch.Stop();

        var finalized = _record with
        {
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            DurationMs = _stopwatch.ElapsedMilliseconds,
            Spans = [.._spans],
            TimestampUtc = DateTimeOffset.UtcNow
        };

        _service.FinalizeTrace(this, finalized);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_finalized)
        {
            // If not explicitly marked, default to Success
            Finalize(TraceStatus.Success);
        }
    }
}
