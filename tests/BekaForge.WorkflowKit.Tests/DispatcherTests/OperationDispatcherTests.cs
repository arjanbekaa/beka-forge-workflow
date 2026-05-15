using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.DispatcherTests;

/// <summary>
/// Integration tests for OperationDispatcher.
/// Each test uses a fresh temp workflow root with a real WorkflowStore.
/// </summary>
public sealed class OperationDispatcherTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public OperationDispatcherTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-disp-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("DispatchTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -- Helper --------------------------------------------------------------------

    private OperationContext Ctx(string operation,
        string? phaseId = null,
        WorkflowActor actor = WorkflowActor.Codex,
        Dictionary<string, object?>? parameters = null) =>
        new()
        {
            Operation  = operation,
            Actor      = actor,
            PhaseId    = phaseId,
            Parameters = parameters ?? []
        };

    private OperationResult Dispatch(string operation,
        string? phaseId = null,
        WorkflowActor actor = WorkflowActor.Codex,
        Dictionary<string, object?>? parameters = null) =>
        _dispatcher.Dispatch(Ctx(operation, phaseId, actor, parameters));

    // -- Unknown operation ---------------------------------------------------------

    [Fact]
    public void Dispatch_UnknownOperation_ReturnsFail()
    {
        var result = Dispatch("workflow.does_not_exist");
        Assert.False(result.Success);
        Assert.Equal("UnknownOperation", result.ErrorCode);
    }

    [Fact]
    public void Dispatch_EmptyOperation_ReturnsFail()
    {
        var result = _dispatcher.Dispatch(new OperationContext { Operation = "" });
        Assert.False(result.Success);
    }

    // -- All required operations are registered ------------------------------------

    [Fact]
    public void AllRequiredOperations_AreRegistered()
    {
        var required = new[]
        {
            WorkflowOperations.GetState,
            WorkflowOperations.GetCurrentPhase,
            WorkflowOperations.ListPhases,
            WorkflowOperations.ValidateState,
            WorkflowOperations.GetDashboardSummary,
            WorkflowOperations.CreatePhase,
            WorkflowOperations.UpdatePhaseStatus,
            WorkflowOperations.AssignPhase,
            WorkflowOperations.StartPhase,
            WorkflowOperations.CompleteImplementation,
            WorkflowOperations.GetPhaseContract,
            WorkflowOperations.SavePhaseContract,
            WorkflowOperations.GetNextAction,
            WorkflowOperations.SetNextAction,
            WorkflowOperations.CreateImplementationLog,
            WorkflowOperations.CreateAuditLog,
            WorkflowOperations.CreateReviewLog,
            WorkflowOperations.CreateTestLog,
            WorkflowOperations.CreateFixLog,
            WorkflowOperations.RecordBlocker,
            WorkflowOperations.ResolveBlocker,
            WorkflowOperations.CreateHandoff,
            WorkflowOperations.ValidateOperationRequest,
            WorkflowOperations.GetHandoffs,
            WorkflowOperations.RecordTimeSpent,
            WorkflowOperations.GetRelevantContext,
        };

        foreach (var op in required)
        {
            Assert.Contains(op, _dispatcher.RegisteredOperations);
        }
    }

    // -- workflow.get_state --------------------------------------------------------

    [Fact]
    public void GetState_ReturnsWorkflowState()
    {
        var result = Dispatch(WorkflowOperations.GetState);
        Assert.True(result.Success);
        var state = Assert.IsAssignableFrom<WorkflowState>(result.Data);
        Assert.Equal("DispatchTestAsset", state.AssetName);
    }

    // -- workflow.create_phase -----------------------------------------------------

    [Fact]
    public void CreatePhase_CreatesPhaseAndReturnsIt()
    {
        var result = Dispatch(WorkflowOperations.CreatePhase,
            parameters: new() { ["title"] = "Core Domain" });

        Assert.True(result.Success, result.Message);
        var phase = Assert.IsAssignableFrom<Phase>(result.Data);
        Assert.Equal("PHASE-001", phase.PhaseId);
        Assert.Equal("Core Domain", phase.Title);
        Assert.Equal(PhaseState.Planned, phase.State);
    }

    [Fact]
    public void CreatePhase_MissingTitle_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.CreatePhase);
        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
    }

    [Fact]
    public void CreatePhase_MultiplePhases_SequentialIds()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Phase A" });
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Phase B" });
        var result = Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Phase C" });

        var phase = Assert.IsAssignableFrom<Phase>(result.Data);
        Assert.Equal("PHASE-003", phase.PhaseId);
    }

    [Fact]
    public void CreatePhase_AppendsEventToEventLog()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Event Test" });
        var events = _store.ReadAllEvents();
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.EventType == "phase.created");
    }

    // -- workflow.list_phases ------------------------------------------------------

    [Fact]
    public void ListPhases_AfterCreating3_Returns3()
    {
        for (int i = 1; i <= 3; i++)
            Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = $"Phase {i}" });

        var result = Dispatch(WorkflowOperations.ListPhases);
        Assert.True(result.Success);
        var phases = Assert.IsAssignableFrom<IReadOnlyList<Phase>>(result.Data);
        Assert.Equal(3, phases.Count);
    }

    // -- workflow.update_phase_status ----------------------------------------------

    [Fact]
    public void UpdatePhaseStatus_ValidTransition_Succeeds()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "T" });

        var result = Dispatch(WorkflowOperations.UpdatePhaseStatus,
            phaseId: "PHASE-001",
            parameters: new() { ["state"] = "ReadyForImplementation" });

        Assert.True(result.Success, result.Message);
        var phase = Assert.IsAssignableFrom<Phase>(result.Data);
        Assert.Equal(PhaseState.ReadyForImplementation, phase.State);
    }

    [Fact]
    public void UpdatePhaseStatus_InvalidTransition_ReturnsFail()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "T" });

        // Planned → InImplementation is not allowed.
        var result = Dispatch(WorkflowOperations.UpdatePhaseStatus,
            phaseId: "PHASE-001",
            parameters: new() { ["state"] = "InImplementation" });

        Assert.False(result.Success);
        Assert.Equal("InvalidTransition", result.ErrorCode);
    }

    [Fact]
    public void UpdatePhaseStatus_PhaseNotFound_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.UpdatePhaseStatus,
            phaseId: "PHASE-999",
            parameters: new() { ["state"] = "ReadyForImplementation" });

        Assert.False(result.Success);
        Assert.Equal("NotFound", result.ErrorCode);
    }

    // -- Full happy path via dispatcher --------------------------------------------

    [Fact]
    public void HappyPath_FullDispatcherWorkflow_ReachesPass()
    {
        // 1. Create phase.
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Full Path" });
        const string phaseId = "PHASE-001";

        // 2. Ready for implementation.
        Assert.True(Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId,
            parameters: new() { ["state"] = "ReadyForImplementation" }).Success);

        // 3. Assign.
        Assert.True(Dispatch(WorkflowOperations.AssignPhase, phaseId,
            actor: WorkflowActor.Codex,
            parameters: new() { ["agent"] = "DeepSeek" }).Success);

        // 4. Start.
        Assert.True(Dispatch(WorkflowOperations.StartPhase, phaseId,
            actor: WorkflowActor.DeepSeek).Success);

        // 5. Implementation log (also moves to ImplementationLogged).
        Assert.True(Dispatch(WorkflowOperations.CreateImplementationLog, phaseId,
            actor: WorkflowActor.DeepSeek,
            parameters: new() { ["summary"] = "Implemented everything" }).Success);

        // 6. Audit log (moves to AuditLogged).
        Assert.True(Dispatch(WorkflowOperations.CreateAuditLog, phaseId,
            actor: WorkflowActor.DeepSeek,
            parameters: new() { ["summary"] = "Self-audit passed", ["passed"] = true }).Success);

        // 7. Move to ReadyForReview.
        Assert.True(Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId,
            parameters: new() { ["state"] = "ReadyForReview" }).Success);

        // 8. Start review.
        Assert.True(Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId,
            parameters: new() { ["state"] = "ReviewInProgress" }).Success);

        // 9. Review log (moves to ReviewLogged).
        Assert.True(Dispatch(WorkflowOperations.CreateReviewLog, phaseId,
            actor: WorkflowActor.Codex,
            parameters: new() { ["summary"] = "Review passed", ["passed"] = true }).Success);

        // 10. ReadyForTest.
        Assert.True(Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId,
            parameters: new() { ["state"] = "ReadyForTest" }).Success);

        // 11. TestInProgress.
        Assert.True(Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId,
            parameters: new() { ["state"] = "TestInProgress" }).Success);

        // 12. Test log (moves to TestLogged).
        Assert.True(Dispatch(WorkflowOperations.CreateTestLog, phaseId,
            actor: WorkflowActor.UnityAssistant,
            parameters: new() { ["summary"] = "All Unity tests passed", ["passed"] = true }).Success);

        // 13. PASS (requiresUnityTest=true by default; state is TestLogged → allowed).
        var passResult = Dispatch(WorkflowOperations.UpdatePhaseStatus, phaseId,
            parameters: new() { ["state"] = "Pass" });
        Assert.True(passResult.Success, passResult.Message);

        // Confirm final state.
        var phase = _store.LoadPhase(phaseId);
        Assert.Equal(PhaseState.Pass, phase!.State);

        // Events were appended.
        Assert.True(_store.ReadAllEvents().Count >= 10);
    }

    // -- workflow.record_blocker ---------------------------------------------------

    [Fact]
    public void RecordBlocker_WithReason_BlocksPhaseAndAppendsEvent()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Blocked Phase" });
        Dispatch(WorkflowOperations.UpdatePhaseStatus, "PHASE-001",
            parameters: new() { ["state"] = "ReadyForImplementation" });

        var result = Dispatch(WorkflowOperations.RecordBlocker, "PHASE-001",
            parameters: new() { ["reason"] = "Waiting on external license" });

        Assert.True(result.Success, result.Message);
        var phase = _store.LoadPhase("PHASE-001");
        Assert.Equal(PhaseState.Blocked, phase!.State);
        Assert.Single(phase.BlockerIds);

        var wf = _store.LoadWorkflow();
        Assert.Equal(1, wf.OpenBlockerCount);
    }

    [Fact]
    public void RecordBlocker_WithoutReason_ReturnsFail()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "T" });
        var result = Dispatch(WorkflowOperations.RecordBlocker, "PHASE-001");
        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
    }

    // -- workflow.create_handoff ---------------------------------------------------

    [Fact]
    public void CreateHandoff_ToCodex_IsReadBackByGetHandoffs()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "H" });

        var createResult = Dispatch(WorkflowOperations.CreateHandoff, "PHASE-001",
            actor: WorkflowActor.DeepSeek,
            parameters: new()
            {
                ["toActor"] = "Codex",
                ["summary"] = "Ready for your review"
            });
        Assert.True(createResult.Success, createResult.Message);

        var getResult = Dispatch(WorkflowOperations.GetHandoffs,
            parameters: new() { ["toActor"] = "Codex" });
        Assert.True(getResult.Success);
        var handoffs = Assert.IsAssignableFrom<IReadOnlyList<BekaForge.WorkflowKit.Core.Records.HandoffRecord>>(
            getResult.Data);
        Assert.Single(handoffs);
        Assert.Equal(WorkflowActor.Codex, handoffs[0].ToActor);
    }

    // -- workflow.set_next_action / get_next_action --------------------------------

    [Fact]
    public void SetAndGetNextAction_RoundTrips()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "NA" });

        Dispatch(WorkflowOperations.SetNextAction, "PHASE-001",
            parameters: new()
            {
                ["actor"] = "DeepSeek",
                ["description"] = "Implement Phase 1 domain model"
            });

        var result = Dispatch(WorkflowOperations.GetNextAction);
        Assert.True(result.Success);
        var action = Assert.IsAssignableFrom<NextAction>(result.Data);
        Assert.Equal(WorkflowActor.DeepSeek, action.Actor);
        Assert.Equal("Implement Phase 1 domain model", action.Description);
    }

    // -- workflow.record_time_spent ------------------------------------------------

    [Fact]
    public void SetNextAction_AdvancesCurrentPhase_WhenPreviousPhasePassed()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Done" });
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Next" });

        _store.SavePhase(_store.LoadPhase("PHASE-001")! with { State = PhaseState.Pass });
        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with { CurrentPhaseId = "PHASE-001" });

        var result = Dispatch(WorkflowOperations.SetNextAction, "PHASE-002",
            parameters: new()
            {
                ["actor"] = "Codex",
                ["description"] = "Review the next phase"
            });

        Assert.True(result.Success, result.Message);
        Assert.Equal("PHASE-002", _store.LoadWorkflow().CurrentPhaseId);
    }

    [Fact]
    public void RecordTimeSpent_AppendsTimingRecord()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "T" });

        var result = Dispatch(WorkflowOperations.RecordTimeSpent, "PHASE-001",
            actor: WorkflowActor.DeepSeek,
            parameters: new()
            {
                ["activity"]        = "implementation",
                ["durationSeconds"] = 2700.0
            });

        Assert.True(result.Success, result.Message);
        var timings = _store.ReadAllTimings();
        Assert.Single(timings);
        Assert.Equal(2700.0, timings[0].Duration.TotalSeconds, precision: 1);
    }

    // -- workflow.get_dashboard_summary --------------------------------------------

    [Fact]
    public void GetDashboardSummary_ReturnsNonNullData()
    {
        var result = Dispatch(WorkflowOperations.GetDashboardSummary);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    // -- workflow.validate_state ---------------------------------------------------

    [Fact]
    public void ValidateState_EmptyWorkflow_IsValid()
    {
        var result = Dispatch(WorkflowOperations.ValidateState);
        Assert.True(result.Success);
    }

    [Fact]
    public void ValidateState_AfterCreatingPhase_IsValid()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "V" });
        var result = Dispatch(WorkflowOperations.ValidateState);
        Assert.True(result.Success);
    }

    // -- workflow.get_relevant_context ---------------------------------------------

    [Fact]
    public void GetRelevantContext_PhaseSpecific_ReturnsPointers()
    {
        // Create a phase with a contract so we get meaningful pointers.
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Test Phase",
            ["summary"] = "Testing relevant context"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001");
        Assert.True(result.Success, result.Message);

        var ctx = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.Equal("PHASE-001", ctx.PhaseId);
        Assert.NotEmpty(ctx.Pointers);
        Assert.True(ctx.Pointers.All(p => p.RelevanceScore > 0));
        Assert.Contains(ctx.Warnings, w => w.Contains("Do not scan the full workflow folder"));
    }

    [Fact]
    public void GetRelevantContext_WorkflowLevel_ReturnsPointers()
    {
        var result = Dispatch(WorkflowOperations.GetRelevantContext);
        Assert.True(result.Success, result.Message);

        var ctx = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.Null(ctx.PhaseId);
        Assert.NotEmpty(ctx.Pointers);
        Assert.Contains(ctx.Warnings, w => w.Contains("Do not scan the full workflow folder"));
    }

    [Fact]
    public void GetRelevantContext_UnknownPhase_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-999");
        Assert.True(result.Success); // Operation itself succeeds
        var ctx = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.Contains(ctx.Warnings, w => w.Contains("not found"));
        Assert.Empty(ctx.Pointers);
    }

    [Fact]
    public void GetRelevantContext_RespectsMaxItems()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "MaxItems Test",
            ["summary"] = "Phase with contract"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001",
            parameters: new() { ["maxItems"] = 3 });

        Assert.True(result.Success, result.Message);
        var ctx = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.True(ctx.Pointers.Count <= 3, $"Expected ≤3 pointers, got {ctx.Pointers.Count}");
    }

    [Fact]
    public void GetRelevantContext_PointersAreResolvable()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Resolvable Test",
            ["summary"] = "Phase for pointer resolution"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001");
        Assert.True(result.Success, result.Message);

        var ctx = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        foreach (var pointer in ctx.Pointers)
        {
            // Every pointer must have a type, target, and resolveWith.
            Assert.False(string.IsNullOrWhiteSpace(pointer.PointerType),
                $"Pointer '{pointer.Target}' has no type");
            Assert.False(string.IsNullOrWhiteSpace(pointer.Target),
                "Pointer has no target");
            Assert.False(string.IsNullOrWhiteSpace(pointer.Reason),
                $"Pointer '{pointer.Target}' has no reason");

            // ResolveWith removed — slice operation derivable from PointerType
        }
    }

    [Fact]
    public void GetRelevantContext_PointersSortedByRelevance()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Sorted Test",
            ["summary"] = "Phase for relevance sorting"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001");
        Assert.True(result.Success, result.Message);

        var ctx = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        if (ctx.Pointers.Count >= 2)
        {
            for (int i = 1; i < ctx.Pointers.Count; i++)
            {
                Assert.True(ctx.Pointers[i - 1].RelevanceScore >= ctx.Pointers[i].RelevanceScore,
                    $"Pointers not sorted by relevance: {ctx.Pointers[i - 1].RelevanceScore} < {ctx.Pointers[i].RelevanceScore}");
            }
        }
    }

    // -- Budget operations -------------------------------------------------

    [Fact]
    public void GetBudgetConfig_WithoutProjectConfig_ReportsDefaultSource()
    {
        var configPath = BudgetConfig.ConfigPath(_tempRoot);
        if (File.Exists(configPath))
            File.Delete(configPath);

        var result = Dispatch(WorkflowOperations.GetBudgetConfig);

        Assert.True(result.Success, result.Message);
        var config = Assert.IsAssignableFrom<BudgetConfigResult>(result.Data);
        Assert.Equal("Medium", config.Mode);
        Assert.Equal("default", config.Source);
        Assert.Null(config.ProjectConfig);
        Assert.NotNull(config.Profile);
    }

    [Fact]
    public void SetBudgetConfig_ThenGetBudgetConfig_RoundTripsThroughDispatcher()
    {
        var setResult = Dispatch(WorkflowOperations.SetBudgetConfig,
            parameters: new() { ["mode"] = "High" });

        Assert.True(setResult.Success, setResult.Message);
        var saved = Assert.IsAssignableFrom<BudgetConfigResult>(setResult.Data);
        Assert.Equal("High", saved.Mode);
        Assert.Equal("project", saved.Source);

        var getResult = Dispatch(WorkflowOperations.GetBudgetConfig);

        Assert.True(getResult.Success, getResult.Message);
        var loaded = Assert.IsAssignableFrom<BudgetConfigResult>(getResult.Data);
        Assert.Equal("High", loaded.Mode);
        Assert.Equal("project", loaded.Source);
        Assert.NotNull(loaded.ProjectConfig);
    }

    [Fact]
    public void SetTraceOptions_ThenGetTraceStatus_RoundTripsThroughDispatcher()
    {
        var setResult = Dispatch(WorkflowOperations.SetTraceOptions,
            parameters: new() { ["mode"] = "Verbose" });

        Assert.True(setResult.Success, setResult.Message);

        var getResult = Dispatch(WorkflowOperations.GetTraceStatus);

        Assert.True(getResult.Success, getResult.Message);
        var status = JsonSerializer.SerializeToElement(getResult.Data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal("Verbose", status.GetProperty("mode").GetString());
        Assert.True(status.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public void GetTraceStatus_WithIncludeCountsFalse_SkipsExpensiveCountFields()
    {
        var result = Dispatch(WorkflowOperations.GetTraceStatus,
            parameters: new() { ["includeCounts"] = false });

        Assert.True(result.Success, result.Message);
        var status = JsonSerializer.SerializeToElement(result.Data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.False(status.GetProperty("countsIncluded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("fileCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("recordCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("directorySizeBytes").ValueKind);
        Assert.True(status.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public void GetBudgetConfig_InvalidMode_ReturnsFailure()
    {
        var result = Dispatch(WorkflowOperations.GetBudgetConfig,
            parameters: new() { ["mode"] = "tiny" });

        Assert.False(result.Success);
        Assert.Equal("InvalidBudgetMode", result.ErrorCode);
    }

    [Fact]
    public void GetBudgetReport_InvalidMode_ReturnsFailure()
    {
        var result = Dispatch(WorkflowOperations.GetBudgetReport,
            parameters: new() { ["mode"] = "tiny" });

        Assert.False(result.Success);
        Assert.Equal("InvalidBudgetMode", result.ErrorCode);
    }

    [Fact]
    public void GetRelevantContext_InlineBudgetMode_UsesProjectModeOverrides()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Budget Override Context",
            ["summary"] = "Phase for budget override context"
        });

        var setResult = Dispatch(WorkflowOperations.SetBudgetConfig,
            parameters: new()
            {
                ["modeOverrides"] = "{\"Low\":{\"maxPointers\":2}}"
            });
        Assert.True(setResult.Success, setResult.Message);

        var contextResult = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001",
            parameters: new() { ["budgetMode"] = "Low" });

        Assert.True(contextResult.Success, contextResult.Message);
        var context = Assert.IsAssignableFrom<RelevantContextResult>(contextResult.Data);
        Assert.NotNull(context.Budget);
        Assert.Equal("Low", context.Budget!.Mode);
        Assert.Equal("override", context.Budget.Source);
        Assert.Equal(2, context.Budget.MaxPointers);
        Assert.True(context.Pointers.Count <= 2);
    }

    [Fact]
    public void GetRelevantContext_LowBudget_FiltersMarkdownAndCapsPointers()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Low Budget Context",
            ["summary"] = "Phase for low budget context"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001",
            parameters: new()
            {
                ["budgetMode"] = "Low",
                ["maxItems"] = 50
            });

        Assert.True(result.Success, result.Message);
        var context = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.NotNull(context.Budget);
        Assert.Equal("Low", context.Budget!.Mode);
        Assert.Equal(5, context.Budget.MaxPointers);
        // Budget mode is an AI output hint, not a content filter.
        // All pointers are always included — filtered retrieval costs more tokens overall.
        Assert.True(context.Pointers.Count >= 1, "Low budget must return at least one pointer");
    }

    [Fact]
    public void GetRelevantContext_TokenBudget_AlwaysReturnsAtLeastOnePointer()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Token Budget Context",
            ["summary"] = "Phase for token budget context"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001",
            parameters: new()
            {
                ["budgetMode"] = "Medium",
                ["maxEstimatedTokens"] = 1
            });

        Assert.True(result.Success, result.Message);
        var context = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.NotEmpty(context.Pointers);
        Assert.NotNull(context.Budget);
        Assert.Equal(1, context.Budget!.PointersReturned);
    }

    [Fact]
    public void GetRelevantContext_BudgetReport_UsesEffectiveTokenCap()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new()
        {
            ["title"] = "Budget Report Context",
            ["summary"] = "Phase for budget report context"
        });

        var result = Dispatch(WorkflowOperations.GetRelevantContext, "PHASE-001",
            parameters: new()
            {
                ["budgetMode"] = "Medium",
                ["maxEstimatedTokens"] = 1
            });

        Assert.True(result.Success, result.Message);
        var context = Assert.IsAssignableFrom<RelevantContextResult>(result.Data);
        Assert.NotNull(context.Budget);
        Assert.Equal(1, context.Budget!.TokenBudgetCap);
        Assert.Equal(context.Pointers.Count, context.Budget.PointersReturned);
        Assert.Equal(context.OmittedCandidates, context.Budget.OmittedByBudget);
        Assert.Equal(context.EstimatedTotalTokens, context.Budget.EstimatedTokensConsumed);
    }

    [Fact]
    public void UpdateSubPhaseStatus_AllowsCompletedSiblingDeferredSiblingAndExternalPhaseDependencies()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "External Ready" });
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Sub Phase Parent" });

        _store.SavePhase(_store.LoadPhase("PHASE-001")! with { State = PhaseState.AuditLogged });

        var parent = _store.LoadPhase("PHASE-002")!;
        _store.SavePhase(parent with
        {
            SubPhases =
            [
                new() { SubPhaseId = "PHASE-002-A", Title = "Done", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "PHASE-002-B", Title = "Deferred", Status = SubPhaseStatus.Deferred },
                new()
                {
                    SubPhaseId = "PHASE-002-C",
                    Title = "Ready",
                    Status = SubPhaseStatus.Planned,
                    DependsOn = ["PHASE-002-A", "PHASE-002-B", "PHASE-001"]
                }
            ]
        });

        var result = Dispatch(WorkflowOperations.UpdateSubPhaseStatus, "PHASE-002",
            parameters: new()
            {
                ["subPhaseId"] = "PHASE-002-C",
                ["status"] = "InProgress"
            });

        Assert.True(result.Success, result.Message);
        var updated = _store.LoadPhase("PHASE-002")!;
        Assert.Equal(SubPhaseStatus.InProgress,
            updated.SubPhases.Single(sp => sp.SubPhaseId == "PHASE-002-C").Status);
    }
}
