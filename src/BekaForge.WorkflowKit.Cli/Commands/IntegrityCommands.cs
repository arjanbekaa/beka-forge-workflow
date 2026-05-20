using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;

partial class Program
{
    internal static void CmdIntegrity(string? wfRoot, bool json, CliOutputMode mode)
    {
        var report = DispatchIntegrityOperation(wfRoot, mode, WorkflowOperations.GetIntegrityReport);

        if (json)
        {
            WriteJson(report);
            return;
        }

        RenderIntegrityReport(report, mode, title: "Integrity Report");
    }

    internal static void CmdReleaseGate(string? wfRoot, bool json, CliOutputMode mode)
    {
        var gate = DispatchReleaseGateOperation(wfRoot, mode);

        if (json)
        {
            WriteJson(gate);
        }
        else
        {
            Console.WriteLine($"Release gate: {(gate.Passed ? "PASS" : "FAIL")}");
            Console.WriteLine($"Blocking issues: {gate.BlockingIssueCount}");
            Console.WriteLine();
            RenderIntegrityReport(gate.Report, mode, title: "Integrity Report");
        }

        if (!gate.Passed)
            Environment.Exit(1);
    }

    internal static void CmdReleaseReport(string? wfRoot, bool json, CliOutputMode mode)
    {
        var report = DispatchReleaseReadinessOperation<ReleaseCandidateReport>(
            wfRoot,
            mode,
            WorkflowOperations.GetReleaseCandidateReport,
            "Release report operation did not return a release candidate report.");

        if (json)
        {
            WriteJson(report);
            return;
        }

        Console.WriteLine("Release Candidate Report");
        Console.WriteLine("========================");
        Console.WriteLine($"Generated: {report.GeneratedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Blocking reasons: {report.BlockingReasons.Count}");
        Console.WriteLine($"Warnings: {report.WarningReasons.Count}");
        Console.WriteLine($"Support entries: {report.SupportMatrix.Count}");
        Console.WriteLine($"Documentation policy: {report.DocumentationPolicy}");
        Console.WriteLine($"Documentation blocks release: {report.DocumentationCoverageBlocksRelease}");
        Console.WriteLine();

        if (report.BlockingReasons.Count > 0)
        {
            Console.WriteLine("Blocking reasons:");
            foreach (var reason in report.BlockingReasons)
                Console.WriteLine($"- {reason}");
            Console.WriteLine();
        }

        if (report.WarningReasons.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var reason in report.WarningReasons)
                Console.WriteLine($"- {reason}");
            Console.WriteLine();
        }

        RenderIntegrityReport(report.IntegrityReport, mode, "Integrity Report");
    }

    internal static void CmdValidatePublicRelease(string? wfRoot, bool json, CliOutputMode mode)
    {
        var result = DispatchReleaseReadinessOperation<PublicReleaseValidationResult>(
            wfRoot,
            mode,
            WorkflowOperations.ValidatePublicRelease,
            "Public release validation operation did not return a validation result.");

        if (json)
        {
            WriteJson(result);
        }
        else
        {
            Console.WriteLine($"Public release: {(result.Passed ? "PASS" : "FAIL")}");
            Console.WriteLine($"Blocking reasons: {result.BlockingIssueCount}");
            if (result.Report.BlockingReasons.Count > 0)
            {
                Console.WriteLine();
                foreach (var reason in result.Report.BlockingReasons)
                    Console.WriteLine($"- {reason}");
            }
        }

        if (!result.Passed)
            Environment.Exit(1);
    }

    private static WorkflowIntegrityReport DispatchIntegrityOperation(string? wfRoot, CliOutputMode mode, string operationName)
    {
        EnsureWorkflowRootForIntegrity(wfRoot, mode);

        var store = new WorkflowStore(wfRoot!);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = operationName,
            Actor = WorkflowActor.Implementer
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? result.ErrorCode ?? "Integrity command failed.", mode);
            Environment.Exit(1);
        }

        return result.Data as WorkflowIntegrityReport
            ?? throw new InvalidOperationException($"Operation '{operationName}' did not return an integrity report.");
    }

    private static WorkflowReleaseGateResult DispatchReleaseGateOperation(string? wfRoot, CliOutputMode mode)
    {
        EnsureWorkflowRootForIntegrity(wfRoot, mode);

        var store = new WorkflowStore(wfRoot!);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateReleaseGate,
            Actor = WorkflowActor.Implementer
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? result.ErrorCode ?? "Release gate command failed.", mode);
            Environment.Exit(1);
        }

        return result.Data as WorkflowReleaseGateResult
            ?? throw new InvalidOperationException("Release gate operation did not return a release gate result.");
    }

    private static void EnsureWorkflowRootForIntegrity(string? wfRoot, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }
    }

    private static T DispatchReleaseReadinessOperation<T>(
        string? wfRoot,
        CliOutputMode mode,
        string operationName,
        string errorMessage)
    {
        EnsureWorkflowRootForIntegrity(wfRoot, mode);

        var store = new WorkflowStore(wfRoot!);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = operationName,
            Actor = WorkflowActor.Implementer
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? result.ErrorCode ?? "Release readiness command failed.", mode);
            Environment.Exit(1);
        }

        return result.Data is T typed
            ? typed
            : throw new InvalidOperationException(errorMessage);
    }

    private static void RenderIntegrityReport(WorkflowIntegrityReport report, CliOutputMode mode, string title)
    {
        var summary = report.Summary;

        Console.WriteLine(title);
        Console.WriteLine(new string('=', title.Length));
        Console.WriteLine($"Workflow ID: {report.WorkflowId ?? "(unknown)"}");
        Console.WriteLine($"Generated:   {report.GeneratedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Total:       {summary.TotalIssues}");
        Console.WriteLine($"Errors:      {summary.ErrorCount}");
        Console.WriteLine($"Warnings:    {summary.WarningCount}");
        Console.WriteLine($"Info:        {summary.InfoCount}");
        Console.WriteLine($"Blocking:    {summary.ReleaseBlockingCount}");
        Console.WriteLine();

        if (report.Issues.Count == 0)
        {
            Console.WriteLine("No integrity issues found.");
            return;
        }

        Console.WriteLine("Issues:");
        foreach (var issue in report.Issues)
            Console.WriteLine($"  - {FormatIntegrityIssue(issue)}");
    }

    private static string FormatIntegrityIssue(WorkflowIntegrityIssue issue)
    {
        var details = new List<string>
        {
            issue.Code,
            issue.Severity.ToString(),
            issue.Category.ToString(),
            issue.Message
        };

        if (!string.IsNullOrWhiteSpace(issue.Path))
            details.Add($"path={issue.Path}");

        if (issue.LineNumber is not null)
            details.Add($"line={issue.LineNumber}");

        if (!string.IsNullOrWhiteSpace(issue.PhaseId))
            details.Add($"phase={issue.PhaseId}");

        if (!string.IsNullOrWhiteSpace(issue.RecordId))
            details.Add($"record={issue.RecordId}");

        if (!string.IsNullOrWhiteSpace(issue.EntityId))
            details.Add($"entity={issue.EntityId}");

        if (issue.IsReleaseBlocking)
            details.Add("release-blocking");

        return string.Join(" | ", details);
    }
}
