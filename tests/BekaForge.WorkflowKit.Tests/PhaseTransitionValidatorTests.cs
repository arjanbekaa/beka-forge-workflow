using BekaForge.WorkflowKit.Core;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for PhaseTransitionValidator.
/// Covers: happy path, fix path, blocked path, invalid transitions,
/// Unity test requirement rules, and terminal state rejection.
/// </summary>
public sealed class PhaseTransitionValidatorTests
{
    private readonly PhaseTransitionValidator _validator = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private WorkflowResult<PhaseState> Transition(
        PhaseState from,
        PhaseState to,
        bool requiresUnityTest = true,
        string? blockerReason = null,
        string? blockerId = null)
    {
        return _validator.Validate(new TransitionContext
        {
            CurrentState = from,
            TargetState = to,
            RequiresUnityTest = requiresUnityTest,
            BlockerReason = blockerReason,
            BlockerId = blockerId
        });
    }

    private void AssertSuccess(WorkflowResult<PhaseState> result, PhaseState expected)
    {
        Assert.True(result.IsSuccess, $"Expected success but got failure: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.Equal(expected, result.Value);
    }

    private void AssertFailure(WorkflowResult<PhaseState> result, string expectedErrorCode)
    {
        Assert.True(result.IsFailure, "Expected failure but got success.");
        Assert.Equal(expectedErrorCode, result.Error.Code);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Happy-path: full forward traversal to PASS
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HappyPath_PlannedToReadyForImplementation_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.Planned, PhaseState.ReadyForImplementation),
            PhaseState.ReadyForImplementation);
    }

    [Fact]
    public void HappyPath_ReadyForImplementationToAssigned_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReadyForImplementation, PhaseState.AssignedToImplementation),
            PhaseState.AssignedToImplementation);
    }

    [Fact]
    public void HappyPath_AssignedToInImplementation_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.AssignedToImplementation, PhaseState.InImplementation),
            PhaseState.InImplementation);
    }

    [Fact]
    public void HappyPath_InImplementationToImplementationLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.InImplementation, PhaseState.ImplementationLogged),
            PhaseState.ImplementationLogged);
    }

    [Fact]
    public void HappyPath_ImplementationLoggedToAuditLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ImplementationLogged, PhaseState.AuditLogged),
            PhaseState.AuditLogged);
    }

    [Fact]
    public void HappyPath_AuditLoggedToReadyForCodexReview_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.AuditLogged, PhaseState.ReadyForCodexReview),
            PhaseState.ReadyForCodexReview);
    }

    [Fact]
    public void HappyPath_ReadyForCodexReviewToInProgress_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReadyForCodexReview, PhaseState.CodexReviewInProgress),
            PhaseState.CodexReviewInProgress);
    }

    [Fact]
    public void HappyPath_CodexReviewInProgressToLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.CodexReviewInProgress, PhaseState.CodexReviewLogged),
            PhaseState.CodexReviewLogged);
    }

    [Fact]
    public void HappyPath_CodexReviewLoggedToReadyForUnityTest_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.CodexReviewLogged, PhaseState.ReadyForUnityTest),
            PhaseState.ReadyForUnityTest);
    }

    [Fact]
    public void HappyPath_ReadyForUnityTestToInProgress_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReadyForUnityTest, PhaseState.UnityTestInProgress),
            PhaseState.UnityTestInProgress);
    }

    [Fact]
    public void HappyPath_UnityTestInProgressToLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.UnityTestInProgress, PhaseState.UnityTestLogged),
            PhaseState.UnityTestLogged);
    }

    [Fact]
    public void HappyPath_UnityTestLoggedToPass_WithUnityTestRequired_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.UnityTestLogged, PhaseState.Pass, requiresUnityTest: true),
            PhaseState.Pass);
    }

    [Fact]
    public void HappyPath_FullTraversal_AllTransitionsSucceed()
    {
        // Verify the full chain in sequence
        var path = new[]
        {
            PhaseState.Planned,
            PhaseState.ReadyForImplementation,
            PhaseState.AssignedToImplementation,
            PhaseState.InImplementation,
            PhaseState.ImplementationLogged,
            PhaseState.AuditLogged,
            PhaseState.ReadyForCodexReview,
            PhaseState.CodexReviewInProgress,
            PhaseState.CodexReviewLogged,
            PhaseState.ReadyForUnityTest,
            PhaseState.UnityTestInProgress,
            PhaseState.UnityTestLogged,
            PhaseState.Pass
        };

        for (int i = 0; i < path.Length - 1; i++)
        {
            var from = path[i];
            var to = path[i + 1];
            var result = Transition(from, to, requiresUnityTest: true);
            Assert.True(result.IsSuccess,
                $"Expected {from} → {to} to succeed, but got: {(result.IsFailure ? result.Error.Message : "?")}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Fix path
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FixPath_CodexReviewInProgressToRequiresFix_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.CodexReviewInProgress, PhaseState.RequiresFix),
            PhaseState.RequiresFix);
    }

    [Fact]
    public void FixPath_RequiresFixToFixInProgress_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.RequiresFix, PhaseState.FixInProgress),
            PhaseState.FixInProgress);
    }

    [Fact]
    public void FixPath_FixInProgressToFixLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.FixInProgress, PhaseState.FixLogged),
            PhaseState.FixLogged);
    }

    [Fact]
    public void FixPath_FixLoggedBackToReadyForCodexReview_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.FixLogged, PhaseState.ReadyForCodexReview),
            PhaseState.ReadyForCodexReview);
    }

    [Fact]
    public void FixPath_FullCycle_AllTransitionsSucceed()
    {
        var fixPath = new[]
        {
            PhaseState.ReadyForCodexReview,
            PhaseState.CodexReviewInProgress,
            PhaseState.RequiresFix,
            PhaseState.FixInProgress,
            PhaseState.FixLogged,
            PhaseState.ReadyForCodexReview  // cycles back
        };

        for (int i = 0; i < fixPath.Length - 1; i++)
        {
            var from = fixPath[i];
            var to = fixPath[i + 1];
            var result = Transition(from, to);
            Assert.True(result.IsSuccess,
                $"Fix path step {from} → {to} failed: {(result.IsFailure ? result.Error.Message : "?")}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Blocked path
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Blocked_WithoutBlockerReason_ReturnsBlockerRequiredError()
    {
        var result = Transition(PhaseState.InImplementation, PhaseState.Blocked);
        AssertFailure(result, "BlockerRequired");
    }

    [Fact]
    public void Blocked_WithEmptyBlockerReason_ReturnsBlockerRequiredError()
    {
        var result = Transition(PhaseState.InImplementation, PhaseState.Blocked, blockerReason: "   ");
        AssertFailure(result, "BlockerRequired");
    }

    [Fact]
    public void Blocked_WithBlockerReason_Succeeds()
    {
        var result = Transition(PhaseState.InImplementation, PhaseState.Blocked,
            blockerReason: "Waiting for missing Unity package license.");
        AssertSuccess(result, PhaseState.Blocked);
    }

    [Fact]
    public void Blocked_WithBlockerId_Succeeds()
    {
        var result = Transition(PhaseState.ReadyForCodexReview, PhaseState.Blocked,
            blockerId: "BLK-001");
        AssertSuccess(result, PhaseState.Blocked);
    }

    [Fact]
    public void Blocked_FromReadyForImplementation_WithReason_Succeeds()
    {
        var result = Transition(PhaseState.ReadyForImplementation, PhaseState.Blocked,
            blockerReason: "Dependency not ready.");
        AssertSuccess(result, PhaseState.Blocked);
    }

    [Fact]
    public void Blocked_FromUnityTestInProgress_WithReason_Succeeds()
    {
        var result = Transition(PhaseState.UnityTestInProgress, PhaseState.Blocked,
            blockerReason: "Unity Editor crash blocks testing.");
        AssertSuccess(result, PhaseState.Blocked);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. Invalid transitions
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Invalid_PlannedToInImplementation_ReturnsInvalidTransition()
    {
        var result = Transition(PhaseState.Planned, PhaseState.InImplementation);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_ImplementationLoggedToReadyForUnityTest_ReturnsInvalidTransition()
    {
        // Must go through Codex review first
        var result = Transition(PhaseState.ImplementationLogged, PhaseState.ReadyForUnityTest);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_PlannedToPass_ReturnsInvalidTransition()
    {
        // Even with requiresUnityTest=false, must be in CodexReviewLogged to PASS
        var result = Transition(PhaseState.Planned, PhaseState.Pass, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_PlannedToAuditLogged_ReturnsInvalidTransition()
    {
        var result = Transition(PhaseState.Planned, PhaseState.AuditLogged);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_AssignedToImplementationToCodexReviewInProgress_ReturnsInvalidTransition()
    {
        var result = Transition(PhaseState.AssignedToImplementation, PhaseState.CodexReviewInProgress);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_ReadyForCodexReviewToPass_ReturnsInvalidTransition()
    {
        // Must go through review flow first; even with requiresUnityTest=false,
        // PASS requires CodexReviewLogged (not ReadyForCodexReview)
        var result = Transition(PhaseState.ReadyForCodexReview, PhaseState.Pass, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_CodexReviewInProgressToPassWithUnityTestRequired_ReturnsUnityTestRequired()
    {
        // requiresUnityTest=true but current state is not UnityTestLogged
        var result = Transition(PhaseState.CodexReviewInProgress, PhaseState.Pass, requiresUnityTest: true);
        AssertFailure(result, "UnityTestRequired");
    }

    [Fact]
    public void Invalid_CodexReviewInProgressToPassWithoutUnityTest_ReturnsInvalidTransition()
    {
        // requiresUnityTest=false but current state is not CodexReviewLogged
        var result = Transition(PhaseState.CodexReviewInProgress, PhaseState.Pass, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. Unity test requirement rules
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnityTest_PassRequiresUnityTestLogged_WhenRequiresUnityTestTrue()
    {
        // In CodexReviewLogged but requiresUnityTest=true — must have test log first
        var result = Transition(PhaseState.CodexReviewLogged, PhaseState.Pass, requiresUnityTest: true);
        AssertFailure(result, "UnityTestRequired");
    }

    [Fact]
    public void UnityTest_PassFromUnityTestLogged_WhenRequiresUnityTestTrue_Succeeds()
    {
        var result = Transition(PhaseState.UnityTestLogged, PhaseState.Pass, requiresUnityTest: true);
        AssertSuccess(result, PhaseState.Pass);
    }

    [Fact]
    public void UnityTest_PassFromCodexReviewLogged_WhenRequiresUnityTestFalse_Succeeds()
    {
        // No Unity test required — can PASS directly from CodexReviewLogged
        var result = Transition(PhaseState.CodexReviewLogged, PhaseState.Pass, requiresUnityTest: false);
        AssertSuccess(result, PhaseState.Pass);
    }

    [Fact]
    public void UnityTest_PassWithWarnings_WhenRequiresUnityTestTrue_RequiresUnityTestLogged()
    {
        var result = Transition(PhaseState.CodexReviewLogged, PhaseState.PassWithWarnings, requiresUnityTest: true);
        AssertFailure(result, "UnityTestRequired");
    }

    [Fact]
    public void UnityTest_PassWithWarnings_FromUnityTestLogged_WhenRequired_Succeeds()
    {
        var result = Transition(PhaseState.UnityTestLogged, PhaseState.PassWithWarnings, requiresUnityTest: true);
        AssertSuccess(result, PhaseState.PassWithWarnings);
    }

    [Fact]
    public void UnityTest_PassWithWarnings_FromCodexReviewLogged_WhenNotRequired_Succeeds()
    {
        var result = Transition(PhaseState.CodexReviewLogged, PhaseState.PassWithWarnings, requiresUnityTest: false);
        AssertSuccess(result, PhaseState.PassWithWarnings);
    }

    [Fact]
    public void UnityTest_PassWithWarnings_FromWrongState_WhenNotRequired_ReturnsInvalidTransition()
    {
        // requiresUnityTest=false but not in CodexReviewLogged
        var result = Transition(PhaseState.AuditLogged, PhaseState.PassWithWarnings, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 6. Terminal states reject all normal transitions
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Terminal_PassCannotTransitionToAnyNormalState()
    {
        var normalStates = new[]
        {
            PhaseState.Planned,
            PhaseState.ReadyForImplementation,
            PhaseState.AssignedToImplementation,
            PhaseState.InImplementation,
            PhaseState.ImplementationLogged,
            PhaseState.AuditLogged,
            PhaseState.ReadyForCodexReview,
            PhaseState.CodexReviewInProgress,
            PhaseState.CodexReviewLogged,
            PhaseState.RequiresFix,
            PhaseState.FixInProgress,
            PhaseState.FixLogged,
            PhaseState.ReadyForUnityTest,
            PhaseState.UnityTestInProgress,
            PhaseState.UnityTestLogged
        };

        foreach (var target in normalStates)
        {
            var result = Transition(PhaseState.Pass, target);
            Assert.True(result.IsFailure, $"PASS → {target} should fail as PASS is terminal.");
            Assert.Equal("TerminalState", result.Error.Code);
        }
    }

    [Fact]
    public void Terminal_PassToBlocked_ReturnsTerminalState()
    {
        // PASS is terminal; even BLOCKED should be rejected
        var result = Transition(PhaseState.Pass, PhaseState.Blocked, blockerReason: "late blocker");
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_FailedCompileCannotTransitionToNormalProgress()
    {
        var result = Transition(PhaseState.FailedCompile, PhaseState.InImplementation);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_FailedCompileCannotTransitionToReadyForCodexReview()
    {
        var result = Transition(PhaseState.FailedCompile, PhaseState.ReadyForCodexReview);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_FailedArchitectureCannotTransitionToRequiresFix()
    {
        var result = Transition(PhaseState.FailedArchitecture, PhaseState.RequiresFix);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_FailedTestsCannotTransitionToUnityTestInProgress()
    {
        var result = Transition(PhaseState.FailedTests, PhaseState.UnityTestInProgress);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_PassWithWarningsCannotTransitionToReadyForImplementation()
    {
        var result = Transition(PhaseState.PassWithWarnings, PhaseState.ReadyForImplementation);
        AssertFailure(result, "TerminalState");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 7. Failure transitions (valid terminal-entry paths)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FailureTransition_CodexReviewInProgressToFailedArchitecture_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.CodexReviewInProgress, PhaseState.FailedArchitecture),
            PhaseState.FailedArchitecture);
    }

    [Fact]
    public void FailureTransition_InImplementationToFailedCompile_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.InImplementation, PhaseState.FailedCompile),
            PhaseState.FailedCompile);
    }

    [Fact]
    public void FailureTransition_UnityTestInProgressToFailedTests_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.UnityTestInProgress, PhaseState.FailedTests),
            PhaseState.FailedTests);
    }

    [Fact]
    public void FailureTransition_UnityTestLoggedToFailedTests_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.UnityTestLogged, PhaseState.FailedTests),
            PhaseState.FailedTests);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 8. IsTerminal helper
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PhaseState.Pass, true)]
    [InlineData(PhaseState.PassWithWarnings, true)]
    [InlineData(PhaseState.FailedArchitecture, true)]
    [InlineData(PhaseState.FailedCompile, true)]
    [InlineData(PhaseState.FailedTests, true)]
    [InlineData(PhaseState.Planned, false)]
    [InlineData(PhaseState.InImplementation, false)]
    [InlineData(PhaseState.Blocked, false)]
    [InlineData(PhaseState.ReadyForCodexReview, false)]
    [InlineData(PhaseState.UnityTestLogged, false)]
    public void IsTerminal_ReturnsExpectedResult(PhaseState state, bool expectedTerminal)
    {
        Assert.Equal(expectedTerminal, PhaseTransitionValidator.IsTerminal(state));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 9. State immutability: failure does not change input context
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidTransition_DoesNotMutateContext()
    {
        var context = new TransitionContext
        {
            CurrentState = PhaseState.Planned,
            TargetState = PhaseState.Pass
        };

        var result = _validator.Validate(context);

        // Context must be unchanged after a failed validation
        Assert.Equal(PhaseState.Planned, context.CurrentState);
        Assert.Equal(PhaseState.Pass, context.TargetState);
        Assert.True(result.IsFailure);
    }
}
