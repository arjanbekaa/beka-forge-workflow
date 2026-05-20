using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Markdown;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.sync_markdown</c>.
///
/// Regenerates all markdown files (AGENTS.md, workflow.md, per-phase docs),
/// merging generated regions while preserving human-written content.
///
/// Returns the list of file paths that were written.
/// </summary>
internal sealed class SyncMarkdownHandler : IOperationHandler
{
    private readonly WorkflowStore _store;

    public string OperationName => WorkflowOperations.SyncMarkdown;

    public SyncMarkdownHandler(WorkflowStore store)
    {
        _store = store;
    }

    public OperationResult Execute(OperationContext context)
    {
        var service = new MarkdownSyncService(_store);
        var written = service.SyncAll();
        var refreshed = RefreshManagedMarkdownTimestamps(_store.WorkflowRoot);

        // Also export the generated operation manifest and routing rules.
        try
        {
            OperationManifestCatalog.ExportToFile(_store.WorkflowRoot);
            ToolRoutingCatalog.ExportToFile(_store.WorkflowRoot);
        }
        catch
        {
            // Manifest and routing export are best-effort during sync.
        }

        var touchedOnly = refreshed
            .Where(path => !written.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        string msg = written.Count == 0 && touchedOnly.Length == 0
            ? "All markdown files are already up to date."
            : written.Count == 0
                ? $"Refreshed {touchedOnly.Length} unchanged markdown file(s)."
                : touchedOnly.Length == 0
                    ? $"Synced {written.Count} file(s): {string.Join(", ", written.Select(Path.GetFileName))}"
                    : $"Synced {written.Count} file(s) and refreshed {touchedOnly.Length} unchanged markdown file(s).";

        return OperationResult.Ok(new
        {
            writtenFiles = written,
            refreshedFiles = touchedOnly
        }, msg);
    }

    private static IReadOnlyList<string> RefreshManagedMarkdownTimestamps(string workflowRoot)
    {
        var managedPaths = EnumerateManagedMarkdownPaths(workflowRoot)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var now = DateTime.UtcNow;
        foreach (var path in managedPaths)
            File.SetLastWriteTimeUtc(path, now);

        return managedPaths;
    }

    private static IEnumerable<string> EnumerateManagedMarkdownPaths(string workflowRoot)
    {
        yield return WorkflowLayout.AgentsMdPath(workflowRoot);
        yield return WorkflowLayout.ClaudeMdPath(workflowRoot);
        yield return WorkflowLayout.RulesMdPath(workflowRoot);
        yield return WorkflowLayout.BekaWorkflowSystemPromptPath(workflowRoot);
        yield return WorkflowLayout.WorkflowMdPath(workflowRoot);
        yield return WorkflowLayout.ArchitectureMdPath(workflowRoot);
        yield return WorkflowLayout.ImplementationPlanMdPath(workflowRoot);
        yield return WorkflowLayout.ImplementationLogMdPath(workflowRoot);
        yield return WorkflowLayout.FixLogMdPath(workflowRoot);
        yield return WorkflowLayout.AuditLogMdPath(workflowRoot);
        yield return WorkflowLayout.ReviewLogMdPath(workflowRoot);
        yield return WorkflowLayout.TestingLogMdPath(workflowRoot);
        yield return WorkflowLayout.CurrentStatusMdPath(workflowRoot);
        yield return WorkflowLayout.MigrationNotesMdPath(workflowRoot);
        yield return WorkflowLayout.ExtractionAuditMdPath(workflowRoot);
        yield return WorkflowLayout.KnownLimitationsMdPath(workflowRoot);
        yield return WorkflowLayout.ExtensionGuideMdPath(workflowRoot);
        yield return WorkflowLayout.ConsistencyCheckMdPath(workflowRoot);
        yield return WorkflowLayout.FinalReviewMdPath(workflowRoot);
        yield return WorkflowLayout.PromptHeaderMdPath(workflowRoot);

        var phasesDir = WorkflowLayout.PhasesDir(workflowRoot);
        if (!Directory.Exists(phasesDir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(phasesDir, "PHASE-*.json"))
        {
            var phaseId = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(phaseId))
                yield return WorkflowLayout.PhaseMdPath(workflowRoot, phaseId);
        }
    }
}
