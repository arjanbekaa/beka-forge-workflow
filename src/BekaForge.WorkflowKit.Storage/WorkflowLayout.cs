namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Defines the canonical path layout for a .bekaforge/ workflow root.
/// All storage components derive paths from these methods — never hardcode paths elsewhere.
/// </summary>
public static class WorkflowLayout
{
    public const string WorkflowKitDir = ".workflowkit";
    public const string LegacyBekaForgeDir = ".bekaforge";
    public const string BekaForgeDir = WorkflowKitDir;
    public const string SchemaVersion = "1.0";

    // ── Root ─────────────────────────────────────────────────────────────────────

    /// <summary>Path to the .bekaforge/ directory inside the workflow root.</summary>
    public static string Root(string workflowRoot) =>
        Directory.Exists(LegacyRoot(workflowRoot)) && !Directory.Exists(WorkflowKitRoot(workflowRoot))
            ? LegacyRoot(workflowRoot)
            : WorkflowKitRoot(workflowRoot);

    public static string WorkflowKitRoot(string workflowRoot) =>
        Path.Combine(workflowRoot, WorkflowKitDir);

    public static string LegacyRoot(string workflowRoot) =>
        Path.Combine(workflowRoot, LegacyBekaForgeDir);

    // ── State files ──────────────────────────────────────────────────────────────

    /// <summary>Authoritative workflow state file. Written atomically.</summary>
    public static string WorkflowFile(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "workflow.json");

    /// <summary>ID sequence tracking file. Written atomically.</summary>
    public static string SequencesFile(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "sequences.json");

    // ── Append-only event log ────────────────────────────────────────────────────

    /// <summary>Append-only event log. Every state mutation appends one entry.</summary>
    public static string EventsLog(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "events.jsonl");

    // ── Phases directory ─────────────────────────────────────────────────────────

