using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using System.Text;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Repairs a narrow set of authoritative integrity problems through a handler-backed path.
/// This is intentionally limited to known-safe repairs:
/// - duplicate audit IDs reused across different records/phases
/// - duplicate review IDs reused across different records/phases
/// - stale phase references that still point at the duplicated IDs
/// - legacy TEST-* records missing from phase ValidationLogIds
/// </summary>
public sealed class RepairAuthoritativeIntegrityHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RepairAuthoritativeIntegrity;

    public OperationResult Execute(OperationContext context)
    {
        var workflow = store.LoadWorkflow();
        var phaseId = context.PhaseId
            ?? workflow.CurrentPhaseId
            ?? "PHASE-060";

        var phases = store.LoadAllPhases()
            .ToDictionary(static phase => phase.PhaseId, StringComparer.OrdinalIgnoreCase);

        var repairs = new List<string>();
        var changedPhases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workflowChanged = false;

        var auditRepair = RepairDuplicateIds(
            WorkflowLayout.AuditLog(store.WorkflowRoot),
            "AUD-",
            static record => record.AuditId,
            static record => record.PhaseId,
            static (record, newId) => record with { AuditId = newId });
        ApplyEvidenceReferenceRepairs(
            phases,
            auditRepair.PhaseReplacements,
            static phase => phase.AuditLogIds,
            static (phase, ids) => phase with { AuditLogIds = ids },
            changedPhases);
        repairs.AddRange(auditRepair.Repairs);

        var reviewRepair = RepairDuplicateIds(
            WorkflowLayout.ReviewLog(store.WorkflowRoot),
            "REV-",
            static record => record.ReviewId,
            static record => record.PhaseId,
            static (record, newId) => record with { ReviewId = newId });
        ApplyEvidenceReferenceRepairs(
            phases,
            reviewRepair.PhaseReplacements,
            static phase => phase.ReviewLogIds,
            static (phase, ids) => phase with { ReviewLogIds = ids },
            changedPhases);
        repairs.AddRange(reviewRepair.Repairs);

        var legacyValidationRepairs = EnsureLegacyTestValidationAliases(phases, changedPhases);
        repairs.AddRange(legacyValidationRepairs);
        repairs.AddRange(RepairFalsePassStates(phases, changedPhases));

        foreach (var changedPhaseId in changedPhases)
        {
            var phase = phases[changedPhaseId] with { UpdatedUtc = DateTimeOffset.UtcNow };
            phases[changedPhaseId] = phase;
            store.SavePhase(phase);
        }

        var repairedOpenBlockerCount = CountOpenBlockers(store.ReadAllBlockers());
        if (workflow.OpenBlockerCount != repairedOpenBlockerCount)
        {
            workflow = workflow with
            {
                OpenBlockerCount = repairedOpenBlockerCount,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            workflowChanged = true;
            repairs.Add($"workflow.json: reconciled openBlockerCount to {repairedOpenBlockerCount}.");
        }

        if (!string.IsNullOrWhiteSpace(workflow.CurrentPhaseId)
            && phases.TryGetValue(workflow.CurrentPhaseId, out var currentPhase)
            && workflow.LastStatus != currentPhase.State)
        {
            workflow = workflow with
            {
                LastStatus = currentPhase.State,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            workflowChanged = true;
            repairs.Add($"workflow.json: reconciled lastStatus to {currentPhase.State} for current phase {workflow.CurrentPhaseId}.");
        }

        if (workflowChanged)
            store.SaveWorkflow(workflow);

        if (repairs.Count == 0)
        {
            return OperationResult.Ok(new AuthoritativeIntegrityRepairResult
            {
                Repaired = false,
                Summary = "No authoritative integrity repairs were needed.",
                RepairsPerformed = []
            });
        }

        WriteRepairArtifact(phaseId, repairs);

        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "workflow.integrity.authoritative_repaired",
            Actor = context.Actor,
            PhaseId = phaseId,
            Summary = $"Authoritative integrity repair updated {repairs.Count} item(s)."
        });

        return OperationResult.Ok(new AuthoritativeIntegrityRepairResult
        {
            Repaired = true,
            Summary = $"Repaired {repairs.Count} authoritative integrity item(s).",
            RepairsPerformed = repairs
        });
    }

    private LogRepairResult<AuditRecord> RepairDuplicateIds(
        string path,
        string prefix,
        Func<AuditRecord, string?> idSelector,
        Func<AuditRecord, string?> phaseSelector,
        Func<AuditRecord, string, AuditRecord> withNewId) =>
        RepairDuplicateIdsCore(path, prefix, idSelector, phaseSelector, withNewId);

    private LogRepairResult<ReviewRecord> RepairDuplicateIds(
        string path,
        string prefix,
        Func<ReviewRecord, string?> idSelector,
        Func<ReviewRecord, string?> phaseSelector,
        Func<ReviewRecord, string, ReviewRecord> withNewId) =>
        RepairDuplicateIdsCore(path, prefix, idSelector, phaseSelector, withNewId);

    private static LogRepairResult<TRecord> RepairDuplicateIdsCore<TRecord>(
        string path,
        string prefix,
        Func<TRecord, string?> idSelector,
        Func<TRecord, string?> phaseSelector,
        Func<TRecord, string, TRecord> withNewId)
        where TRecord : class
    {
        if (!File.Exists(path))
            return LogRepairResult<TRecord>.Empty;

        var lines = File.ReadAllLines(path);
        var parsedRecords = new List<ParsedRecord<TRecord>>(lines.Length);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextNumber = 0;
        var repairs = new List<string>();
        var phaseReplacements = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                parsedRecords.Add(new ParsedRecord<TRecord>(line, null, null, null));
                continue;
            }

            var record = WorkflowSerializer.Deserialize<TRecord>(line)
                ?? throw new InvalidOperationException($"Could not deserialize authoritative record at '{path}' line {index + 1}.");
            var recordId = idSelector(record)?.Trim();
            var phaseId = phaseSelector(record)?.Trim();
            parsedRecords.Add(new ParsedRecord<TRecord>(line, record, recordId, phaseId));

            if (string.IsNullOrWhiteSpace(recordId))
                continue;

            usedIds.Add(recordId);
            if (TryExtractNumericSuffix(recordId, prefix, out var number))
                nextNumber = Math.Max(nextNumber, number);
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updatedLines = new string[lines.Length];
        for (var index = 0; index < parsedRecords.Count; index++)
        {
            var parsed = parsedRecords[index];
            if (parsed.Record is null || string.IsNullOrWhiteSpace(parsed.RecordId))
            {
                updatedLines[index] = parsed.RawLine;
                continue;
            }

            if (seenIds.Add(parsed.RecordId))
            {
                updatedLines[index] = parsed.RawLine;
                continue;
            }

            var newId = NextAvailableId(prefix, usedIds, ref nextNumber);
            var updated = withNewId(parsed.Record, newId);
            updatedLines[index] = WorkflowSerializer.SerializeJsonl(updated);
            changed = true;

            if (!string.IsNullOrWhiteSpace(parsed.PhaseId))
            {
                if (!phaseReplacements.TryGetValue(parsed.PhaseId, out var replacements))
                {
                    replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    phaseReplacements.Add(parsed.PhaseId, replacements);
                }

                replacements[parsed.RecordId] = newId;
            }

            repairs.Add($"{Path.GetFileName(path)} line {index + 1}: {parsed.RecordId} -> {newId}");
        }

        if (changed)
        {
            AtomicFileWriter.Write(path, string.Join(Environment.NewLine, updatedLines) + Environment.NewLine);
        }

        return new LogRepairResult<TRecord>(repairs, phaseReplacements);
    }

    private static void ApplyEvidenceReferenceRepairs(
        Dictionary<string, Phase> phases,
        IReadOnlyDictionary<string, Dictionary<string, string>> phaseReplacements,
        Func<Phase, IReadOnlyList<string>> selector,
        Func<Phase, IReadOnlyList<string>, Phase> withIds,
        HashSet<string> changedPhases)
    {
        foreach (var (phaseId, replacements) in phaseReplacements)
        {
            if (!phases.TryGetValue(phaseId, out var phase))
                continue;

            var ids = selector(phase).ToArray();
            var changed = false;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!replacements.TryGetValue(ids[i], out var replacement))
                    continue;

                ids[i] = replacement;
                changed = true;
            }

            if (!changed)
                continue;

            phases[phaseId] = withIds(phase, ids);
            changedPhases.Add(phaseId);
        }
    }

    private static IReadOnlyList<string> EnsureLegacyTestValidationAliases(
        Dictionary<string, Phase> phases,
        HashSet<string> changedPhases)
    {
        var repairs = new List<string>();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PHASE-001"] = "TEST-001",
            ["PHASE-002"] = "TEST-002"
        };

        foreach (var (phaseId, testId) in aliases)
        {
            if (!phases.TryGetValue(phaseId, out var phase))
                continue;

            if (phase.ValidationLogIds.Contains(testId, StringComparer.OrdinalIgnoreCase))
                continue;

            phases[phaseId] = phase with
            {
                ValidationLogIds = [.. phase.ValidationLogIds, testId]
            };
            changedPhases.Add(phaseId);
            repairs.Add($"{phaseId}: linked legacy validation alias {testId}");
        }

        return repairs;
    }

    private static IReadOnlyList<string> RepairFalsePassStates(
        Dictionary<string, Phase> phases,
        HashSet<string> changedPhases)
    {
        var repairs = new List<string>();

        foreach (var (phaseId, phase) in phases)
        {
            if (phase.State is not (PhaseState.Pass or PhaseState.PassWithWarnings))
                continue;

            var hasImplementation = phase.ImplementationLogIds.Count > 0;
            var hasAudit = phase.AuditLogIds.Count > 0;
            var hasReview = phase.ReviewLogIds.Count > 0;
            var hasValidation = phase.ValidationLogIds.Count > 0 || phase.TestLogIds.Count > 0;

            if (hasImplementation || hasAudit || hasReview || hasValidation)
                continue;

            phases[phaseId] = phase with { State = PhaseState.Planned };
            changedPhases.Add(phaseId);
            repairs.Add($"{phaseId}: downgraded terminal success state to Planned because no evidence references exist.");
        }

        return repairs;
    }

    private void WriteRepairArtifact(string phaseId, IReadOnlyList<string> repairs)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var artifactPath = WorkflowLayout.EvidenceArtifactPath(store.WorkflowRoot, phaseId, timestamp, "authoritative-integrity-repair");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        AtomicFileWriter.Write(artifactPath, string.Join(Environment.NewLine, repairs) + Environment.NewLine);
    }

    private static string NextAvailableId(string prefix, HashSet<string> usedIds, ref int nextNumber)
    {
        while (true)
        {
            nextNumber++;
            var candidate = $"{prefix}{nextNumber:D3}";
            if (usedIds.Add(candidate))
                return candidate;
        }
    }

    private static bool TryExtractNumericSuffix(string value, string prefix, out int number)
    {
        number = 0;
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value.AsSpan(prefix.Length), out number)
            && number > 0;
    }

    private static int CountOpenBlockers(IEnumerable<BlockerRecord> records) =>
        records
            .GroupBy(record => record.BlockerId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Count(record => !record.IsResolved);

    private sealed record LogRepairResult<TRecord>(
        IReadOnlyList<string> Repairs,
        IReadOnlyDictionary<string, Dictionary<string, string>> PhaseReplacements)
    {
        public static LogRepairResult<TRecord> Empty { get; } =
            new([], new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed record ParsedRecord<TRecord>(
        string RawLine,
        TRecord? Record,
        string? RecordId,
        string? PhaseId)
        where TRecord : class;
}

public sealed record AuthoritativeIntegrityRepairResult
{
    public bool Repaired { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> RepairsPerformed { get; init; } = [];
}
