namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Defines the canonical path layout for a .workflowkit/ workflow root.
/// All storage components derive paths from these methods — never hardcode paths elsewhere.
/// </summary>
public static class WorkflowLayout
{
    public const string WorkflowKitDir = ".workflowkit";
    public const string LegacyBekaForgeDir = ".bekaforge";
    public const string BekaForgeDir = WorkflowKitDir;
    public const string SchemaVersion = "1.0";

    /// <summary>Path to the .workflowkit/ directory inside the workflow root.</summary>
    public static string Root(string workflowRoot) =>
        Directory.Exists(LegacyRoot(workflowRoot)) && !Directory.Exists(WorkflowKitRoot(workflowRoot))
            ? LegacyRoot(workflowRoot)
            : WorkflowKitRoot(workflowRoot);

    public static string WorkflowKitRoot(string workflowRoot) =>
        Path.Combine(workflowRoot, WorkflowKitDir);

    public static string LegacyRoot(string workflowRoot) =>
        Path.Combine(workflowRoot, LegacyBekaForgeDir);

    /// <summary>Authoritative workflow state file. Written atomically.</summary>
    public static string WorkflowFile(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "workflow.json");

    /// <summary>ID sequence tracking file. Written atomically.</summary>
    public static string SequencesFile(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "sequences.json");

    /// <summary>Append-only event log. Every state mutation appends one entry.</summary>
    public static string EventsLog(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "events.jsonl");

