using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Returns the current cache diagnostics snapshot: hit rate, package count,
/// memory estimate, LRU order, and configuration limits.
/// </summary>
public sealed class GetCacheStatusHandler : IOperationHandler
{
    private readonly ContextPackageCache _cache;

    public string OperationName => WorkflowOperations.GetCacheStatus;

    public GetCacheStatusHandler(ContextPackageCache cache)
    {
        _cache = cache;
    }

    public OperationResult Execute(OperationContext context)
    {
        var diagnostics = _cache.GetDiagnostics();
        return OperationResult.Ok(diagnostics);
    }
}
