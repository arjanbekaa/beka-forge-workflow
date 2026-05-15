using System.Text.RegularExpressions;
using BekaForge.WorkflowKit.AgentContracts;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Hybrid context retriever that scores candidate pointers using multiple signals.
/// Combines: exact match, path match, operation name match, phase/log metadata match,
/// BM25/TF-IDF lexical scoring, fuzzy matching, and C# symbol-oriented signals.
///
/// All scoring is deterministic and local — no external ranking services.
/// The index is a rebuildable read model; source JSON/JSONL is authoritative.
///
/// PHASE-024: Budget-aware hybrid retrieval.
/// </summary>
public sealed class HybridContextRetriever
{
    private readonly string _workflowRoot;
    private readonly WorkflowStore _store;

    public HybridContextRetriever(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        _store = new WorkflowStore(workflowRoot);
    }

    /// <summary>
    /// Scores a list of candidate pointers against a query string using multiple signals.
    /// Returns the same list but with updated RelevanceScore and Reason fields.
    /// </summary>
    /// <param name="candidates">Candidate pointers with initial scores.</param>
    /// <param name="query">The search query (task description, operation name, etc.).</param>
    /// <param name="taskType">Optional task type for weighting (implementation, audit, review, test, fix).</param>
    /// <returns>Scored and re-ranked candidates.</returns>
    public List<(ContextPointer Pointer, double Score)> Score(
        List<(ContextPointer Pointer, double Score)> candidates,
        string? query,
        string? taskType)
    {
        if (string.IsNullOrWhiteSpace(query))
            return candidates;

        // Normalize query
        var queryLower = query.ToLowerInvariant().Trim();
        var queryTokens = Tokenize(queryLower);

        // Score each candidate with multi-signal weighting
        var scored = new List<(ContextPointer Pointer, double Score, string SignalUsed)>();
        foreach (var (pointer, baseScore) in candidates)
        {
            var signals = ScoreSignals(pointer, queryLower, queryTokens, taskType);
            // Blend: 60% original score + 40% hybrid signals (capped at 1.0)
            var hybridScore = signals.Where(s => s.Score > 0).Sum(s => s.Score);
            var finalScore = Math.Min(1.0, baseScore * 0.6 + hybridScore * 0.4);

            // Best signal for explainability
            var bestSignal = signals
                .Where(s => s.Score > 0)
                .OrderByDescending(s => s.Score)
                .FirstOrDefault();

            var reason = bestSignal.SignalName is not null
                ? $"{pointer.Reason} [{bestSignal.SignalName}: {bestSignal.Score:F2}]"
                : pointer.Reason;

            scored.Add((pointer with { RelevanceScore = finalScore, Reason = reason }, finalScore, bestSignal.SignalName ?? "base"));
        }

        return scored
            .Select(s => (s.Pointer, s.Score))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Pointer.EstimatedTokens > 0 ? s.Pointer.EstimatedTokens : int.MaxValue)
            .ThenBy(s => s.Pointer.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // -- Multi-signal scoring -----------------------------------------------------

    private List<(string? SignalName, double Score)> ScoreSignals(
        ContextPointer pointer,
        string queryLower,
        string[] queryTokens,
        string? taskType)
    {
        var signals = new List<(string? SignalName, double Score)>();

        // 1. Exact match on target or reason
        var targetLower = pointer.Target?.ToLowerInvariant() ?? "";
        var reasonLower = pointer.Reason?.ToLowerInvariant() ?? "";

        if (targetLower == queryLower || reasonLower == queryLower)
            signals.Add(("exact_match", 0.30));

        // 2. Path match (file paths, record IDs)
        if (!string.IsNullOrEmpty(pointer.Target) &&
            targetLower.Contains(queryLower, StringComparison.Ordinal))
            signals.Add(("path_match", 0.20));

        // 3. Operation name match (query contains pointer type keyword)
        if (!string.IsNullOrEmpty(pointer.PointerType))
        {
            var ptrType = pointer.PointerType.ToLowerInvariant();
            if (queryLower.Contains(ptrType, StringComparison.Ordinal))
                signals.Add(("operation_match", 0.25));
        }

        // 4. Phase/log metadata match (target/reason tokens matching query tokens)
        var targetTokens = Tokenize(targetLower);
        var reasonTokens = Tokenize(reasonLower);
        var metadataOverlap = queryTokens.Intersect(targetTokens).Count() +
                              queryTokens.Intersect(reasonTokens).Count();

        if (metadataOverlap > 0)
            signals.Add(("metadata_match", Math.Min(0.25, metadataOverlap * 0.05)));

        // 5. TF-IDF / BM25 lexical scoring (simplified — token frequency in target + reason)
        var lexicalScore = ComputeLexicalScore(queryTokens, targetTokens, reasonTokens);
        if (lexicalScore > 0)
            signals.Add(("lexical_tfidf", lexicalScore * 0.20));

        // 6. Fuzzy matching (Levenshtein-based on target)
        if (!string.IsNullOrEmpty(pointer.Target))
        {
            var fuzzyScore = ComputeFuzzyScore(queryLower, targetLower);
            if (fuzzyScore > 0)
                signals.Add(("fuzzy_match", fuzzyScore * 0.15));
        }

        // 7. C# symbol-oriented signals (class/method names in target or reason)
        var symbolScore = ComputeCSharpSymbolScore(queryTokens, targetLower, reasonLower);
        if (symbolScore > 0)
            signals.Add(("csharp_symbol", symbolScore * 0.15));

        // 8. Task-type bias
        if (!string.IsNullOrEmpty(taskType))
        {
            var taskBiasScore = ComputeTaskTypeBias(taskType, pointer, reasonLower);
            if (taskBiasScore > 0)
                signals.Add(("task_type_bias", taskBiasScore * 0.10));
        }

        return signals;
    }

    // -- Lexical Scoring (TF-IDF approximation) -----------------------------------

    /// <summary>
    /// Simplified TF-IDF: token frequency in target + reason, weighted by inverse
    /// document frequency across all candidates (approximated).
    /// </summary>
    private static double ComputeLexicalScore(string[] queryTokens, string[] targetTokens, string[] reasonTokens)
    {
        if (queryTokens.Length == 0) return 0;

        double score = 0;
        var allTargetTokens = targetTokens.Concat(reasonTokens).ToArray();

        foreach (var qt in queryTokens)
        {
            if (qt.Length < 2) continue;
            // TF: count in target/reason
            var tf = (double)allTargetTokens.Count(t => t.Contains(qt, StringComparison.Ordinal));
            if (tf > 0)
            {
                // IDF approximation: shorter tokens are more common, longer are rarer
                var idf = Math.Log(1.0 + 10.0 / Math.Max(1, qt.Length));
                score += tf * idf;
            }
        }

        return Math.Min(1.0, score / Math.Max(1, queryTokens.Length));
    }

    // -- Fuzzy Matching -----------------------------------------------------------

    /// <summary>
    /// Normalized Levenshtein similarity between two strings.
    /// Returns 0.0 to 1.0 where 1.0 is exact match.
    /// </summary>
    private static double ComputeFuzzyScore(string query, string target)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target))
            return 0;

        var distance = LevenshteinDistance(query, target);
        var maxLen = Math.Max(query.Length, target.Length);
        if (maxLen == 0) return 1.0;

        return 1.0 - (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    // -- C# Symbol-Oriented Signals -----------------------------------------------

    // Common C# code symbols to detect
    private static readonly Regex ClassNamePattern = new(
        @"\b(class|struct|record|interface|enum)\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MethodNamePattern = new(
        @"\b(void|bool|int|long|string|float|double|decimal|var|async|Task|ActionResult|IActionResult)\s+(\w+)\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PropertyPattern = new(
        @"\b(public|private|protected|internal)\s+\w+\s+(\w+)\s*\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] CSharpKeywords =
    {
        "class", "struct", "record", "interface", "enum",
        "method", "property", "field", "constructor", "delegate",
        "event", "namespace", "controller", "service", "handler",
        "repository", "model", "dto", "viewmodel", "component"
    };

    /// <summary>
    /// Scores a pointer based on C# symbol matches: class names, method names,
    /// property names, and common C# patterns in the target or reason.
    /// </summary>
    private static double ComputeCSharpSymbolScore(
        string[] queryTokens, string targetLower, string reasonLower)
    {
        double score = 0;
        var combined = targetLower + " " + reasonLower;

        // Check for C# keywords in query matching pointer content
        foreach (var qt in queryTokens)
        {
            if (qt.Length < 2) continue;

            // Class/method detection in the query
            if (CSharpKeywords.Contains(qt) && combined.Contains(qt, StringComparison.Ordinal))
                score += 0.15;

            // PascalCase symbol matching (class/method names are typically PascalCase)
            if (qt.Length > 2 && char.IsUpper(qt[0]) && combined.Contains(qt, StringComparison.Ordinal))
                score += 0.10;
        }

        // Pattern-based: detect class/method declarations in pointer target
        if (ClassNamePattern.IsMatch(combined))
            score += 0.10;
        if (MethodNamePattern.IsMatch(combined))
            score += 0.10;
        if (PropertyPattern.IsMatch(combined))
            score += 0.05;

        return Math.Min(0.50, score);
    }

    // -- Task-Type Bias -----------------------------------------------------------

    /// <summary>
    /// Biases scoring based on task type (implementation, audit, review, test, fix).
    /// Certain pointer types are more relevant for certain task types.
    /// </summary>
    private static double ComputeTaskTypeBias(string taskType, ContextPointer pointer, string reasonLower)
    {
        return taskType.ToLowerInvariant() switch
        {
            "implementation" => pointer.PointerType switch
            {
                "record" when reasonLower.Contains("implementation") => 0.30,
                "phase_contract" => 0.20,
                "markdown_region" when reasonLower.Contains("implementation plan") => 0.15,
                _ => 0
            },
            "audit" => pointer.PointerType switch
            {
                "record" when reasonLower.Contains("audit") => 0.30,
                "markdown_region" when reasonLower.Contains("audit") => 0.20,
                "phase_contract" => 0.15,
                _ => 0
            },
            "review" => pointer.PointerType switch
            {
                "record" when reasonLower.Contains("review") => 0.30,
                "markdown_region" when reasonLower.Contains("review") => 0.20,
                _ => 0
            },
            "test" => pointer.PointerType switch
            {
                "record" when reasonLower.Contains("test") => 0.30,
                "markdown_region" when reasonLower.Contains("test") => 0.20,
                _ => 0
            },
            "fix" => pointer.PointerType switch
            {
                "record" when reasonLower.Contains("fix") => 0.30,
                "record" when reasonLower.Contains("blocker") => 0.15,
                _ => 0
            },
            _ => 0
        };
    }

    // -- Tokenization -------------------------------------------------------------

    /// <summary>
    /// Tokenizes a string into lowercase alphanumeric tokens, splitting on
    /// spaces, punctuation, and PascalCase/camelCase boundaries.
    /// </summary>
    private static string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Split on non-alphanumeric characters
        var tokens = Regex.Split(text, @"[^a-zA-Z0-9]+")
            .Where(t => t.Length > 0)
            .SelectMany(SplitCamelCase)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 1 || char.IsLetterOrDigit(t[0]))
            .Distinct()
            .ToArray();

        return tokens;
    }

    /// <summary>
    /// Splits a PascalCase or camelCase token into constituent words.
    /// E.g., "GetRelevantContext" -> ["Get", "Relevant", "Context"]
    /// </summary>
    private static string[] SplitCamelCase(string token)
    {
        if (token.Length <= 1) return [token];

        var parts = new List<string>();
        var start = 0;

        for (var i = 1; i < token.Length; i++)
        {
            if (char.IsUpper(token[i]) && (char.IsLower(token[i - 1]) ||
                (i + 1 < token.Length && char.IsLower(token[i + 1]))))
            {
                parts.Add(token[start..i]);
                start = i;
            }
        }
        parts.Add(token[start..]);

        return parts.ToArray();
    }
}
