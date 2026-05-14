using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.recommend_operation</c>.
///
/// Takes a natural-language task description and returns an OperationRecommendation
/// with ranked operation matches, safety warnings, and safer alternatives
/// where appropriate.
/// </summary>
public sealed class RecommendOperationHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RecommendOperation;

    public OperationResult Execute(OperationContext context)
    {
        var task = context.GetString("task");
        if (string.IsNullOrWhiteSpace(task))
            return OperationResult.Fail("ValidationFailed", "Parameter 'task' is required.");

        var recommendation = ToolRoutingCatalog.Recommend(task);
        return OperationResult.Ok(recommendation);
    }
}
