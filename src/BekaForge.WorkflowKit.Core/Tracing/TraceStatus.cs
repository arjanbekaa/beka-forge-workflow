namespace BekaForge.WorkflowKit.Core.Tracing;

/// <summary>
/// Outcome of a traced operation or span.
/// </summary>
public enum TraceStatus
{
    /// <summary>Operation completed without errors.</summary>
    Success,

    /// <summary>Operation completed with warnings.</summary>
    Warning,

    /// <summary>Operation failed with errors.</summary>
    Failed
}
