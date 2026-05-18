using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.StorageTests;

/// <summary>
/// Tests for WorkflowMetadataService — planning metadata writes and backward compatibility.
/// </summary>
public sealed class WorkflowMetadataServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly WorkflowMetadataService _svc;

    public WorkflowMetadataServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-meta-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("MetaTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _svc = new WorkflowMetadataService(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -- Current work update --------------------------------------------------

    [Fact]
    public void UpdateCurrentWork_UpdatesNextActionAndAppendsEvent()
    {
        var eventsBefore = _store.ReadAllEvents().Count;

        var result = _svc.UpdateCurrentWork(
            "Build the new dashboard",
            WorkflowActor.Codex,
            Urgency.High,
            DateTimeOffset.UtcNow.AddDays(3),
            pinnedFinishNow: true);

        Assert.True(result.IsSuccess);

        var workflow = _store.LoadWorkflow();
        Assert.NotNull(workflow.NextAction);
        Assert.Contains("Build the new dashboard", workflow.NextAction.Description);
        Assert.Equal(WorkflowActor.Codex, workflow.NextAction.Actor);
        Assert.Equal(Urgency.High, workflow.NextAction.Urgency);
        Assert.True(workflow.NextAction.PinnedFinishNow);
        Assert.NotNull(workflow.NextAction.DueDate);

        Assert.Equal(eventsBefore + 1, _store.ReadAllEvents().Count);
    }

    [Fact]
    public void UpdateCurrentWork_EmptyDescription_ReturnsFailure()
    {
        var result = _svc.UpdateCurrentWork("", WorkflowActor.Codex);
        Assert.True(result.IsFailure);
    }

    // -- Metadata write validation: historical logs untouched -----------------

    [Fact]
    public void UpdateCurrentWork_DoesNotTouchReviewLog()
    {
        _store.AppendReview(new ReviewRecord
        {
            ReviewId = "REV-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Codex,
            Summary = "Some review",
            Passed = true
        });

        var before = _store.ReadAllReviews().Count;
        _svc.UpdateCurrentWork("New action", WorkflowActor.Codex);
        Assert.Equal(before, _store.ReadAllReviews().Count);
    }

    [Fact]
    public void UpdateCurrentWork_DoesNotTouchFixLog()
    {
        _store.AppendFix(new FixRecord
        {
            FixId = "FIX-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Some fix"
        });

        var before = _store.ReadAllFixes().Count;
        _svc.UpdateCurrentWork("New action", WorkflowActor.Codex);
        Assert.Equal(before, _store.ReadAllFixes().Count);
    }

    [Fact]
    public void UpdateCurrentWork_DoesNotTouchHandoffLog()
    {
        _store.AppendHandoff(new HandoffRecord
        {
            HandoffId = "HANDOFF-001",
            PhaseId = "PHASE-001",
            FromActor = WorkflowActor.DeepSeek,
            ToActor = WorkflowActor.Codex,
            Summary = "Some handoff"
        });

        var before = _store.ReadAllHandoffs().Count;
        _svc.UpdateCurrentWork("New action", WorkflowActor.Codex);
        Assert.Equal(before, _store.ReadAllHandoffs().Count);
    }

    // -- Backward compatibility -----------------------------------------------

    [Fact]
    public void LoadWorkflow_WithoutUrgencyOnNextAction_DefaultsToMedium()
    {
        var workflow = _store.LoadWorkflow();
        Assert.Null(workflow.NextAction);
    }

    [Fact]
    public void WorkflowDashboardSummary_BackwardCompat_DefaultUrgency()
    {
        var summary = WorkflowDashboardSummaryBuilder.Build(_store);
        Assert.Equal(Urgency.Medium, summary.NextActionUrgency);
        Assert.Null(summary.NextActionDueDate);
        Assert.False(summary.PinnedFinishNow);
        Assert.Equal(0, summary.ActiveOrchestrationSessionCount);
    }

    [Fact]
    public void WorkflowDashboardSummary_IncludesActiveOrchestrationSessionCount()
    {
        _store.SaveOrchestrationSession(new OrchestrationSession
        {
            SessionId = "ORS-001",
            PhaseId = "PHASE-040",
            WorkflowId = _store.LoadWorkflow().WorkflowId,
            ManagerActor = WorkflowActor.Codex,
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ObjectiveSnapshot = "Objective",
            ScopeSnapshot = "Scope"
        });

        var summary = WorkflowDashboardSummaryBuilder.Build(_store);
        Assert.Equal(1, summary.ActiveOrchestrationSessionCount);
    }

    [Fact]
    public void UpdateCurrentWork_AdvancesCurrentPhase_WhenPreviousPhasePassed()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Passed",
            State = PhaseState.Pass
        });
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002",
            PhaseNumber = 2,
            Title = "Next"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            CurrentPhaseId = "PHASE-001",
            PhaseIds = ["PHASE-001", "PHASE-002"]
        });

        var result = _svc.UpdateCurrentWork(
            "Start the next phase",
            WorkflowActor.Codex,
            phaseId: "PHASE-002");

        Assert.True(result.IsSuccess);
        Assert.Equal("PHASE-002", _store.LoadWorkflow().CurrentPhaseId);
    }
}
