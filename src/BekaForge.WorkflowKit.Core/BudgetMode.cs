namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Budget modes for context retrieval.
/// Controls how many pointers are returned, what content is included,
/// and how much metadata accompanies each pointer.
///
/// Priority: inline budget override &gt; project config &gt; built-in default (Medium).
/// </summary>
public enum BudgetMode
{
    /// <summary>Minimal context: max 5 pointers, summaries only, no markdown, no traces, no inline content.</summary>
    Low = 0,

    /// <summary>Default mode: max 20 pointers, summaries + key metadata, markdown regions, no inline content.</summary>
    Medium = 1,

    /// <summary>Expanded context: max 40 pointers, full metadata, markdown + traces, inline for small files.</summary>
    High = 2,

    /// <summary>Unrestricted: max 100 pointers, all content, full inline allowed, traces included.</summary>
    Full = 3
}
