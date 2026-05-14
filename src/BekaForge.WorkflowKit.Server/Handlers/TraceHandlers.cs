using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Tracing;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Returns trace system status: mode, file count, record count, directory size.</summary>
public sealed class GetTraceStatusHandler(TraceStore store, WorkflowTraceService service) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetTraceStatus;

    public OperationResult Execute(OperationContext context)
    {
        var includeCounts = context.GetBool("includeCounts", defaultValue: true);
        var status = new
        {
            Mode = service.Options.Mode.ToString(),
            RetentionDays = service.Options.RetentionDays,
            MaxDirectorySizeBytes = service.Options.MaxDirectorySizeBytes,
            FileCount = includeCounts ? (int?)store.GetFileCount() : null,
            RecordCount = includeCounts ? (int?)store.GetRecordCount() : null,
            DirectorySizeBytes = includeCounts ? (long?)store.GetDirectorySizeBytes() : null,
            CountsIncluded = includeCounts,
            IsEnabled = service.IsEnabled
        };
        return OperationResult.Ok(status);
    }
}

/// <summary>Lists recent trace records with optional filters.</summary>
public sealed class ListTracesHandler(TraceStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ListTraces;

    public OperationResult Execute(OperationContext context)
    {
        var maxResults = context.Get<int?>("maxResults") ?? 50;
        var phaseId = context.GetString("phaseId");
        var operationName = context.GetString("operation");
        var status = context.GetString("status");
        var actor = context.GetString("actor");

        var traces = store.List(maxResults: Math.Min(maxResults, 500));

        // Apply filters
        var filtered = traces.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(phaseId))
            filtered = filtered.Where(t =>
                string.Equals(t.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(operationName))
            filtered = filtered.Where(t =>
                t.OperationName.Contains(operationName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<TraceStatus>(status, ignoreCase: true, out var ts))
            filtered = filtered.Where(t => t.Status == ts);
        if (!string.IsNullOrWhiteSpace(actor) &&
            Enum.TryParse<WorkflowActor>(actor, ignoreCase: true, out var act))
            filtered = filtered.Where(t => t.PerformedBy == act);

        var results = filtered.Take(maxResults).ToList();
        return OperationResult.Ok(new { traces = results, count = results.Count });
    }
}

/// <summary>Gets a single trace by ID with full span detail.</summary>
public sealed class GetTraceHandler(TraceStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetTrace;

    public OperationResult Execute(OperationContext context)
    {
        var traceId = context.GetString("traceId");
        if (string.IsNullOrWhiteSpace(traceId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'traceId' is required.");

        var trace = store.Get(traceId);
        if (trace is null)
            return OperationResult.Fail("NotFound", $"Trace '{traceId}' not found.");

        return OperationResult.Ok(trace);
    }
}

/// <summary>Clears trace files older than the configured retention period.</summary>
public sealed class ClearOldTracesHandler(TraceStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ClearOldTraces;

    public OperationResult Execute(OperationContext context)
    {
        var deleted = store.ClearOldTraces();
        return OperationResult.Ok(new { filesDeleted = deleted });
    }
}

/// <summary>Updates trace options in-memory. Does not persist across restarts.</summary>
public sealed class SetTraceOptionsHandler(TraceStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.SetTraceOptions;

    public OperationResult Execute(OperationContext context)
    {
        var mode = context.GetString("mode");
        var retentionDays = context.Get<int?>("retentionDays");
        var maxDirSizeMb = context.Get<long?>("maxDirectorySizeBytes");

        var current = store.GetOptions();

        if (!string.IsNullOrWhiteSpace(mode))
        {
            if (!Enum.TryParse<TraceMode>(mode, ignoreCase: true, out var traceMode))
                return OperationResult.Fail("ValidationFailed",
                    $"Invalid trace mode '{mode}'. Valid values: Off, Basic, Verbose.");
            current = current with { Mode = traceMode };
        }

        if (retentionDays.HasValue)
        {
            if (retentionDays.Value < 1 || retentionDays.Value > 365)
                return OperationResult.Fail("ValidationFailed",
                    "retentionDays must be between 1 and 365.");
            current = current with { RetentionDays = retentionDays.Value };
        }

        if (maxDirSizeMb.HasValue)
        {
            if (maxDirSizeMb.Value < 1024 * 1024 || maxDirSizeMb.Value > 1024L * 1024 * 1024)
                return OperationResult.Fail("ValidationFailed",
                    "maxDirectorySizeBytes must be between 1 MB and 1 GB.");
            current = current with { MaxDirectorySizeBytes = maxDirSizeMb.Value };
        }

        store.SetOptions(current);
        return OperationResult.Ok(current);
    }
}
