using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Reads and writes workflow.json using atomic file writes.
/// workflow.json is the authoritative top-level state for a workflow.
/// </summary>
public sealed class WorkflowStateRepository
{
    private readonly string _workflowRoot;

    public WorkflowStateRepository(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
    }

    /// <summary>
    /// Loads the workflow state from workflow.json.
    /// Throws <see cref="InvalidOperationException"/> if the file does not exist.
    /// Throws <see cref="StorageException"/> if the file is corrupt or unreadable.
    /// </summary>
    public WorkflowState Load()
    {
        var path = WorkflowLayout.WorkflowFile(_workflowRoot);

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Workflow is not initialized at '{_workflowRoot}'. Run 'bfwf init' or call workflow.initialize first.");

        try
        {
            var json = File.ReadAllText(path);
            var state = WorkflowSerializer.Deserialize<WorkflowState>(json);
            return state ?? throw new StorageException("workflow.json deserialized to null.");
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to read workflow.json at '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves the workflow state to workflow.json atomically.
    /// The previous state is never partially overwritten.
    /// </summary>
    public void Save(WorkflowState state)
    {
        var path = WorkflowLayout.WorkflowFile(_workflowRoot);
        try
        {
            var json = WorkflowSerializer.SerializeState(state);
            AtomicFileWriter.Write(path, json);
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to write workflow.json at '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>Returns true if workflow.json exists at the configured root.</summary>
    public bool Exists() => WorkflowLayout.IsInitialized(_workflowRoot);
}
