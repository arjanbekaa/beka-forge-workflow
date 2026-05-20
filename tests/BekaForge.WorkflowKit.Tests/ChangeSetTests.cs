using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class ChangeSetTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public ChangeSetTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-changeset-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("ChangeSetTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ValidateChangeSet_ValidFile_ReturnsPreviewAndWritesNothing()
    {
        var file = WriteChangeSet("""
            {
              "schemaVersion": "1.0",
              "title": "Roadmap import",
              "operations": [
                {
                  "type": "createPhase",
                  "refId": "core",
                  "parameters": {
                    "title": "Core phase",
                    "summary": "Create the core phase",
                    "objective": "Create a phase from a ChangeSet",
                    "scope": "Phase metadata only",
                    "acceptanceCriteria": ["Phase exists"]
                  }
                }
              ]
            }
            """);

        var result = Dispatch(WorkflowOperations.ValidateChangeSet, file);

        Assert.True(result.Success, result.Message);
        var report = Assert.IsType<WorkflowChangeSetValidationReport>(result.Data);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(i => i.Message)));
        Assert.Single(report.OperationPreviews);
        Assert.Empty(_store.LoadWorkflow().PhaseIds);
    }

    [Fact]
    public void ApplyChangeSet_DryRun_ReturnsValidReportAndCreatesZeroPhases()
    {
        var file = WriteValidTwoPhaseChangeSet();

        var result = Dispatch(WorkflowOperations.ApplyChangeSet, file, new() { ["dryRun"] = true });

        Assert.True(result.Success, result.Message);
        var report = Assert.IsType<WorkflowChangeSetApplyReport>(result.Data);
        Assert.True(report.DryRun);
        Assert.False(report.Applied);
        Assert.True(report.Validation.IsValid);
        Assert.Empty(_store.LoadWorkflow().PhaseIds);
    }

    [Fact]
    public void ApplyChangeSet_ValidFile_CreatesSequentialPhasesAndResolvesRefIds()
    {
        var file = WriteValidTwoPhaseChangeSet();

        var result = Dispatch(WorkflowOperations.ApplyChangeSet, file);

        Assert.True(result.Success, result.Message);
        var report = Assert.IsType<WorkflowChangeSetApplyReport>(result.Data);
        Assert.True(report.Applied);
        Assert.Equal("PHASE-001", report.CreatedIds["core"]);
        Assert.Equal("PHASE-002", report.CreatedIds["followup"]);

        var first = _store.LoadPhase("PHASE-001")!;
        var second = _store.LoadPhase("PHASE-002")!;
        Assert.Equal("Core phase", first.Title);
        Assert.Equal("Follow-up phase", second.Title);
        Assert.Equal("PHASE-001", second.Dependencies.Single());
        Assert.Equal("PHASE-001", second.Contract!.DependsOnPhaseIds.Single());
        Assert.Equal("PHASE-002", _store.LoadWorkflow().NextAction!.PhaseId);
    }

    [Fact]
    public void ApplyChangeSet_InvalidFile_DoesNotWriteAnyPhase()
    {
        var file = WriteChangeSet("""
            {
              "schemaVersion": "1.0",
              "title": "Bad import",
              "operations": [
                { "type": "createPhase", "parameters": { "summary": "Missing title and objective" } },
                { "type": "writeFile", "parameters": { "path": ".workflowkit/workflow.json", "content": "{}" } }
              ]
            }
            """);

        var result = Dispatch(WorkflowOperations.ApplyChangeSet, file);

        Assert.True(result.Success, result.Message);
        var report = Assert.IsType<WorkflowChangeSetApplyReport>(result.Data);
        Assert.False(report.Applied);
        Assert.False(report.Validation.IsValid);
        Assert.Contains(report.Validation.Issues, issue => issue.Code == "MissingPhaseTitle");
        Assert.Contains(report.Validation.Issues, issue => issue.Code == "RawFileMutationRejected");
        Assert.Empty(_store.LoadWorkflow().PhaseIds);
    }

    [Fact]
    public void ValidateChangeSet_ForwardRef_FailsValidation()
    {
        var file = WriteChangeSet("""
            {
              "schemaVersion": "1.0",
              "title": "Bad refs",
              "operations": [
                {
                  "type": "createPhase",
                  "refId": "later",
                  "parameters": {
                    "title": "Later",
                    "objective": "Later objective",
                    "scope": "Later scope",
                    "dependencies": ["$ref:not-yet"]
                  }
                }
              ]
            }
            """);

        var result = Dispatch(WorkflowOperations.ValidateChangeSet, file);

        Assert.True(result.Success, result.Message);
        var report = Assert.IsType<WorkflowChangeSetValidationReport>(result.Data);
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, issue => issue.Code == "UnresolvedRefId");
    }

    [Fact]
    public void ManifestAndRouting_ExposeChangeSetOperations()
    {
        Assert.Contains(WorkflowOperations.ValidateChangeSet, _dispatcher.RegisteredOperations);
        Assert.Contains(WorkflowOperations.ApplyChangeSet, _dispatcher.RegisteredOperations);

        var manifestOps = OperationManifestCatalog.GetAll().Select(entry => entry.OperationName).ToHashSet();
        Assert.Contains(WorkflowOperations.ValidateChangeSet, manifestOps);
        Assert.Contains(WorkflowOperations.ApplyChangeSet, manifestOps);

        var recommendation = ToolRoutingCatalog.Recommend("create a large roadmap with many phases");
        Assert.Contains(recommendation.Recommendations,
            entry => entry.OperationName == WorkflowOperations.ValidateChangeSet);
    }

    private OperationResult Dispatch(
        string operation,
        string file,
        Dictionary<string, object?>? parameters = null)
    {
        parameters ??= [];
        parameters["file"] = file;
        return _dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            Parameters = parameters
        });
    }

    private string WriteValidTwoPhaseChangeSet() => WriteChangeSet("""
        {
          "schemaVersion": "1.0",
          "title": "Two phase roadmap",
          "description": "Creates two phases and points next action at the second.",
          "operations": [
            {
              "type": "createPhase",
              "refId": "core",
              "parameters": {
                "title": "Core phase",
                "summary": "Create the core phase",
                "objective": "Create the core phase",
                "scope": "Core metadata",
                "acceptanceCriteria": ["Core phase exists"]
              }
            },
            {
              "type": "createPhase",
              "refId": "followup",
              "parameters": {
                "title": "Follow-up phase",
                "summary": "Create the dependent phase",
                "objective": "Create the dependent phase",
                "scope": "Dependent metadata",
                "dependencies": ["$ref:core"],
                "dependsOnPhaseIds": ["$ref:core"],
                "acceptanceCriteria": ["Dependency is resolved"]
              }
            },
            {
              "type": "setNextAction",
              "parameters": {
                "phaseId": "$ref:followup",
                "actor": "Implementer",
                "description": "Implement the dependent phase",
                "operationHint": "workflow.apply_changeset"
              }
            }
          ]
        }
        """);

    private string WriteChangeSet(string json)
    {
        using var document = JsonDocument.Parse(json);
        var formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var file = Path.Combine(_tempRoot, $"changeset-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, formatted);
        return file;
    }
}
