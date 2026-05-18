using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetNextActionHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetNextAction;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        return OperationResult.Ok(state.NextAction);
    }
}

public sealed class SetNextActionHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.SetNextAction;

    public OperationResult Execute(OperationContext context)
    {
        var description = context.GetString("description");
        if (string.IsNullOrWhiteSpace(description))
            return OperationResult.Fail("ValidationFailed", "Parameter 'description' is required.");

        var actorName = context.GetString("actor");
        if (!Enum.TryParse<WorkflowActor>(actorName, ignoreCase: true, out var actor))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown actor '{actorName}'. See WorkflowActor for valid values.");

        var nextAction = new NextAction
        {
            ActionId      = store.NextTimingId(), // reuse timing sequence for action IDs
            Actor         = actor,
            Description   = description,
            PhaseId       = context.PhaseId ?? context.GetString("phaseId"),
            OperationHint = context.GetString("operationHint"),
            SetUtc        = DateTimeOffset.UtcNow
        };

        var state = store.LoadWorkflow();
        store.SaveWorkflow(state with
        {
            CurrentPhaseId = ResolveCurrentPhaseId(state, nextAction.PhaseId),
            NextAction  = nextAction,
            UpdatedUtc  = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "workflow.next_action.set",
            Actor     = context.Actor,
            PhaseId   = context.PhaseId,
            Summary   = $"Next action set for {actor}: {description}"
        });

        return OperationResult.Ok(nextAction);
    }

    private string? ResolveCurrentPhaseId(WorkflowState workflow, string? requestedPhaseId)
    {
        if (string.IsNullOrWhiteSpace(requestedPhaseId))
            return workflow.CurrentPhaseId;

        if (string.Equals(workflow.CurrentPhaseId, requestedPhaseId, StringComparison.OrdinalIgnoreCase))
            return workflow.CurrentPhaseId;

        if (store.LoadPhase(requestedPhaseId) is null)
            return workflow.CurrentPhaseId;

        if (string.IsNullOrWhiteSpace(workflow.CurrentPhaseId))
            return requestedPhaseId;

        var currentPhase = store.LoadPhase(workflow.CurrentPhaseId);
        if (currentPhase is not null
            && (PhaseProgress.IsSuccessfulTerminal(currentPhase.State) || currentPhase.DeferredUtc is not null))
            return requestedPhaseId;

        return workflow.CurrentPhaseId;
    }
}
