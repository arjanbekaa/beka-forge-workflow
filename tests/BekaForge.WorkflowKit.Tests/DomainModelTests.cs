using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for the Core domain model types.
/// Verifies construction, defaults, and basic properties of all record types.
/// </summary>
public sealed class DomainModelTests
{
    // -- PhaseState enum completeness ----------------------------------------------

    [Fact]
    public void PhaseState_HasAllRequiredValues()
    {
        var required = new[]
        {
            "Planned", "ReadyForImplementation", "AssignedToImplementation",
            "InImplementation", "ImplementationLogged", "AuditLogged",
            "ReadyForReview", "ReviewInProgress", "ReviewLogged",
            "RequiresFix", "FixInProgress", "FixLogged",
            "ReadyForTest", "TestInProgress", "TestLogged",
            "Pass", "PassWithWarnings", "Blocked",
            "FailedArchitecture", "FailedCompile", "FailedValidation"
        };

        var defined = Enum.GetNames<PhaseState>();
        foreach (var name in required)
        {
            Assert.Contains(name, defined);
        }
    }

    [Fact]
    public void PhaseState_HasExactly21Values()
    {
        Assert.Equal(21, Enum.GetValues<PhaseState>().Length);
    }

    // -- WorkflowActor enum --------------------------------------------------------

    [Fact]
    public void WorkflowActor_HasAllRequiredValues()
    {
        Assert.Contains(WorkflowActor.Codex, Enum.GetValues<WorkflowActor>());
        Assert.Contains(WorkflowActor.DeepSeek, Enum.GetValues<WorkflowActor>());
        Assert.Contains(WorkflowActor.UnityAssistant, Enum.GetValues<WorkflowActor>());
        Assert.Contains(WorkflowActor.UnityBridge, Enum.GetValues<WorkflowActor>());
        Assert.Contains(WorkflowActor.User, Enum.GetValues<WorkflowActor>());
        Assert.Contains(WorkflowActor.WorkflowKit, Enum.GetValues<WorkflowActor>());
    }

    // -- WorkflowError -------------------------------------------------------------

    [Fact]
    public void WorkflowError_InvalidTransition_ContainsFromAndToStateNames()
    {
        var error = WorkflowError.InvalidTransition(PhaseState.Planned, PhaseState.Pass, "test reason");
        Assert.Equal("InvalidTransition", error.Code);
        Assert.Contains("Planned", error.Message);
        Assert.Contains("Pass", error.Message);
        Assert.Contains("test reason", error.Message);
    }

    [Fact]
    public void WorkflowError_TerminalState_ContainsStateName()
    {
        var error = WorkflowError.TerminalState(PhaseState.FailedCompile);
        Assert.Equal("TerminalState", error.Code);
        Assert.Contains("FailedCompile", error.Message);
    }

    [Fact]
    public void WorkflowError_BlockerRequired_HasExpectedCode()
    {
        var error = WorkflowError.BlockerRequired();
        Assert.Equal("BlockerRequired", error.Code);
    }

    [Fact]
    public void WorkflowError_UnityTestRequired_HasExpectedCode()
    {
        var error = WorkflowError.ValidationRequired();
        Assert.Equal("ValidationRequired", error.Code);
    }

    // -- WorkflowResult ------------------------------------------------------------

    [Fact]
    public void WorkflowResult_Ok_IsSuccessAndNotFailure()
    {
        var result = WorkflowResult.Ok("data");
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("data", result.Value);
    }

    [Fact]
    public void WorkflowResult_Fail_IsFailureAndNotSuccess()
    {
        var error = new WorkflowError("TestCode", "Test message");
        var result = WorkflowResult.Fail<string>(error);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("TestCode", result.Error.Code);
    }

