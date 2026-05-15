using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdInit(string rootPath, string assetName, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            Console.Error.WriteLine("ERROR: Asset name is required. Usage: bfwf init \"Asset Name\" [--root <path>] [--force]");
            Environment.Exit(1);
        }

        var init = new WorkflowInitializer(rootPath);
        if (init.IsInitialized() && !overwrite)
        {
            Console.Error.WriteLine($"ERROR: Workflow already exists at '{rootPath}'. Use --force to overwrite.");
            Environment.Exit(1);
        }

        try
        {
            var state = init.Initialize(assetName, force: overwrite);
            Console.WriteLine("Beka Forge Workflow initialized.");
            Console.WriteLine($"  Asset:     {state.AssetName}");
            Console.WriteLine($"  ID:        {state.WorkflowId}");
            Console.WriteLine($"  Root:      {state.RootPath}");
            Console.WriteLine($"  Created:   {state.CreatedUtc:yyyy-MM-dd HH:mm} UTC");
            Console.WriteLine($"  Schema:    {state.SchemaVersion}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Environment.Exit(1);
        }
    }

    internal static void CmdServer(string subCmd, string rootPath, int serverPort)
    {
        // Re-parse root and discover workflowRoot from CommandLineArgs so we don't
        // rely on captured top-level variables.
        var rootOverride = ParseFlag(CommandLineArgs, "--root");
        var discoveredWorkflowRoot = DiscoverWorkflowRoot(rootOverride ?? rootPath);

        switch (subCmd.ToLowerInvariant())
        {
            case "start":
                var effectivePort = serverPort > 0 ? serverPort : LocalServerBootstrap.DefaultPort;
                var effectiveRoot = !string.IsNullOrWhiteSpace(rootOverride) ? rootOverride : rootPath;
                var (fileName, arguments) = LocalServerBootstrap.GetServerLaunchCommand(effectiveRoot, effectivePort);

                Console.WriteLine($"Starting Beka Forge Workflow server on http://localhost:{effectivePort}");
                Console.WriteLine($"  Root: {effectiveRoot}");

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null)
                {
                    Console.Error.WriteLine("ERROR: Failed to start Beka Forge Workflow server.");
                    Environment.Exit(1);
                }

                Console.WriteLine($"  PID:  {proc.Id}");
                Console.WriteLine($"  URL:  http://localhost:{effectivePort}");
                break;

            case "stop":
                Console.WriteLine("Send POST /api/shutdown to the server, or press Ctrl+C in the server terminal.");
                break;

            case "status":
                if (discoveredWorkflowRoot is not null && WorkflowLayout.IsInitialized(discoveredWorkflowRoot))
                    Console.WriteLine($"Beka Forge Workflow is initialized at: {discoveredWorkflowRoot}");
                else
                    Console.WriteLine("No Beka Forge Workflow project is initialized in this directory tree.");
                break;

            default:
                Console.Error.WriteLine($"Unknown server sub-command: {subCmd}");
                Console.Error.WriteLine("Usage: bfwf server start|stop|status [--root <path>] [--port <num>]");
                Environment.Exit(1);
                break;
        }
    }
}
