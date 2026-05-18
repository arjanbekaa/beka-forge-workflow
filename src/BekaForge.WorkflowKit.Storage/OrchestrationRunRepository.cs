using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Reads and writes orchestration run JSON files under
/// .workflowkit/orchestration/runs/.
/// </summary>
public sealed class OrchestrationRunRepository
{
    private readonly string _workflowRoot;

    public OrchestrationRunRepository(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
    }

    public OrchestrationRun? Load(string runId)
    {
        var path = WorkflowLayout.OrchestrationRunFile(_workflowRoot, runId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return WorkflowSerializer.Deserialize<OrchestrationRun>(json)
                ?? throw new StorageException($"{runId}.json deserialized to null.");
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to read orchestration run file '{path}': {ex.Message}", ex);
        }
    }

    public IReadOnlyList<OrchestrationRun> LoadAll()
    {
        var dir = WorkflowLayout.OrchestrationRunsDir(_workflowRoot);
        if (!Directory.Exists(dir))
            return [];

        var runs = new List<OrchestrationRun>();
        foreach (var file in Directory.EnumerateFiles(dir, "ORR-*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var run = WorkflowSerializer.Deserialize<OrchestrationRun>(json);
                if (run is not null)
                    runs.Add(run);
            }
            catch (Exception ex)
            {
                throw new StorageException($"Failed to read orchestration run file '{file}': {ex.Message}", ex);
            }
        }

        return runs
            .OrderBy(r => r.StartedUtc)
            .ThenBy(r => r.RunId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save(OrchestrationRun run)
    {
        var path = WorkflowLayout.OrchestrationRunFile(_workflowRoot, run.RunId);
        try
        {
            var json = WorkflowSerializer.SerializeState(run);
            AtomicFileWriter.Write(path, json);
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to write orchestration run file for '{run.RunId}': {ex.Message}", ex);
        }
    }

    public bool Exists(string runId) =>
        File.Exists(WorkflowLayout.OrchestrationRunFile(_workflowRoot, runId));
}
