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

        string msg = written.Count == 0
            ? "All markdown files are already up to date."
            : $"Synced {written.Count} file(s): {string.Join(", ", written.Select(Path.GetFileName))}";

        return OperationResult.Ok(written, msg);
    }
}
