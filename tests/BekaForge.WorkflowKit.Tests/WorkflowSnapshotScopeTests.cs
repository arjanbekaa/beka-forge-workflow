using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class WorkflowSnapshotScopeTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public WorkflowSnapshotScopeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-snapshot-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("SnapshotScopeAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    public static IEnumerable<object[]> SnapshotOperations()
    {
        yield return ["implementation"];
        yield return ["audit"];
        yield return ["review"];
        yield return ["test"];
        yield return ["fix"];
        yield return ["validation-log"];
        yield return ["validation-skip"];
        yield return ["validation-complete-user"];
    }

    [Theory]
    [MemberData(nameof(SnapshotOperations))]
    public void NonCurrentPhase_LogAndValidationOperations_DoNotOverwriteWorkflowLastStatus(string operationKind)
    {
        var currentPhaseId = CreatePhase("Current");
        var targetPhaseId = CreatePhase("Target");

        SetPhaseState(currentPhaseId, PhaseState.ReadyForReview);
        PreparePhaseForOperation(targetPhaseId, operationKind);

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            CurrentPhaseId = currentPhaseId,
            LastStatus = PhaseState.ReadyForReview
        });

        var result = ExecuteSnapshotOperation(targetPhaseId, operationKind);

        Assert.True(result.Success, result.Message);

        var updatedWorkflow = _store.LoadWorkflow();
        Assert.Equal(currentPhaseId, updatedWorkflow.CurrentPhaseId);
        Assert.Equal(PhaseState.ReadyForReview, updatedWorkflow.LastStatus);
        Assert.Equal(ExpectedResultingState(operationKind), _store.LoadPhase(targetPhaseId)!.State);
        AssertLastIdWasUpdated(updatedWorkflow, operationKind);
    }

    [Theory]
    [MemberData(nameof(SnapshotOperations))]
    public void CurrentPhase_LogAndValidationOperations_AdvanceWorkflowLastStatus(string operationKind)
    {
        var phaseId = CreatePhase("Current");

        PreparePhaseForOperation(phaseId, operationKind);

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            CurrentPhaseId = phaseId,
            LastStatus = StartingState(operationKind)
        });

        var result = ExecuteSnapshotOperation(phaseId, operationKind);

        Assert.True(result.Success, result.Message);

        var updatedWorkflow = _store.LoadWorkflow();
        Assert.Equal(phaseId, updatedWorkflow.CurrentPhaseId);
        Assert.Equal(ExpectedResultingState(operationKind), updatedWorkflow.LastStatus);
        Assert.Equal(ExpectedResultingState(operationKind), _store.LoadPhase(phaseId)!.State);
        AssertLastIdWasUpdated(updatedWorkflow, operationKind);
    }

    private string CreatePhase(string title)
    {
        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.CreatePhase,
            Actor = WorkflowActor.Implementer,
            Parameters = new Dictionary<string, object?> { ["title"] = title }
        });

        Assert.True(result.Success, result.Message);
        return Assert.IsType<Phase>(result.Data).PhaseId;
    }

    private void PreparePhaseForOperation(string phaseId, string operationKind)
    {
        SetPhaseState(phaseId, StartingState(operationKind));
    }

    private void SetPhaseState(string phaseId, PhaseState state)
    {
        var phase = _store.LoadPhase(phaseId)!;
        _store.SavePhase(phase with
        {
            State = state,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }

    private static PhaseState StartingState(string operationKind) => operationKind switch
    {
        "implementation" => PhaseState.InImplementation,
        "audit" => PhaseState.ImplementationLogged,
        "review" => PhaseState.ReviewInProgress,
        "test" => PhaseState.TestInProgress,
        "fix" => PhaseState.FixInProgress,
        "validation-log" => PhaseState.TestInProgress,
        "validation-skip" => PhaseState.TestInProgress,
        "validation-complete-user" => PhaseState.TestInProgress,
        _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null)
    };

    private static PhaseState ExpectedResultingState(string operationKind) => operationKind switch
    {
        "implementation" => PhaseState.ImplementationLogged,
        "audit" => PhaseState.AuditLogged,
        "review" => PhaseState.ReviewLogged,
        "test" => PhaseState.TestLogged,
        "fix" => PhaseState.FixLogged,
        "validation-log" => PhaseState.TestLogged,
        "validation-skip" => PhaseState.TestLogged,
        "validation-complete-user" => PhaseState.TestLogged,
        _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null)
    };

    private OperationResult ExecuteSnapshotOperation(string phaseId, string operationKind)
    {
        return operationKind switch
        {
            "implementation" => Dispatch(WorkflowOperations.CreateImplementationLog, phaseId, WorkflowActor.Implementer,
                new Dictionary<string, object?> { ["summary"] = "Implemented scoped snapshot fix." }),
            "audit" => Dispatch(WorkflowOperations.CreateAuditLog, phaseId, WorkflowActor.Auditor,
                new Dictionary<string, object?> { ["summary"] = "Audit passed with scoped snapshot.", ["passed"] = true }),
            "review" => Dispatch(WorkflowOperations.CreateReviewLog, phaseId, WorkflowActor.Reviewer,
                new Dictionary<string, object?> { ["summary"] = "Review passed with scoped snapshot.", ["passed"] = true }),
            "test" => Dispatch(WorkflowOperations.CreateTestLog, phaseId, WorkflowActor.Validator,
                new Dictionary<string, object?> { ["summary"] = "Legacy test log passed.", ["passed"] = true }),
            "fix" => Dispatch(WorkflowOperations.CreateFixLog, phaseId, WorkflowActor.Fixer,
                new Dictionary<string, object?> { ["summary"] = "Applied the required fix." }),
            "validation-log" => Dispatch(WorkflowOperations.CreateValidationLog, phaseId, WorkflowActor.Validator,
                new Dictionary<string, object?>
                {
                    ["summary"] = "Automated validation passed.",
                    ["validationType"] = "AutomatedCommand",
                    ["validationResult"] = "Passed",
                    ["command"] = "dotnet test",
                    ["exitCode"] = 0,
                    ["evidenceItems"] = """[{"description":"dotnet test passed","source":2,"reference":"local-run"}]"""
                }),
            "validation-skip" => Dispatch(WorkflowOperations.SkipValidation, phaseId, WorkflowActor.Validator,
                new Dictionary<string, object?>
                {
                    ["summary"] = "Validation was not needed.",
                    ["skipReason"] = "Static handler-only change.",
                    ["validationType"] = "SkippedNotNeeded"
                }),
            "validation-complete-user" => Dispatch(WorkflowOperations.CompleteUserValidation, phaseId, WorkflowActor.HumanOwner,
                new Dictionary<string, object?>
                {
                    ["summary"] = "Human validation completed successfully.",
                    ["validationResult"] = "Passed",
                    ["evidenceItems"] = """[{"description":"Human owner confirmed behavior","source":1,"reference":"manual-check"}]"""
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null)
        };
    }

    private OperationResult Dispatch(
        string operation,
        string? phaseId,
        WorkflowActor actor,
        Dictionary<string, object?> parameters)
    {
        return _dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            PhaseId = phaseId,
            Actor = actor,
            Parameters = parameters
        });
    }

    private static void AssertLastIdWasUpdated(WorkflowState workflow, string operationKind)
    {
        switch (operationKind)
        {
            case "implementation":
                Assert.StartsWith("IMP-", workflow.LastImplementationId);
                break;
            case "audit":
                Assert.StartsWith("AUD-", workflow.LastAuditId);
                break;
            case "review":
                Assert.StartsWith("REV-", workflow.LastReviewId);
                break;
            case "test":
                Assert.StartsWith("TEST-", workflow.LastTestId);
                break;
            case "fix":
                Assert.StartsWith("FIX-", workflow.LastFixId);
                break;
            case "validation-log":
            case "validation-skip":
            case "validation-complete-user":
                Assert.StartsWith("VAL-", workflow.LastValidationId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null);
        }
    }
}
