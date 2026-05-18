using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetProjectGuidanceHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetProjectGuidance;

    public OperationResult Execute(OperationContext context)
    {
        var section = context.GetString("section");
        if (string.IsNullOrWhiteSpace(section))
            return OperationResult.Fail("ValidationFailed", "Parameter 'section' is required.");

        var metadata = new WorkflowMetadataService(store);
        var content = metadata.GetProjectGuidance(section);
        if (content is null)
            return OperationResult.Fail("NotFound", $"No project guidance recorded for '{section}'.");

        return OperationResult.Ok(new
        {
            Section = section,
            Content = content
        });
    }
}

public sealed class SetProjectGuidanceHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.SetProjectGuidance;

    public OperationResult Execute(OperationContext context)
    {
        var section = context.GetString("section");
        var content = context.GetString("content");
        if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(content))
            return OperationResult.Fail("ValidationFailed", "Parameters 'section' and 'content' are required.");

        var metadata = new WorkflowMetadataService(store);
        var result = metadata.UpdateProjectGuidance(section, content, context.PhaseId ?? context.GetString("phaseId"));
        if (result.IsFailure)
            return OperationResult.FromError(result.Error);

        return OperationResult.Ok(new
        {
            Section = section,
            Content = content.Trim()
        });
    }
}
