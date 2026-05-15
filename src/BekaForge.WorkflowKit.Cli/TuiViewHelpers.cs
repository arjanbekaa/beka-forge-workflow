using System.Text;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Tracing;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// PHASE-023-D: Pure formatting and state-machine helpers extracted from
/// <see cref="TuiApp"/> and <see cref="WritePalette"/>.
///
/// These methods have no Terminal.Gui dependencies and are therefore
/// unit-testable without initialising a console.  Both <see cref="TuiApp"/>
/// and <see cref="WritePalette"/> delegate to this class.
///
/// Responsibilities:
///   - <see cref="StateTag"/>      → short display label for a <see cref="PhaseState"/>
///   - <see cref="ProgressBar"/>   → Unicode block-character progress bar string
///   - <see cref="WordWrap"/>      → break a long string into fixed-width lines
///   - <see cref="ValidNextStates"/> → valid forward transitions for a phase state
///   - <see cref="IsTerminalState"/> → true for states that cannot transition further
/// </summary>
public static class TuiViewHelpers
{
    // -- Display formatting ----------------------------------------------------

    /// <summary>
    /// Returns a short (≤5-char) display tag for the given <paramref name="state"/>.
    /// Used in the phase-list panel where column width is constrained.
    /// </summary>
    public static string StateTag(PhaseState state) => state switch
    {
        PhaseState.Pass                     => "PASS",
        PhaseState.PassWithWarnings         => "PASS!",
        PhaseState.Planned                  => "plan",
        PhaseState.ReadyForImplementation   => "RDY",
        PhaseState.AssignedToImplementation => "ASN",
        PhaseState.InImplementation         => "WIP",
        PhaseState.ImplementationLogged     => "IMP",
        PhaseState.AuditLogged              => "AUD",
        PhaseState.ReadyForReview      => "RREV",
        PhaseState.ReviewInProgress    => "REV",
        PhaseState.ReviewLogged        => "REV✓",
        PhaseState.RequiresFix              => "FIX?",
        PhaseState.FixInProgress            => "FIX",
        PhaseState.FixLogged                => "FIX✓",
        PhaseState.Blocked                  => "BLKD",
        PhaseState.FailedArchitecture
            or PhaseState.FailedCompile
            or PhaseState.FailedValidation       => "FAIL",
        _ => state.ToString()[..Math.Min(4, state.ToString().Length)]
    };

