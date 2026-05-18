using BekaForge.WorkflowKit.Markdown;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Cli;

public sealed record TuiBootstrapResult(string? WorkflowRoot, bool InitializedNow, bool Cancelled);

public static class TuiBootstrap
{
    public static TuiBootstrapResult EnsureWorkflowReady(
        string startDir,
        TextReader input,
        TextWriter output,
        bool allowAncestorDiscovery = true)
    {
        var discoveredRoot = DiscoverWorkflowRoot(startDir, allowAncestorDiscovery);
        if (discoveredRoot is not null)
            return new TuiBootstrapResult(discoveredRoot, false, false);

        output.Write("Beka Forge Workflow is not set up in this folder. Set it up now? [Y/n] ");
        var response = input.ReadLine();
        output.WriteLine();

        if (!ShouldInitialize(response))
            return new TuiBootstrapResult(null, false, true);

        var workflowRoot = InitializeWorkflowInFolder(startDir, output);

        output.WriteLine("Beka Forge Workflow initialized for this folder.");
        output.WriteLine("Starting TUI...");
        return new TuiBootstrapResult(workflowRoot, true, false);
    }

    public static string InitializeWorkflowInFolder(string startDir, TextWriter? output = null)
    {
        var workflowRoot = Path.GetFullPath(startDir);
        if (WorkflowLayout.IsInitialized(workflowRoot))
            return workflowRoot;

        ConsoleWaitIndicator? wait = null;
        if (output is not null)
            wait = ConsoleWaitIndicator.Start(output, "Initializing workflow");

        try
        {
            var assetName = DeriveAssetName(workflowRoot);
            var initializer = new WorkflowInitializer(workflowRoot);
            initializer.Initialize(assetName);

            var store = new WorkflowStore(workflowRoot);
            var sync = new MarkdownSyncService(store);
            sync.SyncAll();
            wait?.Complete(" ready.");
            return workflowRoot;
        }
        finally
        {
            wait?.Dispose();
        }
    }

    public static string BuildUninitializedDetailText(string startDir)
    {
        var workflowRoot = Path.GetFullPath(startDir);
        var folderName = DeriveAssetName(workflowRoot);

        return string.Join(Environment.NewLine, new[]
        {
            "  No workflow is initialized for this folder.",
            string.Empty,
            $"  Folder: {folderName}",
            $"  Path:   {workflowRoot}",
            string.Empty,
            "  Press I to initialize Beka Forge Workflow here.",
            "  The asset name will default to this folder name.",
            string.Empty,
            "  Available keys:",
            "    I initialize workflow",
            "    R refresh discovery",
            "    Q quit"
        });
    }

    public static bool ShouldInitialize(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return true;

        var normalized = response.Trim();
        return !normalized.Equals("n", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    public static string DeriveAssetName(string startDir)
    {
        var fullPath = Path.GetFullPath(startDir);
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? "Beka Forge Workflow Project" : name;
    }

    public static string? DiscoverWorkflowRoot(string startDir, bool allowAncestorDiscovery = true)
    {
        var dir = Path.GetFullPath(startDir);

        if (!allowAncestorDiscovery)
            return WorkflowLayout.IsInitialized(dir) ? dir : null;

        while (true)
        {
            if (WorkflowLayout.IsInitialized(dir))
                return dir;

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                return null;

            dir = parent;
        }
    }
}
