using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for <see cref="BekaForge.WorkflowKit.Dashboard.Wpf.DashboardViewModel"/>.
/// Focuses on the data-loading logic, not the WPF UI.
/// </summary>
public sealed class DashboardViewModelTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowInitializer _initializer;

    public DashboardViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-dash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _initializer = new WorkflowInitializer(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Load_EmptyPath_ReturnsFalse()
    {
        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.False(vm.Load(""));
        Assert.Contains("No root path", vm.ErrorMessage);
    }

    [Fact]
    public void Load_UninitializedRoot_ReturnsFalse()
    {
        var unusedDrive = "ZYXWVUTSRQPONMLKJIHGFEDCBA"
            .Select(ch => $"{ch}:\\")
            .FirstOrDefault(path => !Directory.Exists(path))
            ?? Path.Combine(_tempRoot, "DefinitelyMissingRoot");

        var uninitializedPath = Path.Combine(unusedDrive, "bfwf-uninitialized-root");
        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.False(vm.Load(uninitializedPath));
        Assert.Contains(".workflowkit/workflow.json", vm.ErrorMessage);
    }

    [Fact]
    public void Load_InitializedWorkflow_ReturnsTrue()
    {
        _initializer.Initialize("TestAsset");
        var vm = new Dashboard.Wpf.DashboardViewModel();

        Assert.True(vm.Load(_tempRoot));
        Assert.Equal("TestAsset", vm.AssetName);
        Assert.True(vm.IsLoaded);
        Assert.Empty(vm.ErrorMessage);
        Assert.Empty(vm.Phases);
    }

    [Fact]
    public void Load_WorkflowWithPhases_ReturnsPhasesInOrder()
    {
        var state = _initializer.Initialize("MultiPhase");
        var store = new WorkflowStore(_tempRoot);

        // Create phases out of sequence order to test sorting.
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-003", PhaseNumber = 3, Title = "Phase C",
            State = PhaseState.Pass
        });
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001", PhaseNumber = 1, Title = "Phase A",
            State = PhaseState.Planned
        });
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002", PhaseNumber = 2, Title = "Phase B",
            State = PhaseState.InImplementation,
            AssignedAgent = WorkflowActor.DeepSeek
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));
        Assert.Equal(3, vm.Phases.Count);

        // Must be sorted by PhaseNumber.
        Assert.Equal("PHASE-001", vm.Phases[0].PhaseId);
        Assert.Equal("PHASE-002", vm.Phases[1].PhaseId);
        Assert.Equal("PHASE-003", vm.Phases[2].PhaseId);

        // Check assigned agent.
        Assert.Equal("DeepSeek", vm.Phases[1].AssignedAgent);
        Assert.Equal("-", vm.Phases[0].AssignedAgent);
    }

    [Fact]
    public void Load_PhaseWithLogs_CountsAreCorrect()
    {
        _initializer.Initialize("LogTest");
        var store = new WorkflowStore(_tempRoot);

        var phase = new Phase
        {
            PhaseId = "PHASE-001", PhaseNumber = 1, Title = "Log Phase",
            ImplementationLogIds = ["IMP-001", "IMP-002"],
            AuditLogIds = ["AUD-001"],
            ReviewLogIds = ["REV-001", "REV-002", "REV-003"],
            ValidationLogIds = [],
            FixLogIds = ["FIX-001"]
        };
        store.SavePhase(phase);

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));
        Assert.Single(vm.Phases);

        var row = vm.Phases[0];
        Assert.Equal(2, row.ImplementationCount);
        Assert.Equal(1, row.AuditCount);
        Assert.Equal(3, row.ReviewCount);
        Assert.Equal(0, row.TestCount);
        Assert.Equal(1, row.FixCount);
    }

    [Fact]
    public void Load_NextAction_ParsedCorrectly()
    {
        var state = _initializer.Initialize("ActionTest");
        var store = new WorkflowStore(_tempRoot);

        store.SaveWorkflow(state with
        {
            NextAction = new NextAction
            {
                ActionId = "TIME-001",
                Actor = WorkflowActor.Codex,
                Description = "Review Phase 1 architecture"
            }
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));
        Assert.Contains("[Codex]", vm.NextActionDescription);
        Assert.Contains("Review Phase 1 architecture", vm.NextActionDescription);
    }

    [Fact]
    public void Load_NoNextAction_ShowsNotSet()
    {
        _initializer.Initialize("NoAction");
        var vm = new Dashboard.Wpf.DashboardViewModel();

        Assert.True(vm.Load(_tempRoot));
        Assert.Equal("(not set)", vm.NextActionDescription);
    }

    [Fact]
    public void Load_OpenBlockerCount_Reflected()
    {
        _initializer.Initialize("BlockerTest");
        var store = new WorkflowStore(_tempRoot);
        store.AppendBlocker(new BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Waiting on design input",
            ReportedBy = WorkflowActor.Codex
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));
        Assert.Equal(1, vm.OpenBlockerCount);
        Assert.Contains(vm.OpenBlockers, b => b.Contains("Waiting on design input"));
    }

    [Fact]
    public void Load_PhaseStateFormatting_UsesConstants()
    {
        var state = _initializer.Initialize("FormatTest");
        var store = new WorkflowStore(_tempRoot);

        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001", PhaseNumber = 1, Title = "Pass",
            State = PhaseState.Pass
        });
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002", PhaseNumber = 2, Title = "Blocked",
            State = PhaseState.Blocked
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));

        Assert.Equal("PASS", vm.Phases[0].StateDisplay);
        Assert.Equal("BLOCKED", vm.Phases[1].StateDisplay);
    }

    [Fact]
    public void Load_ComputesPhaseAndOverallProgress()
    {
        _initializer.Initialize("ProgressTest");
        var store = new WorkflowStore(_tempRoot);

        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001", PhaseNumber = 1, Title = "Done",
            State = PhaseState.Pass
        });
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002", PhaseNumber = 2, Title = "Working",
            State = PhaseState.InImplementation
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));

        Assert.Equal(100, vm.Phases[0].ProgressPercent);
        Assert.Equal(35, vm.Phases[1].ProgressPercent);
        Assert.Equal(68, vm.OverallProgressPercent);
        Assert.Equal(1, vm.CompletedPhases);
    }

    [Fact]
    public void Load_PlannedPhaseWithCompletedSubPhases_ShowsZeroProgress()
    {
        _initializer.Initialize("PlannedSubPhaseProgress");
        var store = new WorkflowStore(_tempRoot);

        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-052",
            PhaseNumber = 52,
            Title = "Planned checklist",
            State = PhaseState.Planned,
            SubPhases =
            [
                new() { SubPhaseId = "PHASE-052-A", Title = "Inventory", Status = SubPhaseStatus.Completed },
                new() { SubPhaseId = "PHASE-052-B", Title = "Design", Status = SubPhaseStatus.Completed }
            ]
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));

        Assert.Single(vm.Phases);
        Assert.Equal(0, vm.Phases[0].ProgressPercent);
        Assert.Equal(0, vm.OverallProgressPercent);
    }

    [Fact]
    public void Load_RecentActivity_IncludesEventsAndLogs()
    {
        _initializer.Initialize("ActivityTest");
        var store = new WorkflowStore(_tempRoot);
        store.AppendEvent(new WorkflowEvent
        {
            EventId = "EVT-001",
            EventType = "phase.created",
            Actor = WorkflowActor.Codex,
            PhaseId = "PHASE-001",
            Summary = "Created first phase"
        });
        store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Implemented first phase",
            Status = PhaseState.ImplementationLogged
        });

        var vm = new Dashboard.Wpf.DashboardViewModel();
        Assert.True(vm.Load(_tempRoot));

        Assert.Contains(vm.RecentActivity, a => a.Contains("Created first phase"));
        Assert.Contains(vm.RecentActivity, a => a.Contains("Implemented first phase"));
    }

    [Fact]
    public void DiscoverWorkflowRoot_WalksUpFromSubdirectory()
    {
        _initializer.Initialize("DiscoverTest");
        var nested = Path.Combine(_tempRoot, "Assets", "Scripts");
        Directory.CreateDirectory(nested);

        var discovered = Dashboard.Wpf.DashboardViewModel.DiscoverWorkflowRoot(nested);

        Assert.Equal(_tempRoot, discovered);
    }
}
