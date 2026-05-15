using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for Tool Routing and Recommendation API (PHASE-008).
/// Covers routing rules, search, recommend, explain, safety warnings,
/// deterministic behavior, and file export.
/// </summary>
public sealed class ToolRoutingTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public ToolRoutingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-routing-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("RoutingTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private OperationContext Ctx(string operation,
        Dictionary<string, object?>? parameters = null) =>
        new() { Operation = operation, Actor = WorkflowActor.Codex, Parameters = parameters ?? [] };

    private OperationResult Dispatch(string operation,
        Dictionary<string, object?>? parameters = null) =>
        _dispatcher.Dispatch(Ctx(operation, parameters));

    // -- DTOs ---------------------------------------------------------------------

    [Fact]
    public void ToolRoutingRule_Construction_RoundTrips()
    {
        var rule = new ToolRoutingRule
        {
            IntentKeyword = "get state",
            OperationName = "workflow.get_state",
            Confidence     = 1.0,
            IsPrimary      = true
        };
        Assert.Equal("get state", rule.IntentKeyword);
        Assert.Equal("workflow.get_state", rule.OperationName);
        Assert.Equal(1.0, rule.Confidence);
        Assert.True(rule.IsPrimary);
    }

    [Fact]
    public void OperationRecommendation_Construction_HasRequiredFields()
    {
        var rec = new OperationRecommendation
        {
            TaskDescription  = "get current workflow state",
            Recommendations  = [],
            Warnings         = ["test warning"],
            SaferAlternative = null
        };
        Assert.NotEmpty(rec.TaskDescription);
        Assert.NotNull(rec.Recommendations);
        Assert.NotEmpty(rec.Warnings);
    }

    // -- Routing rules ------------------------------------------------------------

    [Fact]
    public void GetRules_ReturnsNonEmptyList()
    {
        var rules = ToolRoutingCatalog.GetRules();
        Assert.NotEmpty(rules);
    }

    [Fact]
    public void GetRules_HasExpectedIntentKeywords()
    {
        var keywords = ToolRoutingCatalog.GetRules()
            .Select(r => r.IntentKeyword)
            .ToHashSet();

        // Spot-check a representative set.
        Assert.Contains("get state", keywords);
        Assert.Contains("create phase", keywords);
        Assert.Contains("blocker", keywords);
        Assert.Contains("manifest", keywords);
        Assert.Contains("search", keywords);
        Assert.Contains("recommend", keywords);
        Assert.Contains("explain", keywords);
    }

    [Fact]
    public void GetRules_EveryRuleHasValidOperationName()
    {
        var validOps = OperationManifestCatalog.GetAll()
            .Select(e => e.OperationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in ToolRoutingCatalog.GetRules())
        {
            Assert.True(validOps.Contains(rule.OperationName),
                $"Routing rule '{rule.IntentKeyword}' → '{rule.OperationName}' points to unknown operation.");
        }
    }

    [Fact]
    public void GetRules_ConfidenceInRange()
    {
        foreach (var rule in ToolRoutingCatalog.GetRules())
        {
            Assert.True(rule.Confidence >= 0.0 && rule.Confidence <= 1.0,
                $"Rule '{rule.IntentKeyword}' has confidence {rule.Confidence} out of [0,1].");
        }
    }

    // -- Determinism --------------------------------------------------------------

    [Fact]
    public void GetRules_IsDeterministic()
    {
        var r1 = ToolRoutingCatalog.GetRules();
        var r2 = ToolRoutingCatalog.GetRules();
        Assert.Equal(r1.Count, r2.Count);
        for (int i = 0; i < r1.Count; i++)
        {
            Assert.Equal(r1[i].IntentKeyword, r2[i].IntentKeyword);
            Assert.Equal(r1[i].OperationName, r2[i].OperationName);
            Assert.Equal(r1[i].Confidence, r2[i].Confidence);
            Assert.Equal(r1[i].IsPrimary, r2[i].IsPrimary);
        }
    }

    [Fact]
    public void Recommend_IsDeterministic_SameInputSameOutput()
    {
        var r1 = ToolRoutingCatalog.Recommend("I need to get the current workflow state");
        var r2 = ToolRoutingCatalog.Recommend("I need to get the current workflow state");
        Assert.Equal(r1.Recommendations.Count, r2.Recommendations.Count);
        Assert.Equal(r1.Warnings.Count, r2.Warnings.Count);
        for (int i = 0; i < r1.Recommendations.Count; i++)
        {
            Assert.Equal(r1.Recommendations[i].OperationName, r2.Recommendations[i].OperationName);
        }
    }

    // -- Search -------------------------------------------------------------------

    [Fact]
    public void Search_ByKeyword_ReturnsMatchingRules()
    {
        var results = ToolRoutingCatalog.Search("blocker");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.OperationName == WorkflowOperations.RecordBlocker);
    }

    [Fact]
    public void Search_UnknownKeyword_ReturnsEmpty()
    {
        var results = ToolRoutingCatalog.Search("xyznonexistent123");
        Assert.Empty(results);
    }

    // -- Recommend ----------------------------------------------------------------

    [Fact]
    public void Recommend_GetState_ReturnsPrimaryMatch()
    {
        var result = ToolRoutingCatalog.Recommend("get the workflow state");
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains(result.Recommendations,
            r => r.OperationName == WorkflowOperations.GetState);
    }

    [Fact]
    public void Recommend_CreatePhase_ReturnsPrimaryMatch()
    {
        var result = ToolRoutingCatalog.Recommend("create a new phase");
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains(result.Recommendations,
            r => r.OperationName == WorkflowOperations.CreatePhase);
    }

    [Fact]
    public void Recommend_NoMatch_ReturnsFallback()
    {
        var result = ToolRoutingCatalog.Recommend("flummox the widgets");
        Assert.NotEmpty(result.Recommendations);
        // Fallback should be get_context_bundle
        Assert.Contains(result.Recommendations,
            r => r.OperationName == WorkflowOperations.GetContextBundle);
    }

    // -- Safety warnings ---------------------------------------------------------

    [Fact]
    public void Recommend_WriteOperation_HasWarning()
    {
        var result = ToolRoutingCatalog.Recommend("create a new phase");
        var writeEntry = result.Recommendations.FirstOrDefault(
            r => r.AccessLevel == OperationAccessLevel.Write);
        if (writeEntry is not null)
        {
            Assert.NotNull(writeEntry.AccessWarning);
            Assert.Contains("Write", writeEntry.AccessWarning);
        }
    }

    [Fact]
    public void Recommend_ReadOperation_HasNoWarning()
    {
        var result = ToolRoutingCatalog.Recommend("get the workflow state");
        var readEntries = result.Recommendations.Where(
            r => r.AccessLevel == OperationAccessLevel.Read);
        foreach (var entry in readEntries)
        {
            Assert.Null(entry.AccessWarning);
        }
    }

    [Fact]
    public void Recommend_DangerousIntent_EditFile_ReturnsWarning()
    {
        var result = ToolRoutingCatalog.Recommend("edit file workflow.json to change the phase");
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("dangerous"));
        Assert.NotNull(result.SaferAlternative);
    }

    [Fact]
    public void Recommend_DangerousIntent_RewriteLog_ReturnsWarning()
    {
        var result = ToolRoutingCatalog.Recommend("rewrite log files directly");
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("corrupt"));
        Assert.NotNull(result.SaferAlternative);
    }

    [Fact]
    public void Recommend_DangerousIntent_DeleteRecord_ReturnsWarning()
    {
        var result = ToolRoutingCatalog.Recommend("delete record from implementation log");
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("not allowed"));
        Assert.NotNull(result.SaferAlternative);
    }

    [Fact]
    public void Recommend_SafeIntent_HasNoDangerWarnings()
    {
        var result = ToolRoutingCatalog.Recommend("get the current phase");
        var dangerWarnings = result.Warnings.Where(w => w.Contains("⚠"));
        Assert.Empty(dangerWarnings);
    }

    // -- Explain -----------------------------------------------------------------

    [Fact]
    public void Explain_KnownOperation_ReturnsManifestEntry()
    {
        var result = Dispatch(WorkflowOperations.ExplainOperation,
            new() { ["operation"] = WorkflowOperations.GetState });
        Assert.True(result.Success);
        var entry = Assert.IsType<OperationManifestEntry>(result.Data);
        Assert.Equal(WorkflowOperations.GetState, entry.OperationName);
    }

    [Fact]
    public void Explain_UnknownOperation_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.ExplainOperation,
            new() { ["operation"] = "workflow.does_not_exist" });
        Assert.False(result.Success);
        Assert.Equal("NotFound", result.ErrorCode);
    }

    [Fact]
    public void Explain_MissingParameter_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.ExplainOperation);
        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
    }

    // -- Dispatcher registration -------------------------------------------------

    [Fact]
    public void SearchOperations_IsRegistered()
    {
        Assert.Contains(WorkflowOperations.SearchOperations, _dispatcher.RegisteredOperations);
    }

    [Fact]
    public void RecommendOperation_IsRegistered()
    {
        Assert.Contains(WorkflowOperations.RecommendOperation, _dispatcher.RegisteredOperations);
    }

    [Fact]
    public void ExplainOperation_IsRegistered()
    {
        Assert.Contains(WorkflowOperations.ExplainOperation, _dispatcher.RegisteredOperations);
    }

    // -- Search via dispatcher ----------------------------------------------------

    [Fact]
    public void SearchOperations_ReturnsMatches()
    {
        var result = Dispatch(WorkflowOperations.SearchOperations,
            new() { ["keyword"] = "blocker" });
        Assert.True(result.Success, result.Message);
        var matches = Assert.IsAssignableFrom<IReadOnlyList<OperationManifestEntry>>(result.Data);
        Assert.NotEmpty(matches);
        Assert.Contains(matches, m => m.OperationName == WorkflowOperations.RecordBlocker);
    }

    [Fact]
    public void SearchOperations_MissingParam_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.SearchOperations);
        Assert.False(result.Success);
    }

    // -- Recommend via dispatcher -------------------------------------------------

    [Fact]
    public void RecommendOperation_ReturnsRecommendation()
    {
        var result = Dispatch(WorkflowOperations.RecommendOperation,
            new() { ["task"] = "get the workflow status" });
        Assert.True(result.Success, result.Message);
        var rec = Assert.IsType<OperationRecommendation>(result.Data);
        Assert.NotEmpty(rec.Recommendations);
    }

    [Fact]
    public void RecommendOperation_MissingParam_ReturnsFail()
    {
        var result = Dispatch(WorkflowOperations.RecommendOperation);
        Assert.False(result.Success);
    }

    // -- File export -------------------------------------------------------------

    [Fact]
    public void ExportToFile_CreatesRoutingRulesFile()
    {
        ToolRoutingCatalog.ExportToFile(_tempRoot);

        var path = WorkflowLayout.ToolRoutingRulesPath(_tempRoot);
        Assert.True(File.Exists(path));

        var json = File.ReadAllText(path);
        Assert.NotEmpty(json);

        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public void ExportToFile_ProducesSameRulesAsGetRules()
    {
        ToolRoutingCatalog.ExportToFile(_tempRoot);

        var path = WorkflowLayout.ToolRoutingRulesPath(_tempRoot);
        var json = File.ReadAllText(path);
        var fromFile = JsonSerializer.Deserialize<List<ToolRoutingRule>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(fromFile);
        var inMemory = ToolRoutingCatalog.GetRules();
        Assert.Equal(inMemory.Count, fromFile.Count);

        for (int i = 0; i < inMemory.Count; i++)
        {
            Assert.Equal(inMemory[i].IntentKeyword, fromFile[i].IntentKeyword);
            Assert.Equal(inMemory[i].OperationName, fromFile[i].OperationName);
        }
    }

    // -- WorkflowLayout path -----------------------------------------------------

    [Fact]
    public void WorkflowLayout_ToolRoutingRulesPath_EndsWithJson()
    {
        var path = WorkflowLayout.ToolRoutingRulesPath(_tempRoot);
        Assert.EndsWith("tool-routing-rules.json", path);
    }
}
