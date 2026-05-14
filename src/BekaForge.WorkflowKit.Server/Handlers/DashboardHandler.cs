using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetDashboardSummaryHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetDashboardSummary;

    public OperationResult Execute(OperationContext context) =>
        OperationResult.Ok(WorkflowDashboardSummaryBuilder.Build(store));
}
