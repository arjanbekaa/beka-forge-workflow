using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Markdown.Generators;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Markdown;

/// <summary>
/// Orchestrates all markdown file generation for a workflow.
///
/// For each file it:
///   1. Reads the current file content (if it exists).
///   2. Generates fresh content for every BEKAFORGE region.
///   3. Merges via <see cref="HumanSectionPreserver"/> — human-written
///      text is never touched.
///   4. Writes back only when the result differs from what was on disk.
/// </summary>
public sealed class MarkdownSyncService
{
    private readonly WorkflowStore         _store;
    private readonly HumanSectionPreserver _preserver = new();

    // -- Generators -------------------------------------------------------------
    private readonly AgentsMdGenerator _agents = new();
    private readonly WorkflowRulesMdGenerator _rules = new();
    private readonly WorkflowMdGenerator _workflow = new();
    private readonly ArchitectureMdGenerator _architecture = new();
    private readonly ImplementationPlanMdGenerator _implementationPlan = new();
    private readonly PhaseContractMdGenerator     _contract  = new();
    private readonly ImplementationLogMdGenerator _implLog   = new();
    private readonly AuditLogMdGenerator          _auditLog  = new();
    private readonly ReviewLogMdGenerator         _reviewLog = new();
    private readonly TestingLogMdGenerator        _testLog   = new();
    private readonly FixLogMdGenerator            _fixLog    = new();
    private readonly CurrentStatusMdGenerator     _status    = new();
    private readonly DashboardStatusMdGenerator   _dashboardStatus = new();
    private readonly BekaWorkflowSystemPromptGenerator _compatPointer = new();

    public MarkdownSyncService(WorkflowStore store)
    {
        _store = store;
    }

    // -- Public entry point ----------------------------------------------------

