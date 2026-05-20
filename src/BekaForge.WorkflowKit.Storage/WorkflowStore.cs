using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// The primary storage facade for a single workflow root.
///
/// WorkflowStore coordinates all storage operations:
/// - Atomic JSON reads/writes for workflow state and phase state
/// - Append-only JSONL writes for all log, event, blocker, handoff, and timing records
/// - Sequential ID allocation via IdSequenceStore
///
/// Every state-changing operation that appends a record is the caller's responsibility
/// to also call AppendEvent (the dispatcher layer owns event creation).
///
/// WorkflowStore is not thread-safe for concurrent writes. Single-process access assumed in v1.
/// </summary>
public sealed class WorkflowStore
{
    private readonly string _root;
    private readonly WorkflowStateRepository _workflowRepo;
    private readonly PhaseRepository _phaseRepo;
    private readonly OrchestrationSessionRepository _orchestrationSessionRepo;
    private readonly OrchestrationRunRepository _orchestrationRunRepo;
    private readonly DocumentationLedgerStore _documentationLedgerStore;
    private readonly IdSequenceStore _sequences;
    private IContextPackageInvalidator? _invalidator;

    public string WorkflowRoot => _root;

    /// <summary>Optional cache invalidator. Set by application startup to evict cached packages on writes.</summary>
    public IContextPackageInvalidator? Invalidator { get => _invalidator; set => _invalidator = value; }

    public WorkflowStore(string workflowRoot)
    {
        _root = workflowRoot;
        _workflowRepo = new WorkflowStateRepository(workflowRoot);
        _phaseRepo = new PhaseRepository(workflowRoot);
        _orchestrationSessionRepo = new OrchestrationSessionRepository(workflowRoot);
        _orchestrationRunRepo = new OrchestrationRunRepository(workflowRoot);
        _documentationLedgerStore = new DocumentationLedgerStore(workflowRoot);
        _sequences = new IdSequenceStore(workflowRoot);
    }

    // -- Initialization check -----------------------------------------------------

    public bool IsInitialized() => WorkflowLayout.IsInitialized(_root);

    // -- WorkflowState -------------------------------------------------------------

    public WorkflowState LoadWorkflow() => _workflowRepo.Load();

    public void SaveWorkflow(WorkflowState state) { _workflowRepo.Save(state); _invalidator?.InvalidateWorkflow(); }

    // -- Phases --------------------------------------------------------------------

    public Phase? LoadPhase(string phaseId) => _phaseRepo.Load(phaseId);

    public IReadOnlyList<Phase> LoadAllPhases() => _phaseRepo.LoadAll();

    public void SavePhase(Phase phase) { _phaseRepo.Save(phase); _invalidator?.InvalidatePhase(phase.PhaseId); }

    public bool PhaseExists(string phaseId) => _phaseRepo.Exists(phaseId);

    public string NextAvailablePhaseId(IReadOnlyList<string>? existingPhaseIds = null)
    {
        var usedNumbers = new HashSet<int>();
        foreach (var phaseId in existingPhaseIds ?? LoadWorkflow().PhaseIds)
        {
            if (TryParsePhaseNumber(phaseId, out var number))
                usedNumbers.Add(number);
        }

        var nextNumber = 1;
        while (usedNumbers.Contains(nextNumber))
            nextNumber++;

        _sequences.EnsurePhaseAtLeast(nextNumber);
        return WorkflowIdFormatter.Phase(nextNumber);
    }

    public void EnsurePhaseSequenceAtLeast(int phaseNumber) =>
        _sequences.EnsurePhaseAtLeast(phaseNumber);

    public bool DeletePhase(string phaseId)
    {
        var deleted = _phaseRepo.Delete(phaseId);
        if (deleted)
            _invalidator?.InvalidatePhase(phaseId);

        return deleted;
    }

    // -- Orchestration sessions ---------------------------------------------------

    public OrchestrationSession? LoadOrchestrationSession(string sessionId) =>
        _orchestrationSessionRepo.Load(sessionId);

    public IReadOnlyList<OrchestrationSession> LoadAllOrchestrationSessions() =>
        _orchestrationSessionRepo.LoadAll();

    public void SaveOrchestrationSession(OrchestrationSession session)
    {
        _orchestrationSessionRepo.Save(session);
        _invalidator?.InvalidatePhase(session.PhaseId);
    }

    public bool OrchestrationSessionExists(string sessionId) =>
        _orchestrationSessionRepo.Exists(sessionId);

    // -- Orchestration runs -------------------------------------------------------

    public OrchestrationRun? LoadOrchestrationRun(string runId) =>
        _orchestrationRunRepo.Load(runId);

    public IReadOnlyList<OrchestrationRun> LoadAllOrchestrationRuns() =>
        _orchestrationRunRepo.LoadAll();

    public void SaveOrchestrationRun(OrchestrationRun run)
    {
        _orchestrationRunRepo.Save(run);
        _invalidator?.InvalidatePhase(run.PhaseId);
    }

    public bool OrchestrationRunExists(string runId) =>
        _orchestrationRunRepo.Exists(runId);

    // -- Documentation ledger ----------------------------------------------------

    public IReadOnlyList<DocumentationLedgerRecord> LoadDocumentationLedger() =>
        _documentationLedgerStore.Load();

    public void SaveDocumentationLedger(IReadOnlyList<DocumentationLedgerRecord> records) =>
        _documentationLedgerStore.Save(records);

    // -- Orchestration append-only history ---------------------------------------

    public void AppendOrchestrationGateDecision(OrchestrationGateDecisionRecord record)
    {
        JsonlAppender.Append(WorkflowLayout.OrchestrationGateDecisionsLog(_root), record);
        _invalidator?.InvalidatePhase(record.PhaseId);
    }

