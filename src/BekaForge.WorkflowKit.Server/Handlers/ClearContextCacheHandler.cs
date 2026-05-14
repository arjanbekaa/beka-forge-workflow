using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Clears all non-pinned packages from the context cache.
/// Pinned packages (current_work, phase without taskType) survive.
/// Source JSON/JSONL is unaffected — this is safe and non-destructive.
/// </summary>
public sealed class ClearContextCacheHandler : IOperationHandler
{
    private readonly ContextPackageCache _cache;

    public string OperationName => WorkflowOperations.ClearContextCache;

    public ClearContextCacheHandler(ContextPackageCache cache)
    {
        _cache = cache;
    }

    public OperationResult Execute(OperationContext context)
    {
        var clearPinned = context.GetBool("clearPinned", defaultValue: false);
        var removed = _cache.Clear(clearPinned);
        return OperationResult.Ok(new
        {
            removed,
            remaining = _cache.PackageCount,
            message = clearPinned
                ? $"Cleared {removed} packages (including pinned). {_cache.PackageCount} remaining."
                : $"Cleared {removed} non-pinned packages. {_cache.PackageCount} remaining (pinned)."
        });
    }
}
