using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Processes pending operations from the offline operation inbox.
/// Each pending .operation.json is validated, dispatched through the normal
/// handler pipeline, and moved to processed/ or failed/.
/// </summary>
public sealed class ProcessInboxHandler : IOperationHandler
{
    private readonly string _workflowRoot;
    private readonly OperationDispatcher _dispatcher;

    public string OperationName => WorkflowOperations.ProcessInbox;

    public ProcessInboxHandler(string workflowRoot, OperationDispatcher dispatcher)
    {
        _workflowRoot = workflowRoot;
        _dispatcher = dispatcher;
    }

    public OperationResult Execute(OperationContext context)
    {
        var processor = new InboxProcessor(_workflowRoot, _dispatcher);
        var result = processor.ProcessAll(context.Actor);

        return OperationResult.Ok(result);
    }
}
