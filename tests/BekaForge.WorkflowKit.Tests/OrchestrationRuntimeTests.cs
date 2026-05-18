using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class OrchestrationRuntimeTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public OrchestrationRuntimeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-orc-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("OrchestrationRuntimeAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void StartSession_CreatesRootImplementerRun_AndRejectsDuplicateActiveSession()
    {
        var phaseId = CreatePhase("Deterministic runtime");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);

        var started = Dispatch(WorkflowOperations.StartOrchestrationSession, phaseId, WorkflowActor.Planner, new()
        {
            ["objectiveSnapshot"] = "Build the runtime",
            ["scopeSnapshot"] = "Runtime only"
        });

        Assert.True(started.Success, started.Message);
        var session = Assert.IsType<OrchestrationSession>(started.Data);
        Assert.Equal(OrchestrationSessionState.WaitingForAgent, session.SessionState);
        Assert.Equal(1, session.Attempts.ImplementationAttempts);

        var runs = _store.LoadAllOrchestrationRuns();
        Assert.Single(runs);
        Assert.Equal(OrchestrationRunRole.Implementer, runs[0].Role);

        var duplicate = Dispatch(WorkflowOperations.StartOrchestrationSession, phaseId, WorkflowActor.Planner);
        Assert.False(duplicate.Success);
        Assert.Equal("DuplicateActiveSession", duplicate.ErrorCode);
    }

    [Fact]
    public void Runtime_FullHappyPath_CompletesPhasePass()
    {
        var phaseId = CreatePhase("Runtime happy path");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);

        var implementerRun = ActiveRun(session);
        StartRun(implementerRun.RunId);
        var imp = LogImplementation(phaseId, "Initial runtime implementation.");
        ReportRun(implementerRun.RunId, "Implementation completed.", imp);
        AcceptRun(implementerRun.RunId);
        var afterImplementation = Advance(session.SessionId);

        Assert.Equal(OrchestrationSessionState.WaitingForAgent, afterImplementation.SessionState);
        Assert.Equal(OrchestrationRunRole.Auditor, afterImplementation.ActiveRunRole);

        var auditRun = ActiveRun(afterImplementation);
        StartRun(auditRun.RunId);
        var aud = LogAudit(phaseId, passed: true, "Audit passed.");
        ReportRun(auditRun.RunId, "Audit completed.", aud);
        AcceptRun(auditRun.RunId);
        var afterAudit = Advance(session.SessionId);

        Assert.Equal(PhaseState.ReadyForReview, _store.LoadPhase(phaseId)!.State);
        Assert.Equal(OrchestrationRunRole.Reviewer, afterAudit.ActiveRunRole);

        var reviewRun = ActiveRun(afterAudit);
        StartRun(reviewRun.RunId);
        var rev = LogReview(phaseId, passed: true, requiresFix: false, "Review passed.");
        ReportRun(reviewRun.RunId, "Review completed.", rev);
        AcceptRun(reviewRun.RunId);
        var afterReview = Advance(session.SessionId);

        Assert.Equal(PhaseState.ReadyForTest, _store.LoadPhase(phaseId)!.State);
        Assert.Equal(OrchestrationRunRole.Tester, afterReview.ActiveRunRole);

        var validationRun = ActiveRun(afterReview);
        StartRun(validationRun.RunId);
        var val = LogValidation(phaseId, ValidationResult.Passed, "Validation passed.");
        ReportRun(validationRun.RunId, "Validation completed.", val);
        AcceptRun(validationRun.RunId);
        var finalStatus = Advance(session.SessionId);

        Assert.Equal(OrchestrationSessionState.CompletedPass, finalStatus.SessionState);
        Assert.Null(finalStatus.ActiveRunId);
        Assert.Equal(PhaseState.Pass, _store.LoadPhase(phaseId)!.State);
        Assert.Equal(4, _store.ReadAllOrchestrationGateDecisions().Count);
    }

    [Fact]
    public void Runtime_ReviewFixLoop_ReentersAuditAndIncrementsCounters()
    {
        var phaseId = CreatePhase("Review fix loop");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);

        var implementerRun = ActiveRun(session);
        StartRun(implementerRun.RunId);
        var imp = LogImplementation(phaseId, "Initial implementation.");
        ReportRun(implementerRun.RunId, "Implementation done.", imp);
        AcceptRun(implementerRun.RunId);
        var afterImplementation = Advance(session.SessionId);

        var auditRun = ActiveRun(afterImplementation);
        StartRun(auditRun.RunId);
        var aud = LogAudit(phaseId, passed: true, "Audit passed.");
        ReportRun(auditRun.RunId, "Audit done.", aud);
        AcceptRun(auditRun.RunId);
        var afterAudit = Advance(session.SessionId);

        var reviewRun = ActiveRun(afterAudit);
        StartRun(reviewRun.RunId);
        var rev = LogReview(phaseId, passed: false, requiresFix: true, "Review requires fixes.", "Missing retry guard.");
        ReportRun(reviewRun.RunId, "Review found issues.", rev);
        AcceptRun(reviewRun.RunId);
        var afterReview = Advance(session.SessionId);

        Assert.Equal(PhaseState.RequiresFix, _store.LoadPhase(phaseId)!.State);
        Assert.Equal(OrchestrationRunRole.Fixer, afterReview.ActiveRunRole);

        var fixRun = ActiveRun(afterReview);
        StartRun(fixRun.RunId);
        var fix = LogFix(phaseId, "Applied review fixes.");
        ReportRun(fixRun.RunId, "Fix complete.", fix);
        AcceptRun(fixRun.RunId);
        var afterFix = Advance(session.SessionId);

        Assert.Equal(PhaseState.ImplementationLogged, _store.LoadPhase(phaseId)!.State);
        Assert.Equal(OrchestrationRunRole.Auditor, afterFix.ActiveRunRole);
        Assert.Equal(2, afterFix.Attempts.AuditAttempts);
        Assert.Equal(1, afterFix.Attempts.FixAttempts);
    }

    [Fact]
    public void Runtime_ValidationFailure_WhenAttemptsExhausted_CompletesFailure()
    {
        var phaseId = CreatePhase("Validation failure");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var start = Dispatch(WorkflowOperations.StartOrchestrationSession, phaseId, WorkflowActor.Planner, new()
        {
            ["maxValidationAttempts"] = 1
        });
        Assert.True(start.Success, start.Message);
        var session = Assert.IsType<OrchestrationSession>(start.Data);

        var implementerRun = ActiveRun(session);
        StartRun(implementerRun.RunId);
        var imp = LogImplementation(phaseId, "Initial implementation.");
        ReportRun(implementerRun.RunId, "Implementation done.", imp);
        AcceptRun(implementerRun.RunId);
        var afterImplementation = Advance(session.SessionId);

        var auditRun = ActiveRun(afterImplementation);
        StartRun(auditRun.RunId);
        var aud = LogAudit(phaseId, passed: true, "Audit passed.");
        ReportRun(auditRun.RunId, "Audit done.", aud);
        AcceptRun(auditRun.RunId);
        var afterAudit = Advance(session.SessionId);

        var reviewRun = ActiveRun(afterAudit);
        StartRun(reviewRun.RunId);
        var rev = LogReview(phaseId, passed: true, requiresFix: false, "Review passed.");
        ReportRun(reviewRun.RunId, "Review done.", rev);
        AcceptRun(reviewRun.RunId);
        var afterReview = Advance(session.SessionId);

        var validationRun = ActiveRun(afterReview);
        StartRun(validationRun.RunId);
        var val = LogValidation(phaseId, ValidationResult.Failed, "Validation failed.");
        ReportRun(validationRun.RunId, "Validation failed.", val);
        AcceptRun(validationRun.RunId);
        var finalStatus = Advance(session.SessionId);

        Assert.Equal(OrchestrationSessionState.CompletedFailure, finalStatus.SessionState);
        Assert.NotNull(finalStatus.LatestGateDecisionId);
        Assert.Equal(PhaseState.TestLogged, _store.LoadPhase(phaseId)!.State);
    }

    [Fact]
    public void Runtime_AdvanceIsReplaySafe_WhenCurrentRunIsAlreadyQueued()
    {
        var phaseId = CreatePhase("Replay safe");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);

        var implementerRun = ActiveRun(session);
        StartRun(implementerRun.RunId);
        var imp = LogImplementation(phaseId, "Implementation.");
        ReportRun(implementerRun.RunId, "Implementation complete.", imp);
        AcceptRun(implementerRun.RunId);

        var afterFirstAdvance = Advance(session.SessionId);
        var runCount = _store.LoadAllOrchestrationRuns().Count;
        var afterSecondAdvance = Advance(session.SessionId);

        Assert.Equal(runCount, _store.LoadAllOrchestrationRuns().Count);
        Assert.Equal(afterFirstAdvance.ActiveRunId, afterSecondAdvance.ActiveRunId);
        Assert.Equal(OrchestrationRunRole.Auditor, afterSecondAdvance.ActiveRunRole);
    }

    [Fact]
    public void PauseAndCancelSession_UpdateRuntimeState()
    {
        var phaseId = CreatePhase("Pause and cancel");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);

        var run = ActiveRun(session);
        StartRun(run.RunId);

        var paused = Dispatch(WorkflowOperations.PauseOrchestrationSession, actor: WorkflowActor.Planner, parameters: new()
        {
            ["sessionId"] = session.SessionId,
            ["reason"] = "Operator pause"
        });
        Assert.True(paused.Success, paused.Message);
        var pausedStatus = Assert.IsType<OrchestrationStatusSnapshot>(paused.Data);
        Assert.Equal(OrchestrationSessionState.WaitingForAgent, pausedStatus.SessionState);

        var cancelled = Dispatch(WorkflowOperations.CancelOrchestrationSession, actor: WorkflowActor.Planner, parameters: new()
        {
            ["sessionId"] = session.SessionId,
            ["reason"] = "Operator cancel"
        });
        Assert.True(cancelled.Success, cancelled.Message);
        var cancelledSession = Assert.IsType<OrchestrationSession>(cancelled.Data);
        Assert.Equal(OrchestrationSessionState.Cancelled, cancelledSession.SessionState);
    }

    [Fact]
    public void Runtime_PendingHumanValidation_SetsAttentionOutcome()
    {
        var phaseId = CreatePhase("Pending human validation");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);

        var implementerRun = ActiveRun(session);
        StartRun(implementerRun.RunId);
        var imp = LogImplementation(phaseId, "Initial implementation.");
        ReportRun(implementerRun.RunId, "Implementation done.", imp);
        AcceptRun(implementerRun.RunId);
        var afterImplementation = Advance(session.SessionId);

        var auditRun = ActiveRun(afterImplementation);
        StartRun(auditRun.RunId);
        var aud = LogAudit(phaseId, passed: true, "Audit passed.");
        ReportRun(auditRun.RunId, "Audit done.", aud);
        AcceptRun(auditRun.RunId);
        var afterAudit = Advance(session.SessionId);

        var reviewRun = ActiveRun(afterAudit);
        StartRun(reviewRun.RunId);
        var rev = LogReview(phaseId, passed: true, requiresFix: false, "Review passed.");
        ReportRun(reviewRun.RunId, "Review done.", rev);
        AcceptRun(reviewRun.RunId);
        var afterReview = Advance(session.SessionId);

        var validationRun = ActiveRun(afterReview);
        StartRun(validationRun.RunId);
        var requestValidation = Dispatch(WorkflowOperations.RequestUserValidation, phaseId, WorkflowActor.Validator, new()
        {
            ["summary"] = "Human validation required.",
            ["manualSteps"] = "Open the app\nVerify the flow"
        });
        Assert.True(requestValidation.Success, requestValidation.Message);
        var validationId = _store.ReadAllValidations().Last().ValidationId;
        ReportRun(validationRun.RunId, "Validation handed to human.", validationId);
        AcceptRun(validationRun.RunId);
        var waiting = Advance(session.SessionId);

        Assert.Equal(OrchestrationSessionState.WaitingForHuman, waiting.SessionState);
        Assert.Equal(AttentionDerivedOutcome.NeedsHumanValidation, waiting.DerivedAttentionOutcome);
        Assert.True(waiting.AttentionFlags.HumanValidationRequired);
    }

    [Fact]
    public void RequestHumanAttention_ThenClearAttention_TransitionsBackToReadyToContinue()
    {
        var phaseId = CreatePhase("Manual attention");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);
        var run = ActiveRun(session);

        var requested = Dispatch(WorkflowOperations.RequestOrchestrationHumanAttention, actor: WorkflowActor.Planner, parameters: new()
        {
            ["sessionId"] = session.SessionId,
            ["runId"] = run.RunId,
            ["manualReviewRequired"] = true,
            ["reasonRecordIds"] = "REV-001",
            ["reason"] = "Operator review required."
        });
        Assert.True(requested.Success, requested.Message);
        var waiting = Assert.IsType<OrchestrationStatusSnapshot>(requested.Data);
        Assert.Equal(OrchestrationSessionState.WaitingForHuman, waiting.SessionState);
        Assert.Equal(AttentionDerivedOutcome.NeedsManualReview, waiting.DerivedAttentionOutcome);

        var cleared = Dispatch(WorkflowOperations.ClearOrchestrationAttentionFlags, actor: WorkflowActor.Planner, parameters: new()
        {
            ["sessionId"] = session.SessionId,
            ["runId"] = run.RunId,
            ["flags"] = "ManualReviewRequired"
        });
        Assert.True(cleared.Success, cleared.Message);
        var ready = Assert.IsType<OrchestrationStatusSnapshot>(cleared.Data);
        Assert.Equal(OrchestrationSessionState.WaitingForAgent, ready.SessionState);
        Assert.Equal(AttentionDerivedOutcome.ReadyToContinue, ready.DerivedAttentionOutcome);
    }

    [Fact]
    public void OrchestrationState_PersistsAcrossStoreReload()
    {
        var phaseId = CreatePhase("Persistence");
        AdvancePhaseTo(phaseId, PhaseState.ReadyForImplementation);
        var session = StartSession(phaseId);

        var requested = Dispatch(WorkflowOperations.RequestOrchestrationHumanAttention, actor: WorkflowActor.Planner, parameters: new()
        {
            ["sessionId"] = session.SessionId,
            ["runId"] = session.ActiveRunId,
            ["blockedByEnvironment"] = true,
            ["externalToolRequired"] = true,
            ["reasonRecordIds"] = "VAL-001",
            ["reason"] = "External environment dependency."
        });
        Assert.True(requested.Success, requested.Message);

        var reloadedStore = new WorkflowStore(_tempRoot);
        var reloadedDispatcher = new OperationDispatcher(reloadedStore);
        var status = reloadedDispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.GetOrchestrationAttentionStatus,
            Actor = WorkflowActor.Planner,
            Parameters = new Dictionary<string, object?> { ["sessionId"] = session.SessionId }
        });

        Assert.True(status.Success, status.Message);
        var snapshot = Assert.IsType<OrchestrationStatusSnapshot>(status.Data);
        Assert.Equal(OrchestrationSessionState.Blocked, snapshot.SessionState);
        Assert.Equal(AttentionDerivedOutcome.BlockedByEnvironment, snapshot.DerivedAttentionOutcome);
        Assert.True(snapshot.AttentionFlags.ExternalToolRequired);
    }

    private string CreatePhase(string title)
    {
        var result = Dispatch(WorkflowOperations.CreatePhase, actor: WorkflowActor.Planner, parameters: new()
        {
            ["title"] = title,
            ["objective"] = title,
            ["scope"] = title
        });
        Assert.True(result.Success, result.Message);
        return Assert.IsType<Phase>(result.Data).PhaseId;
    }

    private void AdvancePhaseTo(string phaseId, PhaseState state)
    {
        var result = Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId, WorkflowActor.Planner, new()
        {
            ["state"] = state.ToString()
        });
        Assert.True(result.Success, result.Message);
    }

    private OrchestrationSession StartSession(string phaseId)
    {
        var result = Dispatch(WorkflowOperations.StartOrchestrationSession, phaseId, WorkflowActor.Planner, new()
        {
            ["objectiveSnapshot"] = "Implement the runtime",
            ["scopeSnapshot"] = "Runtime only"
        });
        Assert.True(result.Success, result.Message);
        return Assert.IsType<OrchestrationSession>(result.Data);
    }

    private OrchestrationStatusSnapshot Advance(string sessionId)
    {
        var result = Dispatch(WorkflowOperations.AdvanceOrchestrationSession, actor: WorkflowActor.Planner, parameters: new()
        {
            ["sessionId"] = sessionId
        });
        Assert.True(result.Success, result.Message);
        return Assert.IsType<OrchestrationStatusSnapshot>(result.Data);
    }

    private OrchestrationRun ActiveRun(OrchestrationSession session) =>
        _store.LoadOrchestrationRun(session.ActiveRunId!)!;

    private OrchestrationRun ActiveRun(OrchestrationStatusSnapshot snapshot) =>
        _store.LoadOrchestrationRun(snapshot.ActiveRunId!)!;

    private void StartRun(string runId)
    {
        var result = Dispatch(WorkflowOperations.StartOrchestrationRun, actor: WorkflowActor.WorkflowSystem, parameters: new()
        {
            ["runId"] = runId
        });
        Assert.True(result.Success, result.Message);
    }

    private void ReportRun(string runId, string summary, string recordId)
    {
        var result = Dispatch(WorkflowOperations.ReportOrchestrationRun, actor: WorkflowActor.WorkflowSystem, parameters: new()
        {
            ["runId"] = runId,
            ["summary"] = summary,
            ["producedRecordIds"] = recordId
        });
        Assert.True(result.Success, result.Message);
    }

    private void AcceptRun(string runId)
    {
        var result = Dispatch(WorkflowOperations.AcceptOrchestrationRun, actor: WorkflowActor.Planner, parameters: new()
        {
            ["runId"] = runId
        });
        Assert.True(result.Success, result.Message);
    }

    private string LogImplementation(string phaseId, string summary)
    {
        var result = Dispatch(WorkflowOperations.CreateImplementationLog, phaseId, WorkflowActor.Implementer, new()
        {
            ["summary"] = summary
        });
        Assert.True(result.Success, result.Message);
        return Assert.IsType<ImplementationRecord>(result.Data).ImplementationId;
    }

    private string LogAudit(string phaseId, bool passed, string summary)
    {
        var result = Dispatch(WorkflowOperations.CreateAuditLog, phaseId, WorkflowActor.Auditor, new()
        {
            ["summary"] = summary,
            ["passed"] = passed
        });
        Assert.True(result.Success, result.Message);
        return Assert.IsType<AuditRecord>(result.Data).AuditId;
    }

    private string LogReview(string phaseId, bool passed, bool requiresFix, string summary, string? issue = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["summary"] = summary,
            ["passed"] = passed,
            ["requiresFix"] = requiresFix
        };
        if (!string.IsNullOrWhiteSpace(issue))
            parameters["issues"] = issue;

        var result = Dispatch(WorkflowOperations.CreateReviewLog, phaseId, WorkflowActor.Reviewer, parameters);
        Assert.True(result.Success, result.Message);
        return Assert.IsType<ReviewRecord>(result.Data).ReviewId;
    }

    private string LogFix(string phaseId, string summary)
    {
        var result = Dispatch(WorkflowOperations.CreateFixLog, phaseId, WorkflowActor.Fixer, new()
        {
            ["summary"] = summary
        });
        Assert.True(result.Success, result.Message);
        return Assert.IsType<FixRecord>(result.Data).FixId;
    }

    private string LogValidation(string phaseId, ValidationResult resultKind, string summary)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["summary"] = summary,
            ["validationType"] = ValidationType.AutomatedCommand.ToString(),
            ["validationResult"] = resultKind.ToString(),
            ["evidenceItems"] = """
                [{"description":"Automated test output","source":2,"reference":"local-test"}]
                """
        };
        if (resultKind == ValidationResult.Failed)
        {
            parameters["command"] = "dotnet test";
            parameters["exitCode"] = 1;
        }
        else
        {
            parameters["command"] = "dotnet test";
            parameters["exitCode"] = 0;
        }

        var result = Dispatch(WorkflowOperations.CreateValidationLog, phaseId, WorkflowActor.Validator, parameters);
        Assert.True(result.Success, result.Message);
        return Assert.IsType<ValidationRecord>(result.Data).ValidationId;
    }

    private OperationResult Dispatch(
        string operation,
        string? phaseId = null,
        WorkflowActor actor = WorkflowActor.Planner,
        Dictionary<string, object?>? parameters = null) =>
        _dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            PhaseId = phaseId,
            Actor = actor,
            Parameters = parameters ?? []
        });
}