    /// <summary>
    /// Regenerates and saves all markdown files that are out of date.
    /// Returns the list of file paths that were written.
    /// </summary>
    public IReadOnlyList<string> SyncAll()
    {
        var written = new List<string>();

        written.AddRange(SyncAgentsMd());
        written.AddRange(SyncClaudeMd());
        written.AddRange(SyncWorkflowRulesMd());
        written.AddRange(SyncBekaWorkflowSystemPromptMd());
        written.AddRange(SyncWorkflowMd());

        // Read all log records once for efficiency — filter per phase below.
        var allImpls   = _store.ReadAllImplementations();
        var allAudits  = _store.ReadAllAudits();
        var allReviews = _store.ReadAllReviews();
        var allTests   = _store.ReadAllTests();
        var allFixes   = _store.ReadAllFixes();
        var allBlockers = _store.ReadAllBlockers();

        var workflow = _store.LoadWorkflow();
        var phases   = _store.LoadAllPhases();
        var summary  = WorkflowDashboardSummaryBuilder.Build(_store);

        written.AddRange(SyncArchitectureMd(workflow, phases));
        written.AddRange(SyncImplementationPlanMd(summary));
        written.AddRange(SyncProjectGuidanceDocs(summary));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.ImplementationLogMdPath(_store.WorkflowRoot),
            MarkdownRegion.ImplementationLog,
            _implLog.Generate(allImpls)));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.FixLogMdPath(_store.WorkflowRoot),
            MarkdownRegion.FixLog,
            _fixLog.Generate(allFixes)));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.AuditLogMdPath(_store.WorkflowRoot),
            MarkdownRegion.AuditLog,
            _auditLog.Generate(allAudits)));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.ReviewLogMdPath(_store.WorkflowRoot),
            MarkdownRegion.ReviewLog,
            _reviewLog.Generate(allReviews)));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.TestingLogMdPath(_store.WorkflowRoot),
            MarkdownRegion.TestingLog,
            _testLog.Generate(allTests)));
        written.AddRange(SyncCurrentStatusMd(summary));

        foreach (var phase in phases)
        {
            written.AddRange(SyncPhaseMd(
                phase, workflow,
                allImpls, allAudits, allReviews, allTests, allBlockers));
        }

        return written;
    }

    // -- BekaWorkflowSystemPrompt.md ------------------------------------------

    private IEnumerable<string> SyncBekaWorkflowSystemPromptMd()
    {
        string path = WorkflowLayout.BekaWorkflowSystemPromptPath(_store.WorkflowRoot);
        string generated = _compatPointer.Generate();
        string current = SafeReadFile(path);

        if (generated == current)
            return Array.Empty<string>();

        string? dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string tmp = Path.Combine(dir ?? Path.GetTempPath(), $".tmp_{Path.GetFileName(path)}_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(tmp, generated, System.Text.Encoding.UTF8);
            File.Move(tmp, path, overwrite: true);
            return new[] { path };
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    private IEnumerable<string> SyncWorkflowRulesMd()
    {
        string path = WorkflowLayout.RulesMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.WorkflowKitSystemPrompt] = _rules.Generate()
        };

        return WriteIfChanged(path, current, sections);
    }

    
    // -- AGENTS.md -------------------------------------------------------------

    private IEnumerable<string> SyncAgentsMd()
    {
        string path    = WorkflowLayout.AgentsMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.AgentsRoles] = _agents.Generate()
        };

        return WriteIfChanged(path, current, sections);
    }

    // -- CLAUDE.md -------------------------------------------------------------

    private IEnumerable<string> SyncClaudeMd()
    {
        string path    = WorkflowLayout.ClaudeMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.AgentsRoles] = _agents.Generate()
        };

        return WriteIfChanged(path, current, sections);
    }

    // -- workflow.md -----------------------------------------------------------

    private IEnumerable<string> SyncWorkflowMd()
    {
        string path    = WorkflowLayout.WorkflowMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var workflow = _store.LoadWorkflow();
        var phases   = _store.LoadAllPhases();

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.WorkflowOverview] = _workflow.Generate(workflow, phases)
        };

        return WriteIfChanged(path, current, sections);
    }

    // -- PHASE-NNN.md ----------------------------------------------------------

    private IEnumerable<string> SyncArchitectureMd(
        WorkflowState workflow,
        IReadOnlyList<Phase> phases)
    {
        string path = WorkflowLayout.ArchitectureMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.ArchitectureOverview] = _architecture.Generate(workflow, phases)
        };

        return WriteIfChanged(path, current, sections);
    }

    private IEnumerable<string> SyncImplementationPlanMd(WorkflowDashboardSummary summary)
    {
        string path = WorkflowLayout.ImplementationMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.ImplementationPlan] = _implementationPlan.Generate(summary)
        };

        return WriteIfChanged(path, current, sections);
    }

    private IEnumerable<string> SyncProjectGuidanceDocs(WorkflowDashboardSummary summary)
    {
        var written = new List<string>();

        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.MigrationNotesMdPath(_store.WorkflowRoot),
            MarkdownRegion.MigrationNotes,
            "Record breaking changes, upgrade paths, moved files, renamed APIs, and required migration steps here.\n\n_No migration notes recorded yet._"));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.ExtractionAuditMdPath(_store.WorkflowRoot),
            MarkdownRegion.ExtractionAudit,
            "Record what was extracted, what remains coupled, dependency risks, and follow-up cleanup here.\n\n_No extraction audit entries recorded yet._"));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.KnownLimitationsMdPath(_store.WorkflowRoot),
            MarkdownRegion.KnownLimitations,
            "Record deferred work, unsupported cases, rough edges, and known risks here.\n\n_No known limitations recorded yet._"));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.ExtensionGuideMdPath(_store.WorkflowRoot),
            MarkdownRegion.ExtensionGuide,
            "Record how to extend this project safely: phase patterns, document rules, state files, tests, and integration points.\n\n_No extension guidance recorded yet._"));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.ConsistencyCheckMdPath(_store.WorkflowRoot),
            MarkdownRegion.ConsistencyCheck,
            GenerateConsistencyCheck(summary)));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.FinalReviewMdPath(_store.WorkflowRoot),
            MarkdownRegion.FinalReview,
            "Record the final review, release/readiness decision, unresolved risks, validation status, and final sign-off here.\n\n_No final review recorded yet._"));
        written.AddRange(SyncAggregateLogMd(
            WorkflowLayout.PromptHeaderMdPath(_store.WorkflowRoot),
            MarkdownRegion.PromptHeader,
            "Read `.workflowkit/workflow/Rules.md` before you answer, edit files, run `bfwf`, or call workflow tools. If the Rules file or required workflow tool calls are unavailable, stop and tell the user exactly what is blocked.\n\nJSON/JSONL under `.workflowkit/` is the source of truth. Markdown is generated context and must not replace structured state updates."));

        return written;
    }

    private IEnumerable<string> SyncAggregateLogMd(
        string path,
        string section,
        string content)
    {
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [section] = content
        };

        return WriteIfChanged(path, current, sections);
    }

    private IEnumerable<string> SyncCurrentStatusMd(WorkflowDashboardSummary summary)
    {
        string path = WorkflowLayout.CurrentStatusMdPath(_store.WorkflowRoot);
        string current = SafeReadFile(path);

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.CurrentStatus] = _dashboardStatus.Generate(summary)
        };

        return WriteIfChanged(path, current, sections);
    }

    private static string GenerateConsistencyCheck(WorkflowDashboardSummary summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Project:** {summary.AssetName}  ");
        sb.AppendLine($"**Workflow ID:** {summary.WorkflowId}  ");
        sb.AppendLine($"**Total phases:** {summary.TotalPhases}  ");
        sb.AppendLine($"**Completed phases:** {summary.CompletedPhases}  ");
        sb.AppendLine($"**Blocked phases:** {summary.BlockedPhases}  ");
        sb.AppendLine($"**Failed phases:** {summary.FailedPhases}  ");
        sb.AppendLine($"**Overall progress:** {summary.OverallProgressPercent}%  ");
        sb.AppendLine();
        sb.AppendLine("## Checks");
        sb.AppendLine();
        sb.AppendLine("- Workflow state file exists.");
        sb.AppendLine("- Phase summaries loaded from structured phase JSON.");
        sb.AppendLine("- Dashboard progress calculated from phase states.");
        sb.AppendLine("- Logs should be verified against `.workflowkit/logs/*.jsonl` before final review.");

        return sb.ToString().TrimEnd();
    }

    private IEnumerable<string> SyncPhaseMd(
        Phase                        phase,
        WorkflowState                workflow,
        IReadOnlyList<ImplementationRecord> allImpls,
        IReadOnlyList<AuditRecord>          allAudits,
        IReadOnlyList<ReviewRecord>         allReviews,
        IReadOnlyList<TestRecord>           allTests,
        IReadOnlyList<BlockerRecord>        allBlockers)
    {
        string path    = WorkflowLayout.PhaseMdPath(_store.WorkflowRoot, phase.PhaseId);
        string current = SafeReadFile(path);

        // Filter global logs to only this phase's records.
        var implLogs   = allImpls  .Where(r => r.PhaseId == phase.PhaseId).ToList();
        var auditLogs  = allAudits .Where(r => r.PhaseId == phase.PhaseId).ToList();
        var reviewLogs = allReviews.Where(r => r.PhaseId == phase.PhaseId).ToList();
        var testLogs   = allTests  .Where(r => r.PhaseId == phase.PhaseId).ToList();
        var blockers   = OpenBlockers(
                         allBlockers.Where(b => b.PhaseId == phase.PhaseId));

        // Show the global next action only if it targets this specific phase.
        NextAction? nextAction = (workflow.NextAction?.PhaseId == phase.PhaseId)
                                 ? workflow.NextAction
                                 : null;

        var sections = new Dictionary<string, string>
        {
            [MarkdownRegion.PhaseContract]      = _contract.Generate(phase, phase.Contract),
            [MarkdownRegion.ImplementationLog]  = _implLog .Generate(implLogs),
            [MarkdownRegion.AuditLog]           = _auditLog.Generate(auditLogs),
            [MarkdownRegion.ReviewLog]          = _reviewLog.Generate(reviewLogs),
            [MarkdownRegion.TestingLog]         = _testLog  .Generate(testLogs),
            [MarkdownRegion.CurrentStatus]      = _status   .Generate(phase, nextAction, blockers)
        };

        return WriteIfChanged(path, current, sections);
    }

    // -- Helpers ---------------------------------------------------------------

    private IEnumerable<string> WriteIfChanged(
        string path,
        string current,
        IReadOnlyDictionary<string, string> sections)
    {
        string merged = _preserver.Merge(current, sections);

        if (merged == current)
            return Array.Empty<string>();

        // Ensure the parent directory exists.
        string? dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Atomic write: write to a temp file then move.
        string tmp = Path.Combine(
            dir ?? Path.GetTempPath(),
            $".tmp_{Path.GetFileName(path)}_{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(tmp, merged, System.Text.Encoding.UTF8);
            File.Move(tmp, path, overwrite: true);
            return new[] { path };
        }
        catch
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
            throw;
        }
    }

    private static string SafeReadFile(string path)
    {
        if (!File.Exists(path))
            return string.Empty;
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// Returns only open (unresolved) blockers from the filtered set.
    /// Because the JSONL is append-only, the last entry per BlockerId wins.
    /// </summary>
    private static IReadOnlyList<BlockerRecord> OpenBlockers(
        IEnumerable<BlockerRecord> blockers)
    {
        // Last-write wins per BlockerId.
        var latest = new Dictionary<string, BlockerRecord>();
        foreach (var b in blockers)
            latest[b.BlockerId] = b;

        return latest.Values
                     .Where(b => !b.IsResolved)
                     .OrderBy(b => b.BlockerId)
                     .ToList();
    }
}
