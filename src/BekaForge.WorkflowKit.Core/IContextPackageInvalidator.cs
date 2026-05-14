namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Interface that the Cache assembly implements so that WorkflowStore can
/// notify the cache when source data changes, without Storage depending on Cache.
///
/// WorkflowStore holds a nullable reference to this interface. On every write
/// path (SavePhase, AppendImplementation, etc.), it calls Invalidate() so
/// cached packages for the affected phase/record are evicted.
/// </summary>
public interface IContextPackageInvalidator
{
    /// <summary>
    /// Called by WorkflowStore after any write to phase data.
    /// The cache should evict any packages for this phase.
    /// </summary>
    void InvalidatePhase(string phaseId);

    /// <summary>
    /// Called by WorkflowStore after any append to a JSONL log.
    /// The cache should evict any packages for this phase's records.
    /// </summary>
    void InvalidateRecords(string phaseId);

    /// <summary>
    /// Called by WorkflowStore after workflow-level changes
    /// (next action, features, etc.).
    /// </summary>
    void InvalidateWorkflow();

    /// <summary>
    /// Called after any write to a file tracked by the cache.
    /// </summary>
    void InvalidateFile(string relativePath);
}
