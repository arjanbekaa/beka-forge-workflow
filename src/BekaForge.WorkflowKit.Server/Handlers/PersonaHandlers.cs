using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class ListPersonasHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PersonaPolicyService _service = new(store);

    public string OperationName => WorkflowOperations.ListPersonas;

    public OperationResult Execute(OperationContext context)
    {
        var result = _service.LoadCatalog();
        return OperationResult.Ok(new
        {
            source = result.Source,
            warnings = result.Warnings,
            personas = result.Catalog.Personas
        });
    }
}

public sealed class GetPersonaHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PersonaPolicyService _service = new(store);

    public string OperationName => WorkflowOperations.GetPersona;

    public OperationResult Execute(OperationContext context)
    {
        var personaId = context.GetString("personaId");
        if (string.IsNullOrWhiteSpace(personaId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'personaId' is required.");

        var result = _service.LoadCatalog();
        var persona = _service.ResolvePersona(result.Catalog, personaId);
        if (persona is null)
            return OperationResult.Fail("NotFound", $"Persona '{personaId}' was not found.");

        var policies = result.Catalog.TaskPolicies
            .Where(policy => persona.SupportedTaskTypes.Contains(policy.TaskType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return OperationResult.Ok(new
        {
            source = result.Source,
            warnings = result.Warnings,
            persona,
            taskPolicies = policies
        });
    }
}

public sealed class RecommendPersonaHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PersonaPolicyService _service = new(store);

    public string OperationName => WorkflowOperations.RecommendPersona;

    public OperationResult Execute(OperationContext context)
    {
        var task = context.GetString("task");
        if (string.IsNullOrWhiteSpace(task))
            return OperationResult.Fail("ValidationFailed", "Parameter 'task' is required.");

        var maxResults = context.Get<int>("maxResults");
        var result = _service.Recommend(
            task,
            context.GetString("requestedOperation"),
            context.GetString("requestedActor"),
            maxResults <= 0 ? 3 : maxResults);

        return OperationResult.Ok(result);
    }
}

public sealed class ValidatePersonaTaskHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PersonaPolicyService _service = new(store);

    public string OperationName => WorkflowOperations.ValidatePersonaTask;

    public OperationResult Execute(OperationContext context)
    {
        var personaId = context.GetString("personaId");
        var task = context.GetString("task");

        if (string.IsNullOrWhiteSpace(personaId) || string.IsNullOrWhiteSpace(task))
        {
            return OperationResult.Fail(
                "ValidationFailed",
                "Parameters 'personaId' and 'task' are required.");
        }

        var result = _service.Validate(
            personaId,
            task,
            context.GetString("requestedOperation"),
            context.GetString("requestedActor"),
            context.GetString("requestedPhaseId"),
            context.GetBool("hasEvidence"),
            context.GetBool("humanApproved"));

        return OperationResult.Ok(result);
    }
}
