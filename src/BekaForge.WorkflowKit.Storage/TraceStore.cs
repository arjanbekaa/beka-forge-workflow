using BekaForge.WorkflowKit.Core.Tracing;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Append-only JSONL store for developer traces.
/// Traces are diagnostics only — never source of truth.
/// One file per day: traces/trace-YYYY-MM-DD.jsonl
/// </summary>
public sealed class TraceStore : ITraceStore
{
    private readonly string _workflowRoot;
    private readonly TraceOptions _options;

    public TraceStore(string workflowRoot, TraceOptions? options = null)
    {
        _workflowRoot = workflowRoot;
        _options = options ?? TraceOptions.Default;
    }

    /// <summary>Path to today's trace file.</summary>
    private string TodayTracePath()
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(tracesDir, $"trace-{today}.jsonl");
    }

    /// <summary>Appends a single trace record.</summary>
    public void Append(TraceRecord trace)
    {
        if (_options.Mode == TraceMode.Off)
            return;

        // Truncate metadata values
        var sanitized = trace with
        {
            Metadata = TruncateMetadata(trace.Metadata),
            Spans = [..trace.Spans.Select(s => s with
            {
                Metadata = TruncateMetadata(s.Metadata)
            })]
        };

        JsonlAppender.Append(TodayTracePath(), sanitized);
    }

    /// <summary>Lists all trace records from a date range.</summary>
    public IReadOnlyList<TraceRecord> List(DateTimeOffset? from = null, DateTimeOffset? to = null, int maxResults = 100)
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        if (!Directory.Exists(tracesDir))
            return [];

        var results = new List<TraceRecord>();
        var fromDate = from?.Date ?? DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays).Date;
        var toDate = to?.Date ?? DateTimeOffset.UtcNow.Date.AddDays(1).Date;

        var traceFiles = Directory.GetFiles(tracesDir, "trace-*.jsonl")
            .OrderByDescending(f => f);

        foreach (var file in traceFiles)
        {
            if (results.Count >= maxResults)
                break;

            var records = JsonlAppender.ReadAll<TraceRecord>(file);
            foreach (var rec in records)
            {
                if (rec.TimestampUtc.Date >= fromDate && rec.TimestampUtc.Date <= toDate)
                    results.Add(rec);

                if (results.Count >= maxResults)
                    break;
            }
        }

        return results.OrderByDescending(r => r.TimestampUtc).ToList();
    }

    /// <summary>Gets a single trace by ID.</summary>
    public TraceRecord? Get(string traceId)
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        if (!Directory.Exists(tracesDir))
            return null;

        foreach (var file in Directory.GetFiles(tracesDir, "trace-*.jsonl").OrderByDescending(f => f))
        {
            var records = JsonlAppender.ReadAll<TraceRecord>(file);
            var found = records.LastOrDefault(r =>
                string.Equals(r.TraceId, traceId, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>Gets the current trace options (read from in-memory, not persisted).</summary>
    public TraceOptions GetOptions() => _options;

    /// <summary>Updates trace options in-memory. Does not persist across restarts.</summary>
    public void SetOptions(TraceOptions options)
    {
        // Options are in-memory only for now.
        // We mutate the internal field via reflection to keep the API clean.
        typeof(TraceStore)
            .GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .SetValue(this, options);
    }

    /// <summary>Clears traces older than RetentionDays.</summary>
    public int ClearOldTraces()
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        if (!Directory.Exists(tracesDir))
            return 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
        var deleted = 0;

        foreach (var file in Directory.GetFiles(tracesDir, "trace-*.jsonl"))
        {
            // Parse date from filename: trace-YYYY-MM-DD.jsonl
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("trace-") &&
                DateTime.TryParseExact(name[6..], "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                if (fileDate.Date < cutoff.Date)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
        }

        return deleted;
    }

    /// <summary>Total size of the traces directory in bytes.</summary>
    public long GetDirectorySizeBytes()
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        if (!Directory.Exists(tracesDir))
            return 0;

        return Directory.GetFiles(tracesDir, "trace-*.jsonl")
            .Sum(f => new FileInfo(f).Length);
    }

    /// <summary>Number of trace files.</summary>
    public int GetFileCount()
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        if (!Directory.Exists(tracesDir))
            return 0;
        return Directory.GetFiles(tracesDir, "trace-*.jsonl").Length;
    }

    /// <summary>Number of trace records across all files.</summary>
    public int GetRecordCount()
    {
        var tracesDir = WorkflowLayout.TracesDir(_workflowRoot);
        if (!Directory.Exists(tracesDir))
            return 0;

        return Directory.GetFiles(tracesDir, "trace-*.jsonl")
            .Sum(f => JsonlAppender.ReadAll<TraceRecord>(f).Count);
    }

    private IReadOnlyDictionary<string, string> TruncateMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0 || _options.MaxMetadataValueLength <= 0)
            return metadata;

        var max = _options.MaxMetadataValueLength;
        return metadata
            .Select(kvp => kvp.Value.Length > max
                ? new KeyValuePair<string, string>(kvp.Key, kvp.Value[..max] + "...")
                : kvp)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
