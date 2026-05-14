using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.search_operations</c>.
///
/// Takes a keyword query and returns matching OperationManifestEntry items
/// whose name, category, or summary contain the keyword.
/// </summary>
public sealed class SearchOperationsHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.SearchOperations;

    public OperationResult Execute(OperationContext context)
    {
        var keyword = context.GetString("keyword");
        if (string.IsNullOrWhiteSpace(keyword))
            return OperationResult.Fail("ValidationFailed", "Parameter 'keyword' is required.");

        var lowerKeyword = keyword.ToLowerInvariant();

        var matches = OperationManifestCatalog.GetAll()
            .Where(e =>
                e.OperationName.ToLowerInvariant().Contains(lowerKeyword) ||
                e.Category.ToLowerInvariant().Contains(lowerKeyword) ||
                e.Summary.ToLowerInvariant().Contains(lowerKeyword))
            .OrderBy(e => e.OperationName)
            .ToList();

        return OperationResult.Ok(matches);
    }
}
