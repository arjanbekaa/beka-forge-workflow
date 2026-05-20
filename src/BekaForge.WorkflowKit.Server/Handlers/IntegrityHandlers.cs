using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetIntegrityReportHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetIntegrityReport;

    public OperationResult Execute(OperationContext context)
    {
        var report = BuildIntegrityReport(store);
        return OperationResult.Ok(report, $"Integrity report generated with {report.Summary.TotalIssues} issues.");
    }

    internal static WorkflowIntegrityReport BuildIntegrityReport(WorkflowStore store)
    {
        var service = new WorkflowIntegrityService(store);
        var reports = new[]
        {
            service.CheckPhaseRegistry(),
            service.CheckAppendOnlyLogs(),
            service.CheckEvidenceReferences(),
            service.CheckPhaseCompletionEvidence(),
            service.CheckMarkdownMirrorDrift(),
            service.CheckReadModelStaleness(),
            service.CheckOperationMetadataConsistency()
        };

        var workflowId = reports.Select(static report => report.WorkflowId)
            .FirstOrDefault(static workflowId => !string.IsNullOrWhiteSpace(workflowId));

        return WorkflowIntegrityReport.Create(
            workflowId,
            reports.SelectMany(static report => report.Issues),
            reports.Max(static report => report.GeneratedUtc));
    }
}

public sealed class ValidateReleaseGateHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ValidateReleaseGate;

    public OperationResult Execute(OperationContext context)
    {
        var report = GetIntegrityReportHandler.BuildIntegrityReport(store);
        var blockingIssueCount = report.Summary.ReleaseBlockingCount;
        var passed = blockingIssueCount == 0;

        return OperationResult.Ok(
            new WorkflowReleaseGateResult
            {
                Report = report,
                Passed = passed,
                BlockingIssueCount = blockingIssueCount
            },
            passed
                ? "Release gate passed."
                : $"Release gate failed with {blockingIssueCount} release-blocking integrity issues.");
    }
}

public sealed class GetReleaseCandidateReportHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PublicReleaseReadinessService _service = new(store);

    public string OperationName => WorkflowOperations.GetReleaseCandidateReport;

    public OperationResult Execute(OperationContext context)
    {
        var report = _service.BuildReport();
        return OperationResult.Ok(
            report,
            $"Release candidate report generated with {report.BlockingReasons.Count} blocking reason(s).");
    }
}

public sealed class ValidatePublicReleaseHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PublicReleaseReadinessService _service = new(store);

    public string OperationName => WorkflowOperations.ValidatePublicRelease;

    public OperationResult Execute(OperationContext context)
    {
        var result = _service.Validate();
        return OperationResult.Ok(
            result,
            result.Passed
                ? "Public release validation passed."
                : $"Public release validation failed with {result.BlockingIssueCount} blocking reason(s).");
    }
}
