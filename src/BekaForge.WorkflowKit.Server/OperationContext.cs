using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// Input parameters for a dispatcher operation call.
/// Carries the operation name, optional phase target, actor identity,
/// and a flexible parameter bag parsed from the caller's request.
/// </summary>
public sealed record OperationContext
{
    public required string Operation { get; init; }

    /// <summary>The actor making this operation call (used for event/record authoring).</summary>
    public WorkflowActor Actor { get; init; } = WorkflowActor.WorkflowKit;

    /// <summary>Phase ID targeted by this operation, if applicable.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Typed parameter bag. Keys are operation-specific field names.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>Gets a parameter value by key, returning null if not present.</summary>
    public T? Get<T>(string key)
    {
        if (!Parameters.TryGetValue(key, out var raw))
            return default;

        if (raw is T typed)
            return typed;

        if (raw is null)
            return default;

        // Handle common numeric boxing mismatches (e.g. int/long from JSON).
        try
        {
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>Optional trace scope for developer observability. Handlers can add spans to it.</summary>
    public BekaForge.WorkflowKit.Core.Tracing.WorkflowTraceScope? TraceScope { get; init; }

    /// <summary>Gets a required parameter, returning null if missing or unconvertible.</summary>
    public string? GetString(string key) => Get<string>(key);

    /// <summary>Gets a boolean parameter.</summary>
    public bool GetBool(string key, bool defaultValue = false)
    {
        // Must check key presence first — Get<bool> returns default(bool)=false when absent,
        // which is indistinguishable from an explicit false value via the `is bool b` pattern.
        if (!Parameters.ContainsKey(key))
            return defaultValue;
        return Get<bool>(key) is bool b ? b : defaultValue;
    }
}
