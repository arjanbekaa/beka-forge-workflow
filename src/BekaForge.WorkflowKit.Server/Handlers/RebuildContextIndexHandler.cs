using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.rebuild_context_index</c>.
///
/// Rebuilds the SQLite context index from authoritative JSON/JSONL sources.
/// Returns an IndexHealth summary with record counts and any errors.
/// </summary>
public sealed class RebuildContextIndexHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RebuildContextIndex;

    public OperationResult Execute(OperationContext context)
    {
        var builder = new ContextIndexBuilder(store.WorkflowRoot);
        var health = builder.Rebuild();
        return OperationResult.Ok(health);
    }
}
