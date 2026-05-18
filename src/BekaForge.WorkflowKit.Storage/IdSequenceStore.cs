using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using System.Security.Cryptography;
using System.Text;

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
    private const int LockRetryLimit = 200;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);
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
    private const string OrchestrationSessionKey = "orchestrationSession";
    private const string OrchestrationRunKey = "orchestrationRun";
    private const string OrchestrationGateDecisionKey = "orchestrationGateDecision";
    private const string OrchestrationRunEventKey = "orchestrationRunEvent";

    private readonly string _workflowRoot;
    private readonly string _filePath;
    private readonly string _mutexName;
    private Dictionary<string, int> _sequences;

    public IdSequenceStore(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        _filePath = WorkflowLayout.SequencesFile(workflowRoot);
        _mutexName = BuildMutexName(_filePath);
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
    public string NextOrchestrationSessionId() => Next(OrchestrationSessionKey, WorkflowIdFormatter.OrchestrationSession);
    public string NextOrchestrationRunId() => Next(OrchestrationRunKey, WorkflowIdFormatter.OrchestrationRun);
    public string NextOrchestrationGateDecisionId() => Next(OrchestrationGateDecisionKey, WorkflowIdFormatter.OrchestrationGateDecision);
    public string NextOrchestrationRunEventId() => Next(OrchestrationRunEventKey, WorkflowIdFormatter.OrchestrationRunEvent);

    /// <summary>Returns the current (last used) number for a given key, or 0 if not yet used.</summary>
    public int CurrentNumber(string key) =>
        WithExclusiveAccess(sequences =>
        {
            var current = ReconcileObservedMaximum(sequences, key, out var dirty);
            return (current, dirty);
        });

    /// <summary>Ensures the stored phase sequence is at least the provided number.</summary>
    public void EnsurePhaseAtLeast(int number) => EnsureAtLeast(PhaseKey, number);

    // -- Internal -----------------------------------------------------------------

    private string Next(string key, Func<int, string> formatter) =>
        WithExclusiveAccess(sequences =>
        {
            var current = ReconcileObservedMaximum(sequences, key, out _);
            var next = current + 1;
            sequences[key] = next;
            return (formatter(next), true);
        });

    private void EnsureAtLeast(string key, int number)
    {
        if (number <= 0)
            return;

        WithExclusiveAccess<object?>(sequences =>
        {
            var current = ReconcileObservedMaximum(sequences, key, out var dirty);
            if (number <= current)
                return (null, dirty);

            sequences[key] = number;
            return (null, true);
        });
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

    private void Save(Dictionary<string, int> sequences)
    {
        var json = WorkflowSerializer.SerializeState(sequences);
        AtomicFileWriter.Write(_filePath, json);
    }

    private T WithExclusiveAccess<T>(Func<Dictionary<string, int>, (T Result, bool Dirty)> action)
    {
        using var mutex = AcquireLock();
        try
        {
            var sequences = Load();
            var (result, dirty) = action(sequences);
            if (dirty)
                Save(sequences);

            _sequences = sequences;
            return result;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private Mutex AcquireLock()
    {
        var mutex = new Mutex(false, _mutexName);

        for (var attempt = 0; attempt < LockRetryLimit; attempt++)
        {
            try
            {
                if (mutex.WaitOne(LockRetryDelay))
                    return mutex;
            }
            catch (AbandonedMutexException)
            {
                return mutex;
            }
        }

        mutex.Dispose();
        throw new IOException($"Could not acquire workflow sequence mutex '{_mutexName}'.");
    }

    private int ReconcileObservedMaximum(Dictionary<string, int> sequences, string key, out bool dirty)
    {
        dirty = false;
        sequences.TryGetValue(key, out var stored);
        var observed = GetObservedMaximum(key);
        if (observed > stored)
        {
            sequences[key] = observed;
            dirty = true;
            return observed;
        }

        return stored;
    }

    private int GetObservedMaximum(string key) => key switch
    {
        PhaseKey => GetObservedPhaseMaximum(),
        ImplementationKey => GetObservedMaximum(WorkflowLayout.ImplementationLog(_workflowRoot), static (ImplementationRecord record) => record.ImplementationId),
        AuditKey => GetObservedMaximum(WorkflowLayout.AuditLog(_workflowRoot), static (AuditRecord record) => record.AuditId),
        ReviewKey => GetObservedMaximum(WorkflowLayout.ReviewLog(_workflowRoot), static (ReviewRecord record) => record.ReviewId),
        ValidationKey => GetObservedMaximum(WorkflowLayout.ValidationLog(_workflowRoot), static (ValidationRecord record) => record.ValidationId),
        TestKey => GetObservedMaximum(WorkflowLayout.TestLog(_workflowRoot), static (TestRecord record) => record.TestId),
        FixKey => GetObservedMaximum(WorkflowLayout.FixLog(_workflowRoot), static (FixRecord record) => record.FixId),
        BlockerKey => GetObservedMaximum(WorkflowLayout.BlockersLog(_workflowRoot), static (BlockerRecord record) => record.BlockerId),
        HandoffKey => GetObservedMaximum(WorkflowLayout.HandoffsLog(_workflowRoot), static (HandoffRecord record) => record.HandoffId),
        TimingKey => GetObservedMaximum(WorkflowLayout.TimingLog(_workflowRoot), static (TimingRecord record) => record.TimingId),
        EventKey => GetObservedMaximum(WorkflowLayout.EventsLog(_workflowRoot), static (WorkflowEvent record) => record.EventId),
        OrchestrationSessionKey => GetObservedMaximumFromJsonFiles(
            WorkflowLayout.OrchestrationSessionsDir(_workflowRoot),
            "ORS-*.json",
            static (OrchestrationSession record) => record.SessionId),
        OrchestrationRunKey => GetObservedMaximumFromJsonFiles(
            WorkflowLayout.OrchestrationRunsDir(_workflowRoot),
            "ORR-*.json",
            static (OrchestrationRun record) => record.RunId),
        OrchestrationGateDecisionKey => GetObservedMaximum(
            WorkflowLayout.OrchestrationGateDecisionsLog(_workflowRoot),
            static (OrchestrationGateDecisionRecord record) => record.GateDecisionId),
        OrchestrationRunEventKey => GetObservedMaximum(
            WorkflowLayout.OrchestrationRunEventsLog(_workflowRoot),
            static (OrchestrationRunEventRecord record) => record.RunEventId),
        _ => 0
    };

    private int GetObservedPhaseMaximum()
    {
        var max = 0;

        if (Directory.Exists(WorkflowLayout.PhasesDir(_workflowRoot)))
        {
            foreach (var path in Directory.GetFiles(WorkflowLayout.PhasesDir(_workflowRoot), "PHASE-*.json"))
            {
                var phaseId = Path.GetFileNameWithoutExtension(path);
                if (TryExtractNumericSuffix(phaseId, "PHASE-", out var number))
                    max = Math.Max(max, number);
            }
        }

        var workflowPath = WorkflowLayout.WorkflowFile(_workflowRoot);
        if (File.Exists(workflowPath))
        {
            try
            {
                var workflow = WorkflowSerializer.Deserialize<WorkflowState>(File.ReadAllText(workflowPath));
                if (workflow is not null)
                {
                    foreach (var phaseId in workflow.PhaseIds)
                    {
                        if (TryExtractNumericSuffix(phaseId, "PHASE-", out var number))
                            max = Math.Max(max, number);
                    }
                }
            }
            catch
            {
                // If workflow.json cannot be read here, keep the highest value observed elsewhere.
            }
        }

        return max;
    }

    private static int GetObservedMaximum<TRecord>(string path, Func<TRecord, string?> idSelector)
    {
        if (!File.Exists(path))
            return 0;

        var max = 0;
        foreach (var record in JsonlAppender.ReadAll<TRecord>(path))
        {
            if (TryExtractNumericSuffix(idSelector(record), out var number))
                max = Math.Max(max, number);
        }

        return max;
    }

    private static int GetObservedMaximumFromJsonFiles<TRecord>(string directoryPath, string searchPattern, Func<TRecord, string?> idSelector)
    {
        if (!Directory.Exists(directoryPath))
            return 0;

        var max = 0;
        foreach (var path in Directory.GetFiles(directoryPath, searchPattern))
        {
            try
            {
                var json = File.ReadAllText(path);
                var record = WorkflowSerializer.Deserialize<TRecord>(json);
                if (record is not null && TryExtractNumericSuffix(idSelector(record), out var number))
                    max = Math.Max(max, number);
            }
            catch
            {
                // Keep the highest value observed elsewhere if one file is unreadable.
            }
        }

        return max;
    }

    private static bool TryExtractNumericSuffix(string? id, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var dashIndex = id.LastIndexOf('-');
        return dashIndex >= 0
            && dashIndex < id.Length - 1
            && int.TryParse(id.AsSpan(dashIndex + 1), out number)
            && number > 0;
    }

    private static bool TryExtractNumericSuffix(string? id, string prefix, out int number)
    {
        number = 0;
        return !string.IsNullOrWhiteSpace(id)
            && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(id.AsSpan(prefix.Length), out number)
            && number > 0;
    }

    private static string BuildMutexName(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        return $"BekaForge.WorkflowKit.Sequences.{Convert.ToHexString(bytes)}";
    }
}
