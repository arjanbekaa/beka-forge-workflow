using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class RecordBlockerHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.RecordBlocker;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed", "Parameter 'reason' is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var blockerId = store.NextBlockerId();

        // Validate the BLOCKED transition.
        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState  = phase.State,
            TargetState   = PhaseState.Blocked,
            BlockerReason = reason,
            BlockerId     = blockerId
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var record = new BlockerRecord
        {
            BlockerId  = blockerId,
            PhaseId    = phaseId,
            Reason     = reason,
            ReportedBy = context.Actor,
            IsResolved = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        store.AppendBlocker(record);

        var updatedPhase = phase with
        {
            State      = PhaseState.Blocked,
            BlockerIds = [..phase.BlockerIds, blockerId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastStatus       = PhaseState.Blocked,
            OpenBlockerCount = wf.OpenBlockerCount + 1,
            UpdatedUtc       = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "blocker.recorded",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Blocker recorded for {phaseId}: {blockerId} — {reason}",
            PayloadReference = blockerId
        });

        return OperationResult.Ok(record);
    }
}

public sealed class ResolveBlockerHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ResolveBlocker;

    public OperationResult Execute(OperationContext context)
    {
        var blockerId = context.GetString("blockerId");
        if (string.IsNullOrWhiteSpace(blockerId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'blockerId' is required.");

        var resolution = context.GetString("resolution") ?? "Resolved.";

        // Load all blockers and find this one.
        var all = store.ReadAllBlockers();
        var blocker = all.FirstOrDefault(b => b.BlockerId == blockerId);
        if (blocker is null)
            return OperationResult.Fail("NotFound", $"Blocker '{blockerId}' not found.");

        if (blocker.IsResolved)
            return OperationResult.Fail("ValidationFailed", $"Blocker '{blockerId}' is already resolved.");

        // Append a resolved version (JSONL is append-only; the most recent entry wins on load).
        var resolved = blocker with
        {
            IsResolved  = true,
            Resolution  = resolution,
            ResolvedUtc = DateTimeOffset.UtcNow
        };
        store.AppendBlocker(resolved);

        // Update open blocker count on workflow.
        var openCount = all.Count(b => !b.IsResolved) - 1; // subtract the one we just resolved
        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            OpenBlockerCount = Math.Max(0, openCount),
            UpdatedUtc       = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "blocker.resolved",
            Actor            = context.Actor,
            PhaseId          = blocker.PhaseId,
            Summary          = $"Blocker {blockerId} resolved: {resolution}",
            PayloadReference = blockerId
        });

        return OperationResult.Ok(resolved);
    }
}
