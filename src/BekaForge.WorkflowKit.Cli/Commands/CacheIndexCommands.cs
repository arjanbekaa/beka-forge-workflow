using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text;
using System.Text.Json;

partial class Program
{
    internal static void CmdIndexHealth(string? root, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.RebuildContextIndex
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        var indexDir = Path.Combine(root, ".workflowkit", "index");
        var files = Directory.Exists(indexDir)
            ? Directory.GetFiles(indexDir)
                .Select(file => new FileInfo(file))
                .Select(info => (info.Name, info.Length, info.LastWriteTime))
                .ToList()
            : [];
        var health = result.Data as IndexHealth ?? throw new InvalidOperationException("Expected IndexHealth result.");
        CliRenderer.RenderIndexHealth(health, files, mode);

        Console.WriteLine();
        Console.WriteLine("Note: .workflowkit/index/* is a rebuildable read model - not source of truth.");
    }

    internal static void CmdCacheStatus(string? root, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (root is null) { CliRenderer.Error("No workflow found.", mode); Environment.Exit(1); }

        // Try the server first ? it owns the single shared cache for this project
        var serverStatus = LocalServerBootstrap.GetStatus(root);
        if (serverStatus.IsRunning)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = http.PostAsync(
                    $"http://localhost:{LocalServerBootstrap.DefaultPort}/api/workflow/workflow.get_cache_status",
                    content).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    var jsonStr = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (json)
                    {
                        Console.WriteLine(jsonStr);
                        return;
                    }
                    using var doc = JsonDocument.Parse(jsonStr);
                    var data = doc.RootElement.GetProperty("data");
                    var pkgCount = data.GetProperty("packageCount").GetInt32();
                    var hitRate = data.GetProperty("hitRate").GetDouble();
                    Console.WriteLine($"Server cache: {pkgCount} pkgs, hit rate {hitRate:P0}");
                    return;
                }
            }
            catch { /* fall through to local dispatcher */ }
        }

        var store = new WorkflowStore(root);
        var settingsPath = Path.Combine(root, ".workflowkit", "cache-settings.json");
        var settingsFileExists = File.Exists(settingsPath);
        var settings = CacheSettings.Load(settingsPath);
        var cache = new ContextPackageCache(settings);
        var dispatcher = new OperationDispatcher(store, cache);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.GetCacheStatus
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Data is CacheDiagnostics diagnostics)
            CliRenderer.RenderCacheStatus(diagnostics, settings, settingsFileExists, mode);
        else
            Console.WriteLine(JsonSerializer.Serialize(result.Data, CreatePrettyJsonOptions()));
    }

    internal static void CmdCacheClear(string? root)
    {
        if (root is null) { Console.Error.WriteLine("ERROR: No workflow found."); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var settings = CacheSettings.Load(Path.Combine(root, ".workflowkit", "cache-settings.json"));
        var cache = new ContextPackageCache(settings);
        var dispatcher = new OperationDispatcher(store, cache);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ClearContextCache
        });

        if (!result.Success)
        {
            Console.Error.WriteLine($"ERROR: {result.Message}");
            Environment.Exit(1);
        }

        var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("Cache Cleared");
        Console.WriteLine("------------");
        Console.WriteLine(jsonOutput);
    }

    internal static void CmdSubPhase(string subCmd, string? root, string? phaseId, string? subPhaseId)
    {
        if (root is null) { Console.Error.WriteLine("ERROR: No workflow found."); Environment.Exit(1); }
        if (string.IsNullOrWhiteSpace(phaseId)) { Console.Error.WriteLine("ERROR: --phase is required."); Environment.Exit(1); }
        if (string.IsNullOrWhiteSpace(subPhaseId)) { Console.Error.WriteLine("ERROR: sub-phase ID is required."); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.UpdateSubPhaseStatus,
            PhaseId   = phaseId,
            Parameters = new Dictionary<string, object?>
            {
                ["subPhaseId"] = subPhaseId,
                ["status"] = subCmd
            }
        });

        if (!result.Success)
        {
            Console.Error.WriteLine($"ERROR: {result.Message}");
            Environment.Exit(1);
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
