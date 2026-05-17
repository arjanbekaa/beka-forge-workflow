using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Tracks and persists the next sequential number for each entity ID type.
/// Persisted atomically in .workflowkit/sequences.json.
///
/// ID types tracked: Phase, Implementation, Audit, Review, Validation, Test (legacy),
/// Fix, Blocker, Handoff, Timing, Event.
///
/// Each call to a Next* method increments the counter and saves immediately.
/// This ensures that even if the process crashes between operations, IDs
/// are never reused.
/// </summary>
public sealed class IdSequenceStore
{
    private const string PhaseKey          = "phase";
    private const string ImplementationKey = "implementation";
    private const string AuditKey          = "audit";
    private const string ReviewKey         = "review";
    private const string ValidationKey     = "validation";
    private const string TestKey           = "test";
    private const string FixKey            = "fix";
    private const string BlockerKey        = "blocker";
    private const string HandoffKey        = "handoff";
    private const string TimingKey         = "timing";
    private const string EventKey          = "event";

    private readonly string _filePath;
    private Dictionary<string, int> _sequences;

    public IdSequenceStore(string workflowRoot)
    {
        _filePath = WorkflowLayout.SequencesFile(workflowRoot);
        _sequences = Load();
    }

    // -- Next ID methods -----------------------------------------------------------

    public string NextPhaseId()          => Next(PhaseKey,          WorkflowIdFormatter.Phase);
    public string NextImplementationId() => Next(ImplementationKey, WorkflowIdFormatter.Implementation);
    public string NextAuditId()          => Next(AuditKey,          WorkflowIdFormatter.Audit);
    public string NextReviewId()         => Next(ReviewKey,         WorkflowIdFormatter.Review);
    public string NextValidationId()     => Next(ValidationKey,     WorkflowIdFormatter.Validation);
    public string NextTestId()           => Next(TestKey,           WorkflowIdFormatter.Test);
    public string NextFixId()            => Next(FixKey,            WorkflowIdFormatter.Fix);
    public string NextBlockerId()        => Next(BlockerKey,        WorkflowIdFormatter.Blocker);
    public string NextHandoffId()        => Next(HandoffKey,        WorkflowIdFormatter.Handoff);
    public string NextTimingId()         => Next(TimingKey,         WorkflowIdFormatter.Timing);
    public string NextEventId()          => Next(EventKey,          WorkflowIdFormatter.Event);

    /// <summary>Returns the current (last used) number for a given key, or 0 if not yet used.</summary>
    public int CurrentNumber(string key) =>
        _sequences.TryGetValue(key, out var n) ? n : 0;

    /// <summary>Ensures the stored phase sequence is at least the provided number.</summary>
    public void EnsurePhaseAtLeast(int number) => EnsureAtLeast(PhaseKey, number);

    // -- Internal -----------------------------------------------------------------

    private string Next(string key, Func<int, string> formatter)
    {
        _sequences.TryGetValue(key, out var current);
        var next = current + 1;
        _sequences[key] = next;
        Save();
        return formatter(next);
    }

    private void EnsureAtLeast(string key, int number)
    {
        if (number <= 0)
            return;

        _sequences.TryGetValue(key, out var current);
        if (number <= current)
            return;

        _sequences[key] = number;
        Save();
    }

    private Dictionary<string, int> Load()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = WorkflowSerializer.Deserialize<Dictionary<string, int>>(json);
            return loaded ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // If sequences.json is corrupt, start fresh.
            // This is safer than crashing — IDs will still be unique going forward.
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        var json = WorkflowSerializer.SerializeState(_sequences);
        AtomicFileWriter.Write(_filePath, json);
    }
}
