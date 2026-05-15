using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.DispatcherTests;

/// <summary>
/// Integration tests for workflow.validate_operation_request.
/// </summary>
public sealed class ValidateOperationRequestTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public ValidateOperationRequestTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-val-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("ValidationTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);

        // Create a test phase for phase-dependent tests
        _dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.CreatePhase,
            Actor     = WorkflowActor.Codex,
            Parameters = new Dictionary<string, object?>
            {
                ["title"]   = "Validation Test Phase",
                ["summary"] = "Created for validation tests"
            }
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private OperationContext Ctx(string operation,
        string? phaseId = null,
        WorkflowActor actor = WorkflowActor.Codex,
        Dictionary<string, object?>? parameters = null) =>
        new()
        {
            Operation  = operation,
            Actor      = actor,
            PhaseId    = phaseId,
            Parameters = parameters ?? []
        };

    private OperationResult Dispatch(string operation,
        string? phaseId = null,
        WorkflowActor actor = WorkflowActor.Codex,
        Dictionary<string, object?>? parameters = null) =>
        _dispatcher.Dispatch(Ctx(operation, phaseId, actor, parameters));

    // -- ValidateOperationRequest --------------------------------------------------

    [Fact]
    public void ValidateOperationRequest_MissingTargetOperation_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest);
        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
        Assert.Contains("targetOperation", result.Message);
    }

    [Fact]
    public void ValidateOperationRequest_UnknownOperation_ReturnsInvalidWithAlternatives()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            parameters: new() { ["targetOperation"] = "workflow.does_not_exist" });

        Assert.True(result.Success, result.Message); // Operation itself succeeds
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        Assert.Equal("workflow.does_not_exist", validation.OperationName);
        Assert.NotEmpty(validation.Issues);
        Assert.Contains(validation.Issues, i => i.Code == "UnknownOperation");

        // Unknown operation should offer safer alternatives
        var unknownIssue = validation.Issues.First(i => i.Code == "UnknownOperation");
        Assert.NotNull(unknownIssue.SaferAlternatives);
        Assert.NotEmpty(unknownIssue.SaferAlternatives!);
        // Alternatives should be valid read operations
        Assert.All(unknownIssue.SaferAlternatives!, alt =>
            Assert.StartsWith("workflow.", alt));
    }

    [Fact]
    public void ValidateOperationRequest_ValidReadOperation_ReturnsValid()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            parameters: new() { ["targetOperation"] = WorkflowOperations.GetState });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.True(validation.IsValid);
        Assert.Equal(WorkflowOperations.GetState, validation.OperationName);
        Assert.Equal(OperationAccessLevel.Read, validation.AccessLevel);
        Assert.Empty(validation.Issues.Where(i => i.Severity == "error"));
    }

    [Fact]
    public void ValidateOperationRequest_WriteOperation_WarnsAboutMutation()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreatePhase,
                ["title"] = "New phase",
                ["summary"] = "Phase summary"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.True(validation.IsValid); // No errors, warnings are ok
        Assert.Equal(OperationAccessLevel.Write, validation.AccessLevel);
        Assert.Contains(validation.Issues, i =>
            i.Severity == "warning" && i.Code == "WriteOperation");
    }

    [Fact]
    public void ValidateOperationRequest_AppendOperation_HasWriteTargets()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: "PHASE-001",
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateImplementationLog,
                ["summary"] = "Implementation summary"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.True(validation.IsValid);
        Assert.Equal(OperationAccessLevel.Append, validation.AccessLevel);
        Assert.NotNull(validation.WriteTargets);
        Assert.NotEmpty(validation.WriteTargets!);
        // Write targets must be safe operation names, not raw file paths
        Assert.All(validation.WriteTargets!, wt =>
        {
            Assert.StartsWith("workflow.", wt.OperationName);
            Assert.False(string.IsNullOrWhiteSpace(wt.TargetDescription));
            Assert.DoesNotContain(".jsonl", wt.TargetDescription?.Split(' ').FirstOrDefault() ?? ""); // Not a raw path
            Assert.DoesNotContain("\\", wt.TargetDescription ?? "");
        });
    }

    [Fact]
    public void ValidateOperationRequest_MissingRequiredParameters_ReportsErrors()
    {
        // CreateImplementationLog requires "summary" — don't provide it
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: "PHASE-001",
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateImplementationLog
                // summary intentionally omitted
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i =>
            i.Severity == "error" && i.Code == "MissingRequiredParameter");
    }

    [Fact]
    public void ValidateOperationRequest_MissingPhaseId_ReportsError()
    {
        // CreateImplementationLog requires phaseId — don't provide it
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: null, // No phaseId
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateImplementationLog,
                ["summary"]         = "Test summary"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i =>
            i.Severity == "error" && i.Code == "MissingPhaseId");
    }

    [Fact]
    public void ValidateOperationRequest_UnsuitableActor_ReportsWarning()
    {
        // CreateAuditLog is only suitable for deepSeek — call as Codex
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: "PHASE-001",
            actor: WorkflowActor.Codex,
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateAuditLog,
                ["summary"]         = "Test audit"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.Contains(validation.Issues, i =>
            i.Severity == "warning" && i.Code == "UnsuitableActor");
    }

    [Fact]
    public void ValidateOperationRequest_UnsafeRawOperation_ReturnsError()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            parameters: new() { ["targetOperation"] = "file.write" });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        var rawIssue = validation.Issues.FirstOrDefault(i => i.Code == "UnsafeRawOperation");
        Assert.NotNull(rawIssue);
        Assert.NotNull(rawIssue!.SaferAlternatives);
        Assert.NotEmpty(rawIssue.SaferAlternatives!);
    }

    [Fact]
    public void ValidateOperationRequest_WriteTargetsNeverExposeRawPaths()
    {
        // Check that ALL write-capable operations return WriteTargets with operation names, not paths
        var writeOps = new[]
        {
            WorkflowOperations.CreatePhase,
            WorkflowOperations.CreateImplementationLog,
            WorkflowOperations.SyncMarkdown,
            WorkflowOperations.RebuildContextIndex,
            WorkflowOperations.SetNextAction,
            WorkflowOperations.RecordBlocker,
            WorkflowOperations.CreateHandoff,
            WorkflowOperations.RecordTimeSpent,
        };

        foreach (var op in writeOps)
        {
            var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
                parameters: new() { ["targetOperation"] = op });

            Assert.True(result.Success, $"Validation for '{op}' failed: {result.Message}");
            var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);

            if (validation.WriteTargets is { Count: > 0 })
            {
                foreach (var wt in validation.WriteTargets)
                {
                    Assert.StartsWith("workflow.", wt.OperationName);
                    // TargetDescription must not contain raw file paths
                    Assert.False(wt.TargetDescription.Contains(".jsonl\\"),
                        $"WriteTarget for '{op}' exposes raw path: {wt.TargetDescription}");
                    Assert.False(wt.TargetDescription.Contains(".json\\"),
                        $"WriteTarget for '{op}' exposes raw path: {wt.TargetDescription}");
                }
            }
        }
    }

    [Fact]
    public void ValidateOperationRequest_PhaseNotFound_ReportsWarning()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: "PHASE-999",
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateFixLog,
                ["summary"]         = "Test fix"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.Contains(validation.Issues, i =>
            i.Severity == "warning" && i.Code == "PhaseNotFound");
    }

    [Fact]
    public void ValidateOperationRequest_ProvidesRequiredParametersList()
    {
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            parameters: new() { ["targetOperation"] = WorkflowOperations.CreateImplementationLog });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.NotNull(validation.RequiredParameters);
        Assert.NotEmpty(validation.RequiredParameters!);
        Assert.Contains("summary", validation.RequiredParameters);
    }

    [Fact]
    public void ValidateOperationRequest_AppendOperation_NoWriteWarning()
    {
        // Append operations should NOT get the "WriteOperation" warning
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: "PHASE-001",
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateFixLog,
                ["summary"]         = "Test fix"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.DoesNotContain(validation.Issues, i => i.Code == "WriteOperation");
    }

    [Fact]
    public void ValidateOperationRequest_SuitableActor_NoActorWarning()
    {
        // CreateAuditLog is suitable for deepSeek
        var result = Dispatch(WorkflowOperations.ValidateOperationRequest,
            phaseId: "PHASE-001",
            actor: WorkflowActor.DeepSeek,
            parameters: new()
            {
                ["targetOperation"] = WorkflowOperations.CreateAuditLog,
                ["summary"]         = "Test self-audit"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsAssignableFrom<OperationValidationResult>(result.Data);
        Assert.DoesNotContain(validation.Issues, i => i.Code == "UnsuitableActor");
    }

    [Fact]
    public void ValidateOperationRequest_NoWriteTargetsExposeLineRanges()
    {
        // Check that WriteTarget descriptions never contain line range patterns
        var writeOps = OperationManifestCatalog.GetWriteCapableEntries();

        foreach (var entry in writeOps)
        {
            if (entry.WriteTargets is { Count: > 0 })
            {
                foreach (var wt in entry.WriteTargets)
                {
                    // Line ranges look like "lines 10-20" or "L10-L20"
                    Assert.DoesNotContain("line", wt.TargetDescription.ToLowerInvariant());
                    Assert.DoesNotContain("L10", wt.TargetDescription);
                    Assert.DoesNotContain("offset", wt.TargetDescription.ToLowerInvariant());
                }
            }
        }
    }
}
