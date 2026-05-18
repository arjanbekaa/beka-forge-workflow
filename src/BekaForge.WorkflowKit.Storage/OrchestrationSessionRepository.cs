using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Reads and writes orchestration session JSON files under
/// .workflowkit/orchestration/sessions/.
/// </summary>
public sealed class OrchestrationSessionRepository
{
    private readonly string _workflowRoot;

    public OrchestrationSessionRepository(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
    }

    public OrchestrationSession? Load(string sessionId)
    {
        var path = WorkflowLayout.OrchestrationSessionFile(_workflowRoot, sessionId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return WorkflowSerializer.Deserialize<OrchestrationSession>(json)
                ?? throw new StorageException($"{sessionId}.json deserialized to null.");
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to read orchestration session file '{path}': {ex.Message}", ex);
        }
    }

    public IReadOnlyList<OrchestrationSession> LoadAll()
    {
        var dir = WorkflowLayout.OrchestrationSessionsDir(_workflowRoot);
        if (!Directory.Exists(dir))
            return [];

        var sessions = new List<OrchestrationSession>();
        foreach (var file in Directory.EnumerateFiles(dir, "ORS-*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var session = WorkflowSerializer.Deserialize<OrchestrationSession>(json);
                if (session is not null)
                    sessions.Add(session);
            }
            catch (Exception ex)
            {
                throw new StorageException($"Failed to read orchestration session file '{file}': {ex.Message}", ex);
            }
        }

        return sessions
            .OrderBy(s => s.StartedUtc)
            .ThenBy(s => s.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save(OrchestrationSession session)
    {
        var path = WorkflowLayout.OrchestrationSessionFile(_workflowRoot, session.SessionId);
        try
        {
            var json = WorkflowSerializer.SerializeState(session);
            AtomicFileWriter.Write(path, json);
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to write orchestration session file for '{session.SessionId}': {ex.Message}", ex);
        }
    }

    public bool Exists(string sessionId) =>
        File.Exists(WorkflowLayout.OrchestrationSessionFile(_workflowRoot, sessionId));
}
