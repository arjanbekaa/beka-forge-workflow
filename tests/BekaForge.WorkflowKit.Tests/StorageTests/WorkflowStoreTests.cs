using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using System.Collections.Concurrent;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.StorageTests;

/// <summary>
/// Integration tests for WorkflowStore — the storage facade.
/// Each test initializes a fresh temp directory.
/// </summary>
public sealed class WorkflowStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;

    public WorkflowStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-store-{Guid.NewGuid():N}");
        // Initialize a real workflow so the store has a valid root.
        new WorkflowInitializer(_tempRoot).Initialize("StoreTestAsset");
        _store = new WorkflowStore(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -- WorkflowState round-trip --------------------------------------------------

    [Fact]
    public void LoadWorkflow_ReturnsInitialState()
    {
        var state = _store.LoadWorkflow();
        Assert.Equal("StoreTestAsset", state.AssetName);
        Assert.Equal(WorkflowLayout.SchemaVersion, state.SchemaVersion);
    }

    [Fact]
    public void SaveAndLoadWorkflow_RoundTrips()
    {
        var original = _store.LoadWorkflow();
        var updated = original with
        {
            CurrentPhaseId = "PHASE-001",
            LastStatus = PhaseState.InImplementation,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        _store.SaveWorkflow(updated);
        var loaded = _store.LoadWorkflow();

        Assert.Equal("PHASE-001", loaded.CurrentPhaseId);
        Assert.Equal(PhaseState.InImplementation, loaded.LastStatus);
    }

    [Fact]
    public void SaveWorkflow_DoesNotCorruptOnReload()
    {
        var state = _store.LoadWorkflow() with
        {
            ArchitectureConstraints = ["No external APIs", "Unity 2022.3 LTS only"],
            PhaseIds = ["PHASE-001", "PHASE-002"]
        };
        _store.SaveWorkflow(state);

        var loaded = _store.LoadWorkflow();
        Assert.Equal(2, loaded.ArchitectureConstraints.Count);
        Assert.Contains("No external APIs", loaded.ArchitectureConstraints);
        Assert.Equal(2, loaded.PhaseIds.Count);
    }

    // -- Phase round-trip ----------------------------------------------------------

    [Fact]
    public void SaveAndLoadPhase_RoundTrips()
    {
        var phase = new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Core Domain",
            Summary = "Phase 1 domain model",
            State = PhaseState.InImplementation,
            AssignedAgent = WorkflowActor.DeepSeek
        };

        _store.SavePhase(phase);
        var loaded = _store.LoadPhase("PHASE-001");

        Assert.NotNull(loaded);
        Assert.Equal("PHASE-001", loaded.PhaseId);
        Assert.Equal(PhaseState.InImplementation, loaded.State);
        Assert.Equal(WorkflowActor.DeepSeek, loaded.AssignedAgent);
        Assert.Equal("Core Domain", loaded.Title);
    }

    [Fact]
    public void LoadPhase_WhenNotExists_ReturnsNull()
    {
        var result = _store.LoadPhase("PHASE-999");
        Assert.Null(result);
    }

    [Fact]
    public void LoadAllPhases_ReturnsAllSavedPhases()
    {
        for (int i = 1; i <= 3; i++)
        {
            _store.SavePhase(new Phase
            {
                PhaseId = $"PHASE-00{i}",
                PhaseNumber = i,
                Title = $"Phase {i}"
            });
        }

        var all = _store.LoadAllPhases();
        Assert.Equal(3, all.Count);
        Assert.Equal("PHASE-001", all[0].PhaseId);
        Assert.Equal("PHASE-003", all[2].PhaseId);
    }

    [Fact]
    public void LoadAllPhases_ReturnsPhasesOrderedByNumber()
    {
        // Save out of order.
        _store.SavePhase(new Phase { PhaseId = "PHASE-003", PhaseNumber = 3, Title = "C" });
        _store.SavePhase(new Phase { PhaseId = "PHASE-001", PhaseNumber = 1, Title = "A" });
        _store.SavePhase(new Phase { PhaseId = "PHASE-002", PhaseNumber = 2, Title = "B" });

        var all = _store.LoadAllPhases();
        Assert.Equal(1, all[0].PhaseNumber);
        Assert.Equal(2, all[1].PhaseNumber);
        Assert.Equal(3, all[2].PhaseNumber);
    }

    // -- Events --------------------------------------------------------------------

    [Fact]
    public void AppendEvent_IsReadBack()
    {
        var evt = new WorkflowEvent
        {
            EventId = "EVT-001",
            EventType = "phase.created",
            Actor = WorkflowActor.Codex,
            Summary = "Phase 1 created",
            PhaseId = "PHASE-001"
        };
        _store.AppendEvent(evt);

        var all = _store.ReadAllEvents();
        Assert.Single(all);
        Assert.Equal("EVT-001", all[0].EventId);
    }

    [Fact]
    public void AppendEvent_MultipleEvents_AllReadBack()
    {
        for (int i = 1; i <= 10; i++)
        {
            _store.AppendEvent(new WorkflowEvent
            {
                EventId = $"EVT-{i:D3}",
                EventType = "test",
                Actor = WorkflowActor.WorkflowKit,
                Summary = $"event {i}"
            });
        }

        Assert.Equal(10, _store.ReadAllEvents().Count);
    }

    // -- Implementation log --------------------------------------------------------

    [Fact]
    public void AppendImplementation_RoundTrips()
    {
        var record = new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Implemented core types",
            Status = PhaseState.ImplementationLogged,
            FilesModified = ["Core/Phase.cs", "Core/PhaseState.cs"]
        };

        _store.AppendImplementation(record);
        var all = _store.ReadAllImplementations();

        Assert.Single(all);
        Assert.Equal("IMP-001", all[0].ImplementationId);
        Assert.Equal(2, all[0].FilesModified.Count);
    }

    // -- Blocker round-trip --------------------------------------------------------

    [Fact]
    public void AppendBlocker_RoundTrips()
    {
        var blocker = new BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Unity package license missing",
            ReportedBy = WorkflowActor.Codex,
            IsResolved = false
        };
        _store.AppendBlocker(blocker);

        var all = _store.ReadAllBlockers();
        Assert.Single(all);
        Assert.Equal("BLK-001", all[0].BlockerId);
        Assert.False(all[0].IsResolved);
    }

    // -- Handoff round-trip --------------------------------------------------------

    [Fact]
    public void AppendHandoff_RoundTrips()
    {
        var handoff = new HandoffRecord
        {
            HandoffId = "HANDOFF-001",
            PhaseId = "PHASE-001",
            FromActor = WorkflowActor.DeepSeek,
            ToActor = WorkflowActor.Codex,
            Summary = "Ready for review",
            OperationHint = WorkflowKit.AgentContracts.WorkflowOperations.GetContextBundle
        };
        _store.AppendHandoff(handoff);

        var all = _store.ReadAllHandoffs();
        Assert.Single(all);
        Assert.Equal(WorkflowActor.Codex, all[0].ToActor);
    }

    // -- Timing round-trip --------------------------------------------------------

    [Fact]
    public void AppendTiming_RoundTrips()
    {
        var duration = TimeSpan.FromMinutes(47.5);
        var record = new TimingRecord
        {
            TimingId = "TIME-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Activity = "implementation",
            Duration = duration
        };
        _store.AppendTiming(record);

        var all = _store.ReadAllTimings();
        Assert.Single(all);
        // Allow small floating-point delta from seconds-based serialization.
        Assert.Equal(duration.TotalSeconds, all[0].Duration.TotalSeconds, precision: 3);
    }

    [Fact]
    public void SaveAndLoadOrchestrationSession_RoundTrips()
    {
        var session = new OrchestrationSession
        {
            SessionId = "ORS-001",
            PhaseId = "PHASE-040",
            WorkflowId = _store.LoadWorkflow().WorkflowId,
            ManagerActor = WorkflowActor.Codex,
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ObjectiveSnapshot = "Build orchestration storage",
            ScopeSnapshot = "Core and storage"
        };

        _store.SaveOrchestrationSession(session);
        var loaded = _store.LoadOrchestrationSession("ORS-001");

        Assert.NotNull(loaded);
        Assert.Equal("ORS-001", loaded.SessionId);
        Assert.Equal(OrchestrationSessionState.WaitingForAgent, loaded.SessionState);
    }

    [Fact]
    public void SaveAndLoadOrchestrationRun_RoundTrips()
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

        _store.SaveOrchestrationRun(run);
        var loaded = _store.LoadOrchestrationRun("ORR-001");

        Assert.NotNull(loaded);
        Assert.Equal("ORR-001", loaded.RunId);
        Assert.Equal(OrchestrationRunRole.Implementer, loaded.Role);
    }

    [Fact]
    public void AppendOrchestrationGateDecision_RoundTrips()
    {
        var record = new OrchestrationGateDecisionRecord
        {
            GateDecisionId = "OGD-001",
            SessionId = "ORS-001",
            PhaseId = "PHASE-041",
            RunId = "ORR-001",
            GateKind = OrchestrationGateKind.Audit,
            Decision = OrchestrationDecision.Advance,
            DecisionActor = WorkflowActor.Codex,
            Rationale = "Audit passed."
        };

        _store.AppendOrchestrationGateDecision(record);
        var all = _store.ReadAllOrchestrationGateDecisions();

        Assert.Single(all);
        Assert.Equal("OGD-001", all[0].GateDecisionId);
    }

    [Fact]
    public void AppendOrchestrationRunEvent_RoundTrips()
    {
        var record = new OrchestrationRunEventRecord
        {
            RunEventId = "ORE-001",
            SessionId = "ORS-001",
            RunId = "ORR-001",
            PhaseId = "PHASE-041",
            EventKind = OrchestrationEventKind.RunCreated,
            Actor = WorkflowActor.Codex,
            Summary = "Run created."
        };

        _store.AppendOrchestrationRunEvent(record);
        var all = _store.ReadAllOrchestrationRunEvents();

        Assert.Single(all);
        Assert.Equal("ORE-001", all[0].RunEventId);
    }

    // -- ID allocation -------------------------------------------------------------

    [Fact]
    public void NextPhaseId_ReturnsSequentialIds()
    {
        Assert.Equal("PHASE-001", _store.NextPhaseId());
        Assert.Equal("PHASE-002", _store.NextPhaseId());
    }

    [Fact]
    public void AllIdTypes_ReturnCorrectFirstId()
    {
        Assert.Equal("IMP-001",     _store.NextImplementationId());
        Assert.Equal("AUD-001",     _store.NextAuditId());
        Assert.Equal("REV-001",     _store.NextReviewId());
        Assert.Equal("TEST-001",    _store.NextTestId());
        Assert.Equal("FIX-001",     _store.NextFixId());
        Assert.Equal("BLK-001",     _store.NextBlockerId());
        Assert.Equal("HANDOFF-001", _store.NextHandoffId());
        Assert.Equal("TIME-001",    _store.NextTimingId());
        Assert.Equal("EVT-001",     _store.NextEventId());
        Assert.Equal("ORS-001",     _store.NextOrchestrationSessionId());
        Assert.Equal("ORR-001",     _store.NextOrchestrationRunId());
        Assert.Equal("OGD-001",     _store.NextOrchestrationGateDecisionId());
        Assert.Equal("ORE-001",     _store.NextOrchestrationRunEventId());
    }

    [Fact]
    public void NextAuditId_ReconcilesStaleSequenceAgainstObservedLog()
    {
        _store.AppendAudit(new AuditRecord
        {
            AuditId = "AUD-030",
            PhaseId = "PHASE-030",
            Actor = WorkflowActor.Auditor,
            Summary = "Observed audit",
            Passed = true
        });

        File.WriteAllText(WorkflowLayout.SequencesFile(_tempRoot), """
            {
              "audit": 1
            }
            """);

        var reloadedStore = new WorkflowStore(_tempRoot);
        Assert.Equal("AUD-031", reloadedStore.NextAuditId());
    }

    [Fact]
    public void NextAuditId_IsUniqueAcrossConcurrentStoreInstances()
    {
        const int taskCount = 20;
        var ids = new ConcurrentBag<string>();

        Parallel.For(0, taskCount, _ =>
        {
            var store = new WorkflowStore(_tempRoot);
            ids.Add(store.NextAuditId());
        });

        Assert.Equal(taskCount, ids.Count);
        Assert.Equal(taskCount, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("AUD-001", ids);
        Assert.Contains($"AUD-{taskCount:D3}", ids);
    }

    [Fact]
    public void NextOrchestrationSessionId_ReconcilesStaleSequenceAgainstObservedJsonFiles()
    {
        _store.SaveOrchestrationSession(new OrchestrationSession
        {
            SessionId = "ORS-030",
            PhaseId = "PHASE-040",
            WorkflowId = _store.LoadWorkflow().WorkflowId,
            ManagerActor = WorkflowActor.Codex,
            ObjectiveSnapshot = "Observed session",
            ScopeSnapshot = "Observed scope"
        });

        File.WriteAllText(WorkflowLayout.SequencesFile(_tempRoot), """
            {
              "orchestrationSession": 1
            }
            """);

        var reloadedStore = new WorkflowStore(_tempRoot);
        Assert.Equal("ORS-031", reloadedStore.NextOrchestrationSessionId());
    }

    // -- Safety: unknown files are not deleted -------------------------------------

    [Fact]
    public void SaveWorkflow_DoesNotDeleteUnknownFiles()
    {
        // Place a custom file in .bekaforge/ that WorkflowKit did not create.
        var customPath = Path.Combine(WorkflowLayout.Root(_tempRoot), "custom-notes.txt");
        File.WriteAllText(customPath, "human note");

        // Perform a save operation.
        var state = _store.LoadWorkflow();
        _store.SaveWorkflow(state with { UpdatedUtc = DateTimeOffset.UtcNow });

        // The custom file must still exist.
        Assert.True(File.Exists(customPath), "Unknown files in .bekaforge/ must not be deleted.");
    }
}
