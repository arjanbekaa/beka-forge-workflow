using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Budget profile defining limits and behavior for a specific budget mode.
/// Built-in defaults exist for each <see cref="BudgetMode"/>.
/// Project config can override these via <see cref="BudgetProfileOverride"/>.
/// </summary>
public sealed record BudgetProfile
{
    /// <summary>The budget mode this profile applies to.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BudgetMode Mode { get; init; }

    /// <summary>Maximum number of context pointers to return.</summary>
    public int MaxPointers { get; init; } = 20;

    /// <summary>Maximum number of log records per type (implementation, audit, review, test, fix).</summary>
    public int MaxLogRecords { get; init; } = 4;

    /// <summary>Maximum length of summary text in characters. 0 = no limit.</summary>
    public int MaxSummaryLength { get; init; } = 300;

    /// <summary>Whether to include generated markdown regions in context.</summary>
    public bool IncludeMarkdown { get; init; } = true;

    /// <summary>Whether to include trace spans in context.</summary>
    public bool IncludeTraces { get; init; } = false;

    /// <summary>Whether to include inline file content in context pointers.</summary>
    public bool IncludeInlineContent { get; init; } = false;

    /// <summary>Maximum bytes of inline content per file (when IncludeInlineContent is true). 0 = no limit.</summary>
    public int InlineContentMaxBytes { get; init; } = 0;

    /// <summary>Maximum estimated tokens across all returned pointers. 0 = no limit.</summary>
    public int MaxEstimatedTokens { get; init; } = 0;

    /// <summary>Reranking priority: "relevance", "recency", or "balanced".</summary>
    public string Priority { get; init; } = "relevance";

    /// <summary>Human-readable description of this profile for display/reports.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Returns the built-in default profile for the given budget mode.
    /// These defaults are used when no project config or override exists.
    /// </summary>
    public static BudgetProfile DefaultFor(BudgetMode mode) => mode switch
    {
        BudgetMode.Low => new BudgetProfile
        {
            Mode = BudgetMode.Low,
            MaxPointers = 5,
            MaxLogRecords = 1,
            MaxSummaryLength = 120,
            IncludeMarkdown = false,
            IncludeTraces = false,
            IncludeInlineContent = false,
            InlineContentMaxBytes = 0,
            MaxEstimatedTokens = 2000,
            Priority = "relevance",
            Description = "Minimal: essential pointers only, no markdown/traces/inline content."
        },
        BudgetMode.Medium => new BudgetProfile
        {
            Mode = BudgetMode.Medium,
            MaxPointers = 20,
            MaxLogRecords = 2,
            MaxSummaryLength = 300,
            IncludeMarkdown = true,
            IncludeTraces = false,
            IncludeInlineContent = false,
            InlineContentMaxBytes = 0,
            MaxEstimatedTokens = 8000,
            Priority = "relevance",
            Description = "Default: standard pointers with markdown, summaries, key metadata."
        },
        BudgetMode.High => new BudgetProfile
        {
            Mode = BudgetMode.High,
            MaxPointers = 40,
            MaxLogRecords = 3,
            MaxSummaryLength = 500,
            IncludeMarkdown = true,
            IncludeTraces = true,
            IncludeInlineContent = true,
            InlineContentMaxBytes = 0,
            MaxEstimatedTokens = 16000,
            Priority = "balanced",
            Description = "Expanded: more pointers, traces, inline content, longer summaries."
        },
        BudgetMode.Full => new BudgetProfile
        {
            Mode = BudgetMode.Full,
            MaxPointers = 100,
            MaxLogRecords = 5,
            MaxSummaryLength = 0,
            IncludeMarkdown = true,
            IncludeTraces = true,
            IncludeInlineContent = true,
            InlineContentMaxBytes = 0,
            MaxEstimatedTokens = 0,
            Priority = "balanced",
            Description = "Unrestricted: all context, full content, no limits."
        },
        _ => DefaultFor(BudgetMode.Medium)
    };
}
