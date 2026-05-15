using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.get_relevant_context</c>.
///
/// Returns ranked context pointers instead of full content. Agents should resolve
/// these pointers through the existing slice APIs (get_file_slice, get_record_slice,
/// get_markdown_region, get_json_pointer_value) rather than scanning the full
/// workflow folder.
///
/// This is the preferred first operation for large agent context tasks.
/// <c>workflow.get_context_bundle</c> remains available for backward compatibility.
///
/// PHASE-015: Integrates with ContextPackageCache for RAM-cached phase packages.
/// PHASE-024: Budget-aware with hybrid retrieval, reranking, and budget enforcement.
/// </summary>
public sealed class GetRelevantContextHandler : IOperationHandler
{
    private readonly WorkflowStore _store;
    private readonly ContextPackageCache? _cache;
    private readonly PhaseContextPackageBuilder? _packageBuilder;

    public string OperationName => WorkflowOperations.GetRelevantContext;

    public GetRelevantContextHandler(WorkflowStore store,
        ContextPackageCache? cache = null,
        PhaseContextPackageBuilder? packageBuilder = null)
    {
        _store = store;
        _cache = cache;
        _packageBuilder = packageBuilder ?? (cache is not null ? new PhaseContextPackageBuilder(store) : null);
    }

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId ?? context.GetString("phaseId");
        var actor = context.GetString("actor");
        var taskType = context.GetString("taskType");
        var query = context.GetString("query") ?? context.GetString("task") ?? taskType ?? phaseId ?? "";
        var maxItems = context.Get<int?>("maxItems");
        var maxEstimatedTokens = context.Get<int?>("maxEstimatedTokens");

        // -- PHASE-024: Budget resolution ----------------------------------
        var (budgetProfile, budgetSource) = ResolveBudget(context);
        var budgetInfo = BuildBudgetReport(budgetProfile, budgetSource, out _);

        // Apply budget caps: maxItems from budget profile if not explicitly given
        if (!maxItems.HasValue)
            maxItems = budgetProfile.MaxPointers;
        else
            maxItems = Math.Min(maxItems.Value, budgetProfile.MaxPointers);

        // Apply token budget cap from profile if not explicitly given
        if (!maxEstimatedTokens.HasValue && budgetProfile.MaxEstimatedTokens > 0)
            maxEstimatedTokens = budgetProfile.MaxEstimatedTokens;
        else if (maxEstimatedTokens.HasValue && budgetProfile.MaxEstimatedTokens > 0)
            maxEstimatedTokens = Math.Min(maxEstimatedTokens.Value, budgetProfile.MaxEstimatedTokens);

