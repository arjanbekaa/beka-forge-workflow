using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.explain_operation</c>.
///
/// Takes a canonical operation name and returns its manifest entry
/// with full metadata: access level, category, summary, and handler type.
/// </summary>
public sealed class ExplainOperationHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ExplainOperation;

    public OperationResult Execute(OperationContext context)
    {
        var operationName = context.GetString("operation");
        if (string.IsNullOrWhiteSpace(operationName))
            return OperationResult.Fail("ValidationFailed", "Parameter 'operation' is required.");

        var entry = OperationManifestCatalog.GetAll()
            .FirstOrDefault(e => e.OperationName.Equals(operationName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return OperationResult.Fail("NotFound",
                $"No manifest entry found for operation '{operationName}'. " +
                "Use workflow.get_operation_manifest to list all operations.");

        return OperationResult.Ok(entry);
    }
}
