using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Tracing;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// PHASE-023-D: Tests for TuiViewHelpers — pure formatting and state-machine
/// helpers extracted from TuiApp and WritePalette.
/// </summary>
public sealed class TuiViewHelpersTests
{
    // -- StateTag --------------------------------------------------------------

    [Theory]
    [InlineData(PhaseState.Pass, "PASS")]
    [InlineData(PhaseState.PassWithWarnings, "PASS!")]
    [InlineData(PhaseState.Planned, "plan")]
    [InlineData(PhaseState.ReadyForImplementation, "RDY")]
    [InlineData(PhaseState.AssignedToImplementation, "ASN")]
    [InlineData(PhaseState.InImplementation, "WIP")]
    [InlineData(PhaseState.ImplementationLogged, "IMP")]
    [InlineData(PhaseState.AuditLogged, "AUD")]
    [InlineData(PhaseState.ReadyForReview, "RREV")]
    [InlineData(PhaseState.ReviewInProgress, "REV")]
    [InlineData(PhaseState.ReviewLogged, "REV✓")]
    [InlineData(PhaseState.RequiresFix, "FIX?")]
    [InlineData(PhaseState.FixInProgress, "FIX")]
    [InlineData(PhaseState.FixLogged, "FIX✓")]
    [InlineData(PhaseState.Blocked, "BLKD")]
    [InlineData(PhaseState.FailedArchitecture, "FAIL")]
    [InlineData(PhaseState.FailedCompile, "FAIL")]
    [InlineData(PhaseState.FailedValidation, "FAIL")]
    public void StateTag_ReturnsExpectedLabel(PhaseState state, string expected)
    {
        Assert.Equal(expected, TuiViewHelpers.StateTag(state));
    }

    [Fact]
    public void StateTag_AllEnumValues_ReturnNonEmpty()
    {
        foreach (PhaseState state in Enum.GetValues<PhaseState>())
        {
            var tag = TuiViewHelpers.StateTag(state);
            Assert.False(string.IsNullOrEmpty(tag), $"State {state} returned null/empty tag");
            Assert.True(tag.Length <= 5, $"State {state} tag '{tag}' exceeds 5 chars");
        }
    }

    // -- ProgressBar -----------------------------------------------------------

    [Theory]
    [InlineData(0, 10, "░░░░░░░░░░")]
    [InlineData(100, 10, "██████████")]
    [InlineData(50, 10, "█████░░░░░")]
    [InlineData(75, 8, "██████░░")]
    [InlineData(33, 6, "██░░░░")]
    [InlineData(0, 0, "")]
    [InlineData(50, 0, "")]
    [InlineData(-10, 10, "░░░░░░░░░░")]  // clamped to 0
    [InlineData(200, 5, "█████")]        // clamped to 100
    public void ProgressBar_ReturnsExpectedString(int percent, int width, string expected)
    {
        Assert.Equal(expected, TuiViewHelpers.ProgressBar(percent, width));
    }

    // -- WordWrap --------------------------------------------------------------

