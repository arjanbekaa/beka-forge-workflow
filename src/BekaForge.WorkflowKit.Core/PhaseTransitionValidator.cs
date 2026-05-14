namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Enforces the BekaForge phase lifecycle state machine.
///
/// Rules:
/// 1.  start_phase only works from ASSIGNED_TO_IMPLEMENTATION.
/// 2.  complete_implementation moves IN_IMPLEMENTATION → IMPLEMENTATION_LOGGED.
/// 3.  audit log moves IMPLEMENTATION_LOGGED → AUDIT_LOGGED.
/// 4.  READY_FOR_CODEX_REVIEW comes from AUDIT_LOGGED or FIX_LOGGED.
/// 5.  Codex review starts only from READY_FOR_CODEX_REVIEW.
/// 6.  Codex review is logged only from CODEX_REVIEW_IN_PROGRESS.
/// 7.  READY_FOR_UNITY_TEST requires CODEX_REVIEW_LOGGED.
/// 8.  PASS requires UNITY_TEST_LOGGED unless RequiresUnityTest = false.
/// 9.  PASS_WITH_WARNINGS requires UNITY_TEST_LOGGED unless RequiresUnityTest = false.
/// 10. BLOCKED requires a non-empty blocker reason or blocker ID.
/// 11. Terminal states (PASS, PASS_WITH_WARNINGS, FAILED_*) reject all normal transitions.
/// 12. Invalid transitions return WorkflowError and must not mutate state.
/// </summary>
public sealed class PhaseTransitionValidator
{
    private static readonly IReadOnlySet<PhaseState> TerminalStates = new HashSet<PhaseState>
    {
        PhaseState.Pass,
        PhaseState.PassWithWarnings,
        PhaseState.FailedArchitecture,
        PhaseState.FailedCompile,
        PhaseState.FailedTests
    };

    /// <summary>
    /// Validates the requested transition in the given context.
    /// Returns Ok(targetState) if valid, or Fail(error) if not.
    /// This method is pure — it never mutates state and never throws.
    /// </summary>
    public WorkflowResult<PhaseState> Validate(TransitionContext context)
    {
        var current = context.CurrentState;
        var target = context.TargetState;

        // Rule 11: Terminal states reject all normal transitions.
        if (TerminalStates.Contains(current))
            return WorkflowResult.Fail<PhaseState>(WorkflowError.TerminalState(current));

        // Rule 10: BLOCKED requires a blocker reason or blocker ID.
        if (target == PhaseState.Blocked)
        {
            var hasReason = !string.IsNullOrWhiteSpace(context.BlockerReason);
            var hasId = !string.IsNullOrWhiteSpace(context.BlockerId);
            if (!hasReason && !hasId)
                return WorkflowResult.Fail<PhaseState>(WorkflowError.BlockerRequired());

            // BLOCKED is allowed from any non-terminal active state.
            return WorkflowResult.Ok(target);
        }

        // Rules 8 & 9: PASS and PASS_WITH_WARNINGS — unity test gate.
        if (target == PhaseState.Pass || target == PhaseState.PassWithWarnings)
        {
            if (context.RequiresUnityTest)
            {
                // Must be in UNITY_TEST_LOGGED to pass with Unity testing required.
                if (current != PhaseState.UnityTestLogged)
                    return WorkflowResult.Fail<PhaseState>(WorkflowError.UnityTestRequired());
            }
            else
            {
                // Without Unity testing, PASS is allowed directly from CODEX_REVIEW_LOGGED.
                if (current != PhaseState.CodexReviewLogged)
                    return WorkflowResult.Fail<PhaseState>(
                        WorkflowError.InvalidTransition(current, target,
                            "PASS without Unity test requires current state to be CODEX_REVIEW_LOGGED."));
            }
            return WorkflowResult.Ok(target);
        }

        // All other transitions must be in the allowed table.
        if (!IsAllowedTransition(current, target))
            return WorkflowResult.Fail<PhaseState>(
                WorkflowError.InvalidTransition(current, target,
                    "This transition is not defined in the BekaForge phase state machine."));

        return WorkflowResult.Ok(target);
    }

    /// <summary>
    /// The explicit allowed transition table. Only transitions listed here are valid
    /// (subject to the special-cased rules above for BLOCKED, PASS, PASS_WITH_WARNINGS).
    /// </summary>
    private static bool IsAllowedTransition(PhaseState from, PhaseState to) => (from, to) switch
    {
        // ── Happy path ──────────────────────────────────────────────────────────────
        (PhaseState.Planned,                    PhaseState.ReadyForImplementation)  => true,
        (PhaseState.ReadyForImplementation,     PhaseState.AssignedToImplementation) => true,
        (PhaseState.AssignedToImplementation,   PhaseState.InImplementation)         => true,  // start_phase
        (PhaseState.InImplementation,           PhaseState.ImplementationLogged)     => true,  // complete_implementation
        (PhaseState.ImplementationLogged,       PhaseState.AuditLogged)              => true,
        (PhaseState.AuditLogged,                PhaseState.ReadyForCodexReview)      => true,
        (PhaseState.ReadyForCodexReview,        PhaseState.CodexReviewInProgress)    => true,
        (PhaseState.CodexReviewInProgress,      PhaseState.CodexReviewLogged)        => true,
        (PhaseState.CodexReviewLogged,          PhaseState.ReadyForUnityTest)        => true,  // requires review logged (rule 7)
        (PhaseState.ReadyForUnityTest,          PhaseState.UnityTestInProgress)      => true,
        (PhaseState.UnityTestInProgress,        PhaseState.UnityTestLogged)          => true,

        // ── Fix path ─────────────────────────────────────────────────────────────────
        (PhaseState.CodexReviewInProgress,      PhaseState.RequiresFix)              => true,
        (PhaseState.RequiresFix,                PhaseState.FixInProgress)            => true,
        (PhaseState.FixInProgress,              PhaseState.FixLogged)                => true,
        (PhaseState.FixLogged,                  PhaseState.ReadyForCodexReview)      => true,  // cycles back

        // ── Failure transitions ───────────────────────────────────────────────────────
        (PhaseState.CodexReviewInProgress,      PhaseState.FailedArchitecture)       => true,
        (PhaseState.CodexReviewLogged,          PhaseState.FailedArchitecture)       => true,
        (PhaseState.InImplementation,           PhaseState.FailedCompile)            => true,
        (PhaseState.ImplementationLogged,       PhaseState.FailedCompile)            => true,
        (PhaseState.AuditLogged,                PhaseState.FailedCompile)            => true,
        (PhaseState.UnityTestInProgress,        PhaseState.FailedTests)              => true,
        (PhaseState.UnityTestLogged,            PhaseState.FailedTests)              => true,

        _                                                                             => false
    };

    /// <summary>Returns true if the given state is a terminal state (no further transitions allowed).</summary>
    public static bool IsTerminal(PhaseState state) => TerminalStates.Contains(state);

    /// <summary>Returns the set of all terminal phase states.</summary>
    public static IReadOnlySet<PhaseState> GetTerminalStates() => TerminalStates;
}
