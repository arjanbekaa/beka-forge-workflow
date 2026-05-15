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

    // -----------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------

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
            RequiresValidation = requiresUnityTest,
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

    // -----------------------------------------------------------------------------
    // 1. Happy-path: full forward traversal to PASS
    // -----------------------------------------------------------------------------

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
    public void HappyPath_AuditLoggedToReadyForReview_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.AuditLogged, PhaseState.ReadyForReview),
            PhaseState.ReadyForReview);
    }

    [Fact]
    public void HappyPath_ReadyForReviewToInProgress_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReadyForReview, PhaseState.ReviewInProgress),
            PhaseState.ReviewInProgress);
    }

    [Fact]
    public void HappyPath_ReviewInProgressToLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReviewInProgress, PhaseState.ReviewLogged),
            PhaseState.ReviewLogged);
    }

    [Fact]
    public void HappyPath_ReviewLoggedToReadyForTest_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReviewLogged, PhaseState.ReadyForTest),
            PhaseState.ReadyForTest);
    }

    [Fact]
    public void HappyPath_ReadyForTestToInProgress_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReadyForTest, PhaseState.TestInProgress),
            PhaseState.TestInProgress);
    }

    [Fact]
    public void HappyPath_TestInProgressToLogged_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.TestInProgress, PhaseState.TestLogged),
            PhaseState.TestLogged);
    }

    [Fact]
    public void HappyPath_TestLoggedToPass_WithUnityTestRequired_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.TestLogged, PhaseState.Pass, requiresUnityTest: true),
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
            PhaseState.ReadyForReview,
            PhaseState.ReviewInProgress,
            PhaseState.ReviewLogged,
            PhaseState.ReadyForTest,
            PhaseState.TestInProgress,
            PhaseState.TestLogged,
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

    // -----------------------------------------------------------------------------
    // 2. Fix path
    // -----------------------------------------------------------------------------

    [Fact]
    public void FixPath_ReviewInProgressToRequiresFix_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReviewInProgress, PhaseState.RequiresFix),
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
    public void FixPath_FixLoggedBackToReadyForReview_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.FixLogged, PhaseState.ReadyForReview),
            PhaseState.ReadyForReview);
    }

    [Fact]
    public void FixPath_FullCycle_AllTransitionsSucceed()
    {
        var fixPath = new[]
        {
            PhaseState.ReadyForReview,
            PhaseState.ReviewInProgress,
            PhaseState.RequiresFix,
            PhaseState.FixInProgress,
            PhaseState.FixLogged,
            PhaseState.ReadyForReview  // cycles back
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

    // -----------------------------------------------------------------------------
    // 3. Blocked path
    // -----------------------------------------------------------------------------

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
        var result = Transition(PhaseState.ReadyForReview, PhaseState.Blocked,
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
    public void Blocked_FromTestInProgress_WithReason_Succeeds()
    {
        var result = Transition(PhaseState.TestInProgress, PhaseState.Blocked,
            blockerReason: "Unity Editor crash blocks testing.");
        AssertSuccess(result, PhaseState.Blocked);
    }

    // -----------------------------------------------------------------------------
    // 4. Invalid transitions
    // -----------------------------------------------------------------------------

    [Fact]
    public void Invalid_PlannedToInImplementation_ReturnsInvalidTransition()
    {
        var result = Transition(PhaseState.Planned, PhaseState.InImplementation);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_ImplementationLoggedToReadyForTest_ReturnsInvalidTransition()
    {
        // Must go through Codex review first
        var result = Transition(PhaseState.ImplementationLogged, PhaseState.ReadyForTest);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_PlannedToPass_ReturnsInvalidTransition()
    {
        // Even with requiresUnityTest=false, must be in ReviewLogged to PASS
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
    public void Invalid_AssignedToImplementationToReviewInProgress_ReturnsInvalidTransition()
    {
        var result = Transition(PhaseState.AssignedToImplementation, PhaseState.ReviewInProgress);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_ReadyForReviewToPass_ReturnsInvalidTransition()
    {
        // Must go through review flow first; even with requiresUnityTest=false,
        // PASS requires ReviewLogged (not ReadyForReview)
        var result = Transition(PhaseState.ReadyForReview, PhaseState.Pass, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    [Fact]
    public void Invalid_ReviewInProgressToPassWithUnityTestRequired_ReturnsUnityTestRequired()
    {
        // requiresUnityTest=true but current state is not TestLogged
        var result = Transition(PhaseState.ReviewInProgress, PhaseState.Pass, requiresUnityTest: true);
        AssertFailure(result, "ValidationRequired");
    }

    [Fact]
    public void Invalid_ReviewInProgressToPassWithoutUnityTest_ReturnsInvalidTransition()
    {
        // requiresUnityTest=false but current state is not ReviewLogged
        var result = Transition(PhaseState.ReviewInProgress, PhaseState.Pass, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    // -----------------------------------------------------------------------------
    // 5. Unity test requirement rules
    // -----------------------------------------------------------------------------

    [Fact]
    public void UnityTest_PassRequiresTestLogged_WhenRequiresValidationTrue()
    {
        // In ReviewLogged but requiresUnityTest=true — must have test log first
        var result = Transition(PhaseState.ReviewLogged, PhaseState.Pass, requiresUnityTest: true);
        AssertFailure(result, "ValidationRequired");
    }

    [Fact]
    public void UnityTest_PassFromTestLogged_WhenRequiresValidationTrue_Succeeds()
    {
        var result = Transition(PhaseState.TestLogged, PhaseState.Pass, requiresUnityTest: true);
        AssertSuccess(result, PhaseState.Pass);
    }

    [Fact]
    public void UnityTest_PassFromReviewLogged_WhenRequiresValidationFalse_Succeeds()
    {
        // No Unity test required — can PASS directly from ReviewLogged
        var result = Transition(PhaseState.ReviewLogged, PhaseState.Pass, requiresUnityTest: false);
        AssertSuccess(result, PhaseState.Pass);
    }

    [Fact]
    public void UnityTest_PassWithWarnings_WhenRequiresValidationTrue_RequiresTestLogged()
    {
        var result = Transition(PhaseState.ReviewLogged, PhaseState.PassWithWarnings, requiresUnityTest: true);
        AssertFailure(result, "ValidationRequired");
    }

    [Fact]
    public void UnityTest_PassWithWarnings_FromTestLogged_WhenRequired_Succeeds()
    {
        var result = Transition(PhaseState.TestLogged, PhaseState.PassWithWarnings, requiresUnityTest: true);
        AssertSuccess(result, PhaseState.PassWithWarnings);
    }

    [Fact]
    public void UnityTest_PassWithWarnings_FromReviewLogged_WhenNotRequired_Succeeds()
    {
        var result = Transition(PhaseState.ReviewLogged, PhaseState.PassWithWarnings, requiresUnityTest: false);
        AssertSuccess(result, PhaseState.PassWithWarnings);
    }

    [Fact]
    public void UnityTest_PassWithWarnings_FromWrongState_WhenNotRequired_ReturnsInvalidTransition()
    {
        // requiresUnityTest=false but not in ReviewLogged
        var result = Transition(PhaseState.AuditLogged, PhaseState.PassWithWarnings, requiresUnityTest: false);
        AssertFailure(result, "InvalidTransition");
    }

    // -----------------------------------------------------------------------------
    // 6. Terminal states reject all normal transitions
    // -----------------------------------------------------------------------------

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
            PhaseState.ReadyForReview,
            PhaseState.ReviewInProgress,
            PhaseState.ReviewLogged,
            PhaseState.RequiresFix,
            PhaseState.FixInProgress,
            PhaseState.FixLogged,
            PhaseState.ReadyForTest,
            PhaseState.TestInProgress,
            PhaseState.TestLogged
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
    public void Terminal_FailedCompileCannotTransitionToReadyForReview()
    {
        var result = Transition(PhaseState.FailedCompile, PhaseState.ReadyForReview);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_FailedArchitectureCannotTransitionToRequiresFix()
    {
        var result = Transition(PhaseState.FailedArchitecture, PhaseState.RequiresFix);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_FailedValidationCannotTransitionToTestInProgress()
    {
        var result = Transition(PhaseState.FailedValidation, PhaseState.TestInProgress);
        AssertFailure(result, "TerminalState");
    }

    [Fact]
    public void Terminal_PassWithWarningsCannotTransitionToReadyForImplementation()
    {
        var result = Transition(PhaseState.PassWithWarnings, PhaseState.ReadyForImplementation);
        AssertFailure(result, "TerminalState");
    }

    // -----------------------------------------------------------------------------
    // 7. Failure transitions (valid terminal-entry paths)
    // -----------------------------------------------------------------------------

    [Fact]
    public void FailureTransition_ReviewInProgressToFailedArchitecture_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.ReviewInProgress, PhaseState.FailedArchitecture),
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
    public void FailureTransition_TestInProgressToFailedValidation_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.TestInProgress, PhaseState.FailedValidation),
            PhaseState.FailedValidation);
    }

    [Fact]
    public void FailureTransition_TestLoggedToFailedValidation_Succeeds()
    {
        AssertSuccess(
            Transition(PhaseState.TestLogged, PhaseState.FailedValidation),
            PhaseState.FailedValidation);
    }

    // -----------------------------------------------------------------------------
    // 8. IsTerminal helper
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(PhaseState.Pass, true)]
    [InlineData(PhaseState.PassWithWarnings, true)]
    [InlineData(PhaseState.FailedArchitecture, true)]
    [InlineData(PhaseState.FailedCompile, true)]
    [InlineData(PhaseState.FailedValidation, true)]
    [InlineData(PhaseState.Planned, false)]
    [InlineData(PhaseState.InImplementation, false)]
    [InlineData(PhaseState.Blocked, false)]
    [InlineData(PhaseState.ReadyForReview, false)]
    [InlineData(PhaseState.TestLogged, false)]
    public void IsTerminal_ReturnsExpectedResult(PhaseState state, bool expectedTerminal)
    {
        Assert.Equal(expectedTerminal, PhaseTransitionValidator.IsTerminal(state));
    }

    // -----------------------------------------------------------------------------
    // 9. State immutability: failure does not change input context
    // -----------------------------------------------------------------------------

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
