namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Result of a file slice operation. Returns exact content within the requested range
/// plus metadata: path, range, hash, stale flag, and warnings.
/// </summary>
public sealed record FileSliceResult
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public int TotalLines { get; init; }
    public string? ContentHash { get; init; }
    public bool IsStale { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of a JSONL record lookup by record ID prefix (IMP-001, AUD-001, etc.).
/// </summary>
public sealed record RecordSliceResult
{
    public required string RecordId { get; init; }
    public required string RecordType { get; init; }
    public required string JsonContent { get; init; }
    public string? PhaseId { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of a JSON Pointer read against a workflow or phase JSON file.
/// </summary>
public sealed record JsonPointerResult
{
    public required string SourceFile { get; init; }
    public required string Pointer { get; init; }
    public string? Value { get; init; }
    public bool Found { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of a generated markdown region lookup by section name.
/// </summary>
public sealed record MarkdownRegionResult
{
    public required string FilePath { get; init; }
    public required string SectionName { get; init; }
    public required string Content { get; init; }
    public bool Found { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of a file history lookup — returns all records referencing a given file.
/// </summary>
public sealed record FileHistoryResult
{
    public required string FilePath { get; init; }
    public required IReadOnlyList<FileHistoryEntry> Entries { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// A single entry in a file's history — which phase/record touched it and when.
/// </summary>
public sealed record FileHistoryEntry
{
    public required string PhaseId { get; init; }
    public required string RecordType { get; init; }
    public required string RecordId { get; init; }
    public string? Summary { get; init; }
}

// ── Relevant Context API (PHASE-011) ────────────────────────────────────────────

/// <summary>
/// A ranked context pointer — tells an agent what to read and how to resolve it.
/// Agents should resolve pointers through slice APIs (get_file_slice, get_record_slice, etc.)
/// rather than scanning the full workflow folder.
/// </summary>
public sealed record ContextPointer
{
    /// <summary>The type of this pointer: file, record, markdown_region, json_pointer, or phase_contract.</summary>
    public required string PointerType { get; init; }

    /// <summary>The target path, record ID, or section name to resolve.</summary>
    public required string Target { get; init; }

    /// <summary>Relevance score from 0.0 (least relevant) to 1.0 (most relevant).</summary>
    public double RelevanceScore { get; init; }

    /// <summary>Human-readable reason why this pointer is relevant to the current task.</summary>
    public required string Reason { get; init; }

    /// <summary>Estimated token count for the resolved content. -1 if unknown.</summary>
    public int EstimatedTokens { get; init; } = -1;

    // ResolveWith removed — derivable from PointerType (e.g. markdown_region → get_markdown_region)
}

/// <summary>
/// Result of workflow.get_relevant_context — a ranked list of context pointers
/// plus metadata about what was omitted and any warnings.
/// </summary>
public sealed record RelevantContextResult
{
    /// <summary>The phase ID this context was requested for, or null for workflow-level.</summary>
    public string? PhaseId { get; init; }

    /// <summary>The task type filter applied, if any.</summary>
    public string? TaskType { get; init; }

    /// <summary>Ranked list of context pointers, ordered by relevance (highest first).</summary>
    public required IReadOnlyList<ContextPointer> Pointers { get; init; }

    /// <summary>Number of candidates omitted because of maxItems or maxEstimatedTokens limits.</summary>
    public int OmittedCandidates { get; init; }

    /// <summary>Total estimated tokens across all returned pointers. -1 if unknown.</summary>
    public int EstimatedTotalTokens { get; init; } = -1;

    /// <summary>Warnings and guidance for the agent (e.g., "Do not scan the full workflow folder").</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True if this result was served from the context package cache (PHASE-015).</summary>
    public bool IsFromCache { get; init; }

    /// <summary>Budget metadata for this context response (PHASE-024). Null when budget is not in use.</summary>
    public BudgetReport? Budget { get; init; }
}

// ── Budget-Aware Context (PHASE-024) ─────────────────────────────────────────────

/// <summary>
/// Budget metadata included in context responses.
/// Reports which budget mode was applied, consumption stats, and limits.
/// </summary>
public sealed record BudgetReport
{
    /// <summary>The budget mode that was applied (after priority resolution).</summary>
    public string Mode { get; init; } = "Medium";

    /// <summary>Source of the budget: "override", "project", or "default".</summary>
    public string Source { get; init; } = "default";

    /// <summary>Maximum pointers allowed by this budget.</summary>
    public int MaxPointers { get; init; } = 20;

    /// <summary>How many pointers were actually returned.</summary>
    public int PointersReturned { get; init; }

    /// <summary>How many candidates were omitted due to budget caps.</summary>
    public int OmittedByBudget { get; init; }

    /// <summary>Estimated tokens consumed by returned pointers. -1 if unknown.</summary>
    public int EstimatedTokensConsumed { get; init; } = -1;

    /// <summary>Token budget cap (0 = unlimited).</summary>
    public int TokenBudgetCap { get; init; }

    /// <summary>True if inline content was included in pointers.</summary>
    public bool IncludesInlineContent { get; init; }

    /// <summary>Budget-specific warnings (e.g., "file too large for inline under current budget").</summary>
    public IReadOnlyList<string> BudgetWarnings { get; init; } = [];
}

/// <summary>
/// Full budget report from workflow.get_budget_report — provides budget
/// configuration and consumption snapshot.
/// </summary>
public sealed record BudgetConfigResult
{
    /// <summary>The resolved budget mode.</summary>
    public string Mode { get; init; } = "Medium";

    /// <summary>Source of the budget: "override", "project", or "default".</summary>
    public string Source { get; init; } = "default";

    /// <summary>The effective budget profile being used.</summary>
    public BudgetProfileData? Profile { get; init; }

    /// <summary>Project-level budget config (if any).</summary>
    public BudgetProfileData? ProjectConfig { get; init; }

    /// <summary>Built-in default profile for the current mode.</summary>
    public BudgetProfileData? DefaultProfile { get; init; }

    /// <summary>Human-readable summary of the budget configuration.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Serializable budget profile data used in reports and config responses.
/// </summary>
public sealed record BudgetProfileData
{
    public int MaxPointers { get; init; }
    public int MaxLogRecords { get; init; }
    public int MaxSummaryLength { get; init; }
    public bool IncludeMarkdown { get; init; }
    public bool IncludeTraces { get; init; }
    public bool IncludeInlineContent { get; init; }
    public int InlineContentMaxBytes { get; init; }
    public int MaxEstimatedTokens { get; init; }
    public string Priority { get; init; } = "relevance";
    public string Description { get; init; } = "";
}