    [Fact]
    public void WorkflowResult_AccessValueOnFailure_Throws()
    {
        var result = WorkflowResult.Fail<int>(new WorkflowError("E", "msg"));
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void WorkflowResult_AccessErrorOnSuccess_Throws()
    {
        var result = WorkflowResult.Ok(42);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void WorkflowResult_UnitOk_IsSuccess()
    {
        var result = WorkflowResult.Ok();
        Assert.True(result.IsSuccess);
        Assert.Equal(Unit.Value, result.Value);
    }

    // -- PhaseContract defaults ----------------------------------------------------

    [Fact]
    public void PhaseContract_RequiresValidation_DefaultsToTrue()
    {
        var contract = new PhaseContract
        {
            Objective = "Do something",
            Scope = "Some scope"
        };
        Assert.True(contract.RequiresValidation);
    }

    [Fact]
    public void PhaseContract_CanSetRequiresValidationFalse()
    {
        var contract = new PhaseContract
        {
            Objective = "Pure C# types",
            Scope = "Domain model only",
            RequiresValidation = false
        };
        Assert.False(contract.RequiresValidation);
    }

    [Fact]
    public void PhaseContract_Collections_DefaultToEmpty()
    {
        var contract = new PhaseContract { Objective = "X", Scope = "Y" };
        Assert.Empty(contract.ArchitectureConstraints);
        Assert.Empty(contract.AcceptanceCriteria);
        Assert.Empty(contract.RequiredFilesOrAreas);
        Assert.Empty(contract.DependsOnPhaseIds);
        Assert.Empty(contract.ExecutionLanes);
    }

    // -- Phase defaults ------------------------------------------------------------

    [Fact]
    public void Phase_State_DefaultsToPlanned()
    {
        var phase = new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Test Phase"
        };
        Assert.Equal(PhaseState.Planned, phase.State);
    }

    [Fact]
    public void Phase_LogCollections_DefaultToEmpty()
    {
        var phase = new Phase { PhaseId = "PHASE-001", PhaseNumber = 1, Title = "T" };
        Assert.Empty(phase.ImplementationLogIds);
        Assert.Empty(phase.AuditLogIds);
        Assert.Empty(phase.ReviewLogIds);
        Assert.Empty(phase.ValidationLogIds);
        Assert.Empty(phase.FixLogIds);
        Assert.Empty(phase.BlockerIds);
        Assert.Empty(phase.HandoffIds);
    }

    // -- Record types can be constructed ------------------------------------------

    [Fact]
    public void ImplementationRecord_CanBeConstructed()
    {
        var record = new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Implemented domain model",
            Status = PhaseState.ImplementationLogged
        };
        Assert.Equal("IMP-001", record.ImplementationId);
        Assert.Equal(WorkflowActor.DeepSeek, record.Actor);
    }

    [Fact]
    public void AuditRecord_CanBeConstructed()
    {
        var record = new AuditRecord
        {
            AuditId = "AUD-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Self-audit passed",
            Passed = true
        };
        Assert.True(record.Passed);
        Assert.Empty(record.Issues);
    }

    [Fact]
    public void ReviewRecord_CanBeConstructed()
    {
        var record = new ReviewRecord
        {
            ReviewId = "REV-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Codex,
            Summary = "Architecture review passed",
            Passed = true
        };
        Assert.Equal(WorkflowActor.Codex, record.Actor);
        Assert.False(record.RequiresFix);
    }

    [Fact]
    public void TestRecord_CanBeConstructed()
    {
        var record = new TestRecord
        {
            TestId = "TEST-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.UnityAssistant,
            Summary = "All Unity tests passed",
            Passed = true
        };
        Assert.Equal("TEST-001", record.TestId);
        Assert.Empty(record.FailedTests);
    }

    [Fact]
    public void FixRecord_CanBeConstructed()
    {
        var record = new FixRecord
        {
            FixId = "FIX-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Fixed architecture violation"
        };
        Assert.Equal("FIX-001", record.FixId);
        Assert.Empty(record.FilesModified);
    }