    /// <summary>Directory containing one JSON file per phase.</summary>
    public static string PhasesDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "phases");

    /// <summary>Authoritative state file for a single phase. Written atomically.</summary>
    public static string PhaseFile(string workflowRoot, string phaseId) =>
        Path.Combine(PhasesDir(workflowRoot), $"{phaseId}.json");

    /// <summary>Directory containing all JSONL log files.</summary>
    public static string LogsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "logs");

    public static string ImplementationLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "implementation.jsonl");

    public static string AuditLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "audit.jsonl");

    public static string ReviewLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "review.jsonl");

    /// <summary>Legacy test log (TEST-NNN records). Kept for backward compatibility.</summary>
    public static string TestLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "test.jsonl");

    /// <summary>Validation log (VAL-NNN records). Replaces the legacy test log.</summary>
    public static string ValidationLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "validation.jsonl");

    public static string FixLog(string workflowRoot) =>
        Path.Combine(LogsDir(workflowRoot), "fix.jsonl");

    public static string BlockersDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "blockers");

    public static string BlockersLog(string workflowRoot) =>
        Path.Combine(BlockersDir(workflowRoot), "blockers.jsonl");

    public static string HandoffsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "handoffs");

    public static string HandoffsLog(string workflowRoot) =>
        Path.Combine(HandoffsDir(workflowRoot), "handoffs.jsonl");

    // -- Orchestration ------------------------------------------------------------

    public static string OrchestrationDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "orchestration");

    public static string OrchestrationSessionsDir(string workflowRoot) =>
        Path.Combine(OrchestrationDir(workflowRoot), "sessions");

    public static string OrchestrationRunsDir(string workflowRoot) =>
        Path.Combine(OrchestrationDir(workflowRoot), "runs");

    public static string OrchestrationLogsDir(string workflowRoot) =>
        Path.Combine(OrchestrationDir(workflowRoot), "logs");

    public static string OrchestrationSessionFile(string workflowRoot, string sessionId) =>
        Path.Combine(OrchestrationSessionsDir(workflowRoot), $"{sessionId}.json");

    public static string OrchestrationRunFile(string workflowRoot, string runId) =>
        Path.Combine(OrchestrationRunsDir(workflowRoot), $"{runId}.json");

    public static string OrchestrationGateDecisionsLog(string workflowRoot) =>
        Path.Combine(OrchestrationLogsDir(workflowRoot), "gate-decisions.jsonl");

    public static string OrchestrationRunEventsLog(string workflowRoot) =>
        Path.Combine(OrchestrationLogsDir(workflowRoot), "run-events.jsonl");

    /// <summary>Directory for rebuildable index/read-model files. Not source of truth.</summary>
    public static string IndexDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "index");

    /// <summary>Generated operation manifest JSON. Rebuildable — not source of truth.</summary>
    public static string OperationManifestPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "operation-manifest.json");

    /// <summary>Generated tool routing rules JSON. Rebuildable — not source of truth.</summary>
    public static string ToolRoutingPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "tool-routing.json");

    /// <summary>Generated context index SQLite database. Rebuildable — not source of truth.</summary>
    public static string ContextIndexPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "context-index.db");

    /// <summary>Metrics directory for timing and handoff records.</summary>
    public static string MetricsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "metrics");

    public static string TimingLog(string workflowRoot) =>
        Path.Combine(MetricsDir(workflowRoot), "timing.jsonl");

    // -- Generated markdown paths ------------------------------------------------

    public static string WorkflowDocsDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "workflow");

    public static string RulesMdPath(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "Rules.md");

    public static string WorkflowMdPath(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "workflow.md");

    public static string DocsDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "docs");

    public static string ArchitectureMdPath(string workflowRoot) =>
        Path.Combine(DocsDir(workflowRoot), "Architecture.md");

    public static string ImplementationPlanMdPath(string workflowRoot) =>
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

    /// <summary>Legacy testing markdown directory. Kept for backward compatibility.</summary>
    public static string TestingMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "04_Testing");

    /// <summary>Legacy testing log markdown path. Kept for backward compatibility.</summary>
    public static string TestingLogMdPath(string workflowRoot) =>
        Path.Combine(TestingMdDir(workflowRoot), "TestingLog.md");

    /// <summary>Validation markdown directory. Replaces the legacy testing directory.</summary>
    public static string ValidationMdDir(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "04_Validation");

    /// <summary>Validation log markdown path.</summary>
    public static string ValidationLogMdPath(string workflowRoot) =>
        Path.Combine(ValidationMdDir(workflowRoot), "ValidationLog.md");

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

    // -- Initialization check ----------------------------------------------------

    /// <summary>Returns true if a workflow has been initialized at this root.</summary>
    public static bool IsInitialized(string workflowRoot) =>
        File.Exists(Path.Combine(WorkflowKitRoot(workflowRoot), "workflow.json"))
        || File.Exists(Path.Combine(LegacyRoot(workflowRoot), "workflow.json"));

    /// <summary>
    /// Returns all directories that must exist in a valid .workflowkit/ layout.
    /// Created by <see cref="WorkflowInitializer"/> on first init and on repair.
    /// </summary>
    public static IReadOnlyList<string> RequiredDirectories(string workflowRoot) =>
    [
        Root(workflowRoot),
        PhasesDir(workflowRoot),
        LogsDir(workflowRoot),
        BlockersDir(workflowRoot),
        HandoffsDir(workflowRoot),
        MetricsDir(workflowRoot),
        IndexDir(workflowRoot),
        OrchestrationDir(workflowRoot),
        OrchestrationSessionsDir(workflowRoot),
        OrchestrationRunsDir(workflowRoot),
        OrchestrationLogsDir(workflowRoot),
        // PHASE-013: work session locks (added in PHASE-005) and evidence artifacts
        // (added in PHASE-004) were missing — bfwf init did not pre-create them.
        WorkDirPath(workflowRoot),
        Path.Combine(Root(workflowRoot), "evidence"),
    ];

    // ── Git ────────────────────────────────────────────────────────────────────

    public static string GitDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "git");

    public static string GitActivityLog(string workflowRoot) =>
        Path.Combine(GitDir(workflowRoot), "activity.jsonl");

    public static string GitSessionsLog(string workflowRoot) =>
        Path.Combine(GitDir(workflowRoot), "sessions.jsonl");

    // ── Traces ─────────────────────────────────────────────────────────────────

    public static string TracesDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "traces");

    // ── Inbox ──────────────────────────────────────────────────────────────────

    public static string InboxDir(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "inbox");

    public static string InboxProcessedDir(string workflowRoot) =>
        Path.Combine(InboxDir(workflowRoot), "processed");

    public static string InboxFailedDir(string workflowRoot) =>
        Path.Combine(InboxDir(workflowRoot), "failed");

    // ── Database ───────────────────────────────────────────────────────────────

    public static string WorkflowKitDbPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "workflowkit.db");

    // ── Work session locks (PHASE-005) ───────────────────────────────────────

    /// <summary>Directory for active work session lock files.</summary>
    public static string WorkDirPath(string workflowRoot) =>
        Path.Combine(Root(workflowRoot), "work");

    /// <summary>
    /// Path for a per-phase work lock file.
    /// Layout: .workflowkit/work/{phaseId-lowercased}.json
    /// </summary>
    public static string WorkLockPath(string workflowRoot, string phaseId) =>
        Path.Combine(WorkDirPath(workflowRoot), $"{phaseId.ToLowerInvariant()}.json");

    // ── Evidence artifacts (PHASE-004) ────────────────────────────────────────

    /// <summary>
    /// Per-phase directory for evidence artifact files produced by `bfwf validation run`.
    /// Layout: .workflowkit/evidence/{phaseId-lowercased}/
    /// </summary>
    public static string EvidenceDirPath(string workflowRoot, string phaseId) =>
        Path.Combine(Root(workflowRoot), "evidence", phaseId.ToLowerInvariant());

    /// <summary>
    /// Path for a specific evidence artifact file.
    /// Layout: .workflowkit/evidence/{phaseId}/{timestamp}-{id}.txt
    /// </summary>
    public static string EvidenceArtifactPath(string workflowRoot, string phaseId, string timestamp, string id) =>
        Path.Combine(EvidenceDirPath(workflowRoot, phaseId), $"{timestamp}-{id}.txt");

    // ── Root agent files ──────────────────────────────────────────────────────

    public static string AgentsMdPath(string workflowRoot) =>
        Path.Combine(workflowRoot, "AGENTS.md");

    public static string ClaudeMdPath(string workflowRoot) =>
        Path.Combine(workflowRoot, "CLAUDE.md");

    public static string BekaWorkflowSystemPromptPath(string workflowRoot) =>
        Path.Combine(WorkflowDocsDir(workflowRoot), "BekaWorkflowSystemPrompt.md");

    // ── Legacy markdown paths ─────────────────────────────────────────────────

    /// <summary>
    /// Legacy alias for the canonical implementation-plan markdown path.
    /// Kept so older call sites continue to resolve to ImplementationPlan.md.
    /// </summary>
    public static string ImplementationMdPath(string workflowRoot) =>
        ImplementationPlanMdPath(workflowRoot);

    public static string ToolRoutingRulesPath(string workflowRoot) =>
        Path.Combine(IndexDir(workflowRoot), "tool-routing-rules.json");

    public static IReadOnlyList<string> RequiredMarkdownDirectories(string workflowRoot) =>
    [
        WorkflowDocsDir(workflowRoot),
        DocsDir(workflowRoot),
        AuditsMdDir(workflowRoot),
        ImplementationMdDir(workflowRoot),
        TestingMdDir(workflowRoot),
        ValidationMdDir(workflowRoot),
        StatusMdDir(workflowRoot),
        PhasesMdDir(workflowRoot)
    ];
}
