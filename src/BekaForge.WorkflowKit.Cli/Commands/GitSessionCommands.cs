using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Tracing;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Server.Handlers;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdGit(string? subCommand, string? wfRoot, bool json)
    {
        if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        var (operation, label) = subCommand?.ToLowerInvariant() switch
        {
            "status" => (WorkflowOperations.GetGitStatus, "Git Status"),
            "commits" => (WorkflowOperations.ListGitCommits, "Git Commits"),
            "health" => (WorkflowOperations.GetGitHealth, "Git Health"),
            "record" => (WorkflowOperations.RecordGitActivity, "Git Activity Recorded"),
            _ => (null, null)
        };

        if (operation is null)
        {
            Console.Error.WriteLine("Usage: bfwf git <status|commits|health|record>");
            Environment.Exit(2);
        }

        var ctx = new OperationContext { Operation = operation, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
            return;
        }

        if (result.Success)
        {
            Console.WriteLine($"{label}:");
            var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(jsonOutput);
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }

    internal static void CmdSession(string? subCommand, string? wfRoot, bool json)
    {
        if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        var (operation, label) = subCommand?.ToLowerInvariant() switch
        {
            "list" => (WorkflowOperations.ListSessions, "Sessions"),
            "current" => (WorkflowOperations.GetCurrentSession, "Current Session"),
            "end" => (WorkflowOperations.EndSession, "Session Ended"),
            _ => (null, null)
        };

        if (operation is null)
        {
            Console.Error.WriteLine("Usage: bfwf session <list|current|end> [--session-id SES-0001]");
            Environment.Exit(2);
        }

        var ctx = new OperationContext { Operation = operation, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
            return;
        }

        if (result.Success)
        {
            Console.WriteLine($"{label}:");
            var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(jsonOutput);
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }

    internal static void CmdTimeline(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        var ctx = new OperationContext { Operation = WorkflowOperations.GetTimeline, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Success)
        {
            var root = ToJsonElement(result.Data);
            var entries = root.TryGetProperty("entries", out var entriesElement)
                ? JsonSerializer.Deserialize<List<TimelineEntry>>(entriesElement.GetRawText()) ?? []
                : [];
            var count = root.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : entries.Count;
            CliRenderer.RenderTimeline(entries, count, mode);
        }
        else
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }
    }

    internal static void CmdTrace(string? subCommand, string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        var (operation, label) = subCommand?.ToLowerInvariant() switch
        {
            "status" => (WorkflowOperations.GetTraceStatus, "Trace Status"),
            "list" => (WorkflowOperations.ListTraces, "Trace List"),
            "get" => (WorkflowOperations.GetTrace, "Trace Detail"),
            "clear" => (WorkflowOperations.ClearOldTraces, "Traces Cleared"),
            "set-options" => (WorkflowOperations.SetTraceOptions, "Trace Options Updated"),
            _ => (null, null)
        };

        if (operation is null)
        {
            CliRenderer.Error("Usage: bfwf trace <status|list|get|clear|set-options>", mode);
            Environment.Exit(2);
        }

        var ctx = new OperationContext { Operation = operation, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Success)
        {
            switch (subCommand?.ToLowerInvariant())
            {
                case "status":
                    var statusRoot = ToJsonElement(result.Data);
                    CliRenderer.RenderTraceStatus(
                        GetString(statusRoot, "Mode", "unknown"),
                        GetInt32(statusRoot, "RetentionDays"),
                        GetInt64(statusRoot, "MaxDirectorySizeBytes"),
                        GetInt32(statusRoot, "FileCount"),
                        GetInt32(statusRoot, "RecordCount"),
                        GetInt64(statusRoot, "DirectorySizeBytes"),
                        GetBool(statusRoot, "IsEnabled"),
                        mode);
                    break;
                case "list":
                    var listRoot = ToJsonElement(result.Data);
                    var traces = listRoot.TryGetProperty("traces", out var tracesElement)
                        ? JsonSerializer.Deserialize<List<TraceRecord>>(tracesElement.GetRawText()) ?? []
                        : [];
                    var count = listRoot.TryGetProperty("count", out var traceCountElement) ? traceCountElement.GetInt32() : traces.Count;
                    CliRenderer.RenderTraceList(traces, count, mode);
                    break;
                default:
                    Console.WriteLine($"{label}:");
                    Console.WriteLine(JsonSerializer.Serialize(result.Data, CreatePrettyJsonOptions()));
                    break;
            }
        }
        else
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }
    }
}
