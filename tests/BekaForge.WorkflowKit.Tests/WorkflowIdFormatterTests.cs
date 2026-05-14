using BekaForge.WorkflowKit.Core;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for WorkflowIdFormatter.
/// IDs must be deterministic, uppercase, and zero-padded to at least 3 digits.
/// </summary>
public sealed class WorkflowIdFormatterTests
{
    // ── PHASE ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Phase_Number1_ReturnsPhase001()
    {
        Assert.Equal("PHASE-001", WorkflowIdFormatter.Phase(1));
    }

    [Fact]
    public void Phase_Number42_ReturnsPhase042()
    {
        Assert.Equal("PHASE-042", WorkflowIdFormatter.Phase(42));
    }

    [Fact]
    public void Phase_Number999_ReturnsPhase999()
    {
        Assert.Equal("PHASE-999", WorkflowIdFormatter.Phase(999));
    }

    [Fact]
    public void Phase_Number1000_ReturnsFourDigits()
    {
        Assert.Equal("PHASE-1000", WorkflowIdFormatter.Phase(1000));
    }

    // ── IMP ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Implementation_Number1_ReturnsImp001()
    {
        Assert.Equal("IMP-001", WorkflowIdFormatter.Implementation(1));
    }

    [Fact]
    public void Implementation_Number99_ReturnsImp099()
    {
        Assert.Equal("IMP-099", WorkflowIdFormatter.Implementation(99));
    }

    // ── AUD ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Audit_Number1_ReturnsAud001()
    {
        Assert.Equal("AUD-001", WorkflowIdFormatter.Audit(1));
    }

    // ── REV ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Review_Number1_ReturnsRev001()
    {
        Assert.Equal("REV-001", WorkflowIdFormatter.Review(1));
    }

    // ── TEST ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test_Number1_ReturnsTest001()
    {
        Assert.Equal("TEST-001", WorkflowIdFormatter.Test(1));
    }

    // ── FIX ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fix_Number1_ReturnsFix001()
    {
        Assert.Equal("FIX-001", WorkflowIdFormatter.Fix(1));
    }

    // ── BLK ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Blocker_Number1_ReturnsBlk001()
    {
        Assert.Equal("BLK-001", WorkflowIdFormatter.Blocker(1));
    }

    // ── HANDOFF ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Handoff_Number1_ReturnsHandoff001()
    {
        Assert.Equal("HANDOFF-001", WorkflowIdFormatter.Handoff(1));
    }

    [Fact]
    public void Handoff_Number500_ReturnsHandoff500()
    {
        Assert.Equal("HANDOFF-500", WorkflowIdFormatter.Handoff(500));
    }

    // ── TIME ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Timing_Number1_ReturnsTime001()
    {
        Assert.Equal("TIME-001", WorkflowIdFormatter.Timing(1));
    }

    // ── EVT ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Event_Number1_ReturnsEvt001()
    {
        Assert.Equal("EVT-001", WorkflowIdFormatter.Event(1));
    }

    [Fact]
    public void Event_Number123_ReturnsEvt123()
    {
        Assert.Equal("EVT-123", WorkflowIdFormatter.Event(123));
    }

    // ── Validation: zero and negative are rejected ────────────────────────────────

    [Fact]
    public void Phase_Zero_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowIdFormatter.Phase(0));
    }

    [Fact]
    public void Phase_Negative_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowIdFormatter.Phase(-1));
    }

    [Fact]
    public void Implementation_Zero_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowIdFormatter.Implementation(0));
    }

    [Fact]
    public void Audit_Negative_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowIdFormatter.Audit(-100));
    }

    [Fact]
    public void Blocker_Zero_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowIdFormatter.Blocker(0));
    }

    [Fact]
    public void Event_Negative_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowIdFormatter.Event(-1));
    }

    // ── Determinism: same input always produces same output ───────────────────────

    [Fact]
    public void AllFormatters_AreDeterministic()
    {
        Assert.Equal(WorkflowIdFormatter.Phase(7), WorkflowIdFormatter.Phase(7));
        Assert.Equal(WorkflowIdFormatter.Implementation(3), WorkflowIdFormatter.Implementation(3));
        Assert.Equal(WorkflowIdFormatter.Audit(12), WorkflowIdFormatter.Audit(12));
        Assert.Equal(WorkflowIdFormatter.Review(5), WorkflowIdFormatter.Review(5));
        Assert.Equal(WorkflowIdFormatter.Test(1), WorkflowIdFormatter.Test(1));
        Assert.Equal(WorkflowIdFormatter.Fix(99), WorkflowIdFormatter.Fix(99));
        Assert.Equal(WorkflowIdFormatter.Blocker(2), WorkflowIdFormatter.Blocker(2));
        Assert.Equal(WorkflowIdFormatter.Handoff(8), WorkflowIdFormatter.Handoff(8));
        Assert.Equal(WorkflowIdFormatter.Timing(4), WorkflowIdFormatter.Timing(4));
        Assert.Equal(WorkflowIdFormatter.Event(11), WorkflowIdFormatter.Event(11));
    }
}
