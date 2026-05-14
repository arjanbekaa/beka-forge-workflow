using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.get_operation_manifest</c>.
///
/// Returns the full operation manifest — a deterministic, code-owned catalog
/// of every WorkflowKit operation with its access level, category, summary,
/// and handler type mapping.
/// </summary>
public sealed class GetOperationManifestHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetOperationManifest;

    public OperationResult Execute(OperationContext context)
    {
        var manifest = OperationManifestCatalog.Generate();
        return OperationResult.Ok(manifest);
    }
}