    /// <summary>Directory containing one JSON file per phase.</summary>
    public static string PhasesDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "phases");

    /// <summary>Authoritative state file for a single phase. Written atomically.</summary>
    public static string PhaseFile(string workflowRoot, string phaseId) =>
        Path.Combine(PhasesDir(workflowRoot), $"{phaseId}.json");

    // ── Log files ────────────────────────────────────────────────────────────────

    /// <summary>Directory containing all JSONL log files.</summary>
    public static string LogsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "logs");

    public static string ImplementationLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "implementation.jsonl");

    public static string AuditLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "audit.jsonl");

    public static string ReviewLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "review.jsonl");

    public static string TestLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "test.jsonl");

    public static string FixLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "fix.jsonl");

    // ── Blockers ─────────────────────────────────────────────────────────────────

    public static string BlockersDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "blockers");

    public static string BlockersLog(string workflowRoot) =>
        Path.Combine(BlockersDir(workflowRoot), "blockers.jsonl");

    // ── Handoffs ─────────────────────────────────────────────────────────────────

    public static string HandoffsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "handoffs");

    public static string HandoffsLog(string workflowRoot) =>
        Path.Combine(HandoffsDir(workflowRoot), "handoffs.jsonl");

    // ── Index (rebuildable read models) ──────────────────────────────────────────

    /// <summary>Directory for rebuildable index/read-model files. Not source of truth.</summary>
    public static string IndexDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "index");

    /// <summary>Generated operation manifest JSON. Rebuildable — not source of truth.</summary>
    public static string OperationManifestPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "operation-manifest.json");

    /// <summary>Generated tool routing rules JSON. Rebuildable — not source of truth.</summary>
    public static string ToolRoutingRulesPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "tool-routing-rules.json");

    /// <summary>SQLite context index database. Rebuildable — not source of truth.</summary>
    public static string WorkflowKitDbPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "workflowkit.db");

    // ── Traces ───────────────────────────────────────────────────────────────────

    /// <summary>Directory for developer trace files. Diagnostics only, not source of truth.</summary>
    public static string TracesDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "traces");

    // ── Git activity (observational, rebuildable) ────────────────────────────────

    /// <summary>Directory for git activity and session logs. Observational data only — never source of truth.</summary>
    public static string GitDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "git");

    /// <summary>Append-only git activity log. Observational — deleting this file causes no source-of-truth loss.</summary>
    public static string GitActivityLog(string workflowRoot) =>
        Path.Combine(GitDir(workflowRoot), "activity.jsonl");

    /// <summary>Append-only session log. Each session start/end appends one record.</summary>
    public static string GitSessionsLog(string workflowRoot) =>
        Path.Combine(GitDir(workflowRoot), "sessions.jsonl");

    // ── Inbox / offline operation queue (PHASE-019) ───────────────────────────

    /// <summary>
    /// Directory for pending operation files. This is the only planned direct offline write target for agents.
    /// Files are .operation.json named by idempotency key.
    /// </summary>
    public static string InboxDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "inbox");

    /// <summary>
    /// Directory for successfully processed operations.
    /// Processed operations are moved here from inbox.
    /// </summary>
    public static string InboxProcessedDir(string workflowRoot) =>
        Path.Combine(InboxDir(workflowRoot), "processed");

    /// <summary>
    /// Directory for failed operations.
    /// Failed operations are moved here from inbox with error metadata.
    /// </summary>
    public static string InboxFailedDir(string workflowRoot) =>
        Path.Combine(InboxDir(workflowRoot), "failed");


    // ── Metrics ──────────────────────────────────────────────────────────────────

    public static string MetricsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "metrics");

    public static string TimingLog(string workflowRoot) =>
        Path.Combine(MetricsDir(workflowRoot), "timing.jsonl");

    // ── Markdown files (human-readable, outside .bekaforge/) ────────────────────

    /// <summary>AGENTS.md at the workflow root — user-owned agent instructions file. WorkflowKit does not claim ownership.</summary>
    public static string AgentsMdPath(string workflowRoot) =>
        Path.Combine(workflowRoot, "AGENTS.md");

    /// <summary>CLAUDE.md at the workflow root � Claude Code equivalent of AGENTS.md.</summary>
    public static string ClaudeMdPath(string workflowRoot) =>
        Path.Combine(workflowRoot, "CLAUDE.md");

    /// <summary>BekaWorkflowSystemPrompt.md at the workflow root — compatibility pointer for older agent/tool setups.</summary>
    public static string BekaWorkflowSystemPromptPath(string workflowRoot) =>
        Path.Combine(workflowRoot, "BekaWorkflowSystemPrompt.md");

    /// <summary>workflow.md at the workflow root — top-level workflow overview.</summary>
    public static string WorkflowDocsDir(string workflowRoot) =>
        Path.Combine(workflowRoot, "workflow");

    public static string WorkflowMdPath(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "workflow.md");

    /// <summary>workflow/Rules.md — canonical workflow-owned instructions for agents and tools.</summary>
    public static string RulesMdPath(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "Rules.md");

    public static string DocsDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "docs");

    public static string ArchitectureMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "Architecture.md");

    public static string ImplementationMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "ImplementationPlan.md");

    public static string MigrationNotesMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "MigrationNotes.md");

    public static string ExtractionAuditMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "ExtractionAudit.md");

    public static string KnownLimitationsMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "KnownLimitations.md");

    public static string ExtensionGuideMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "ExtensionGuide.md");

    public static string ConsistencyCheckMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "ConsistencyCheck.md");

    public static string FinalReviewMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "FinalReview.md");

    public static string PromptHeaderMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "PromptHeader.md");

    public static string AuditsMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "02_Audits");

    public static string AuditLogMdPath(string workflowRoot) =>
        Path.Combine(AuditsMdDir(workflowRoot), "AuditLog.md");

    public static string ReviewLogMdPath(string workflowRoot) =>
        Path.Combine(AuditsMdDir(workflowRoot), "ReviewLog.md");

    public static string ImplementationMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "03_Implementation");

    public static string ImplementationLogMdPath(string workflowRoot) =>
        Path.Combine(ImplementationMdDir(workflowRoot), "ImplementationLog.md");

    public static string FixLogMdPath(string workflowRoot) =>
        Path.Combine(ImplementationMdDir(workflowRoot), "FixLog.md");

    public static string TestingMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "04_Testing");

    public static string TestingLogMdPath(string workflowRoot) =>
        Path.Combine(TestingMdDir(workflowRoot), "TestingLog.md");

    public static string StatusMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "07_Status");

    public static string CurrentStatusMdPath(string workflowRoot) =>
        Path.Combine(StatusMdDir(workflowRoot), "CurrentStatus.md");

    /// <summary>Directory containing per-phase markdown documents.</summary>
    public static string PhasesMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "phases");

    /// <summary>Per-phase markdown file (phases/PHASE-NNN.md).</summary>
    public static string PhaseMdPath(string workflowRoot, string phaseId) =>
        Path.Combine(PhasesMdDir(workflowRoot), $"{phaseId}.md");

    // ── Initialization check ─────────────────────────────────────────────────────

    /// <summary>Returns true if a workflow has been initialized at this root.</summary>
    public static bool IsInitialized(string workflowRoot) =>
        File.Exists(Path.Combine(WorkflowKitRoot(workflowRoot), "workflow.json"))
        || File.Exists(Path.Combine(LegacyRoot(workflowRoot), "workflow.json"));

    /// <summary>
    /// Returns all directories that must exist in a valid .bekaforge/ layout.
    /// </summary>
    public static IReadOnlyList<string> RequiredDirectories(string workflowRoot) =>
    [
        Root(workflowRoot),
        PhasesDir(workflowRoot),
        LogsDir(workflowRoot),
        BlockersDir(workflowRoot),
        HandoffsDir(workflowRoot),
        MetricsDir(workflowRoot),
        IndexDir(workflowRoot)
    ];

    public static IReadOnlyList<string> RequiredMarkdownDirectories(string workflowRoot) =>
    [
        WorkflowDocsDir(workflowRoot),
        DocsDir(workflowRoot),
        AuditsMdDir(workflowRoot),
        ImplementationMdDir(workflowRoot),
        TestingMdDir(workflowRoot),
        StatusMdDir(workflowRoot),
        PhasesMdDir(workflowRoot)
    ];
}
