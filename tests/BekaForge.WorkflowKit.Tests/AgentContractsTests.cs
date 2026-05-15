using BekaForge.WorkflowKit.AgentContracts;
using System.Collections.Generic;
using BekaForge.WorkflowKit.Core;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for AgentContracts types: operation constants, response envelopes, error codes.
/// </summary>
public sealed class AgentContractsTests
{
    // -- Operation names are non-null, non-empty, and properly prefixed ------------

    [Fact]
    public void AllOperationConstants_AreNonNullAndNonEmpty()
    {
        var fields = typeof(WorkflowOperations)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly);

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"Operation constant {field.Name} must not be null or empty.");
        }
    }

    [Fact]
    public void AllOperationConstants_StartWithWorkflowDotPrefix()
    {
        var fields = typeof(WorkflowOperations)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly);

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.True(value!.StartsWith("workflow."),
                $"Operation constant {field.Name} = '{value}' must start with 'workflow.'");
        }
    }

    [Fact]
    public void WorkflowOperations_GetState_HasExpectedValue()
    {
        Assert.Equal("workflow.get_state", WorkflowOperations.GetState);
    }

    [Fact]
    public void WorkflowOperations_CreatePhase_HasExpectedValue()
    {
        Assert.Equal("workflow.create_phase", WorkflowOperations.CreatePhase);
    }

    [Fact]
    public void WorkflowOperations_SyncMarkdown_HasExpectedValue()
    {
        Assert.Equal("workflow.sync_markdown", WorkflowOperations.SyncMarkdown);
    }

    // -- AgentResponse<T> ---------------------------------------------------------

    [Fact]
    public void AgentResponseT_Ok_SetsSuccessTrueAndData()
    {
        var response = AgentResponse<string>.Ok("hello");
        Assert.True(response.Success);
        Assert.Equal("hello", response.Data);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Message);
    }

    [Fact]
    public void AgentResponseT_Fail_SetsSuccessFalseAndErrorFields()
    {
        var response = AgentResponse<string>.Fail("NotFound", "Phase not found.");
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Equal("NotFound", response.ErrorCode);
        Assert.Equal("Phase not found.", response.Message);
    }

    [Fact]
    public void AgentResponseT_FromError_MapsWorkflowErrorCorrectly()
    {
        var error = WorkflowError.InvalidTransition(PhaseState.Planned, PhaseState.Pass, "skipped gates");
        var response = AgentResponse<int>.FromError(error);
        Assert.False(response.Success);
        Assert.Equal("InvalidTransition", response.ErrorCode);
        Assert.Contains("Planned", response.Message);
        Assert.Contains("Pass", response.Message);
    }

    // -- AgentResponse (untyped) ---------------------------------------------------

    [Fact]
    public void AgentResponse_Ok_SetsSuccessTrue()
    {
        var response = AgentResponse.Ok();
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public void AgentResponse_Fail_SetsSuccessFalse()
    {
        var response = AgentResponse.Fail("StorageError", "Disk write failed.");
        Assert.False(response.Success);
        Assert.Equal("StorageError", response.ErrorCode);
    }

    // -- AgentRequest -------------------------------------------------------------

    [Fact]
    public void AgentRequest_CanBeConstructedWithRequiredFields()
    {
        var request = new AgentRequest
        {
            Operation = WorkflowOperations.GetState
        };
        Assert.Equal("workflow.get_state", request.Operation);
        Assert.Null(request.PhaseId);
        Assert.Empty(request.Parameters);
    }

    [Fact]
    public void AgentRequest_CanIncludePhaseIdAndParameters()
    {
        var request = new AgentRequest
        {
            Operation = WorkflowOperations.UpdatePhaseStatus,
            PhaseId = "PHASE-001",
            Parameters = new Dictionary<string, object?> { ["state"] = "BLOCKED" }
        };
        Assert.Equal("PHASE-001", request.PhaseId);
        Assert.Equal("BLOCKED", request.Parameters["state"]);
    }

    // -- Error codes are non-null and non-empty ------------------------------------

    [Fact]
    public void AllErrorCodeConstants_AreNonNullAndNonEmpty()
    {
        var fields = typeof(WorkflowErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly);

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"Error code constant {field.Name} must not be null or empty.");
        }
    }

    // -- WriteTargetEntry ---------------------------------------------------------

    [Fact]
    public void WriteTargetEntry_CanBeConstructedWithRequiredFields()
    {
        var wt = new WriteTargetEntry
        {
            OperationName     = WorkflowOperations.CreateImplementationLog,
            TargetDescription = "Appends an IMP record to implementation.jsonl",
            AccessLevel       = OperationAccessLevel.Append,
            IsAppendOnly      = true
        };

        Assert.Equal("workflow.create_implementation_log", wt.OperationName);
        Assert.Equal("Appends an IMP record to implementation.jsonl", wt.TargetDescription);
        Assert.Equal(OperationAccessLevel.Append, wt.AccessLevel);
        Assert.True(wt.IsAppendOnly);
        Assert.False(wt.IsEventTracked); // default
        Assert.Null(wt.RequiredParameters);
        Assert.Null(wt.SuitableActors);
    }

    [Fact]
    public void WriteTargetEntry_WithOptionalFields_SetsAllValues()
    {
        var wt = new WriteTargetEntry
        {
            OperationName     = WorkflowOperations.CreatePhase,
            TargetDescription = "Creates a phase JSON file",
            AccessLevel       = OperationAccessLevel.Write,
            IsAppendOnly      = false,
            IsEventTracked    = true,
            RequiredParameters = ["title", "summary"],
            SuitableActors    = ["codex"]
        };

        Assert.True(wt.IsEventTracked);
        Assert.Equal(2, wt.RequiredParameters!.Length);
        Assert.Contains("title", wt.RequiredParameters);
        Assert.Single(wt.SuitableActors!);
        Assert.Equal("codex", wt.SuitableActors![0]);
    }

    // -- ValidationIssue ----------------------------------------------------------

    [Fact]
    public void ValidationIssue_Error_HasCorrectSeverityAndFields()
    {
        var issue = ValidationIssue.Error(
            "MissingRequiredParameter",
            "Missing required parameters: summary",
            field: "summary",
            alternatives: ["workflow.get_state"]);

        Assert.Equal("error", issue.Severity);
        Assert.Equal("MissingRequiredParameter", issue.Code);
        Assert.Contains("summary", issue.Message);
        Assert.Equal("summary", issue.Field);
        Assert.NotNull(issue.SaferAlternatives);
        Assert.Single(issue.SaferAlternatives!);
        Assert.Equal("workflow.get_state", issue.SaferAlternatives![0]);
    }

    [Fact]
    public void ValidationIssue_Warning_HasCorrectSeverity()
    {
        var issue = ValidationIssue.Warning(
            "UnsuitableActor",
            "Actor may not be suitable.",
            field: "actor");

        Assert.Equal("warning", issue.Severity);
        Assert.Equal("UnsuitableActor", issue.Code);
        Assert.Null(issue.SaferAlternatives);
    }

    // -- OperationValidationResult ------------------------------------------------

    [Fact]
    public void OperationValidationResult_ValidRequest_ReturnsIsValidTrue()
    {
        var result = new OperationValidationResult
        {
            IsValid        = true,
            OperationName  = "workflow.get_state",
            AccessLevel    = OperationAccessLevel.Read,
            Issues         = [],
            ProposedActor  = "deepSeek"
        };

        Assert.True(result.IsValid);
        Assert.Equal("workflow.get_state", result.OperationName);
        Assert.Equal(OperationAccessLevel.Read, result.AccessLevel);
        Assert.Empty(result.Issues);
        Assert.Equal("deepSeek", result.ProposedActor);
        Assert.Null(result.ProposedPhaseId);
        Assert.Null(result.WriteTargets);
        Assert.Null(result.RequiredParameters);
    }

    [Fact]
    public void OperationValidationResult_InvalidRequest_HasErrors()
    {
        var issues = new List<ValidationIssue>
        {
            ValidationIssue.Error("UnknownOperation", "Operation not found.")
        };

        var result = new OperationValidationResult
        {
            IsValid        = false,
            OperationName  = "workflow.unknown",
            AccessLevel    = OperationAccessLevel.Read,
            Issues         = issues,
            ProposedActor  = "deepSeek",
            ProposedPhaseId = "PHASE-001"
        };

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("error", result.Issues[0].Severity);
        Assert.Equal("PHASE-001", result.ProposedPhaseId);
    }

    // -- New WorkflowOperations constant ------------------------------------------

    [Fact]
    public void WorkflowOperations_ValidateOperationRequest_HasExpectedValue()
    {
        Assert.Equal("workflow.validate_operation_request", WorkflowOperations.ValidateOperationRequest);
    }
}