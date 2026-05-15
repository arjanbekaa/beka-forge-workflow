using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdBlocker(string subCmd, string? wfRoot, string? phaseId, string? blockerReason, string? blockId, string? blockerResolution)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        switch (subCmd.ToLowerInvariant())
        {
            case "add":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(blockerReason))
                {
                    Console.Error.WriteLine("ERROR: --reason is required.");
                    Environment.Exit(1);
                }

                var addResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.RecordBlocker,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = new Dictionary<string, object?> { ["reason"] = blockerReason }
                });

                if (addResult.Success)
                    Console.WriteLine($"OK - blocker recorded.");
                else
                {
                    Console.Error.WriteLine($"ERROR [{addResult.ErrorCode}]: {addResult.Message}");
                    Environment.Exit(1);
                }
                break;

            case "resolve":
                if (string.IsNullOrWhiteSpace(blockId))
                {
                    Console.Error.WriteLine("ERROR: --blocker-id is required.");
                    Environment.Exit(1);
                }

                var resolveResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.ResolveBlocker,
                    Actor = WorkflowActor.User,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["blockerId"] = blockId,
                        ["resolution"] = blockerResolution ?? "Resolved."
                    }
                });

                if (resolveResult.Success)
                    Console.WriteLine("OK - blocker resolved.");
                else
                {
                    Console.Error.WriteLine($"ERROR [{resolveResult.ErrorCode}]: {resolveResult.Message}");
                    Environment.Exit(1);
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown blocker sub-command: {subCmd}");
                Console.Error.WriteLine("Usage: bfwf blocker add|resolve [...]");
                Environment.Exit(1);
                break;
        }
    }
}
