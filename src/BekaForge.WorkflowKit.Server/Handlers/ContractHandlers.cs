using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetPhaseContractHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetPhaseContract;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        return OperationResult.Ok(phase.Contract);
    }
}

public sealed class SavePhaseContractHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.SavePhaseContract;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var objective = context.GetString("objective");
        var scope     = context.GetString("scope");
        if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(scope))
            return OperationResult.Fail("ValidationFailed",
                "Parameters 'objective' and 'scope' are required.");

        var requiresUnityTest = context.GetBool("requiresUnityTest", defaultValue: true);

        var contract = new PhaseContract
        {
            Objective        = objective,
            Scope            = scope,
            OutOfScope       = context.GetString("outOfScope") ?? string.Empty,
            ImplementationNotes = context.GetString("implementationNotes") ?? string.Empty,
            AuditRequirements   = context.GetString("auditRequirements") ?? string.Empty,
            UnityTestRequirements = context.GetString("unityTestRequirements") ?? string.Empty,
            RequiresUnityTest = requiresUnityTest
        };

        var updated = phase with
        {
            Contract   = contract,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.contract.saved",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"Phase contract saved for {phaseId}"
        });

        return OperationResult.Ok(contract);
    }
}
