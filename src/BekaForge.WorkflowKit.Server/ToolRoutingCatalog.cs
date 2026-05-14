using BekaForge.WorkflowKit.AgentContracts;
using System.Text.Json;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// Code-owned catalog of routing rules that map agent intent keywords
/// to WorkflowKit operations. Each rule has a confidence score and
/// primary/alternative designation.
///
/// The generated .workflowkit/index/tool-routing-rules.json is a rebuildable
/// read model derived from this catalog -- not source of truth.
/// </summary>
public static class ToolRoutingCatalog
{
    /// <summary>
    /// Built-in routing rules. Every rule maps a lowercase intent keyword
    /// or short phrase to one or more operations with confidence scores.
    /// Primary matches use 1.0; alternatives use 0.5-0.7.
    /// </summary>
    public static IReadOnlyList<ToolRoutingRule> GetRules()
    {
        return
        [
            // -- State reads ----------------------------------------------------------
            new() { IntentKeyword = "get state",       OperationName = WorkflowOperations.GetState,         Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "status",          OperationName = WorkflowOperations.GetState,         Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "current phase",   OperationName = WorkflowOperations.GetCurrentPhase,  Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "what phase",      OperationName = WorkflowOperations.GetCurrentPhase,  Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "list phases",     OperationName = WorkflowOperations.ListPhases,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "all phases",      OperationName = WorkflowOperations.ListPhases,       Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "validate",        OperationName = WorkflowOperations.ValidateState,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "check consistency", OperationName = WorkflowOperations.ValidateState,  Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "dashboard",       OperationName = WorkflowOperations.GetDashboardSummary, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "summary",         OperationName = WorkflowOperations.GetDashboardSummary, Confidence = 0.7, IsPrimary = false },
            new() { IntentKeyword = "context",         OperationName = WorkflowOperations.GetContextBundle, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "bundle",          OperationName = WorkflowOperations.GetContextBundle, Confidence = 0.9, IsPrimary = false },

            // -- Relevant context (P0 -- most frequently called by agents) -----------
            new() { IntentKeyword = "relevant context", OperationName = WorkflowOperations.GetRelevantContext, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "get context",      OperationName = WorkflowOperations.GetRelevantContext, Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "what do i need",   OperationName = WorkflowOperations.GetRelevantContext, Confidence = 0.7, IsPrimary = false },

            // -- Phase management ----------------------------------------------------
            new() { IntentKeyword = "create phase",    OperationName = WorkflowOperations.CreatePhase,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "new phase",       OperationName = WorkflowOperations.CreatePhase,      Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "update phase",    OperationName = WorkflowOperations.UpdatePhase,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "change phase",    OperationName = WorkflowOperations.UpdatePhase,      Confidence = 0.7, IsPrimary = false },
            new() { IntentKeyword = "remove phase",    OperationName = WorkflowOperations.RemovePhase,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "delete phase",    OperationName = WorkflowOperations.RemovePhase,      Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "change status",   OperationName = WorkflowOperations.UpdatePhaseStatus, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "transition",      OperationName = WorkflowOperations.UpdatePhaseStatus, Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "assign",          OperationName = WorkflowOperations.AssignPhase,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "who works on",    OperationName = WorkflowOperations.AssignPhase,      Confidence = 0.6, IsPrimary = false },
            new() { IntentKeyword = "start phase",     OperationName = WorkflowOperations.StartPhase,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "begin",           OperationName = WorkflowOperations.StartPhase,       Confidence = 0.7, IsPrimary = false },
            new() { IntentKeyword = "complete implementation", OperationName = WorkflowOperations.CompleteImplementation, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "done implementing", OperationName = WorkflowOperations.CompleteImplementation, Confidence = 0.8, IsPrimary = false },

            // -- Phase contract -------------------------------------------------------
            new() { IntentKeyword = "contract",          OperationName = WorkflowOperations.GetPhaseContract,  Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "acceptance criteria", OperationName = WorkflowOperations.GetPhaseContract, Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "save contract",     OperationName = WorkflowOperations.SavePhaseContract, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "update contract",   OperationName = WorkflowOperations.SavePhaseContract, Confidence = 0.9, IsPrimary = false },

            // -- Next action ----------------------------------------------------------
            new() { IntentKeyword = "next action",     OperationName = WorkflowOperations.GetNextAction,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "what to do",      OperationName = WorkflowOperations.GetNextAction,     Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "set next action", OperationName = WorkflowOperations.SetNextAction,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "plan next",       OperationName = WorkflowOperations.SetNextAction,     Confidence = 0.8, IsPrimary = false },

            // -- Record creation -----------------------------------------------------
            new() { IntentKeyword = "log implementation",   OperationName = WorkflowOperations.CreateImplementationLog, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "record implementation", OperationName = WorkflowOperations.CreateImplementationLog, Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "log audit",        OperationName = WorkflowOperations.CreateAuditLog,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "self audit",       OperationName = WorkflowOperations.CreateAuditLog,    Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "log review",       OperationName = WorkflowOperations.CreateReviewLog,   Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "codex review",     OperationName = WorkflowOperations.CreateReviewLog,   Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "log test",         OperationName = WorkflowOperations.CreateTestLog,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "record test",      OperationName = WorkflowOperations.CreateTestLog,     Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "log fix",          OperationName = WorkflowOperations.CreateFixLog,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "record fix",       OperationName = WorkflowOperations.CreateFixLog,      Confidence = 0.9, IsPrimary = false },

            // -- Blockers -------------------------------------------------------------
            new() { IntentKeyword = "blocker",         OperationName = WorkflowOperations.RecordBlocker,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "blocked",         OperationName = WorkflowOperations.RecordBlocker,     Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "resolve",         OperationName = WorkflowOperations.ResolveBlocker,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "unblock",         OperationName = WorkflowOperations.ResolveBlocker,    Confidence = 0.9, IsPrimary = false },

            // -- Handoffs -------------------------------------------------------------
            new() { IntentKeyword = "handoff",         OperationName = WorkflowOperations.CreateHandoff,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "hand over",       OperationName = WorkflowOperations.CreateHandoff,     Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "get handoffs",    OperationName = WorkflowOperations.GetHandoffs,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "pending handoffs", OperationName = WorkflowOperations.GetHandoffs,      Confidence = 0.9, IsPrimary = false },

            // -- Timing / metrics ----------------------------------------------------
            new() { IntentKeyword = "time spent",      OperationName = WorkflowOperations.RecordTimeSpent,   Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "track time",      OperationName = WorkflowOperations.RecordTimeSpent,   Confidence = 0.9, IsPrimary = false },

            // -- Markdown sync -------------------------------------------------------
            new() { IntentKeyword = "sync markdown",   OperationName = WorkflowOperations.SyncMarkdown,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "regenerate docs", OperationName = WorkflowOperations.SyncMarkdown,      Confidence = 0.9, IsPrimary = false },

            // -- Operation manifest --------------------------------------------------
            new() { IntentKeyword = "manifest",        OperationName = WorkflowOperations.GetOperationManifest, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "operations list", OperationName = WorkflowOperations.GetOperationManifest, Confidence = 0.9, IsPrimary = false },

            // -- Tool routing --------------------------------------------------------
            new() { IntentKeyword = "search",          OperationName = WorkflowOperations.SearchOperations,   Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "find operation",  OperationName = WorkflowOperations.SearchOperations,   Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "recommend",       OperationName = WorkflowOperations.RecommendOperation, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "suggest",         OperationName = WorkflowOperations.RecommendOperation, Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "explain",         OperationName = WorkflowOperations.ExplainOperation,   Confidence = 1.0, IsPrimary = true },

            // -- Safety validation ---------------------------------------------------
            new() { IntentKeyword = "validate request", OperationName = WorkflowOperations.ValidateOperationRequest, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "is this safe",     OperationName = WorkflowOperations.ValidateOperationRequest, Confidence = 0.8, IsPrimary = false },

            // -- Context index -------------------------------------------------------
            new() { IntentKeyword = "rebuild index",   OperationName = WorkflowOperations.RebuildContextIndex, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "reindex",         OperationName = WorkflowOperations.RebuildContextIndex, Confidence = 0.9, IsPrimary = false },

            // -- Slice APIs ----------------------------------------------------------
            new() { IntentKeyword = "file slice",      OperationName = WorkflowOperations.GetFileSlice,        Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "read file",       OperationName = WorkflowOperations.GetFileSlice,        Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "record slice",    OperationName = WorkflowOperations.GetRecordSlice,      Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "find record",     OperationName = WorkflowOperations.GetRecordSlice,      Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "json pointer",    OperationName = WorkflowOperations.GetJsonPointerValue, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "markdown region", OperationName = WorkflowOperations.GetMarkdownRegion,   Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "file history",    OperationName = WorkflowOperations.GetFileHistory,      Confidence = 1.0, IsPrimary = true },

            // -- Budget configuration ------------------------------------------------
            new() { IntentKeyword = "budget config",   OperationName = WorkflowOperations.GetBudgetConfig,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "budget report",   OperationName = WorkflowOperations.GetBudgetReport,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "set budget",      OperationName = WorkflowOperations.SetBudgetConfig,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "token budget",    OperationName = WorkflowOperations.GetBudgetConfig,    Confidence = 0.8, IsPrimary = false },
            new() { IntentKeyword = "context budget",  OperationName = WorkflowOperations.GetBudgetConfig,    Confidence = 0.7, IsPrimary = false },

            // -- Tracing -------------------------------------------------------------
            new() { IntentKeyword = "trace status",    OperationName = WorkflowOperations.GetTraceStatus,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "list traces",     OperationName = WorkflowOperations.ListTraces,         Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "get trace",       OperationName = WorkflowOperations.GetTrace,           Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "set trace",       OperationName = WorkflowOperations.SetTraceOptions,    Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "trace options",   OperationName = WorkflowOperations.SetTraceOptions,    Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "clear traces",    OperationName = WorkflowOperations.ClearOldTraces,     Confidence = 1.0, IsPrimary = true },

            // -- Sessions ------------------------------------------------------------
            new() { IntentKeyword = "list sessions",   OperationName = WorkflowOperations.ListSessions,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "current session", OperationName = WorkflowOperations.GetCurrentSession,  Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "active session",  OperationName = WorkflowOperations.GetCurrentSession,  Confidence = 0.9, IsPrimary = false },
            new() { IntentKeyword = "end session",     OperationName = WorkflowOperations.EndSession,         Confidence = 1.0, IsPrimary = true },

            // -- Timeline ------------------------------------------------------------
            new() { IntentKeyword = "timeline",        OperationName = WorkflowOperations.GetTimeline,        Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "history",         OperationName = WorkflowOperations.GetTimeline,        Confidence = 0.7, IsPrimary = false },

            // -- Git -----------------------------------------------------------------
            new() { IntentKeyword = "git status",      OperationName = WorkflowOperations.GetGitStatus,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "git commits",     OperationName = WorkflowOperations.ListGitCommits,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "git activity",    OperationName = WorkflowOperations.GetGitActivity,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "git branch",      OperationName = WorkflowOperations.GetGitBranchInfo,   Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "git health",      OperationName = WorkflowOperations.GetGitHealth,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "record commit",   OperationName = WorkflowOperations.RecordGitActivity,  Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "log commit",      OperationName = WorkflowOperations.RecordGitActivity,  Confidence = 0.9, IsPrimary = false },

            // -- Cache ---------------------------------------------------------------
            new() { IntentKeyword = "cache status",    OperationName = WorkflowOperations.GetCacheStatus,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "clear cache",     OperationName = WorkflowOperations.ClearContextCache,  Confidence = 1.0, IsPrimary = true },

            // -- Sub-phase -----------------------------------------------------------
            new() { IntentKeyword = "sub-phase",       OperationName = WorkflowOperations.UpdateSubPhaseStatus, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "subtask",         OperationName = WorkflowOperations.UpdateSubPhaseStatus, Confidence = 0.8, IsPrimary = false },

            // -- Inbox / repair ------------------------------------------------------
            new() { IntentKeyword = "process inbox",   OperationName = WorkflowOperations.ProcessInbox,       Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "inbox status",    OperationName = WorkflowOperations.GetInboxStatus,     Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "audit paths",     OperationName = WorkflowOperations.AuditProtectedPaths, Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "repair",          OperationName = WorkflowOperations.RepairConsistency,  Confidence = 1.0, IsPrimary = true },
            new() { IntentKeyword = "fix consistency", OperationName = WorkflowOperations.RepairConsistency,  Confidence = 0.9, IsPrimary = false },
        ];
    }