    /// <summary>
    /// Renders a Unicode block-character progress bar of the given
    /// <paramref name="width"/> representing <paramref name="percent"/> (0–100).
    /// </summary>
    /// <example><c>ProgressBar(75, 8)</c> → <c>"██████░░"</c></example>
    public static string ProgressBar(int percent, int width)
    {
        if (width <= 0) return string.Empty;
        var filled = Math.Clamp((int)Math.Round(percent / 100.0 * width), 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }

    /// <summary>
    /// Splits <paramref name="text"/> into lines no longer than
    /// <paramref name="maxWidth"/> characters, breaking on word boundaries.
    /// </summary>
    public static IEnumerable<string> WordWrap(string text, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0) yield break;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line  = new StringBuilder();

        foreach (var word in words)
        {
            if (line.Length + word.Length + (line.Length > 0 ? 1 : 0) > maxWidth && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0) yield return line.ToString();
    }

    // -- Phase state-machine helpers -------------------------------------------

    /// <summary>
    /// Returns the set of states reachable from <paramref name="current"/>
    /// via a single forward transition.  Returns an empty list for terminal
    /// and unknown states.
    ///
    /// Mirrors the transition table in
    /// <c>PhaseTransitionValidator</c> (Server project) so the write
    /// palette only presents options the server will accept.
    /// </summary>
    public static IReadOnlyList<PhaseState> ValidNextStates(PhaseState current) => current switch
    {
        PhaseState.Planned
            => [PhaseState.ReadyForImplementation, PhaseState.Blocked],

        PhaseState.ReadyForImplementation
            => [PhaseState.AssignedToImplementation, PhaseState.Blocked],

        PhaseState.AssignedToImplementation
            => [PhaseState.InImplementation, PhaseState.Blocked],

        PhaseState.InImplementation
            => [PhaseState.ImplementationLogged, PhaseState.FailedCompile, PhaseState.Blocked],

        PhaseState.ImplementationLogged
            => [PhaseState.AuditLogged, PhaseState.FailedCompile, PhaseState.Blocked],

        PhaseState.AuditLogged
            => [PhaseState.ReadyForReview, PhaseState.Blocked],

        PhaseState.ReadyForReview
            => [PhaseState.ReviewInProgress, PhaseState.Blocked],

        PhaseState.ReviewInProgress
            => [PhaseState.ReviewLogged, PhaseState.RequiresFix,
                PhaseState.FailedArchitecture, PhaseState.Blocked],

        PhaseState.ReviewLogged
            => [PhaseState.ReadyForTest, PhaseState.Blocked],

        PhaseState.ReadyForTest
            => [PhaseState.TestInProgress, PhaseState.Blocked],

        PhaseState.TestInProgress
            => [PhaseState.TestLogged, PhaseState.FailedValidation, PhaseState.Blocked],

        PhaseState.TestLogged
            => [PhaseState.Pass, PhaseState.FailedValidation, PhaseState.Blocked],

        PhaseState.RequiresFix
            => [PhaseState.FixInProgress, PhaseState.Blocked],

        PhaseState.FixInProgress
            => [PhaseState.FixLogged, PhaseState.Blocked],

        PhaseState.FixLogged
            => [PhaseState.ReadyForReview, PhaseState.Blocked],

        // Terminal states and unknown states have no valid forward transitions.
        _ => []
    };

    /// <summary>
    /// Returns <see langword="true"/> for states from which no further
    /// progression is possible (success or failure terminals).
    /// </summary>
    public static bool IsTerminalState(PhaseState state) => state is
        PhaseState.Pass or
        PhaseState.PassWithWarnings or
        PhaseState.FailedArchitecture or
        PhaseState.FailedCompile or
        PhaseState.FailedValidation;

    /// <summary>Returns the next budget mode in the Low → Medium → High → Full loop.</summary>
    public static BudgetMode NextBudgetMode(string? currentMode)
    {
        var current = Enum.TryParse<BudgetMode>(currentMode, ignoreCase: true, out var parsed)
            ? parsed
            : BudgetMode.Medium;

        return current switch
        {
            BudgetMode.Low => BudgetMode.Medium,
            BudgetMode.Medium => BudgetMode.High,
            BudgetMode.High => BudgetMode.Full,
            _ => BudgetMode.Low
        };
    }

    /// <summary>Returns the next trace mode in the Off → Basic → Verbose loop.</summary>
    public static TraceMode NextTraceMode(string? currentMode, bool isEnabled)
    {
        if (!isEnabled)
            return TraceMode.Basic;

        return Enum.TryParse<TraceMode>(currentMode, ignoreCase: true, out var parsed)
            ? parsed switch
            {
                TraceMode.Off => TraceMode.Basic,
                TraceMode.Basic => TraceMode.Verbose,
                _ => TraceMode.Off
            }
            : TraceMode.Basic;
    }

    /// <summary>Formats compact budget diagnostics for the TUI diagnostics bar.</summary>
    public static string BudgetStatus(BudgetConfigResult? budget)
    {
        if (budget?.Profile is null)
            return "Budget: -";

        var tokenCap = budget.Profile.MaxEstimatedTokens > 0
            ? $"{budget.Profile.MaxEstimatedTokens} tok"
            : "unlimited";

        return $"Budget: {budget.Mode} ({budget.Source}) {budget.Profile.MaxPointers} ptrs / {tokenCap}";
    }
}
