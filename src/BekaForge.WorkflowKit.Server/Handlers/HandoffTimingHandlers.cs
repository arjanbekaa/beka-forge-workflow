using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class CreateHandoffHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.CreateHandoff;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var summary = context.GetString("summary");
        if (string.IsNullOrWhiteSpace(summary))
            return OperationResult.Fail("ValidationFailed", "Parameter 'summary' is required.");

        var toActorName = context.GetString("toActor");
        if (!Enum.TryParse<WorkflowActor>(toActorName, ignoreCase: true, out var toActor))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown toActor '{toActorName}'. See WorkflowActor for valid values.");

        var handoffId = store.NextHandoffId();
        var record = new HandoffRecord
        {
            HandoffId     = handoffId,
            PhaseId       = phaseId,
            FromActor     = context.Actor,
            ToActor       = toActor,
            Summary       = summary,
            OperationHint = context.GetString("operationHint"),
            CreatedUtc    = DateTimeOffset.UtcNow
        };

        store.AppendHandoff(record);

        // Add handoff ID to the phase.
        var phase = store.LoadPhase(phaseId);
        if (phase is not null)
        {
            store.SavePhase(phase with
            {
                HandoffIds = [..phase.HandoffIds, handoffId],
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "handoff.created",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Handoff {handoffId} from {context.Actor} to {toActor}: {summary}",
            PayloadReference = handoffId
        });

        return OperationResult.Ok(record);
    }
}

public sealed class GetHandoffsHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetHandoffs;

    public OperationResult Execute(OperationContext context)
    {
        var all = store.ReadAllHandoffs();

        // Optionally filter by actor.
        var actorFilter = context.GetString("toActor");
        if (!string.IsNullOrWhiteSpace(actorFilter) &&
            Enum.TryParse<WorkflowActor>(actorFilter, ignoreCase: true, out var actor))
        {
            all = all.Where(h => h.ToActor == actor).ToList();
        }

        // Optionally filter by phase.
        if (!string.IsNullOrWhiteSpace(context.PhaseId))
            all = all.Where(h => h.PhaseId == context.PhaseId).ToList();

        return OperationResult.Ok(all);
    }
}

public sealed class RecordTimeSpentHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RecordTimeSpent;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var activity = context.GetString("activity");
        if (string.IsNullOrWhiteSpace(activity))
            return OperationResult.Fail("ValidationFailed", "Parameter 'activity' is required.");

        var durationSeconds = context.Get<double>("durationSeconds");
        if (durationSeconds <= 0)
            return OperationResult.Fail("ValidationFailed",
                "Parameter 'durationSeconds' must be a positive number.");

        var timingId = store.NextTimingId();
        var record = new TimingRecord
        {
            TimingId   = timingId,
            PhaseId    = phaseId,
            Actor      = context.Actor,
            Activity   = activity,
            Duration   = TimeSpan.FromSeconds(durationSeconds),
            Notes      = context.GetString("notes") ?? string.Empty,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        store.AppendTiming(record);

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "timing.recorded",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Timing recorded for {phaseId}: {durationSeconds}s on '{activity}'",
            PayloadReference = timingId
        });

        return OperationResult.Ok(record);
    }
}
