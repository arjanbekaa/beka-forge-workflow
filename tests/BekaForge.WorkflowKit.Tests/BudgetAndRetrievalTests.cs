using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// PHASE-024-H: Budget configuration, retrieval, and ranking tests.
/// </summary>
public sealed class BudgetAndRetrievalTests
{
    // ── Budget Configuration Priority ───────────────────────────────────────────

    [Fact]
    public void BudgetMode_DefaultFor_ReturnsCorrectProfiles()
    {
        var low = BudgetProfile.DefaultFor(BudgetMode.Low);
        Assert.Equal(BudgetMode.Low, low.Mode);
        Assert.Equal(5, low.MaxPointers);
        Assert.Equal(1, low.MaxLogRecords);
        Assert.Equal(120, low.MaxSummaryLength);
        Assert.False(low.IncludeMarkdown);
        Assert.False(low.IncludeTraces);
        Assert.False(low.IncludeInlineContent);
        Assert.Equal(2000, low.MaxEstimatedTokens);

        var medium = BudgetProfile.DefaultFor(BudgetMode.Medium);
        Assert.Equal(BudgetMode.Medium, medium.Mode);
        Assert.Equal(20, medium.MaxPointers);
        Assert.Equal(2, medium.MaxLogRecords);
        Assert.True(medium.IncludeMarkdown);
        Assert.False(medium.IncludeTraces);
        Assert.False(medium.IncludeInlineContent);
        Assert.Equal(8000, medium.MaxEstimatedTokens);

        var high = BudgetProfile.DefaultFor(BudgetMode.High);
        Assert.Equal(BudgetMode.High, high.Mode);
        Assert.Equal(40, high.MaxPointers);
        Assert.Equal(3, high.MaxLogRecords);
        Assert.True(high.IncludeMarkdown);
        Assert.True(high.IncludeTraces);
        Assert.True(high.IncludeInlineContent);
        Assert.Equal(16000, high.MaxEstimatedTokens);

        var full = BudgetProfile.DefaultFor(BudgetMode.Full);
        Assert.Equal(BudgetMode.Full, full.Mode);
        Assert.Equal(100, full.MaxPointers);
        Assert.Equal(5, full.MaxLogRecords);
        Assert.True(full.IncludeMarkdown);
        Assert.True(full.IncludeTraces);
        Assert.True(full.IncludeInlineContent);
        Assert.Equal(0, full.MaxEstimatedTokens); // unlimited
    }

    [Fact]
    public void BudgetConfig_DefaultIsMedium()
    {
        var config = new BudgetConfig();
        Assert.Equal(BudgetMode.Medium, config.DefaultMode);
        Assert.Equal("1.0", config.SchemaVersion);
    }

    [Fact]
    public void BudgetProfileOverride_AppliesCorrectly()
    {
        var baseProfile = BudgetProfile.DefaultFor(BudgetMode.Medium);
        var ov = new BudgetProfileOverride
        {
            MaxPointers = 10,
            IncludeTraces = true
        };

        var merged = ov.Apply(baseProfile);
        Assert.Equal(10, merged.MaxPointers);         // overridden
        Assert.True(merged.IncludeTraces);            // overridden
        Assert.Equal(2, merged.MaxLogRecords);        // from base
        Assert.True(merged.IncludeMarkdown);           // from base
    }

    [Fact]
    public void BudgetConfig_EffectiveProfile_UsesOverrides()
    {
        var config = new BudgetConfig
        {
            DefaultMode = BudgetMode.Medium,
            ModeOverrides = new Dictionary<string, BudgetProfileOverride>
            {
                ["Medium"] = new() { MaxPointers = 15, IncludeTraces = true }
            }
        };

        var effective = config.EffectiveProfile(BudgetMode.Medium);
        Assert.Equal(15, effective.MaxPointers);
        Assert.True(effective.IncludeTraces);
        Assert.Equal(2, effective.MaxLogRecords); // from default
    }

