using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

namespace BekaForge.WorkflowKit.Mcp;

/// <summary>
/// Convenience registry mapping project IDs to workflow root paths.
/// This is planning metadata only and is not workflow source of truth.
/// </summary>
public sealed class ProjectRegistry
{
    private readonly string _registryPath;
    private readonly Dictionary<string, string> _entries;

    public ProjectRegistry(string registryLocation)
    {
        _registryPath = ResolveRegistryPath(registryLocation);
        _entries = Load();
    }

    public string RegistryPath => _registryPath;

    /// <summary>Register a project ID to a root path.</summary>
    public void Add(string projectId, string rootPath)
    {
        var normalizedId = projectId.Trim();
        var normalizedRoot = Path.GetFullPath(rootPath.Trim());

        if (!Directory.Exists(normalizedRoot))
            throw new ArgumentException($"Root path does not exist: {normalizedRoot}");

        if (!IsValidWorkflowRoot(normalizedRoot))
            throw new ArgumentException(
                $"Path is not a valid WorkflowKit project (no .workflowkit/workflow.json): {normalizedRoot}");

        _entries[normalizedId] = normalizedRoot;
        Save();
    }

    /// <summary>Remove a registered project ID.</summary>
    public bool Remove(string projectId)
    {
        var removed = _entries.Remove(projectId.Trim());
        if (removed)
            Save();

        return removed;
    }

    /// <summary>Resolve a project ID to its root path. Returns null if not registered.</summary>
    public string? Resolve(string projectId)
    {
        return _entries.TryGetValue(projectId.Trim(), out var root) ? root : null;
    }

    /// <summary>List all registered project IDs.</summary>
    public IReadOnlyDictionary<string, string> List()
    {
        return _entries;
    }

    /// <summary>Validate that a root path is a valid WorkflowKit project.</summary>
    public static bool IsValidWorkflowRoot(string rootPath)
    {
        try
        {
            var full = Path.GetFullPath(rootPath);
            return WorkflowLayout.IsInitialized(full);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveRegistryPath(string registryLocation)
    {
        if (string.IsNullOrWhiteSpace(registryLocation))
            throw new ArgumentException("Registry location must not be empty.", nameof(registryLocation));

        var full = Path.GetFullPath(registryLocation);
        if (full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return full;

        return Path.Combine(full, WorkflowLayout.WorkflowKitDir, "mcp-registry.json");
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_registryPath))
            {
                var json = File.ReadAllText(_registryPath);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (entries is not null)
                    return new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Registry metadata is disposable. Reset on corruption.
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_registryPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_registryPath, json);
    }
}
