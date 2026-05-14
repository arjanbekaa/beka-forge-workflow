using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.StorageTests;

/// <summary>
/// Tests for IdSequenceStore — sequential, persistent ID allocation.
/// </summary>
public sealed class IdSequenceStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public IdSequenceStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-seq-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, WorkflowLayout.WorkflowKitDir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void NextPhaseId_FirstCall_ReturnsPhase001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("PHASE-001", store.NextPhaseId());
    }

    [Fact]
    public void NextPhaseId_ThreeCalls_ReturnSequentialIds()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("PHASE-001", store.NextPhaseId());
        Assert.Equal("PHASE-002", store.NextPhaseId());
        Assert.Equal("PHASE-003", store.NextPhaseId());
    }

    [Fact]
    public void NextImplementationId_FirstCall_ReturnsImp001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("IMP-001", store.NextImplementationId());
    }

    [Fact]
    public void NextAuditId_FirstCall_ReturnsAud001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("AUD-001", store.NextAuditId());
    }

    [Fact]
    public void NextReviewId_FirstCall_ReturnsRev001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("REV-001", store.NextReviewId());
    }

    [Fact]
    public void NextTestId_FirstCall_ReturnsTest001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("TEST-001", store.NextTestId());
    }

    [Fact]
    public void NextFixId_FirstCall_ReturnsFix001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("FIX-001", store.NextFixId());
    }

    [Fact]
    public void NextBlockerId_FirstCall_ReturnsBlk001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("BLK-001", store.NextBlockerId());
    }

    [Fact]
    public void NextHandoffId_FirstCall_ReturnsHandoff001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("HANDOFF-001", store.NextHandoffId());
    }

    [Fact]
    public void NextTimingId_FirstCall_ReturnsTime001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("TIME-001", store.NextTimingId());
    }

    [Fact]
    public void NextEventId_FirstCall_ReturnsEvt001()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("EVT-001", store.NextEventId());
    }

    [Fact]
    public void DifferentEntityTypes_HaveIndependentSequences()
    {
        var store = new IdSequenceStore(_tempRoot);
        Assert.Equal("PHASE-001", store.NextPhaseId());
        Assert.Equal("IMP-001", store.NextImplementationId());
        Assert.Equal("PHASE-002", store.NextPhaseId());
        Assert.Equal("AUD-001", store.NextAuditId());
        Assert.Equal("IMP-002", store.NextImplementationId());
    }

    [Fact]
    public void Sequences_PersistedAcrossInstances()
    {
        // First instance allocates some IDs.
        var store1 = new IdSequenceStore(_tempRoot);
        store1.NextPhaseId();   // PHASE-001
        store1.NextPhaseId();   // PHASE-002
        store1.NextEventId();   // EVT-001

        // New instance must continue from where the first left off.
        var store2 = new IdSequenceStore(_tempRoot);
        Assert.Equal("PHASE-003", store2.NextPhaseId());
        Assert.Equal("EVT-002", store2.NextEventId());
    }

    [Fact]
    public void SequencesFile_IsCreatedAtExpectedPath()
    {
        var store = new IdSequenceStore(_tempRoot);
        store.NextPhaseId();

        Assert.True(File.Exists(WorkflowLayout.SequencesFile(_tempRoot)));
    }

    [Fact]
    public void SequencesFile_IsValidJson()
    {
        var store = new IdSequenceStore(_tempRoot);
        store.NextPhaseId();
        store.NextEventId();

        var json = File.ReadAllText(WorkflowLayout.SequencesFile(_tempRoot));
        // Must be parseable JSON.
        var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json);
        Assert.NotNull(parsed);
        Assert.True(parsed.ContainsKey("phase") || parsed.ContainsKey("Phase"),
            "sequences.json must contain a 'phase' key");
    }
}
