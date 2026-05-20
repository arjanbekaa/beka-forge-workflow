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
        var openBlockerCount = BlockerStateSnapshot.CountOpenBlockersWithLatestState(store.ReadAllBlockers());
        var shouldTrackCurrentPhase = string.Equals(wf.CurrentPhaseId, phaseId, StringComparison.OrdinalIgnoreCase);
        store.SaveWorkflow(wf with
        {
            LastStatus       = shouldTrackCurrentPhase ? PhaseState.Blocked : wf.LastStatus,
            OpenBlockerCount = openBlockerCount,
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

        // Update open blocker count on workflow using the latest state per blocker ID.
        var openCount = BlockerStateSnapshot.CountOpenBlockersWithLatestState(all.Append(resolved));
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

        // PHASE-014: Auto-advance a Blocked phase when its last open blocker is resolved.
        if (!string.IsNullOrWhiteSpace(blocker.PhaseId))
        {
            var phase = store.LoadPhase(blocker.PhaseId);
            if (phase is { State: PhaseState.Blocked })
            {
                // Use the already-loaded `all` snapshot, deduplicated by BlockerId (last wins),
                // excluding the blocker we just resolved. This avoids a JSONL re-read race where
                // both the old unresolved and new resolved record appear for the same ID.
                var remaining = all
                    .GroupBy(b => b.BlockerId)
                    .Select(g => g.Last())   // latest append per blocker = current state
                    .Count(b => string.Equals(b.PhaseId, blocker.PhaseId,
                                    StringComparison.OrdinalIgnoreCase)
                             && b.BlockerId != blockerId   // exclude the one we just resolved
                             && !b.IsResolved);

                if (remaining == 0)
                {
                    var updatedPhase = phase with
                    {
                        State      = PhaseState.ReadyForImplementation,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    store.SavePhase(updatedPhase);
                    WorkflowStatusSnapshot.UpdateWorkflowLastStatusIfCurrentPhase(store, blocker.PhaseId, updatedPhase.State);

                    store.AppendEvent(new WorkflowEvent
                    {
                        EventId   = store.NextEventId(),
                        EventType = "phase.unblocked",
                        Actor     = context.Actor,
                        PhaseId   = blocker.PhaseId,
                        Summary   = $"Phase {blocker.PhaseId} automatically advanced to ReadyForImplementation — all blockers resolved."
                    });
                }
            }
        }

        return OperationResult.Ok(resolved);
    }
}

internal static class BlockerStateSnapshot
{
    public static int CountOpenBlockersWithLatestState(IEnumerable<BlockerRecord> records) =>
        records
            .GroupBy(record => record.BlockerId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Count(record => !record.IsResolved);
}
