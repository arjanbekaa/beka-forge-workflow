namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Estimated token counter for budget observability (PHASE-024-E).
///
/// Provides rough token estimates for text content, file sizes,
/// and context pointer collections. Used by budget reports and trace spans.
///
/// Estimation: ~4 characters per token for English text (conservative).
/// C# code: ~3.5 characters per token due to shorter identifiers.
/// </summary>
public static class TokenEstimator
{
    /// <summary>Characters per token for prose text (English).</summary>
    private const double ProseCharsPerToken = 4.0;

    /// <summary>Characters per token for C# code.</summary>
    private const double CodeCharsPerToken = 3.5;

    /// <summary>Characters per token for JSON content.</summary>
    private const double JsonCharsPerToken = 3.0;

    /// <summary>Overhead multiplier for metadata (pointers, IDs, timestamps).</summary>
    private const double MetadataOverheadFactor = 1.15;

    /// <summary>
    /// Estimates token count for a string of prose text.
    /// </summary>
    public static int EstimateText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / ProseCharsPerToken);
    }

    /// <summary>
    /// Estimates token count for a C# source file based on its byte size.
    /// </summary>
    public static int EstimateCSharpFile(long byteLength)
    {
        if (byteLength <= 0) return -1;
        return (int)Math.Ceiling(byteLength / CodeCharsPerToken);
    }

    /// <summary>
    /// Estimates token count for the content of a file at the given path.
    /// Returns -1 if the file doesn't exist or is unreadable.
    /// </summary>
    public static int EstimateFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return -1;
            var length = new FileInfo(path).Length;
            // Use code estimator for .cs files, prose for .md/.txt
            var isCode = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

            var charsPerToken = isCode ? CodeCharsPerToken :
                (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                ? JsonCharsPerToken : ProseCharsPerToken;

            return (int)Math.Ceiling(length / charsPerToken);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Estimates total tokens for a collection of context pointers.
    /// Includes metadata overhead for the pointer structure itself.
    /// </summary>
    public static int EstimatePointerCollection(IEnumerable<AgentContracts.ContextPointer> pointers)
    {
        var total = 0;
        foreach (var p in pointers)
        {
            if (p.EstimatedTokens > 0)
                total += p.EstimatedTokens;
            else
                // Fallback: estimate from reason + target text length
                total += EstimateText(p.Reason) + EstimateText(p.Target) + 10;
        }
        return (int)Math.Ceiling(total * MetadataOverheadFactor);
    }

    /// <summary>
    /// Estimates token consumption for a budget-aware context response.
    /// </summary>
    public static BudgetConsumption EstimateConsumption(
        IEnumerable<AgentContracts.ContextPointer> pointers,
        int omitted,
        int tokenBudgetCap)
    {
        var consumed = EstimatePointerCollection(pointers);
        var remaining = tokenBudgetCap > 0 ? Math.Max(0, tokenBudgetCap - consumed) : -1;
        var utilization = tokenBudgetCap > 0 ? (double)consumed / tokenBudgetCap : 0;

        return new BudgetConsumption
        {
            EstimatedTokensConsumed = consumed,
            TokenBudgetCap = tokenBudgetCap,
            EstimatedTokensRemaining = remaining,
            BudgetUtilization = utilization,
            PointersReturned = pointers.Count(),
            PointersOmitted = omitted
        };
    }
}

/// <summary>
/// Budget consumption snapshot for trace observability (PHASE-024-E).
/// </summary>
public sealed record BudgetConsumption
{
    public int EstimatedTokensConsumed { get; init; }
    public int TokenBudgetCap { get; init; }
    public int EstimatedTokensRemaining { get; init; } = -1;
    public double BudgetUtilization { get; init; }
    public int PointersReturned { get; init; }
    public int PointersOmitted { get; init; }

    /// <summary>Summary string for trace span metadata.</summary>
    public string Summary => TokenBudgetCap > 0
        ? $"Budget: {PointersReturned} ptrs, {EstimatedTokensConsumed}/{TokenBudgetCap} tokens ({BudgetUtilization:P0})"
        : $"Budget: {PointersReturned} ptrs, {EstimatedTokensConsumed} tokens (unlimited)";
}
