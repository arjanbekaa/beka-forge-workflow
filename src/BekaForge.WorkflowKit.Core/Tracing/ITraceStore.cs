namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// Minimal storage contract used by WorkflowTraceService.
/// Keeps Core free of a direct dependency on the Storage assembly.
/// </summary>
public interface ITraceStore
{
    TraceOptions GetOptions();
    void Append(TraceRecord trace);
    IReadOnlyList<TraceRecord> List(DateTimeOffset? from = null, DateTimeOffset? to = null, int maxResults = 100);
    TraceRecord? Get(string traceId);
}
