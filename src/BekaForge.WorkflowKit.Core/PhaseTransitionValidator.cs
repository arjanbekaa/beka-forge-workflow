namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Enforces the BekaForge phase lifecycle state machine.
///
/// Rules:
/// 1.  start_phase only works from ASSIGNED_TO_IMPLEMENTATION.
/// 2.  complete_implementation moves IN_IMPLEMENTATION → IMPLEMENTATION_LOGGED.
/// 3.  audit log moves IMPLEMENTATION_LOGGED → AUDIT_LOGGED.
/// 4.  READY_FOR_REVIEW comes from AUDIT_LOGGED or FIX_LOGGED.
/// 5.  Review starts only from READY_FOR_REVIEW.
/// 6.  Review is logged only from REVIEW_IN_PROGRESS.
/// 7.  READY_FOR_TEST requires REVIEW_LOGGED.
/// 8.  PASS requires TEST_LOGGED unless RequiresValidation = false.
/// 9.  PASS_WITH_WARNINGS requires TEST_LOGGED unless RequiresValidation = false.
/// 10. BLOCKED requires a non-empty blocker reason or blocker ID.
/// 11. Terminal states (PASS, PASS_WITH_WARNINGS, FAILED_*) reject all normal transitions.
/// 12. Invalid transitions return WorkflowError and must not mutate state.
///
/// Validation gate rules (enforced at handler level, not transition level):
/// - Passed validation requires evidence.
/// - Manual validation types cannot be marked Passed by LLM without human confirmation.
/// - Skipped validation requires a skip reason.
/// </summary>
public sealed class PhaseTransitionValidator
{
    private static readonly IReadOnlySet<PhaseState> TerminalStates = new HashSet<PhaseState>
    {
        PhaseState.Pass,
        PhaseState.PassWithWarnings,
        PhaseState.FailedArchitecture,
        PhaseState.FailedCompile,
        PhaseState.FailedValidation
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

        // Rules 8 & 9: PASS and PASS_WITH_WARNINGS — validation gate.
        if (target == PhaseState.Pass || target == PhaseState.PassWithWarnings)
        {
            // SkippedNotPossible blocks clean Pass — risk must be acknowledged via PassWithWarnings.
            if (target == PhaseState.Pass && context.HasSkippedNotPossible)
                return WorkflowResult.Fail<PhaseState>(
                    WorkflowError.InvalidTransition(current, target,
                        "Phase has a SkippedNotPossible validation. Clean Pass is not allowed. Use PassWithWarnings to acknowledge the risk."));

            if (context.RequiresValidation)
            {
                // Must be in TEST_LOGGED to pass with validation required.
                if (current != PhaseState.TestLogged)
                    return WorkflowResult.Fail<PhaseState>(WorkflowError.ValidationRequired());
            }
            else
            {
                // Without validation, PASS is allowed directly from REVIEW_LOGGED.
                if (current != PhaseState.ReviewLogged)
                    return WorkflowResult.Fail<PhaseState>(
                        WorkflowError.InvalidTransition(current, target,
                            "PASS without validation requires current state to be REVIEW_LOGGED."));
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
        // -- Happy path --------------------------------------------------------------
        (PhaseState.Planned,                    PhaseState.ReadyForImplementation)  => true,
        (PhaseState.ReadyForImplementation,     PhaseState.AssignedToImplementation) => true,
        (PhaseState.AssignedToImplementation,   PhaseState.InImplementation)         => true,  // start_phase
        (PhaseState.InImplementation,           PhaseState.ImplementationLogged)     => true,  // complete_implementation
        (PhaseState.ImplementationLogged,       PhaseState.AuditLogged)              => true,
        (PhaseState.AuditLogged,                PhaseState.ReadyForReview)           => true,
        (PhaseState.ReadyForReview,             PhaseState.ReviewInProgress)         => true,
        (PhaseState.ReviewInProgress,           PhaseState.ReviewLogged)             => true,
        (PhaseState.ReviewLogged,               PhaseState.ReadyForTest)             => true,  // requires review logged (rule 7)
        (PhaseState.ReadyForTest,               PhaseState.TestInProgress)           => true,
        (PhaseState.TestInProgress,             PhaseState.TestLogged)               => true,

        // -- Fix path -----------------------------------------------------------------
        (PhaseState.ReviewInProgress,           PhaseState.RequiresFix)              => true,
        (PhaseState.RequiresFix,                PhaseState.FixInProgress)            => true,
        (PhaseState.FixInProgress,              PhaseState.FixLogged)                => true,
        (PhaseState.FixLogged,                  PhaseState.ReadyForReview)           => true,  // cycles back

        // -- Blocker recovery ----------------------------------------------------------
        // When all blockers are resolved the phase returns to ReadyForImplementation.
        // This transition is also used by ReopenPhaseHandler as a manual fallback.
        (PhaseState.Blocked,                    PhaseState.ReadyForImplementation)   => true,

        // -- Failure transitions -------------------------------------------------------
        (PhaseState.ReviewInProgress,           PhaseState.FailedArchitecture)       => true,
        (PhaseState.ReviewLogged,               PhaseState.FailedArchitecture)       => true,
        (PhaseState.InImplementation,           PhaseState.FailedCompile)            => true,
        (PhaseState.ImplementationLogged,       PhaseState.FailedCompile)            => true,
        (PhaseState.AuditLogged,                PhaseState.FailedCompile)            => true,
        (PhaseState.TestInProgress,             PhaseState.FailedValidation)         => true,
        (PhaseState.TestLogged,                 PhaseState.FailedValidation)         => true,

        _                                                                             => false
    };

    /// <summary>Returns true if the given state is a terminal state (no further transitions allowed).</summary>
    public static bool IsTerminal(PhaseState state) => TerminalStates.Contains(state);

    /// <summary>Returns the set of all terminal phase states.</summary>
    public static IReadOnlySet<PhaseState> GetTerminalStates() => TerminalStates;
}