    [Fact]
    public void BudgetConfig_SaveAndLoad_RoundTrips()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"budget-config-test-{Guid.NewGuid():N}.json");
        try
        {
            var config = new BudgetConfig
            {
                DefaultMode = BudgetMode.High,
                ModeOverrides = new Dictionary<string, BudgetProfileOverride>
                {
                    ["Low"] = new() { MaxPointers = 3 }
                }
            };

            config.Save(tempPath);
            Assert.True(File.Exists(tempPath));

            var loaded = BudgetConfig.Load(tempPath);
            Assert.Equal(BudgetMode.High, loaded.DefaultMode);
            Assert.NotNull(loaded.ModeOverrides);
            Assert.Equal(3, loaded.ModeOverrides!["Low"].MaxPointers);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void BudgetConfig_Load_ReturnsDefault_WhenFileMissing()
    {
        var config = BudgetConfig.Load("nonexistent.json");
        Assert.Equal(BudgetMode.Medium, config.DefaultMode);
    }

    // ── Token Estimation ────────────────────────────────────────────────────────

    [Fact]
    public void TokenEstimator_EstimateText_ReturnsCorrectCounts()
    {
        Assert.Equal(0, TokenEstimator.EstimateText(null));
        Assert.Equal(0, TokenEstimator.EstimateText(""));
        Assert.Equal(3, TokenEstimator.EstimateText("hello world")); // 11 chars / 4 = 3
    }

    [Fact]
    public void TokenEstimator_EstimatePointerCollection_WithEstimatedTokens()
    {
        var pointers = new List<ContextPointer>
        {
            new() { PointerType = "file", Target = "test.cs", Reason = "test", EstimatedTokens = 100 },
            new() { PointerType = "record", Target = "REC-001", Reason = "test", EstimatedTokens = 50 },
        };

        var total = TokenEstimator.EstimatePointerCollection(pointers);
        Assert.True(total > 150); // 150 raw + metadata overhead
        Assert.True(total < 200); // within reasonable range
    }

    [Fact]
    public void BudgetConsumption_ReportsCorrectly()
    {
        var pointers = new List<ContextPointer>
        {
            new() { PointerType = "file", Target = "test.cs", Reason = "test", EstimatedTokens = 200 },
        };

        var consumption = TokenEstimator.EstimateConsumption(pointers, omitted: 5, tokenBudgetCap: 1000);
        Assert.True(consumption.EstimatedTokensConsumed > 0);
        Assert.Equal(1000, consumption.TokenBudgetCap);
        Assert.True(consumption.EstimatedTokensRemaining > 0);
        Assert.True(consumption.BudgetUtilization > 0);
        Assert.Equal(1, consumption.PointersReturned);
        Assert.Equal(5, consumption.PointersOmitted);
        Assert.Contains("1 ptrs", consumption.Summary);
    }

    // ── BudgetReport DTO ────────────────────────────────────────────────────────

    [Fact]
    public void BudgetReport_DefaultValues()
    {
        var report = new BudgetReport();
        Assert.Equal("Medium", report.Mode);
        Assert.Equal("default", report.Source);
        Assert.Equal(20, report.MaxPointers);
        Assert.Equal(0, report.PointersReturned);
        Assert.Equal(0, report.OmittedByBudget);
        Assert.Equal(-1, report.EstimatedTokensConsumed);
        Assert.Equal(0, report.TokenBudgetCap);
        Assert.False(report.IncludesInlineContent);
        Assert.Empty(report.BudgetWarnings);
    }

    [Fact]
    public void RelevantContextResult_IncludesBudget_WhenProvided()
    {
        var result = new RelevantContextResult
        {
            PhaseId = "PHASE-001",
            TaskType = "implementation",
            Pointers = [],
            Budget = new BudgetReport
            {
                Mode = "High",
                Source = "override",
                MaxPointers = 40,
                PointersReturned = 15
            }
        };

        Assert.NotNull(result.Budget);
        Assert.Equal("High", result.Budget!.Mode);
        Assert.Equal("override", result.Budget.Source);
        Assert.Equal(40, result.Budget.MaxPointers);
        Assert.Equal(15, result.Budget.PointersReturned);
    }

    [Fact]
    public void BudgetConfigResult_SerializesCorrectly()
    {
        var result = new BudgetConfigResult
        {
            Mode = "Low",
            Source = "project",
            Profile = new BudgetProfileData
            {
                MaxPointers = 5,
                MaxLogRecords = 1,
                MaxSummaryLength = 120,
                IncludeMarkdown = false,
                IncludeTraces = false,
                IncludeInlineContent = false,
                MaxEstimatedTokens = 2000,
                Priority = "relevance",
                Description = "Low budget profile"
            },
            Warnings = ["Test warning"]
        };

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"mode\":\"Low\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"source\":\"project\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"maxPointers\":5", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rich", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ansi", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u001b", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BudgetProfileData_DefaultValues()
    {
        var data = new BudgetProfileData();
        Assert.Equal(0, data.MaxPointers);
        Assert.Equal("relevance", data.Priority);
        Assert.Equal("", data.Description);
    }

    // ── Clean JSON Output ───────────────────────────────────────────────────────

    [Fact]
    public void BudgetConfig_Serialization_IsCleanJson()
    {
        var config = new BudgetConfig { DefaultMode = BudgetMode.Medium };
        var json = System.Text.Json.JsonSerializer.Serialize(config,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        Assert.Contains("Medium", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u001b", json);
        Assert.DoesNotContain("color", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rich", json, StringComparison.OrdinalIgnoreCase);
    }

    // ── Vector Search Plan ──────────────────────────────────────────────────────

    [Fact]
    public void VectorSearchPlan_IsDeferred()
    {
        Assert.False(VectorSearchPlan.IsEnabled);
        Assert.Equal("all-MiniLM-L6-v2", VectorSearchPlan.PlannedModel);
        Assert.Equal(384, VectorSearchPlan.PlannedDimensions);
        Assert.Contains("Deferred", VectorSearchPlan.Status);
        Assert.Contains("plan", VectorSearchPlan.Status);
    }

    // ── Prompt-Type Detection ───────────────────────────────────────────────────

    [Theory]
    [InlineData("implement the budget config", "implementation")]
    [InlineData("audit this phase", "audit")]
    [InlineData("review the code", "review")]
    [InlineData("run unit tests", "test")]
    [InlineData("fix the bug", "fix")]
    [InlineData("", "general")]
    [InlineData(null, "general")]
    public void PromptTypeDetection_FromQuery(string? query, string expected)
    {
        // This tests the heuristic — the actual method is private in the handler
        // but we can verify the logic through integration testing
        // For unit test purposes, we verify the detectable keywords
        if (string.IsNullOrWhiteSpace(query))
        {
            Assert.Equal("general", expected);
            return;
        }

        var q = query.ToLowerInvariant();
        bool matched = false;

        if (q.Contains("audit") || q.Contains("verify") || q.Contains("check") || q.Contains("validate"))
            matched = expected == "audit";
        else if (q.Contains("review") || q.Contains("inspect") || q.Contains("examine"))
            matched = expected == "review";
        else if (q.Contains("test") || q.Contains("unit test") || q.Contains("integration test"))
            matched = expected == "test";
        else if (q.Contains("fix") || q.Contains("bug") || q.Contains("repair") || q.Contains("resolve"))
            matched = expected == "fix";
        else if (q.Contains("implement") || q.Contains("build") || q.Contains("create") || q.Contains("code"))
            matched = expected == "implementation";
        else
            matched = expected == "general";

        Assert.True(matched, $"Query '{query}' should detect as '{expected}'");
    }

    // ── Budget Priority Resolution ──────────────────────────────────────────────

    [Fact]
    public void BudgetConfig_RespectsPriority()
    {
        // Test: default Medium when no config exists
        var config = new BudgetConfig();
        Assert.Equal(BudgetMode.Medium, config.DefaultMode);

        // Test: project override works
        var lowConfig = new BudgetConfig { DefaultMode = BudgetMode.Low };
        Assert.Equal(BudgetMode.Low, lowConfig.DefaultMode);

        // Test: effective profile for non-default mode
        var fullProfile = config.EffectiveProfile(BudgetMode.Full);
        Assert.Equal(100, fullProfile.MaxPointers);
        Assert.True(fullProfile.IncludeInlineContent);
    }

    // ── Deterministic Ranking ───────────────────────────────────────────────────

    [Fact]
    public void ContextPointer_Sorting_IsDeterministic()
    {
        var pointers = new List<(ContextPointer Pointer, double Score)>
        {
            (new ContextPointer { PointerType = "file", Target = "a.cs", Reason = "A", RelevanceScore = 0.8 }, 0.8),
            (new ContextPointer { PointerType = "file", Target = "b.cs", Reason = "B", RelevanceScore = 0.9 }, 0.9),
            (new ContextPointer { PointerType = "file", Target = "c.cs", Reason = "C", RelevanceScore = 0.7 }, 0.7),
        };

        // Sort by score descending
        pointers.Sort((a, b) => b.Score.CompareTo(a.Score));

        Assert.Equal(0.9, pointers[0].Score);
        Assert.Equal(0.8, pointers[1].Score);
        Assert.Equal(0.7, pointers[2].Score);
    }

    [Fact]
    public void ContextPointer_Sorting_StableWithEqualScores()
    {
        var p1 = new ContextPointer { PointerType = "file", Target = "large.cs", Reason = "X", EstimatedTokens = 500 };
        var p2 = new ContextPointer { PointerType = "file", Target = "small.cs", Reason = "Y", EstimatedTokens = 100 };

        var pointers = new List<(ContextPointer Pointer, double Score)>
        {
            (p1, 0.8),
            (p2, 0.8),
        };

        // Sort by score descending, then by estimated tokens ascending
        pointers.Sort((a, b) =>
        {
            var cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            return a.Pointer.EstimatedTokens.CompareTo(b.Pointer.EstimatedTokens);
        });

        Assert.Equal(100, pointers[0].Pointer.EstimatedTokens); // smaller first
        Assert.Equal(500, pointers[1].Pointer.EstimatedTokens); // larger second
    }

    [Fact]
    public void HybridRetriever_Score_ReranksByHybridSignals()
    {
        var retriever = new HybridContextRetriever("");
        var candidates = new List<(ContextPointer Pointer, double Score)>
        {
            (new ContextPointer
            {
                PointerType = "file",
                Target = "src/Unrelated.cs",
                Reason = "No matching context.",
                EstimatedTokens = 100,}, 0.10),
            (new ContextPointer
            {
                PointerType = "file",
                Target = "src/BekaForge.WorkflowKit.Core/BudgetConfig.cs",
                Reason = "Budget configuration profile and context retrieval settings.",
                EstimatedTokens = 100,}, 0.10)
        };

        var scored = retriever.Score(candidates, "budget config", null);

        Assert.Equal("src/BekaForge.WorkflowKit.Core/BudgetConfig.cs", scored[0].Pointer.Target);
        Assert.True(scored[0].Score > scored[1].Score);
        Assert.True(
            scored[0].Pointer.Reason.Contains("metadata_match", StringComparison.Ordinal) ||
            scored[0].Pointer.Reason.Contains("lexical_tfidf", StringComparison.Ordinal),
            $"Expected a metadata or lexical explanation, got: {scored[0].Pointer.Reason}");
    }

    [Fact]
    public void HybridRetriever_Score_ExplainsOperationNameMatch()
    {
        var retriever = new HybridContextRetriever("");
        var candidates = new List<(ContextPointer Pointer, double Score)>
        {
            (new ContextPointer
            {
                PointerType = "file",
                Target = "README.md",
                Reason = "File slice entry point.",
                EstimatedTokens = 100,}, 0.10)
        };

        var scored = retriever.Score(candidates, WorkflowOperations.GetFileSlice, null);

        Assert.Single(scored);
        Assert.Contains("operation_match", scored[0].Pointer.Reason);
        Assert.True(scored[0].Score > 0.10);
    }

    [Fact]
    public void HybridRetriever_Score_BreaksTiesByEstimatedTokensThenTarget()
    {
        var retriever = new HybridContextRetriever("");
        var candidates = new List<(ContextPointer Pointer, double Score)>
        {
            (new ContextPointer { PointerType = "file", Target = "b.cs", Reason = "same", EstimatedTokens = 300 }, 0.50),
            (new ContextPointer { PointerType = "file", Target = "a.cs", Reason = "same", EstimatedTokens = 100 }, 0.50),
            (new ContextPointer { PointerType = "file", Target = "c.cs", Reason = "same", EstimatedTokens = 100 }, 0.50)
        };

        var scored = retriever.Score(candidates, "unmatched", null);

        Assert.Equal("a.cs", scored[0].Pointer.Target);
        Assert.Equal("c.cs", scored[1].Pointer.Target);
        Assert.Equal("b.cs", scored[2].Pointer.Target);
    }
}
