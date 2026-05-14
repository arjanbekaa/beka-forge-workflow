using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Returns the current status of the offline operation inbox:
/// pending count, processed count, failed count, oldest pending timestamp.
/// </summary>
public sealed class GetInboxStatusHandler : IOperationHandler
{
    private readonly string _workflowRoot;

    public string OperationName => WorkflowOperations.GetInboxStatus;

    public GetInboxStatusHandler(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
    }

    public OperationResult Execute(OperationContext context)
    {
        // InboxProcessor needs a dispatcher for GetStatus, but GetStatus
        // is a pure read operation that doesn't need the dispatcher.
        // Pass null dispatcher — GetStatus won't dispatch anything.
        var processor = new InboxProcessor(_workflowRoot, null!);
        var status = processor.GetStatus();
        return OperationResult.Ok(status);
    }
}
