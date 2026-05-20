using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for the Rebuildable Context Index (PHASE-009).
/// Verifies SQLite index creation, rebuild idempotency, health reporting,
/// and that source JSON/JSONL remains authoritative.
/// </summary>
public sealed class ContextIndexTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public ContextIndexTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-index-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("IndexTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);

        _store.SavePhase(new Core.Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Seed phase",
            Summary = "Seed phase for context index tests.",
            State = Core.PhaseState.Planned
        });

        _store.AppendEvent(new Core.WorkflowEvent
        {
            EventId = "EVT-001",
            EventType = "phase.created",
            Actor = Core.WorkflowActor.Codex,
            PhaseId = "PHASE-001",
            Summary = "Seed event for context index tests."
        });

        // Seed some data so the index has content to index.
        _store.AppendImplementation(new Core.Records.ImplementationRecord
        {
            ImplementationId = "IMP-001", PhaseId = "PHASE-001",
            Actor = Core.WorkflowActor.DeepSeek, Summary = "Test implementation",
            Status = Core.PhaseState.ImplementationLogged
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private OperationContext Ctx(string operation) =>
        new() { Operation = operation, Actor = Core.WorkflowActor.Codex };

    // -- Rebuild creates database -------------------------------------------------

    [Fact]
    public void Rebuild_CreatesDatabaseFile()
    {
        var builder = new ContextIndexBuilder(_tempRoot);
        Assert.False(File.Exists(builder.DatabasePath));

        var health = builder.Rebuild();
        Assert.True(health.DatabaseExists);
        Assert.True(File.Exists(builder.DatabasePath));
    }

    [Fact]
    public void Rebuild_ReturnsHealthWithCounts()
    {
        var builder = new ContextIndexBuilder(_tempRoot);
        var health = builder.Rebuild();

        Assert.True(health.IsHealthy, string.Join(" | ", health.Errors));
        Assert.True(health.PhaseCount > 0, string.Join(" | ", health.Errors));
        Assert.True(health.ImplementationCount > 0);
        Assert.True(health.EventCount > 0);
        Assert.Empty(health.Errors);
    }

    // -- Rebuild is idempotent ----------------------------------------------------

    [Fact]
    public void Rebuild_IsIdempotent()
    {
        var builder = new ContextIndexBuilder(_tempRoot);

        var h1 = builder.Rebuild();
        var h2 = builder.Rebuild();

        Assert.Equal(h1.PhaseCount, h2.PhaseCount);
        Assert.Equal(h1.ImplementationCount, h2.ImplementationCount);
        Assert.Equal(h1.EventCount, h2.EventCount);
    }

    // -- Delete and rebuild restores ----------------------------------------------

    [Fact]
    public void DeleteAndRebuild_RestoresIndex()
    {
        var builder = new ContextIndexBuilder(_tempRoot);

        var h1 = builder.Rebuild();
        Assert.True(File.Exists(builder.DatabasePath));

        File.Delete(builder.DatabasePath);
        Assert.False(File.Exists(builder.DatabasePath));

        var h2 = builder.Rebuild();
        Assert.True(File.Exists(builder.DatabasePath));
        Assert.Equal(h1.PhaseCount, h2.PhaseCount);
        Assert.Equal(h1.ImplementationCount, h2.ImplementationCount);
    }

    // -- Health without database --------------------------------------------------

    [Fact]
    public void GetHealth_NoDatabase_ReturnsNull()
    {
        var builder = new ContextIndexBuilder(_tempRoot);
        var health = builder.GetHealth();
        Assert.Null(health);
    }

    [Fact]
    public void GetHealth_AfterRebuild_ReturnsHealth()
    {
        var builder = new ContextIndexBuilder(_tempRoot);
        builder.Rebuild();

        var health = builder.GetHealth();
        Assert.NotNull(health);
        Assert.True(health.DatabaseExists);
        Assert.True(health.PhaseCount > 0);
    }

    // -- Index never becomes source of truth --------------------------------------

    [Fact]
    public void Index_IsNotSourceOfTruth()
    {
        var builder = new ContextIndexBuilder(_tempRoot);
        builder.Rebuild();

        // Add a record after the index was built.
        _store.AppendImplementation(new Core.Records.ImplementationRecord
        {
            ImplementationId = "IMP-999", PhaseId = "PHASE-001",
            Actor = Core.WorkflowActor.DeepSeek, Summary = "Post-index record",
            Status = Core.PhaseState.ImplementationLogged
        });

        // Index is stale — it doesn't know about the new record.
        var health = builder.GetHealth();
        Assert.NotNull(health);

        // Rebuild picks it up.
        var h2 = builder.Rebuild();
        Assert.Equal(health!.ImplementationCount + 1, h2.ImplementationCount);
    }

    // -- Dispatcher registration -------------------------------------------------

    [Fact]
    public void RebuildContextIndex_IsRegistered()
    {
        Assert.Contains("workflow.rebuild_context_index", _dispatcher.RegisteredOperations);
    }

    // -- Handler returns health ---------------------------------------------------

    [Fact]
    public void Handler_RebuildsAndReturnsHealth()
    {
        var result = _dispatcher.Dispatch(Ctx("workflow.rebuild_context_index"));
        Assert.True(result.Success, result.Message);
        var health = Assert.IsType<IndexHealth>(result.Data);
        Assert.True(health.IsHealthy, string.Join(" | ", health.Errors));
        Assert.True(health.PhaseCount > 0);
    }

    [Fact]
    public void Rebuild_AllowsAppendOnlyBlockerHistoryAndDuplicateLegacyEvents()
    {
        _store.AppendBlocker(new Core.Records.BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Open blocker",
            ReportedBy = Core.WorkflowActor.Codex,
            IsResolved = false
        });
        _store.AppendBlocker(new Core.Records.BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Open blocker",
            ReportedBy = Core.WorkflowActor.Codex,
            IsResolved = true,
            Resolution = "Resolved"
        });

        _store.AppendEvent(new Core.WorkflowEvent
        {
            EventId = "EVT-001",
            EventType = "legacy.duplicate",
            Actor = Core.WorkflowActor.Codex,
            PhaseId = "PHASE-001",
            Summary = "Duplicate legacy event id."
        });

        var builder = new ContextIndexBuilder(_tempRoot);
        var health = builder.Rebuild();

        Assert.True(health.IsHealthy, string.Join(" | ", health.Errors));
        Assert.Empty(health.Errors);
        Assert.True(health.BlockerCount >= 2);
        Assert.True(health.EventCount >= 2);
    }

    // -- WorkflowLayout -----------------------------------------------------------

    [Fact]
    public void WorkflowLayout_WorkflowKitDbPath_EndsWithDb()
    {
        var path = WorkflowLayout.WorkflowKitDbPath(_tempRoot);
        Assert.EndsWith("workflowkit.db", path);
        Assert.Contains(".workflowkit", path);
    }
}
