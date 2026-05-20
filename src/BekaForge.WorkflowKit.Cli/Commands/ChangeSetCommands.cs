using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;

partial class Program
{
    internal static void CmdChangeSet(string subCmd, string? wfRoot, bool json, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var file = ParseFlag(CommandLineArgs, "--file")
            ?? ParseFlag(CommandLineArgs, "--file-path")
            ?? ParseFlag(CommandLineArgs, "--path");
        if (string.IsNullOrWhiteSpace(file))
        {
            CliRenderer.Error("--file is required.", mode);
            Environment.Exit(2);
        }

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var dryRun = HasFlag(CommandLineArgs, "--dry-run");
        var syncMarkdown = HasFlag(CommandLineArgs, "--sync-markdown");

        var operation = subCmd.ToLowerInvariant() switch
        {
            "validate" => WorkflowOperations.ValidateChangeSet,
            "apply" => WorkflowOperations.ApplyChangeSet,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(operation))
        {
            CliRenderer.Error("Usage: bfwf changeset validate|apply --file <path> [--dry-run] [--sync-markdown] [--json]", mode);
            Environment.Exit(2);
        }

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            Parameters = new Dictionary<string, object?>
            {
                ["file"] = file,
                ["dryRun"] = dryRun,
                ["syncMarkdown"] = syncMarkdown
            }
        });

        if (!result.Success)
        {
            if (json)
                WriteJson(new { success = false, errorCode = result.ErrorCode, message = result.Message });
            else
                CliRenderer.Error(result.Message ?? result.ErrorCode ?? "ChangeSet command failed.", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result.Data);
        }
        else if (result.Data is WorkflowChangeSetValidationReport validation)
        {
            RenderValidationReport(validation);
        }
        else if (result.Data is WorkflowChangeSetApplyReport apply)
        {
            RenderApplyReport(apply);
        }

        var valid = result.Data switch
        {
            WorkflowChangeSetValidationReport validation => validation.IsValid,
            WorkflowChangeSetApplyReport apply => apply.Validation.IsValid && (apply.Applied || apply.DryRun),
            _ => true
        };

        if (!valid)
            Environment.Exit(1);
    }

    private static void RenderValidationReport(WorkflowChangeSetValidationReport report)
    {
        Console.WriteLine($"ChangeSet validation: {(report.IsValid ? "VALID" : "INVALID")}");
        if (!string.IsNullOrWhiteSpace(report.Title))
            Console.WriteLine($"Title: {report.Title}");
        Console.WriteLine($"Operations: {report.OperationPreviews.Count}");

        if (report.Issues.Count > 0)
        {
            Console.WriteLine("Issues:");
            foreach (var issue in report.Issues)
                Console.WriteLine($"  - {FormatChangeSetIssue(issue)}");
        }

        if (report.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in report.Warnings)
                Console.WriteLine($"  - {FormatChangeSetIssue(warning)}");
        }

        foreach (var preview in report.OperationPreviews)
            Console.WriteLine($"  [{preview.OperationIndex}] {preview.Type}: {preview.Summary}");
    }

    private static void RenderApplyReport(WorkflowChangeSetApplyReport report)
    {
        Console.WriteLine(report.DryRun
            ? "ChangeSet dry-run complete."
            : report.Applied
                ? "ChangeSet applied."
                : "ChangeSet not applied.");

        RenderValidationReport(report.Validation);

        if (report.AppliedOperations.Count > 0)
        {
            Console.WriteLine("Applied operations:");
            foreach (var operation in report.AppliedOperations)
                Console.WriteLine($"  [{operation.OperationIndex}] {operation.Type}: {(operation.Success ? "OK" : "FAILED")} {operation.CreatedId}");
        }

        if (report.CreatedIds.Count > 0)
        {
            Console.WriteLine("Created IDs:");
            foreach (var (refId, createdId) in report.CreatedIds)
                Console.WriteLine($"  {refId} = {createdId}");
        }

        if (report.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in report.Warnings)
                Console.WriteLine($"  - {FormatChangeSetIssue(warning)}");
        }
    }

    private static string FormatChangeSetIssue(WorkflowChangeSetIssue issue)
    {
        var prefix = issue.OperationIndex is null ? issue.Code : $"{issue.Code} at operation {issue.OperationIndex}";
        return $"{prefix}: {issue.Message}";
    }
}
