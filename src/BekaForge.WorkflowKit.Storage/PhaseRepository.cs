using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Reads and writes per-phase JSON files under .bekaforge/phases/.
/// Each phase has its own file: PHASE-001.json, PHASE-002.json, etc.
/// All writes are atomic.
/// </summary>
public sealed class PhaseRepository
{
    private readonly string _workflowRoot;

    public PhaseRepository(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
    }

    /// <summary>
    /// Loads a phase by ID. Returns null if the phase file does not exist.
    /// Throws <see cref="StorageException"/> if the file exists but is corrupt.
    /// </summary>
    public Phase? Load(string phaseId)
    {
        var path = WorkflowLayout.PhaseFile(_workflowRoot, phaseId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return WorkflowSerializer.Deserialize<Phase>(json)
                ?? throw new StorageException($"{phaseId}.json deserialized to null.");
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageException($"Failed to read phase file '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves a phase to its JSON file atomically.
    /// Creates the phases/ directory if it does not exist.
    /// </summary>
    public void Save(Phase phase)
    {
        var path = WorkflowLayout.PhaseFile(_workflowRoot, phase.PhaseId);
        try
        {
            var json = WorkflowSerializer.SerializeState(phase);
            AtomicFileWriter.Write(path, json);
        }
        catch (Exception ex)
        {
            throw new StorageException(
                $"Failed to write phase file for '{phase.PhaseId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads all phases by reading every PHASE-*.json file in the phases/ directory.
    /// Files are returned sorted by phase number.
    /// </summary>
    public IReadOnlyList<Phase> LoadAll()
    {
        var dir = WorkflowLayout.PhasesDir(_workflowRoot);
        if (!Directory.Exists(dir))
            return [];

        var phases = new List<Phase>();
        foreach (var file in Directory.EnumerateFiles(dir, "PHASE-*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var phase = WorkflowSerializer.Deserialize<Phase>(json);
                if (phase is not null)
                    phases.Add(phase);
            }
            catch (Exception ex)
            {
                throw new StorageException(
                    $"Failed to read phase file '{file}': {ex.Message}", ex);
            }
        }

        return phases.OrderBy(p => p.PhaseNumber).ToList();
    }

    /// <summary>Returns true if a phase file exists for the given ID.</summary>
    public bool Exists(string phaseId) =>
        File.Exists(WorkflowLayout.PhaseFile(_workflowRoot, phaseId));

    /// <summary>Deletes a phase file if it exists.</summary>
    public bool Delete(string phaseId)
    {
        var path = WorkflowLayout.PhaseFile(_workflowRoot, phaseId);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }
}
