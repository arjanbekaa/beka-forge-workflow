using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// The exclusive service for writing planning metadata.
///
/// All planning writes (features, current-work details, urgency, due dates,
/// pinned finish-now) must go through this service. It validates that:
///  - No implementation, audit, review, test, fix, blocker, handoff, or timing
///    log records are ever touched.
///  - Every planning metadata write appends an event to events.jsonl.
///
/// The dashboard (and any future UI) calls this service — never writes
/// files directly.
/// </summary>
public sealed class WorkflowMetadataService
{
    private readonly WorkflowStore _store;

    public WorkflowMetadataService(WorkflowStore store)
    {
        _store = store;
    }

    // -- Current work / planning metadata --------------------------------------

    /// <summary>
    /// Updates the current-work planning metadata (next action, actor, urgency,
    /// due date, pinned finish-now). This writes only to workflow.json; no log
    /// records other than events.jsonl are touched.
    /// </summary>
    public WorkflowResult<Unit> UpdateCurrentWork(
        string description,
        WorkflowActor actor,
        Urgency urgency = Urgency.Medium,
        DateTimeOffset? dueDate = null,
        bool pinnedFinishNow = false,
        string? phaseId = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            return WorkflowResult.Fail<Unit>(
                WorkflowError.ValidationFailed("Next action description must not be empty."));

        var workflow = _store.LoadWorkflow();
        var actionId = _store.NextTimingId(); // reuse TIME- sequence for next-action tracking

        var nextAction = new NextAction
        {
            ActionId = actionId,
            Actor = actor,
            Description = description.Trim(),
            PhaseId = phaseId ?? workflow.CurrentPhaseId,
            Urgency = urgency,
            DueDate = dueDate,
            PinnedFinishNow = pinnedFinishNow,
            SetUtc = DateTimeOffset.UtcNow
        };

        var updated = workflow with
        {
            CurrentPhaseId = ResolveCurrentPhaseId(workflow, nextAction.PhaseId),
            NextAction = nextAction,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        _store.SaveWorkflow(updated);
        AppendEvent("planning.current_work.updated",
            $"Current work updated: [{actor}] {description}");

        return WorkflowResult.Ok();
    }

    private string? ResolveCurrentPhaseId(WorkflowState workflow, string? requestedPhaseId)
    {
        if (string.IsNullOrWhiteSpace(requestedPhaseId))
            return workflow.CurrentPhaseId;

        if (string.Equals(workflow.CurrentPhaseId, requestedPhaseId, StringComparison.OrdinalIgnoreCase))
            return workflow.CurrentPhaseId;

        if (_store.LoadPhase(requestedPhaseId) is null)
            return workflow.CurrentPhaseId;

        if (string.IsNullOrWhiteSpace(workflow.CurrentPhaseId))
            return requestedPhaseId;

        var currentPhase = _store.LoadPhase(workflow.CurrentPhaseId);
        if (currentPhase is not null && PhaseProgress.IsSuccessfulTerminal(currentPhase.State))
            return requestedPhaseId;

        return workflow.CurrentPhaseId;
    }

    // -- Event logging (planning writes only) ----------------------------------

    /// <summary>
    /// Appends an event to events.jsonl for a planning metadata write.
    /// This is the ONLY place planning events are appended.
    /// </summary>
    private void AppendEvent(string eventType, string summary)
    {
        var evt = new WorkflowEvent
        {
            EventId = _store.NextEventId(),
            EventType = eventType,
            Actor = WorkflowActor.Codex,
            Summary = summary,
            PhaseId = _store.LoadWorkflow().CurrentPhaseId,
            Timestamp = DateTimeOffset.UtcNow
        };

        _store.AppendEvent(evt);
    }
}
