using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdProcessInbox(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var ctx = new OperationContext { Operation = WorkflowOperations.ProcessInbox, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Success && result.Data is ProcessInboxResult inboxResult)
        {
            CliRenderer.RenderProcessInbox(inboxResult, mode);
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }

    internal static void CmdInboxStatus(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var ctx = new OperationContext { Operation = WorkflowOperations.GetInboxStatus, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Success && result.Data is InboxStatus status)
        {
            CliRenderer.RenderInboxStatus(
                status.PendingCount, status.ProcessedCount, status.FailedCount,
                status.InboxAvailable, status.OldestPendingUtc?.UtcDateTime, status.PendingFiles, mode);
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }

    internal static void CmdAuditPaths(string? wfRoot, bool json)
    {
        if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var ctx = new OperationContext { Operation = WorkflowOperations.AuditProtectedPaths, Actor = WorkflowActor.Implementer };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
            return;
        }

        if (result.Success && result.Data is ProtectedPathAuditResult auditResult)
        {
            Console.WriteLine($"Protected path audit: {(auditResult.AllProtected ? "PASS" : "FAIL")}");
            Console.WriteLine($"  Summary: {auditResult.Summary}");
            foreach (var entry in auditResult.Paths)
            {
                var icon = entry.IsProtected
                    ? (entry.Status == "ok" || entry.Status == "integrity_ok" ? "  [OK]" : "  [!]")
                    : "  [--]";
                Console.WriteLine($"{icon} {entry.Path} ({entry.Status})");
            }
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }

    internal static void CmdRepair(string? wfRoot, bool json)
    {
        if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var authoritative = HasFlag(CommandLineArgs, "--authoritative");
        var ctx = new OperationContext
        {
            Operation = authoritative
                ? WorkflowOperations.RepairAuthoritativeIntegrity
                : WorkflowOperations.RepairConsistency,
            Actor = WorkflowActor.Implementer
        };
        var result = dispatcher.Dispatch(ctx);

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
            return;
        }

        if (result.Success)
        {
            Console.WriteLine($"Repair results: {result.Data}");
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }
}