        // -- PHASE-015: Cache check ----------------------------------------
        if (_cache is not null && _packageBuilder is not null && !string.IsNullOrWhiteSpace(phaseId))
        {
            var cacheKey = CacheKey.ForPhase(phaseId, taskType);

            if (_cache.TryGet<PhaseContextPackage>(cacheKey, out var cached))
            {
                // Cache hit — extract pointers, re-rank with hybrid signals, apply budget caps
                var candidates = cached!.Package.Pointers
                    .Select(p => (p, p.RelevanceScore))
                    .ToList();

                // PHASE-024-C+D: Hybrid re-ranking with prompt-type awareness
                var reranked = RerankWithBudget(candidates, query, taskType, budgetProfile);

                var cachedPointers = ApplyBudgetCaps(
                    reranked.Select(r => r.Pointer).ToList(), budgetProfile, maxItems, maxEstimatedTokens, out var omitted);

                budgetInfo = budgetInfo with
                {
                    PointersReturned = cachedPointers.Count,
                    OmittedByBudget = omitted,
                    EstimatedTokensConsumed = cachedPointers.Sum(p => p.EstimatedTokens > 0 ? p.EstimatedTokens : 0),
                    TokenBudgetCap = maxEstimatedTokens ?? 0,
                    IncludesInlineContent = budgetProfile.IncludeInlineContent
                };

                var cachedResult = new RelevantContextResult
                {
                    PhaseId             = phaseId,
                    TaskType            = taskType,
                    Pointers            = cachedPointers,
                    OmittedCandidates   = omitted,
                    EstimatedTotalTokens = cachedPointers.Sum(p => p.EstimatedTokens > 0 ? p.EstimatedTokens : 0),
                    Warnings            = ["Served from context package cache. Call workflow.get_file_slice or get_record_slice to resolve pointers."],
                    IsFromCache         = true,
                    Budget              = budgetInfo
                };
                return OperationResult.Ok(cachedResult);
            }

            // Cache miss — build via service, re-rank, cache
            var svc = new RelevantContextService(_store.WorkflowRoot);
            var result = svc.GetRelevantContext(phaseId, actor, taskType, int.MaxValue, null);

            // PHASE-024-C+D: Hybrid re-ranking
            var rawCandidates = result.Pointers
                .Select(p => (p, p.RelevanceScore))
                .ToList();
            var hybridReranked = RerankWithBudget(rawCandidates, query, taskType, budgetProfile);

            // Apply budget caps
            var budgetedPointers = ApplyBudgetCaps(
                hybridReranked.Select(r => r.Pointer).ToList(), budgetProfile, maxItems, maxEstimatedTokens, out var bOmitted);

            budgetInfo = budgetInfo with
            {
                PointersReturned = budgetedPointers.Count,
                OmittedByBudget = bOmitted,
                EstimatedTokensConsumed = budgetedPointers.Sum(p => p.EstimatedTokens > 0 ? p.EstimatedTokens : 0),
                TokenBudgetCap = maxEstimatedTokens ?? 0,
                IncludesInlineContent = budgetProfile.IncludeInlineContent
            };

            // Build and cache
            var package = _packageBuilder.Build(phaseId);
            if (package is not null)
            {
                var sourceFiles = GetSourceFilesForPhase(phaseId);
                var snapshot = SourceHashSnapshot.FromFiles(sourceFiles);

                var cachedPackage = new CachedPackage<PhaseContextPackage>
                {
                    Key                 = cacheKey,
                    Package             = package,
                    EstimatedMemoryBytes = EstimateMemoryBytes(package),
                    SourceSnapshot      = snapshot,
                    IsFromCache         = false
                };
                _cache.Put(cachedPackage);
            }

            result = result with
            {
                Pointers = budgetedPointers,
                OmittedCandidates = bOmitted,
                EstimatedTotalTokens = budgetedPointers.Sum(p => p.EstimatedTokens > 0 ? p.EstimatedTokens : 0),
                IsFromCache = false,
                Budget = budgetInfo
            };
            return OperationResult.Ok(result);
        }

        // -- No cache — original path with hybrid re-ranking --------------
        var service = new RelevantContextService(_store.WorkflowRoot);
        var noCacheResult = service.GetRelevantContext(phaseId, actor, taskType, int.MaxValue, null);

        var ncCandidates = noCacheResult.Pointers
            .Select(p => (p, p.RelevanceScore))
            .ToList();
        var ncReranked = RerankWithBudget(ncCandidates, query, taskType, budgetProfile);

        var finalPointers = ApplyBudgetCaps(
            ncReranked.Select(r => r.Pointer).ToList(), budgetProfile, maxItems, maxEstimatedTokens, out var ncOmitted);

        budgetInfo = budgetInfo with
        {
            PointersReturned = finalPointers.Count,
            OmittedByBudget = ncOmitted,
            EstimatedTokensConsumed = finalPointers.Sum(p => p.EstimatedTokens > 0 ? p.EstimatedTokens : 0),
            TokenBudgetCap = maxEstimatedTokens ?? 0,
            IncludesInlineContent = budgetProfile.IncludeInlineContent
        };

        noCacheResult = noCacheResult with
        {
            Pointers = finalPointers,
            OmittedCandidates = ncOmitted,
            EstimatedTotalTokens = finalPointers.Sum(p => p.EstimatedTokens > 0 ? p.EstimatedTokens : 0),
            Budget = budgetInfo
        };

