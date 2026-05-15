using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Saves or updates the phase contract on an existing phase.</summary>
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
        var scope = context.GetString("scope");
        if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(scope))
            return OperationResult.Fail("ValidationFailed",
                "Parameters 'objective' and 'scope' are required.");

        var requiresValidation = context.GetBool("requiresValidation", defaultValue: true);

        var contract = new PhaseContract
        {
            Objective = objective,
            Scope = scope,
            OutOfScope = context.GetString("outOfScope") ?? string.Empty,
            ArchitectureConstraints = ParseList(context.GetString("architectureConstraints")),
            RequiredFilesOrAreas = ParseList(context.GetString("requiredFilesOrAreas")),
            AcceptanceCriteria = ParseList(context.GetString("acceptanceCriteria")),
            ImplementationNotes = context.GetString("implementationNotes") ?? string.Empty,
            AuditRequirements   = context.GetString("auditRequirements") ?? string.Empty,
            ValidationRequirements = context.GetString("validationRequirements") ?? string.Empty,
            ParallelizationNotes = context.GetString("parallelizationNotes") ?? string.Empty,
            DependsOnPhaseIds = ParseList(context.GetString("dependsOnPhaseIds")),
            RequiresValidation = requiresValidation
        };

        var updated = phase with
        {
            Contract = contract,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "contract.saved",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"Phase contract saved for {phaseId}"
        });

        return OperationResult.Ok(updated);
    }

    private static string[] ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var separators = raw.Contains("||", StringComparison.Ordinal)
            ? ["||"]
            : raw.Contains('\n', StringComparison.Ordinal)
                ? ["\r\n", "\n"]
                : new[] { "," };

        return raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>Reads the phase contract for a given phase.</summary>
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