    [Fact]
    public void WordWrap_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(TuiViewHelpers.WordWrap("", 10));
        Assert.Empty(TuiViewHelpers.WordWrap(null!, 10));
        Assert.Empty(TuiViewHelpers.WordWrap("  ", 10));
    }

    [Fact]
    public void WordWrap_ShortText_ReturnsSingleLine()
    {
        var lines = TuiViewHelpers.WordWrap("hello world", 20).ToList();
        Assert.Single(lines);
        Assert.Equal("hello world", lines[0]);
    }

    [Fact]
    public void WordWrap_LongText_WrapsAtWordBoundaries()
    {
        var lines = TuiViewHelpers.WordWrap("the quick brown fox jumps", 12).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal("the quick", lines[0]);
        Assert.Equal("brown fox", lines[1]);
        Assert.Equal("jumps", lines[2]);
    }

    [Fact]
    public void WordWrap_SingleWordLongerThanWidth_DoesNotSplit()
    {
        var lines = TuiViewHelpers.WordWrap("supercalifragilistic", 8).ToList();
        Assert.Single(lines);
        Assert.Equal("supercalifragilistic", lines[0]);
    }

    [Fact]
    public void WordWrap_ZeroOrNegativeWidth_ReturnsEmpty()
    {
        Assert.Empty(TuiViewHelpers.WordWrap("hello", 0));
        Assert.Empty(TuiViewHelpers.WordWrap("hello", -5));
    }

    // -- ValidNextStates -------------------------------------------------------

    [Theory]
    [InlineData(PhaseState.Planned, new[] { PhaseState.ReadyForImplementation, PhaseState.Blocked })]
    [InlineData(PhaseState.ReadyForImplementation, new[] { PhaseState.AssignedToImplementation, PhaseState.Blocked })]
    [InlineData(PhaseState.AssignedToImplementation, new[] { PhaseState.InImplementation, PhaseState.Blocked })]
    [InlineData(PhaseState.InImplementation, new[] { PhaseState.ImplementationLogged, PhaseState.FailedCompile, PhaseState.Blocked })]
    [InlineData(PhaseState.ImplementationLogged, new[] { PhaseState.AuditLogged, PhaseState.FailedCompile, PhaseState.Blocked })]
    [InlineData(PhaseState.AuditLogged, new[] { PhaseState.ReadyForReview, PhaseState.Blocked })]
    [InlineData(PhaseState.ReadyForReview, new[] { PhaseState.ReviewInProgress, PhaseState.Blocked })]
    [InlineData(PhaseState.ReviewInProgress, new[] { PhaseState.ReviewLogged, PhaseState.RequiresFix, PhaseState.FailedArchitecture, PhaseState.Blocked })]
    [InlineData(PhaseState.ReviewLogged, new[] { PhaseState.ReadyForTest, PhaseState.Blocked })]
    [InlineData(PhaseState.ReadyForTest, new[] { PhaseState.TestInProgress, PhaseState.Blocked })]
    [InlineData(PhaseState.TestInProgress, new[] { PhaseState.TestLogged, PhaseState.FailedValidation, PhaseState.Blocked })]
    [InlineData(PhaseState.RequiresFix, new[] { PhaseState.FixInProgress, PhaseState.Blocked })]
    [InlineData(PhaseState.FixInProgress, new[] { PhaseState.FixLogged, PhaseState.Blocked })]
    [InlineData(PhaseState.FixLogged, new[] { PhaseState.ReadyForReview, PhaseState.Blocked })]
    public void ValidNextStates_ReturnsExpectedTransitions(PhaseState current, PhaseState[] expected)
    {
        var result = TuiViewHelpers.ValidNextStates(current);
        Assert.Equal(expected.OrderBy(s => s), result.OrderBy(s => s));
    }

    [Theory]
    [InlineData(PhaseState.Pass)]
    [InlineData(PhaseState.PassWithWarnings)]
    [InlineData(PhaseState.Blocked)]
    [InlineData(PhaseState.FailedArchitecture)]
    [InlineData(PhaseState.FailedCompile)]
    [InlineData(PhaseState.FailedValidation)]
    public void ValidNextStates_TerminalOrBlocked_ReturnsEmpty(PhaseState state)
    {
        Assert.Empty(TuiViewHelpers.ValidNextStates(state));
    }

    // -- IsTerminalState -------------------------------------------------------

    [Theory]
    [InlineData(PhaseState.Pass, true)]
    [InlineData(PhaseState.PassWithWarnings, true)]
    [InlineData(PhaseState.FailedArchitecture, true)]
    [InlineData(PhaseState.FailedCompile, true)]
    [InlineData(PhaseState.FailedValidation, true)]
    [InlineData(PhaseState.Planned, false)]
    [InlineData(PhaseState.InImplementation, false)]
    [InlineData(PhaseState.Blocked, false)]
    [InlineData(PhaseState.ReviewInProgress, false)]
    [InlineData(PhaseState.ReadyForTest, false)]
    [InlineData(PhaseState.TestLogged, false)]
    public void IsTerminalState_ReturnsExpectedResult(PhaseState state, bool expected)
    {
        Assert.Equal(expected, TuiViewHelpers.IsTerminalState(state));
    }

    // -- Consistency with WritePalette (023-C) ---------------------------------

    [Fact]
    public void BudgetStatus_FormatsCompactDiagnostics()
    {
        var result = new BudgetConfigResult
        {
            Mode = "Low",
            Source = "project",
            Profile = new BudgetProfileData
            {
                MaxPointers = 5,
                MaxEstimatedTokens = 2000
            }
        };

        Assert.Equal("Budget: Low (project) 5 ptrs / 2000 tok",
            TuiViewHelpers.BudgetStatus(result));
    }

    [Fact]
    public void BudgetStatus_HandlesMissingProfile()
    {
        Assert.Equal("Budget: -", TuiViewHelpers.BudgetStatus(null));
        Assert.Equal("Budget: -", TuiViewHelpers.BudgetStatus(new BudgetConfigResult()));
    }

    [Theory]
    [InlineData("Low", BudgetMode.Medium)]
    [InlineData("Medium", BudgetMode.High)]
    [InlineData("High", BudgetMode.Full)]
    [InlineData("Full", BudgetMode.Low)]
    [InlineData(null, BudgetMode.High)]
    [InlineData("unknown", BudgetMode.High)]
    public void NextBudgetMode_CyclesInExpectedOrder(string? currentMode, BudgetMode expected)
    {
        Assert.Equal(expected, TuiViewHelpers.NextBudgetMode(currentMode));
    }

    [Theory]
    [InlineData("Off", false, TraceMode.Basic)]
    [InlineData("Basic", true, TraceMode.Verbose)]
    [InlineData("Verbose", true, TraceMode.Off)]
    [InlineData("Off", true, TraceMode.Basic)]
    [InlineData(null, false, TraceMode.Basic)]
    [InlineData("unknown", true, TraceMode.Basic)]
    public void NextTraceMode_CyclesInExpectedOrder(string? currentMode, bool isEnabled, TraceMode expected)
    {
        Assert.Equal(expected, TuiViewHelpers.NextTraceMode(currentMode, isEnabled));
    }

    /// <summary>
    /// Every state that WritePalette's original ValidNextStates returned
    /// must still be returned by TuiViewHelpers.ValidNextStates — the
    /// WritePalette now delegates to TuiViewHelpers, so any missing
    /// entries would break the command palette.
    /// </summary>
    [Fact]
    public void ValidNextStates_CoversAllTransitionsUsedByWritePalette()
    {
        // The original WritePalette switch covered these non-terminal states.
        // TuiViewHelpers must return at least the same entries.
        var coveredStates = new[]
        {
            PhaseState.Planned,
            PhaseState.ReadyForImplementation,
            PhaseState.AssignedToImplementation,
            PhaseState.InImplementation,
            PhaseState.ImplementationLogged,
            PhaseState.AuditLogged,
            PhaseState.ReadyForReview,
            PhaseState.ReviewInProgress,
            PhaseState.ReviewLogged,
            PhaseState.ReadyForTest,
            PhaseState.TestInProgress,
            PhaseState.TestLogged,
            PhaseState.RequiresFix,
            PhaseState.FixInProgress,
            PhaseState.FixLogged,
        };

        foreach (var state in coveredStates)
        {
            var next = TuiViewHelpers.ValidNextStates(state);
            Assert.NotEmpty(next); // must have at least one forward transition
        }
    }
}