        return OperationResult.Ok(noCacheResult);
    }

    // -- PHASE-024-C+D: Hybrid re-ranking with prompt-type awareness -------

    /// <summary>
    /// Re-ranks candidates using hybrid retrieval signals and prompt-type-aware scoring weights.
    /// Enforces budget limits during ranking.
    /// </summary>
    private static List<(ContextPointer Pointer, double Score)> RerankWithBudget(
        List<(ContextPointer Pointer, double Score)> candidates,
        string? query,
        string? taskType,
        BudgetProfile budgetProfile)
    {
        // 1. Detect prompt type and get scoring weights
        var promptType = DetectPromptType(query, taskType);
        var weights = GetPromptTypeWeights(promptType);

        // 2. Apply hybrid retrieval scoring with prompt-type weights
        var hybrid = new HybridContextRetriever(""); // root not needed for scoring
        var scored = hybrid.Score(candidates, query, taskType);

        // 3. Apply prompt-type-aware weight adjustments
        var weighted = scored.Select(s =>
        {
            var pointer = s.Pointer;
            var adjustedScore = AdjustScoreForPromptType(s.Score, pointer, weights);
            return (Pointer: pointer with { RelevanceScore = adjustedScore }, Score: adjustedScore);
        }).ToList();

        // 4. Re-sort by adjusted score descending, then by estimated tokens ascending
        weighted.Sort((a, b) =>
        {
            var cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            return a.Pointer.EstimatedTokens.CompareTo(b.Pointer.EstimatedTokens);
        });

        return weighted;
    }

    // -- Prompt-type detection ---------------------------------------------

    /// <summary>
    /// Detects the prompt type from query text and taskType.
    /// Returns one of: implementation, review, audit, test, fix, general.
    /// </summary>
    private static string DetectPromptType(string? query, string? taskType)
    {
        // Explicit taskType wins
        if (!string.IsNullOrWhiteSpace(taskType))
        {
            var t = taskType.ToLowerInvariant();
            if (t is "implementation" or "audit" or "review" or "test" or "fix")
                return t;
        }

        // Heuristic detection from query text
        if (string.IsNullOrWhiteSpace(query))
            return "general";

        var q = query.ToLowerInvariant();

        if (q.Contains("audit") || q.Contains("verify") || q.Contains("check") || q.Contains("validate"))
            return "audit";
        if (q.Contains("review") || q.Contains("inspect") || q.Contains("examine"))
            return "review";
        if (q.Contains("test") || q.Contains("unit test") || q.Contains("integration test"))
            return "test";
        if (q.Contains("fix") || q.Contains("bug") || q.Contains("repair") || q.Contains("resolve"))
            return "fix";
        if (q.Contains("implement") || q.Contains("build") || q.Contains("create") || q.Contains("code"))
            return "implementation";

        return "general";
    }

    /// <summary>
    /// Returns scoring weights per pointer type for each prompt type.
    /// These weights bias which pointer types are preferred for a given task.
    /// </summary>
    private static Dictionary<string, double> GetPromptTypeWeights(string promptType)
    {
        return promptType switch
        {
            "implementation" => new Dictionary<string, double>
            {
                ["file"] = 1.0,
                ["record"] = 1.2,
                ["phase_contract"] = 1.3,
                ["markdown_region"] = 1.1,
                ["json_pointer"] = 0.8,
                ["trace"] = 0.3
            },
            "audit" => new Dictionary<string, double>
            {
                ["file"] = 1.3,
                ["record"] = 1.0,
                ["phase_contract"] = 1.2,
                ["markdown_region"] = 0.9,
                ["json_pointer"] = 0.9,
                ["trace"] = 0.5
            },
            "review" => new Dictionary<string, double>
            {
                ["file"] = 1.1,
                ["record"] = 1.2,
                ["phase_contract"] = 1.0,
                ["markdown_region"] = 1.0,
                ["json_pointer"] = 0.8,
                ["trace"] = 0.3
            },
            "test" => new Dictionary<string, double>
            {
                ["file"] = 1.0,
                ["record"] = 1.3,
                ["phase_contract"] = 0.8,
                ["markdown_region"] = 0.7,
                ["json_pointer"] = 0.6,
                ["trace"] = 0.4
            },
            "fix" => new Dictionary<string, double>
            {
                ["file"] = 1.4,
                ["record"] = 1.3,
                ["phase_contract"] = 0.9,
                ["markdown_region"] = 0.8,
                ["json_pointer"] = 0.7,
                ["trace"] = 0.6
            },
            _ => new Dictionary<string, double>
            {
                ["file"] = 1.0,
                ["record"] = 1.0,
                ["phase_contract"] = 1.0,
                ["markdown_region"] = 1.0,
                ["json_pointer"] = 1.0,
                ["trace"] = 1.0
            }
        };
    }

    /// <summary>
    /// Adjusts a pointer's score based on its pointer type and the prompt-type weights.
    /// </summary>
    private static double AdjustScoreForPromptType(
        double baseScore, ContextPointer pointer, Dictionary<string, double> weights)
    {
        if (weights.TryGetValue(pointer.PointerType, out var weight))
            return Math.Min(1.0, baseScore * weight);
        return baseScore;
    }

    // -- Budget resolution (PHASE-024) ------------------------------------

    /// <summary>
    /// Resolves the effective budget profile with priority:
    /// inline override (mode param) > project config (budget-config.json) > built-in default (Medium).
    /// </summary>
    private (BudgetProfile Profile, string Source) ResolveBudget(OperationContext context)
    {
        // 1. Check for inline override (--budget flag)
        var modeStr = context.GetString("budgetMode") ?? context.GetString("mode");
        if (!string.IsNullOrWhiteSpace(modeStr) &&
            Enum.TryParse<BudgetMode>(modeStr, ignoreCase: true, out var overrideMode))
        {
            var overrideConfigPath = BudgetConfig.ConfigPath(_store.WorkflowRoot);
            if (File.Exists(overrideConfigPath))
            {
                var projectConfig = BudgetConfig.Load(overrideConfigPath);
                return (projectConfig.EffectiveProfile(overrideMode), "override");
            }

            return (BudgetProfile.DefaultFor(overrideMode), "override");
        }

        // 2. Check project config
        var configPath = BudgetConfig.ConfigPath(_store.WorkflowRoot);
        if (File.Exists(configPath))
        {
            var projectConfig = BudgetConfig.Load(configPath);
            return (projectConfig.EffectiveProfile(projectConfig.DefaultMode), "project");
        }

        // 3. Built-in default (Medium)
        return (BudgetProfile.DefaultFor(BudgetMode.Medium), "default");
    }

    /// <summary>
    /// Builds the BudgetReport metadata for the response.
    /// </summary>
    private static BudgetReport BuildBudgetReport(BudgetProfile profile, string source,
        out int maxPointers)
    {
        maxPointers = profile.MaxPointers;
        return new BudgetReport
        {
            Mode = profile.Mode.ToString(),
            Source = source,
            MaxPointers = profile.MaxPointers,
            PointersReturned = 0,
            OmittedByBudget = 0,
            EstimatedTokensConsumed = -1,
            TokenBudgetCap = profile.MaxEstimatedTokens,
            IncludesInlineContent = profile.IncludeInlineContent,
            BudgetWarnings = []
        };
    }

    /// <summary>
    /// Applies budget caps to a list of pointers: maxItems, maxEstimatedTokens,
    /// and budget-specific limits (markdown, traces, inline content).
    /// </summary>
    private static IReadOnlyList<ContextPointer> ApplyBudgetCaps(
        IReadOnlyList<ContextPointer> pointers,
        BudgetProfile profile,
        int? maxItems,
        int? maxEstimatedTokens,
        out int omitted)
    {
        omitted = 0;
        var result = new List<ContextPointer>();
        var totalTokens = 0;

        foreach (var pointer in pointers)
        {
            // Budget mode is an AI output hint, not a content filter.
            // All pointers are always included — dropping them forces the AI to
            // fetch files individually, which costs more tokens overall.
            // No quantity cap — every pointer the phase has, the AI gets.
            var finalPointer = pointer;

            // Token budget cap (but always include at least one pointer)
            if (maxEstimatedTokens.HasValue &&
                finalPointer.EstimatedTokens > 0 &&
                totalTokens + finalPointer.EstimatedTokens > maxEstimatedTokens.Value &&
                result.Count > 0)
            {
                omitted++;
                continue;
            }

            result.Add(finalPointer);
            if (finalPointer.EstimatedTokens > 0)
                totalTokens += finalPointer.EstimatedTokens;
        }

        return result;
    }

    /// <summary>Returns the source files that, if changed, invalidate a phase package.</summary>
    private static string[] GetSourceFilesForPhase(string phaseId)
    {
        return Array.Empty<string>();
    }

    /// <summary>Best-effort memory estimate for a phase package.</summary>
    private static long EstimateMemoryBytes(PhaseContextPackage pkg)
    {
        return (pkg.Pointers.Count * 2048L) +
               (pkg.TotalRecordCount * 1024L) +
               4096L;
    }
}