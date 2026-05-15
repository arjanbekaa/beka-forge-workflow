namespace BekaForge.WorkflowKit.Markdown;

/// <summary>
/// Constants and helpers for BekaForge generated region markers.
///
/// WorkflowKit owns only content inside these markers.
/// Everything outside is human-written and must never be overwritten.
///
/// Format:
///   <!-- BEKAFORGE:BEGIN generated:section-name -->
///   ...WorkflowKit-generated content...
///   <!-- BEKAFORGE:END generated:section-name -->
/// </summary>
public static class MarkdownRegion
{
    // -- Canonical section names ----------------------------------------------------

    public const string AgentsRoles          = "agents-roles";
    public const string WorkflowKitSystemPrompt = "workflowkit-system-prompt";
    public const string WorkflowOverview     = "workflow-overview";
    public const string ArchitectureOverview = "architecture-overview";
    public const string ImplementationPlan   = "implementation-plan";
    public const string MigrationNotes       = "migration-notes";
    public const string ExtractionAudit      = "extraction-audit";
    public const string KnownLimitations     = "known-limitations";
    public const string ExtensionGuide       = "extension-guide";
    public const string ConsistencyCheck     = "consistency-check";
    public const string FinalReview          = "final-review";
    public const string PromptHeader         = "prompt-header";
    public const string PhaseContract        = "phase-contract";
    public const string AuditLog             = "audit-log";
    public const string ReviewLog            = "review-log";
    public const string ImplementationLog    = "implementation-log";
    public const string FixLog               = "fix-log";
    public const string TestingLog           = "testing-log";
    public const string ValidationLog        = "validation-log";
    public const string CurrentStatus        = "current-status";

    // -- Marker construction --------------------------------------------------------

    /// <summary>Returns the BEGIN marker for a named section.</summary>
    public static string Begin(string section) =>
        $"<!-- BEKAFORGE:BEGIN generated:{section} -->";

    /// <summary>Returns the END marker for a named section.</summary>
    public static string End(string section) =>
        $"<!-- BEKAFORGE:END generated:{section} -->";

    /// <summary>
    /// Wraps generated content in BEGIN/END markers.
    /// Ensures the content block is separated from surrounding text by blank lines.
    /// </summary>
    public static string Wrap(string section, string content) =>
        $"{Begin(section)}\n{content.TrimEnd()}\n{End(section)}";
}