    public IReadOnlyList<OrchestrationGateDecisionRecord> ReadAllOrchestrationGateDecisions() =>
        JsonlAppender.ReadAll<OrchestrationGateDecisionRecord>(WorkflowLayout.OrchestrationGateDecisionsLog(_root));

    public void AppendOrchestrationRunEvent(OrchestrationRunEventRecord record)
    {
        JsonlAppender.Append(WorkflowLayout.OrchestrationRunEventsLog(_root), record);
        _invalidator?.InvalidatePhase(record.PhaseId);
    }

    public IReadOnlyList<OrchestrationRunEventRecord> ReadAllOrchestrationRunEvents() =>
        JsonlAppender.ReadAll<OrchestrationRunEventRecord>(WorkflowLayout.OrchestrationRunEventsLog(_root));

    // -- Events --------------------------------------------------------------------

    public void AppendEvent(WorkflowEvent evt) =>
        JsonlAppender.Append(WorkflowLayout.EventsLog(_root), evt);

    public IReadOnlyList<WorkflowEvent> ReadAllEvents() =>
        JsonlAppender.ReadAll<WorkflowEvent>(WorkflowLayout.EventsLog(_root));

    // -- Implementation logs -------------------------------------------------------

    public void AppendImplementation(ImplementationRecord record) { JsonlAppender.Append(WorkflowLayout.ImplementationLog(_root), record); if (!string.IsNullOrWhiteSpace(record.PhaseId)) _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<ImplementationRecord> ReadAllImplementations() =>
        JsonlAppender.ReadAll<ImplementationRecord>(WorkflowLayout.ImplementationLog(_root));

    // -- Audit logs ----------------------------------------------------------------

    public void AppendAudit(AuditRecord record) { JsonlAppender.Append(WorkflowLayout.AuditLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<AuditRecord> ReadAllAudits() =>
        JsonlAppender.ReadAll<AuditRecord>(WorkflowLayout.AuditLog(_root));

    // -- Review logs ---------------------------------------------------------------

    public void AppendReview(ReviewRecord record) { JsonlAppender.Append(WorkflowLayout.ReviewLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<ReviewRecord> ReadAllReviews() =>
        JsonlAppender.ReadAll<ReviewRecord>(WorkflowLayout.ReviewLog(_root));

    // -- Validation logs -----------------------------------------------------------

    public void AppendValidation(ValidationRecord record) { JsonlAppender.Append(WorkflowLayout.ValidationLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<ValidationRecord> ReadAllValidations() =>
        JsonlAppender.ReadAll<ValidationRecord>(WorkflowLayout.ValidationLog(_root));

    // -- Test logs (legacy) --------------------------------------------------------

    public void AppendTest(TestRecord record) { JsonlAppender.Append(WorkflowLayout.TestLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<TestRecord> ReadAllTests() =>
        JsonlAppender.ReadAll<TestRecord>(WorkflowLayout.TestLog(_root));

    // -- Fix logs ------------------------------------------------------------------

    public void AppendFix(FixRecord record) { JsonlAppender.Append(WorkflowLayout.FixLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<FixRecord> ReadAllFixes() =>
        JsonlAppender.ReadAll<FixRecord>(WorkflowLayout.FixLog(_root));

    // -- Blockers -----------------------------------------------------------------

    public void AppendBlocker(BlockerRecord record) { JsonlAppender.Append(WorkflowLayout.BlockersLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<BlockerRecord> ReadAllBlockers() =>
        JsonlAppender.ReadAll<BlockerRecord>(WorkflowLayout.BlockersLog(_root));

    // -- Handoffs -----------------------------------------------------------------

    public void AppendHandoff(HandoffRecord record) { JsonlAppender.Append(WorkflowLayout.HandoffsLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<HandoffRecord> ReadAllHandoffs() =>
        JsonlAppender.ReadAll<HandoffRecord>(WorkflowLayout.HandoffsLog(_root));

    // -- Timing -------------------------------------------------------------------

    public void AppendTiming(TimingRecord record) { JsonlAppender.Append(WorkflowLayout.TimingLog(_root), record); _invalidator?.InvalidateRecords(record.PhaseId); }

    public IReadOnlyList<TimingRecord> ReadAllTimings() =>
        JsonlAppender.ReadAll<TimingRecord>(WorkflowLayout.TimingLog(_root));

    // -- ID allocation -------------------------------------------------------------

    public string NextPhaseId()          => _sequences.NextPhaseId();
    public string NextImplementationId() => _sequences.NextImplementationId();
    public string NextAuditId()          => _sequences.NextAuditId();
    public string NextReviewId()         => _sequences.NextReviewId();
    public string NextValidationId()     => _sequences.NextValidationId();
    public string NextTestId()           => _sequences.NextTestId();
    public string NextFixId()            => _sequences.NextFixId();
    public string NextBlockerId()        => _sequences.NextBlockerId();
    public string NextHandoffId()        => _sequences.NextHandoffId();
    public string NextTimingId()         => _sequences.NextTimingId();
    public string NextEventId()          => _sequences.NextEventId();
    public string NextOrchestrationSessionId() => _sequences.NextOrchestrationSessionId();
    public string NextOrchestrationRunId() => _sequences.NextOrchestrationRunId();
    public string NextOrchestrationGateDecisionId() => _sequences.NextOrchestrationGateDecisionId();
    public string NextOrchestrationRunEventId() => _sequences.NextOrchestrationRunEventId();

    private static bool TryParsePhaseNumber(string phaseId, out int number)
    {
        number = 0;
        if (!phaseId.StartsWith("PHASE-", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(phaseId.AsSpan(6), out number) && number > 0;
    }
}
