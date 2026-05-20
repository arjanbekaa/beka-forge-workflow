using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class PersonaPolicyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public PersonaPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-persona-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("PersonaTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private OperationResult Dispatch(
        string operation,
        Dictionary<string, object?>? parameters = null) =>
        _dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            Parameters = parameters ?? new Dictionary<string, object?>()
        });

    [Fact]
    public void WorkflowInitializer_CreatesPersistedPersonaCatalogFiles()
    {
        Assert.True(File.Exists(WorkflowLayout.PersonaProfilesPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.TaskPoliciesPath(_tempRoot)));
    }

    [Fact]
    public void PersonaCatalogStore_LoadsPersistedCatalogFromInitializedWorkflow()
    {
        var store = new PersonaCatalogStore(_tempRoot);
        var result = store.Load();

        Assert.Equal("persisted", result.Source);
        Assert.Contains(result.Catalog.Personas, persona => persona.PersonaId == "implementer");
        Assert.Contains(result.Catalog.TaskPolicies, policy => policy.TaskType == "validation");
    }

    [Fact]
    public void PersonaCatalogStore_FallsBackWhenPersistedPolicyContainsUnsafeOperation()
    {
        File.WriteAllText(
            WorkflowLayout.TaskPoliciesPath(_tempRoot),
            """
            [
              {
                "taskType": "validation",
                "summary": "Unsafe",
                "keywords": ["validate"],
                "allowedActors": ["validator"],
                "allowedOperations": ["workflow.skip_validation"]
              }
            ]
            """);

        var store = new PersonaCatalogStore(_tempRoot);
        var result = store.Load();

        Assert.Equal("builtInDefault", result.Source);
        Assert.Contains(result.Warnings, warning => warning.Contains("cannot authorize unsafe operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListPersonas_ReturnsSeededCatalog()
    {
        var result = Dispatch(WorkflowOperations.ListPersonas);

        Assert.True(result.Success, result.Message);
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(
            result.Data,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        var personas = payload.GetProperty("personas");
        Assert.True(personas.GetArrayLength() >= 5);
    }

    [Fact]
    public void GetPersona_ReturnsPoliciesForAlias()
    {
        var result = Dispatch(
            WorkflowOperations.GetPersona,
            new() { ["personaId"] = "deepseek" });

        Assert.True(result.Success, result.Message);
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(
            result.Data,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Assert.Equal("implementer", payload.GetProperty("persona").GetProperty("personaId").GetString());
        Assert.True(payload.GetProperty("taskPolicies").GetArrayLength() >= 1);
    }

    [Fact]
    public void RecommendPersona_PrefersImplementerForImplementationTask()
    {
        var result = Dispatch(
            WorkflowOperations.RecommendPersona,
            new()
            {
                ["task"] = "Implement persona storage and CLI handlers",
                ["requestedOperation"] = WorkflowOperations.CreateImplementationLog,
                ["requestedActor"] = "Implementer"
            });

        Assert.True(result.Success, result.Message);
        var recommendation = Assert.IsType<PersonaRecommendationResult>(result.Data);
        Assert.NotEmpty(recommendation.Recommendations);
        Assert.Equal("implementer", recommendation.Recommendations[0].PersonaId);
    }

    [Fact]
    public void ValidatePersonaTask_AllowsSafeImplementerPath()
    {
        var result = Dispatch(
            WorkflowOperations.ValidatePersonaTask,
            new()
            {
                ["personaId"] = "implementer",
                ["task"] = "Implement the persona catalog loader and wire the CLI command",
                ["requestedOperation"] = WorkflowOperations.CreateImplementationLog,
                ["requestedActor"] = "Implementer",
                ["requestedPhaseId"] = "PHASE-062"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsType<PersonaTaskValidationResult>(result.Data);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));
        Assert.Equal("implementation", validation.MatchedTaskType);
    }

    [Fact]
    public void ValidatePersonaTask_RejectsReviewByImplementer()
    {
        var result = Dispatch(
            WorkflowOperations.ValidatePersonaTask,
            new()
            {
                ["personaId"] = "implementer",
                ["task"] = "Approve this phase and close the review gate",
                ["requestedOperation"] = WorkflowOperations.CreateReviewLog,
                ["requestedActor"] = "Implementer",
                ["requestedPhaseId"] = "PHASE-062"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsType<PersonaTaskValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue =>
            issue.Contains("forbids operation", StringComparison.OrdinalIgnoreCase)
            || issue.Contains("does not allow operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatePersonaTask_RejectsMissingPhaseContext()
    {
        var result = Dispatch(
            WorkflowOperations.ValidatePersonaTask,
            new()
            {
                ["personaId"] = "validator",
                ["task"] = "Run validation and record the result",
                ["requestedOperation"] = WorkflowOperations.CreateValidationLog,
                ["requestedActor"] = "Validator"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsType<PersonaTaskValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Contains("requires a phase ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatePersonaTask_RejectsValidationBypassPhrase()
    {
        var result = Dispatch(
            WorkflowOperations.ValidatePersonaTask,
            new()
            {
                ["personaId"] = "validator",
                ["task"] = "Skip validation and mark passed without evidence",
                ["requestedOperation"] = WorkflowOperations.CreateValidationLog,
                ["requestedActor"] = "Validator",
                ["requestedPhaseId"] = "PHASE-062"
            });

        Assert.True(result.Success, result.Message);
        var validation = Assert.IsType<PersonaTaskValidationResult>(result.Data);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Contains("forbidden phrase", StringComparison.OrdinalIgnoreCase));
    }
}
