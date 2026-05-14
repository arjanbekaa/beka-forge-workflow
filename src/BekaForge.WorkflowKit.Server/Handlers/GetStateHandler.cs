using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetStateHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetState;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        return OperationResult.Ok(state);
    }
}

public sealed class GetCurrentPhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetCurrentPhase;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        if (state.CurrentPhaseId is null)
            return OperationResult.Ok(null);

        var phase = store.LoadPhase(state.CurrentPhaseId);
        return OperationResult.Ok(phase);
    }
}

public sealed class ListPhasesHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ListPhases;

    public OperationResult Execute(OperationContext context)
    {
        var phases = store.LoadAllPhases();
        return OperationResult.Ok(phases);
    }
}

public sealed class ValidateStateHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ValidateState;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();
        var phases = store.LoadAllPhases();

        var issues = new List<string>();

        // All phase IDs in workflow.json must have corresponding files.
        foreach (var phaseId in state.PhaseIds)
        {
            if (!store.PhaseExists(phaseId))
                issues.Add($"Phase {phaseId} is listed in workflow.json but has no phase file.");
        }

        // Current phase ID must be in the phase list.
        if (state.CurrentPhaseId is not null && !state.PhaseIds.Contains(state.CurrentPhaseId))
            issues.Add($"CurrentPhaseId '{state.CurrentPhaseId}' is not in the phaseIds list.");

        return OperationResult.Ok(new { valid = issues.Count == 0, issues });
    }
}
