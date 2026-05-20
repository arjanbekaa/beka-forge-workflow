using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Markdown;
using BekaForge.WorkflowKit.Mcp;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class WorkflowIntegrityOperationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public WorkflowIntegrityOperationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-integrity-ops-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("Integrity Operation Tests");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void IntegrityOperations_AppearInManifestRoutingAndMcpMapping()
    {
        Assert.Contains(OperationManifestCatalog.GetAll(), entry => entry.OperationName == WorkflowOperations.GetIntegrityReport);
        Assert.Contains(OperationManifestCatalog.GetAll(), entry => entry.OperationName == WorkflowOperations.ValidateReleaseGate);
        Assert.Contains(ToolRoutingCatalog.GetRules(), rule => rule.OperationName == WorkflowOperations.GetIntegrityReport);
        Assert.Contains(ToolRoutingCatalog.GetRules(), rule => rule.OperationName == WorkflowOperations.ValidateReleaseGate);

        var tools = McpToolMapping.GetAllTools();
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.GetIntegrityReport);
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.ValidateReleaseGate);
    }

    [Fact]
    public void GetIntegrityReport_ReturnsAggregatedReport()
    {
        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.GetIntegrityReport,
            Actor = WorkflowActor.Implementer
        });

        Assert.True(result.Success, result.Message);
        var report = Assert.IsType<WorkflowIntegrityReport>(result.Data);
        Assert.NotNull(report.Summary);
    }

    [Fact]
    public void ValidateReleaseGate_FailsWhenBlockingIntegrityIssueExists()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Broken phase"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        File.Delete(WorkflowLayout.PhaseFile(_tempRoot, "PHASE-001"));

        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateReleaseGate,
            Actor = WorkflowActor.Implementer
        });

        Assert.True(result.Success, result.Message);
        var gate = Assert.IsType<WorkflowReleaseGateResult>(result.Data);
        Assert.False(gate.Passed);
        Assert.True(gate.BlockingIssueCount > 0);
        Assert.Contains(gate.Report.Issues, issue => issue.Code == "MissingPhaseFile");
    }

    [Fact]
    public void ValidateReleaseGate_FailsWhenAuthoritativeJsonlIsMalformed()
    {
        File.AppendAllText(
            WorkflowLayout.ReviewLog(_tempRoot),
            "{\"reviewId\":\"REV-001\"" + Environment.NewLine);

        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateReleaseGate,
            Actor = WorkflowActor.Implementer
        });

        Assert.True(result.Success, result.Message);
        var gate = Assert.IsType<WorkflowReleaseGateResult>(result.Data);
        Assert.False(gate.Passed);
        Assert.Equal(1, gate.BlockingIssueCount);
        Assert.Contains(
            gate.Report.Issues,
            issue => issue.Code == "MalformedJsonlLine" && issue.IsReleaseBlocking);
    }

    [Fact]
    public void ValidateReleaseGate_FailsWhenPhaseRegistryHasOrphanPhaseDrift()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Registered phase"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002",
            PhaseNumber = 2,
            Title = "Orphan phase"
        });

        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateReleaseGate,
            Actor = WorkflowActor.Implementer
        });

        Assert.True(result.Success, result.Message);
        var gate = Assert.IsType<WorkflowReleaseGateResult>(result.Data);
        Assert.False(gate.Passed);
        Assert.Equal(1, gate.BlockingIssueCount);
        Assert.Contains(
            gate.Report.Issues,
            issue => issue.Code == "OrphanPhaseFile" && issue.IsReleaseBlocking);
    }

    [Fact]
    public void ValidateReleaseGate_PassesWhenOnlyRebuildableDriftExists()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Drift-only phase"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        new MarkdownSyncService(_store).SyncAll();
        new ContextIndexBuilder(_tempRoot).Rebuild();

        var currentStatusPath = WorkflowLayout.CurrentStatusMdPath(_tempRoot);
        var indexPath = WorkflowLayout.WorkflowKitDbPath(_tempRoot);
        var authoritativePath = WorkflowLayout.WorkflowFile(_tempRoot);
        var staleWriteUtc = DateTime.UtcNow.AddMinutes(-10);
        var freshWriteUtc = DateTime.UtcNow;

        File.SetLastWriteTimeUtc(currentStatusPath, staleWriteUtc);
        File.SetLastWriteTimeUtc(indexPath, staleWriteUtc);
        File.SetLastWriteTimeUtc(authoritativePath, freshWriteUtc);

        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateReleaseGate,
            Actor = WorkflowActor.Implementer
        });

        Assert.True(result.Success, result.Message);
        var gate = Assert.IsType<WorkflowReleaseGateResult>(result.Data);
        Assert.True(gate.Passed);
        Assert.Equal(0, gate.BlockingIssueCount);
        Assert.Contains(gate.Report.Issues, issue => issue.Code == "MarkdownMirrorStale");
        Assert.Contains(gate.Report.Issues, issue => issue.Code == "ReadModelStale");
        Assert.All(gate.Report.Issues, issue => Assert.False(issue.IsReleaseBlocking));
    }
}
