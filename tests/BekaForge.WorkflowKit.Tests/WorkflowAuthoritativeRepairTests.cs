using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Server.Handlers;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class WorkflowAuthoritativeRepairTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public WorkflowAuthoritativeRepairTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-authoritative-repair-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("Authoritative Repair Tests");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void RepairAuthoritativeIntegrity_FixesDuplicateEvidenceIdsAndLegacyTestAliases()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One",
            AuditLogIds = ["AUD-001"],
            ReviewLogIds = ["REV-001"]
        });
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002",
            PhaseNumber = 2,
            Title = "Two",
            AuditLogIds = ["AUD-001"],
            ReviewLogIds = ["REV-001"]
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001", "PHASE-002"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendAudit(new AuditRecord
        {
            AuditId = "AUD-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Auditor,
            Summary = "Phase one audit.",
            Passed = true
        });
        _store.AppendAudit(new AuditRecord
        {
            AuditId = "AUD-001",
            PhaseId = "PHASE-002",
            Actor = WorkflowActor.Auditor,
            Summary = "Phase two audit.",
            Passed = true
        });

        _store.AppendReview(new ReviewRecord
        {
            ReviewId = "REV-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Reviewer,
            Summary = "Phase one review.",
            Passed = true,
            RequiresFix = false
        });
        _store.AppendReview(new ReviewRecord
        {
            ReviewId = "REV-001",
            PhaseId = "PHASE-002",
            Actor = WorkflowActor.Reviewer,
            Summary = "Phase two review.",
            Passed = true,
            RequiresFix = false
        });

        _store.AppendTest(new TestRecord
        {
            TestId = "TEST-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Validator,
            Summary = "Legacy validation.",
            Passed = true
        });

        var before = GetIntegrityReport();
        Assert.Contains(before.Issues, issue => issue.Code == "DuplicateRecordId");
        Assert.Contains(before.Issues, issue => issue.Code == "OrphanLogEvidenceReference");

        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.RepairAuthoritativeIntegrity,
            Actor = WorkflowActor.Implementer,
            PhaseId = "PHASE-060"
        });

        Assert.True(result.Success, result.Message);
        var repair = Assert.IsType<AuthoritativeIntegrityRepairResult>(result.Data);
        Assert.True(repair.Repaired);

        var after = GetIntegrityReport();
        Assert.DoesNotContain(after.Issues, issue => issue.Code == "DuplicateRecordId");
        Assert.DoesNotContain(after.Issues, issue => issue.Code == "ReusedRecordId");
        Assert.DoesNotContain(after.Issues, issue => issue.Code == "MismatchedPhaseEvidenceReference");
        Assert.DoesNotContain(after.Issues, issue => issue.Code == "OrphanLogEvidenceReference");

        var repairedPhaseOne = _store.LoadPhase("PHASE-001")!;
        var repairedPhaseTwo = _store.LoadPhase("PHASE-002")!;
        Assert.Contains("TEST-001", repairedPhaseOne.ValidationLogIds);
        Assert.DoesNotContain("AUD-001", repairedPhaseTwo.AuditLogIds);
        Assert.DoesNotContain("REV-001", repairedPhaseTwo.ReviewLogIds);
    }

    private WorkflowIntegrityReport GetIntegrityReport()
    {
        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.GetIntegrityReport,
            Actor = WorkflowActor.Implementer
        });

        Assert.True(result.Success, result.Message);
        return Assert.IsType<WorkflowIntegrityReport>(result.Data);
    }
}
