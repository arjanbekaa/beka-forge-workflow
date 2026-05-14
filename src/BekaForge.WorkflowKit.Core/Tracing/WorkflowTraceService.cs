using System.Diagnostics;

namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// Service for creating and managing developer traces.
/// All traces are diagnostics only — not source of truth.
/// </summary>
public sealed class WorkflowTraceService
{
    private readonly ITraceStore _store;
    private int _traceCounter;
    private int _spanCounter;

    public WorkflowTraceService(ITraceStore store)
    {
        _store = store;
    }

    public ITraceStore Store => _store;

    /// <summary>Current trace options.</summary>
    public TraceOptions Options => _store.GetOptions();

    /// <summary>Whether tracing is enabled (not Off).</summary>
    public bool IsEnabled => Options.Mode != TraceMode.Off;

    /// <summary>Whether verbose mode is enabled.</summary>
    public bool IsVerbose => Options.Mode == TraceMode.Verbose;

    /// <summary>Starts a new trace scope for an operation.</summary>
    public WorkflowTraceScope StartOperation(string operationName,
        string? phaseId = null,
        string? taskType = null,
        string? requestSummary = null,
        WorkflowActor actor = WorkflowActor.WorkflowKit,
        string? projectId = null,
        string? workflowId = null,
        string? operationCategory = null)
    {
        var traceId = NextTraceId();
        var spanId = NextSpanId();

        var record = new TraceRecord
        {
            TraceId = traceId,
            SpanId = spanId,
            OperationName = operationName,
            OperationCategory = operationCategory ?? CategorizeOperation(operationName),
            PhaseId = phaseId,
            TaskType = taskType,
            RequestSummary = requestSummary ?? string.Empty,
            PerformedBy = actor,
            ProjectId = projectId,
            WorkflowId = workflowId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Status = TraceStatus.Success
        };

        var scope = new WorkflowTraceScope(this, record);

        // In Basic mode, write the start-of-operation trace immediately
        if (Options.Mode == TraceMode.Basic)
        {
            _store.Append(record with { DurationMs = 0, Spans = [] });
        }

        return scope;
    }

    /// <summary>Starts a child trace (for nested operations from the dispatcher).</summary>
    public WorkflowTraceScope StartChildOperation(WorkflowTraceScope? parent,
        string operationName,
        string? phaseId = null,
        string? requestSummary = null)
    {
        var scope = StartOperation(operationName, phaseId, requestSummary: requestSummary);
        if (parent is not null)
        {
            var record = scope.Record with { ParentTraceId = parent.Record.TraceId };
            typeof(WorkflowTraceScope)
                .GetField("_record", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(scope, record);
        }
        return scope;
    }

    /// <summary>Called by WorkflowTraceScope when finalizing.</summary>
    internal void FinalizeTrace(WorkflowTraceScope scope, TraceRecord finalizedRecord)
    {
        // In Basic mode, the start record was already written.
        // We write a completion record with the same TraceId.
        if (Options.Mode == TraceMode.Basic)
        {
            // Write a completion summary record (replaces-as-final in dashboard by TraceId)
            _store.Append(finalizedRecord);
        }
        else if (Options.Mode == TraceMode.Verbose)
        {
            // In Verbose, write only the final record with spans
            _store.Append(finalizedRecord);
        }
    }

    /// <summary>Lists recent traces.</summary>
    public IReadOnlyList<TraceRecord> ListRecent(int maxResults = 50) =>
        _store.List(maxResults: maxResults);

    /// <summary>Records a git activity span on the active trace scope.</summary>
    public void RecordGitSpan(WorkflowTraceScope scope, string commitSha, string branch,
        string? phaseId = null)
    {
        if (!IsVerbose || scope is null) return;

        var span = scope.AddSpan("git.activity", "git",
            $"commit {commitSha[..Math.Min(7, commitSha.Length)]} on {branch}");
        scope.CompleteSpan(span, TraceStatus.Success);
    }

    /// <summary>Gets a trace by ID.</summary>
    public TraceRecord? GetTrace(string traceId) => _store.Get(traceId);

    internal string NextTraceId()
    {
        var id = Interlocked.Increment(ref _traceCounter);
        return $"TRC-{id:D4}";
    }

    internal string NextSpanId()
    {
        var id = Interlocked.Increment(ref _spanCounter);
        return $"SPN-{id:D4}";
    }

    private static string CategorizeOperation(string operationName)
    {
        if (operationName.Contains("get_state") || operationName.Contains("get_current_phase") ||
            operationName.Contains("list_phases") || operationName.Contains("get_dashboard") ||
            operationName.Contains("get_context_bundle"))
            return TraceRecord.Categories.StateRead;

        if (operationName.Contains("phase") || operationName.Contains("start_") ||
            operationName.Contains("complete_") || operationName.Contains("update_phase"))
            return TraceRecord.Categories.PhaseManagement;

        if (operationName.Contains("implementation") || operationName.Contains("audit") ||
            operationName.Contains("review") || operationName.Contains("test") ||
            operationName.Contains("fix") || operationName.Contains("record"))
            return TraceRecord.Categories.RecordCreation;

        if (operationName.Contains("context") || operationName.Contains("relevant"))
            return TraceRecord.Categories.ContextResolution;

        if (operationName.Contains("slice") || operationName.Contains("file_") ||
            operationName.Contains("record_slice"))
            return TraceRecord.Categories.FileSlice;

        if (operationName.Contains("sync_markdown") || operationName.Contains("markdown"))
            return TraceRecord.Categories.MarkdownSync;

        if (operationName.Contains("cache"))
            return TraceRecord.Categories.CacheManagement;

        if (operationName.Contains("blocker"))
            return TraceRecord.Categories.BlockerManagement;

        if (operationName.Contains("validate"))
            return TraceRecord.Categories.Validation;

        return TraceRecord.Categories.Unknown;
    }
}