    [Fact]
    public void BlockerRecord_CanBeConstructed()
    {
        var record = new BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Missing dependency",
            ReportedBy = WorkflowActor.Codex
        };
        Assert.False(record.IsResolved);
        Assert.Null(record.Resolution);
        Assert.Null(record.ResolvedUtc);
    }

    [Fact]
    public void HandoffRecord_CanBeConstructed()
    {
        var record = new HandoffRecord
        {
            HandoffId = "HANDOFF-001",
            PhaseId = "PHASE-001",
            FromActor = WorkflowActor.DeepSeek,
            ToActor = WorkflowActor.Codex,
            Summary = "Ready for review"
        };
        Assert.Equal(WorkflowActor.Codex, record.ToActor);
    }

    [Fact]
    public void TimingRecord_CanBeConstructed()
    {
        var duration = TimeSpan.FromMinutes(45);
        var record = new TimingRecord
        {
            TimingId = "TIME-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Activity = "implementation",
            Duration = duration
        };
        Assert.Equal(duration, record.Duration);
    }

    // -- WorkflowEvent -------------------------------------------------------------

    [Fact]
    public void WorkflowEvent_CanBeConstructed()
    {
        var evt = new WorkflowEvent
        {
            EventId = "EVT-001",
            EventType = "phase.status.changed",
            Actor = WorkflowActor.Codex,
            Summary = "Phase moved to PASS",
            PhaseId = "PHASE-001"
        };
        Assert.Equal("EVT-001", evt.EventId);
        Assert.Equal("phase.status.changed", evt.EventType);
    }

    // -- WorkflowState -------------------------------------------------------------

    [Fact]
    public void WorkflowState_CanBeConstructed()
    {
        var state = new WorkflowState
        {
            SchemaVersion = "1.0",
            WorkflowId = "wf-test-001",
            AssetName = "BekaForgeTestAsset",
            RootPath = "/test/path"
        };
        Assert.Equal("1.0", state.SchemaVersion);
        Assert.Empty(state.PhaseIds);
        Assert.Equal(0, state.OpenBlockerCount);
        Assert.Null(state.CurrentPhaseId);
        Assert.Equal(DocumentationPolicyMode.Manual, state.DocumentationPolicy);
    }

    [Fact]
    public void OrchestrationSession_CanBeConstructed()
    {
        var session = new OrchestrationSession
        {
            SessionId = "ORS-001",
            PhaseId = "PHASE-040",
            WorkflowId = "wf-test-001",
            ManagerActor = WorkflowActor.Codex,
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ObjectiveSnapshot = "Implement orchestration storage.",
            ScopeSnapshot = "Core and storage layer only."
        };

        Assert.Equal("ORS-001", session.SessionId);
        Assert.Equal(WorkflowActor.Codex, session.ManagerActor);
        Assert.Equal(OrchestrationSessionState.WaitingForAgent, session.SessionState);
        Assert.Equal(0, session.Attempts.ImplementationAttempts);
        Assert.Equal(0, session.Attempts.AuditAttempts);
        Assert.Equal(0, session.Attempts.ReviewAttempts);
        Assert.Equal(0, session.Attempts.ValidationAttempts);
        Assert.Equal(0, session.Attempts.FixAttempts);
        Assert.Equal(0, session.Attempts.HumanRequestCount);
        Assert.Equal(3, session.AttemptPolicy.MaxImplementationAttempts);
        Assert.Equal(2, session.AttemptPolicy.MaxAuditAttempts);
    }

    [Fact]
    public void OrchestrationRun_CanBeConstructed()
    {
        var run = new OrchestrationRun
        {
            RunId = "ORR-001",
            SessionId = "ORS-001",
            PhaseId = "PHASE-041",
            RootRunId = "ORR-001",
            Role = OrchestrationRunRole.Implementer,
            AssignedActor = WorkflowActor.DeepSeek
        };

        Assert.Equal("ORR-001", run.RunId);
        Assert.Equal(OrchestrationRunRole.Implementer, run.Role);
        Assert.Equal(OrchestrationRunState.Queued, run.RunState);
    }

    [Fact]
    public void OrchestrationGateDecisionRecord_CanBeConstructed()
    {
        var decision = new OrchestrationGateDecisionRecord
        {
            GateDecisionId = "OGD-001",
            SessionId = "ORS-001",
            PhaseId = "PHASE-041",
            RunId = "ORR-001",
            GateKind = OrchestrationGateKind.Review,
            Decision = OrchestrationDecision.Advance,
            DecisionActor = WorkflowActor.Codex,
            Rationale = "The gate passed."
        };

        Assert.Equal("OGD-001", decision.GateDecisionId);
        Assert.Equal(OrchestrationGateKind.Review, decision.GateKind);
        Assert.Equal(OrchestrationDecision.Advance, decision.Decision);
    }

    [Fact]
    public void OrchestrationRunEventRecord_CanBeConstructed()
    {
        var evt = new OrchestrationRunEventRecord
        {
            RunEventId = "ORE-001",
            SessionId = "ORS-001",
            RunId = "ORR-001",
            PhaseId = "PHASE-041",
            EventKind = OrchestrationEventKind.RunReported,
            Actor = WorkflowActor.DeepSeek,
            Summary = "Run produced an implementation report."
        };

        Assert.Equal("ORE-001", evt.RunEventId);
        Assert.Equal(OrchestrationEventKind.RunReported, evt.EventKind);
        Assert.Equal(WorkflowActor.DeepSeek, evt.Actor);
    }

    [Fact]
    public void AttentionFlagsSnapshot_DefaultsToAllFalseAndEmptyReasons()
    {
        var snapshot = new AttentionFlagsSnapshot();

        Assert.False(snapshot.HumanValidationRequired);
        Assert.False(snapshot.TestsNotRunnable);
        Assert.False(snapshot.ManualReviewRequired);
        Assert.False(snapshot.ExternalToolRequired);
        Assert.False(snapshot.MaxAgentAttemptsReached);
        Assert.False(snapshot.UnresolvedRisk);
        Assert.False(snapshot.BlockedByUser);
        Assert.False(snapshot.BlockedByEnvironment);
        Assert.Empty(snapshot.ReasonRecordIds);
    }

    [Fact]
    public void OrchestrationStatusSnapshot_CanBeConstructed()
    {
        var snapshot = new OrchestrationStatusSnapshot
        {
            SessionId = "ORS-001",
            PhaseId = "PHASE-041",
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = "ORR-001",
            ActiveRunRole = OrchestrationRunRole.Auditor,
            ActiveRunState = OrchestrationRunState.Queued
        };

        Assert.Equal("ORS-001", snapshot.SessionId);
        Assert.Equal(OrchestrationRunRole.Auditor, snapshot.ActiveRunRole);
        Assert.Equal(2, snapshot.AttemptPolicy.MaxAuditAttempts);
    }

    // -- NextAction ----------------------------------------------------------------

    [Fact]
    public void NextAction_CanBeConstructed()
    {
        var action = new NextAction
        {
            ActionId = "TIME-001",
            Actor = WorkflowActor.DeepSeek,
            Description = "Implement Phase 1 domain model"
        };
        Assert.Equal(WorkflowActor.DeepSeek, action.Actor);
        Assert.Null(action.PhaseId);
        Assert.Null(action.OperationHint);
    }

    // -- SubPhase -----------------------------------------------------------------

    [Fact]
    public void SubPhase_CanBeConstructed()
    {
        var sp = new SubPhase
        {
            SubPhaseId = "PHASE-015-A",
            Title = "Core cache infrastructure"
        };
        Assert.Equal("PHASE-015-A", sp.SubPhaseId);
        Assert.Equal(SubPhaseStatus.Planned, sp.Status);
        Assert.Empty(sp.DependsOn);
        Assert.Empty(sp.ImplementationLogIds);
    }

    [Fact]
    public void SubPhase_WithDependencies_HasCorrectDependsOn()
    {
        var sp = new SubPhase
        {
            SubPhaseId = "PHASE-015-B",
            Title = "PhaseContextPackage",
            DependsOn = ["PHASE-015-A"]
        };
        Assert.Single(sp.DependsOn);
        Assert.Equal("PHASE-015-A", sp.DependsOn[0]);
    }

    [Fact]
    public void ExecutionLane_CanBeConstructed()
    {
        var lane = new ExecutionLane
        {
            LaneId = "LANE-A",
            Title = "Planner lane",
            PhaseIds = ["PHASE-029"],
            SubPhaseIds = ["PHASE-029-A"],
            DependsOnLaneIds = ["LANE-SETUP"],
            OwnedAreas = ["src/BekaForge.WorkflowKit.Core"],
            CoordinationNotes = "Avoid overlapping edits in shared parser code."
        };

        Assert.Equal("LANE-A", lane.LaneId);
        Assert.Single(lane.PhaseIds);
        Assert.Single(lane.SubPhaseIds);
        Assert.Single(lane.DependsOnLaneIds);
        Assert.Single(lane.OwnedAreas);
    }

    [Fact]
    public void SubPhaseStatus_HasAllRequiredValues()
    {
        Assert.Contains(SubPhaseStatus.Planned, Enum.GetValues<SubPhaseStatus>());
        Assert.Contains(SubPhaseStatus.InProgress, Enum.GetValues<SubPhaseStatus>());
        Assert.Contains(SubPhaseStatus.Completed, Enum.GetValues<SubPhaseStatus>());
        Assert.Contains(SubPhaseStatus.Blocked, Enum.GetValues<SubPhaseStatus>());
        Assert.Contains(SubPhaseStatus.Deferred, Enum.GetValues<SubPhaseStatus>());
    }

    // -- Phase with SubPhases -----------------------------------------------------

    [Fact]
    public void Phase_WithSubPhases_HasProgressFromSubPhases()
    {
        var phase = new Phase
        {
            PhaseId = "PHASE-015",
            PhaseNumber = 15,
            Title = "Cache",
            State = PhaseState.InImplementation,
            SubPhases =
            [
                new() { SubPhaseId = "A", Title = "Core", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "B", Title = "Integration", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "C", Title = "Packages", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "D", Title = "Deferred", Status = SubPhaseStatus.Deferred },
                new() { SubPhaseId = "E", Title = "Planned", Status = SubPhaseStatus.Planned }
            ]
        };

        var progress = PhaseProgress.ForPhase(phase);
        // 4 completed/deferred out of 5 = 80% * 0.9 = 72%, no in-progress = 72%
        Assert.Equal(72, progress);
    }

    [Fact]
    public void PlannedPhase_WithCompletedSubPhases_DoesNotLookImplemented()
    {
        var phase = new Phase
        {
            PhaseId = "PHASE-052",
            PhaseNumber = 52,
            Title = "Planned phase",
            State = PhaseState.Planned,
            SubPhases =
            [
                new() { SubPhaseId = "A", Title = "Inventory", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "B", Title = "Design", Status = SubPhaseStatus.Completed }
            ]
        };

        Assert.Equal(0, PhaseProgress.ForPhase(phase));
    }

    [Fact]
    public void Phase_WithoutSubPhases_UsesStateForProgress()
    {
        var phase = new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "No sub-phases",
            State = PhaseState.Pass
        };
        Assert.Equal(100, PhaseProgress.ForPhase(phase));
    }

    [Fact]
    public void PassedPhase_WithSubPhases_UsesTerminalProgress()
    {
        var phase = new Phase
        {
            PhaseId = "PHASE-025",
            PhaseNumber = 25,
            Title = "Docs",
            State = PhaseState.Pass,
            SubPhases =
            [
                new() { SubPhaseId = "A", Title = "Docs", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "B", Title = "Deferred follow-up", Status = SubPhaseStatus.Deferred }
            ]
        };

        Assert.Equal(100, PhaseProgress.ForPhase(phase));
    }
}