    /// <summary>
    /// Generates the full routing rules collection for export.
    /// The operation list is deterministic for a given code version.
    /// </summary>
    public static IReadOnlyList<ToolRoutingRule> GenerateRules() => GetRules();

    /// <summary>
    /// Searches routing rules for keywords matching the given text.
    /// Returns rules whose intent keyword is a substring of the search text
    /// (case-insensitive), ordered by confidence descending.
    /// </summary>
    public static IReadOnlyList<ToolRoutingRule> Search(string text)
    {
        var lower = text.ToLowerInvariant();
        return GetRules()
            .Where(r => r.IntentKeyword.Contains(lower) || lower.Contains(r.IntentKeyword))
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.IsPrimary ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// Recommends operations for a natural-language task description.
    /// Tokenizes the text, matches against routing rules, ranks by confidence,
    /// enriches with manifest data, and adds safety warnings.
    /// </summary>
    public static OperationRecommendation Recommend(string taskDescription)
    {
        var lower = taskDescription.ToLowerInvariant();
        var warnings = new List<string>();
        string? saferAlternative = null;

        // Check for dangerous intents first.
        var dangerousPatterns = new (string Pattern, string Warning, string Alternative)[]
        {
            ("edit file",    "Raw file editing is dangerous. Use a safe WorkflowKit operation instead.",   WorkflowOperations.UpdatePhase),
            ("rewrite log",  "Rewriting log files directly will corrupt audit trails. Use safe append operations.", WorkflowOperations.CreateImplementationLog),
            ("delete record","Deleting historical records is not allowed. Use safe WorkflowKit operations.", WorkflowOperations.RecordBlocker),
            ("modify jsonl", "Direct JSONL mutation is unsafe. Use append-only operations.",                WorkflowOperations.CreateImplementationLog),
            ("change json",  "Direct JSON modification bypasses validation. Use safe state operations.",    WorkflowOperations.UpdatePhaseStatus),
        };

        foreach (var (pattern, warning, alt) in dangerousPatterns)
        {
            if (lower.Contains(pattern))
            {
                warnings.Add(warning);
                saferAlternative ??= alt;
            }
        }

        // Match against routing rules.
        var matchedRules = GetRules()
            .Where(r => lower.Contains(r.IntentKeyword))
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.IsPrimary ? 0 : 1)
            .ToList();

        // If no direct match, try partial word matching.
        if (matchedRules.Count == 0)
        {
            var tokens = lower.Split([' ', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
            matchedRules = GetRules()
                .Where(r => tokens.Any(t => r.IntentKeyword.Contains(t) || t.Contains(r.IntentKeyword)))
                .OrderByDescending(r => r.Confidence)
                .ThenBy(r => r.IsPrimary ? 0 : 1)
                .DistinctBy(r => r.OperationName)
                .Take(5)
                .ToList();
        }

        // Enrich with manifest data.
        var manifestEntries = OperationManifestCatalog.GetAll()
            .ToDictionary(e => e.OperationName, StringComparer.OrdinalIgnoreCase);

        var recommendations = new List<OperationRecommendationEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in matchedRules)
        {
            if (!seen.Add(rule.OperationName))
                continue;

            manifestEntries.TryGetValue(rule.OperationName, out var manifestEntry);
            var entry = new OperationRecommendationEntry
            {
                OperationName = rule.OperationName,
                Confidence     = rule.Confidence,
                AccessLevel    = manifestEntry?.AccessLevel ?? OperationAccessLevel.Read,
                Summary        = manifestEntry?.Summary ?? rule.OperationName,
                AccessWarning  = GetAccessWarning(manifestEntry?.AccessLevel ?? OperationAccessLevel.Read)
            };
            recommendations.Add(entry);
        }

        // Collect access-level warnings.
        foreach (var rec in recommendations)
        {
            if (rec.AccessWarning is not null && !warnings.Contains(rec.AccessWarning))
                warnings.Add(rec.AccessWarning);
        }

        // If no matches at all, return a fallback recommendation.
        if (recommendations.Count == 0)
        {
            recommendations.Add(new OperationRecommendationEntry
            {
                OperationName = WorkflowOperations.GetContextBundle,
                Confidence     = 0.3,
                AccessLevel    = OperationAccessLevel.Read,
                Summary        = "No direct match. Use get_context_bundle to explore workflow state.",
                AccessWarning  = null
            });
        }

        return new OperationRecommendation
        {
            TaskDescription  = taskDescription,
            Recommendations  = recommendations,
            Warnings         = warnings,
            SaferAlternative = saferAlternative
        };
    }

    private static string? GetAccessWarning(OperationAccessLevel level) => level switch
    {
        OperationAccessLevel.Write      => "Write -- this operation modifies authoritative state files.",
        OperationAccessLevel.Append     => "Append-only -- safe; adds records without overwriting state.",
        OperationAccessLevel.Regenerate => "Regenerate -- rebuilds generated read models; no state mutation.",
        _                               => null
    };

    /// <summary>
    /// Exports routing rules as a rebuildable JSON file under .workflowkit/index/tool-routing-rules.json.
    /// </summary>
    public static void ExportToFile(string workflowRoot)
    {
        var rules = GenerateRules();
        var path = WorkflowLayout.ToolRoutingRulesPath(workflowRoot);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(path, json);
    }
}
