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

    public bool DeletePhase(string phaseId)
    {
        var deleted = _phaseRepo.Delete(phaseId);
        if (deleted)
            _invalidator?.InvalidatePhase(phaseId);

        return deleted;
    }

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
}
