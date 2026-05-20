using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;

partial class Program
{
    internal static void CmdDocs(string subCmd, string? wfRoot, string? phaseId, CliOutputMode mode, bool json)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var section = ParseFlag(CommandLineArgs, "--section");
        var content = ParseFlag(CommandLineArgs, "--content");
        var dispatcher = new OperationDispatcher(new WorkflowStore(wfRoot));

        switch ((subCmd ?? string.Empty).ToLowerInvariant())
        {
            case "show":
            {
                if (string.IsNullOrWhiteSpace(section))
                {
                    CliRenderer.Error("--section is required.", mode);
                    Environment.Exit(1);
                }

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.GetProjectGuidance,
                    Actor = WorkflowActor.Planner,
                    Parameters = new Dictionary<string, object?> { ["section"] = section }
                });

                if (json)
                {
                    WriteJson(result.Success ? result.Data! : new { result.ErrorCode, result.Message });
                    if (!result.Success) Environment.Exit(1);
                    return;
                }

                if (!result.Success)
                {
                    CliRenderer.Error($"{result.ErrorCode}: {result.Message}", mode);
                    Environment.Exit(1);
                }

                Console.WriteLine(result.Data);
                break;
            }

            case "set":
            {
                if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(content))
                {
                    CliRenderer.Error("--section and --content are required.", mode);
                    Environment.Exit(1);
                }

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.SetProjectGuidance,
                    Actor = WorkflowActor.Planner,
                    PhaseId = phaseId,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["section"] = section,
                        ["content"] = content
                    }
                });

                if (json)
                {
                    WriteJson(result.Success ? result.Data! : new { result.ErrorCode, result.Message });
                    if (!result.Success) Environment.Exit(1);
                    return;
                }

                if (!result.Success)
                {
                    CliRenderer.Error($"{result.ErrorCode}: {result.Message}", mode);
                    Environment.Exit(1);
                }

                CliRenderer.Ok($"Project guidance updated for {section}.", mode);
                break;
            }

            default:
                CliRenderer.Error("Usage: bfwf docs show|set --section <known-limitations|extension-guide|final-review|documentation-policy> [--content \"...\"]", mode);
                Environment.Exit(1);
                break;
        }
    }
}
