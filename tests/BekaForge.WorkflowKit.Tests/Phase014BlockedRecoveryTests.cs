using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// PHASE-014: Blocked phase recovery tests.
/// Covers auto-advance on last-blocker resolve and Blocked → reopen fallback.
/// </summary>
public sealed class Phase014BlockedRecoveryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public Phase014BlockedRecoveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-p014-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("BlockerTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private string CreatePhase(string title = "Test Phase")
    {
        var r = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.CreatePhase,
            Actor     = WorkflowActor.Implementer,
            Parameters = new Dictionary<string, object?> { ["title"] = title }
        });
        Assert.True(r.Success);
        return ((Phase)r.Data!).PhaseId;
    }

    private string RecordBlocker(string phaseId, string reason)
    {
        var r = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.RecordBlocker,
            PhaseId   = phaseId,
            Actor     = WorkflowActor.User,
            Parameters = new Dictionary<string, object?> { ["reason"] = reason }
        });
        Assert.True(r.Success, r.Message);
        return ((BekaForge.WorkflowKit.Core.Records.BlockerRecord)r.Data!).BlockerId;
    }

    private void ResolveBlocker(string blockerId, string resolution = "Fixed.")
    {
        var r = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ResolveBlocker,
            Actor     = WorkflowActor.User,
            Parameters = new Dictionary<string, object?> { ["blockerId"] = blockerId, ["resolution"] = resolution }
        });
        Assert.True(r.Success, r.Message);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolvingLastBlocker_AutoAdvancesBlockedPhaseToReadyForImplementation()
    {
        var phaseId = CreatePhase();
        var blkId = RecordBlocker(phaseId, "Waiting on external API");

        var blocked = _store.LoadPhase(phaseId)!;
        Assert.Equal(PhaseState.Blocked, blocked.State);

        ResolveBlocker(blkId);

        var unblocked = _store.LoadPhase(phaseId)!;
        Assert.Equal(PhaseState.ReadyForImplementation, unblocked.State);
    }

    [Fact]
    public void ResolvingOneOfTwoBlockers_DoesNotAutoAdvance()
    {
        var phaseId = CreatePhase();
        var blk1 = RecordBlocker(phaseId, "First blocker");
        var blk2 = RecordBlocker(phaseId, "Second blocker");

        ResolveBlocker(blk1);

        // Still one open blocker — should remain Blocked.
        var phase = _store.LoadPhase(phaseId)!;
        Assert.Equal(PhaseState.Blocked, phase.State);
    }

    [Fact]
    public void ResolvingAllBlockers_AutoAdvances()
    {
        var phaseId = CreatePhase();
        var blk1 = RecordBlocker(phaseId, "First blocker");
        var blk2 = RecordBlocker(phaseId, "Second blocker");

        ResolveBlocker(blk1);
        ResolveBlocker(blk2);

        var phase = _store.LoadPhase(phaseId)!;
        Assert.Equal(PhaseState.ReadyForImplementation, phase.State);
    }

    [Fact]
    public void AutoAdvance_AppendsPhaseUnblockedEvent()
    {
        var phaseId = CreatePhase();
        var blkId = RecordBlocker(phaseId, "Some blocker");

        ResolveBlocker(blkId);

        var events = _store.ReadAllEvents()
            .Where(e => e.PhaseId == phaseId && e.EventType == "phase.unblocked")
            .ToList();

        Assert.Single(events);
        Assert.Contains("ReadyForImplementation", events[0].Summary);
    }

    [Fact]
    public void ReopenPhase_AcceptsBlockedState()
    {
        var phaseId = CreatePhase();
        RecordBlocker(phaseId, "Hard blocker");

        var phase = _store.LoadPhase(phaseId)!;
        Assert.Equal(PhaseState.Blocked, phase.State);

        var r = _dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.ReopenPhase,
            PhaseId    = phaseId,
            Actor      = WorkflowActor.User,
            Parameters = new Dictionary<string, object?> { ["reason"] = "Manually overriding blocker" }
        });

        Assert.True(r.Success, r.Message);
        Assert.Equal(PhaseState.ReadyForImplementation, _store.LoadPhase(phaseId)!.State);
    }

    [Fact]
    public void Validator_AllowsBlockedToReadyForImplementation()
    {
        var validator = new PhaseTransitionValidator();
        var result = validator.Validate(new TransitionContext
        {
            CurrentState = PhaseState.Blocked,
            TargetState  = PhaseState.ReadyForImplementation
        });
        Assert.True(result.IsSuccess);
    }
}
