using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Initializes a new .bekaforge/ workflow at a given root path.
///
/// Creates the full directory layout and writes an initial workflow.json.
/// Existing files are never deleted - only missing directories and files are created.
/// </summary>
public sealed class WorkflowInitializer
{
    private readonly WorkflowStateRepository _stateRepo;
    private readonly string _workflowRoot;

    public WorkflowInitializer(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        _stateRepo = new WorkflowStateRepository(workflowRoot);
    }

    /// <summary>
    /// Initializes a new workflow at the configured root.
    /// </summary>
    /// <param name="assetName">Name of the Beka Forge asset being developed.</param>
    /// <param name="force">
    /// If true, overwrites an existing workflow.json.
    /// If false, throws <see cref="InvalidOperationException"/> if already initialized.
    /// </param>
    /// <returns>The initial WorkflowState written to disk.</returns>
    public WorkflowState Initialize(string assetName, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            throw new ArgumentException("Asset name must not be empty.", nameof(assetName));

        if (_stateRepo.Exists() && !force)
            throw new InvalidOperationException(
                $"A workflow already exists at '{_workflowRoot}'. Use force=true to reinitialize.");

        foreach (var dir in WorkflowLayout.RequiredDirectories(_workflowRoot))
            Directory.CreateDirectory(dir);
        foreach (var dir in WorkflowLayout.RequiredMarkdownDirectories(_workflowRoot))
            Directory.CreateDirectory(dir);

        var state = new WorkflowState
        {
            SchemaVersion = WorkflowLayout.SchemaVersion,
            WorkflowId = $"wf-{Guid.NewGuid():N}",
            AssetName = assetName.Trim(),
            RootPath = Path.GetFullPath(_workflowRoot),
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        _stateRepo.Save(state);
        CreateStarterMarkdownFiles(state.AssetName);
        return state;
    }

    /// <summary>
    /// Returns true if a workflow has already been initialized at this root.
    /// </summary>
    public bool IsInitialized() => _stateRepo.Exists();

    private void CreateStarterMarkdownFiles(string assetName)
    {
;
        var agentsContent =
            "# Agent Instructions\n\n" +
            "<!-- BEKAFORGE:BEGIN generated:agents-roles -->\n" +
            "## Beka Forge Workflow\n\n" +
            "**STOP. Read `.workflowkit/workflow/Rules.md` NOW before doing anything else.**\n" +
            "This is not optional. Return here only after you have read and understood it.\n" +
            "\n" +
            "### Validation Honesty Rule\n" +
            "Do not log a test as passed unless it actually ran. If you cannot run\n" +
            "validation, ask the user. No fake passes.\n" +
            "<!-- BEKAFORGE:END generated:agents-roles -->\n\n";
        WriteIfMissing(WorkflowLayout.AgentsMdPath(_workflowRoot), agentsContent);
        WriteIfMissing(WorkflowLayout.ClaudeMdPath(_workflowRoot),
            "# Claude Code Instructions\n\n" +
            agentsContent.Substring(agentsContent.IndexOf("<!-- BEKAFORGE:BEGIN")));
        WriteIfMissing(WorkflowLayout.WorkflowMdPath(_workflowRoot),
            $"# {assetName} Workflow\n\n");
        WriteIfMissing(WorkflowLayout.RulesMdPath(_workflowRoot),
            "# Workflow Rules\n\n" +
            "<!-- BEKAFORGE:BEGIN generated:workflowkit-system-prompt -->\n" +
            "Run `bfwf sync-markdown` to populate.\n" +
            "<!-- BEKAFORGE:END generated:workflowkit-system-prompt -->\n\n");
        WriteIfMissing(WorkflowLayout.ArchitectureMdPath(_workflowRoot),
            $"# {assetName} Architecture\n\n");
        WriteIfMissing(WorkflowLayout.ImplementationMdPath(_workflowRoot),
            $"# {assetName} Implementation Plan\n\n");
        WriteIfMissing(WorkflowLayout.MigrationNotesMdPath(_workflowRoot),
            "# Migration Notes\n\n");
        WriteIfMissing(WorkflowLayout.ExtractionAuditMdPath(_workflowRoot),
            "# Extraction Audit\n\n");
        WriteIfMissing(WorkflowLayout.KnownLimitationsMdPath(_workflowRoot),
            "# Known Limitations\n\n");
        WriteIfMissing(WorkflowLayout.ExtensionGuideMdPath(_workflowRoot),
            "# Extension Guide\n\n");
        WriteIfMissing(WorkflowLayout.ConsistencyCheckMdPath(_workflowRoot),
            "# Consistency Check\n\n");
        WriteIfMissing(WorkflowLayout.FinalReviewMdPath(_workflowRoot),
            "# Final Review\n\n");
        WriteIfMissing(WorkflowLayout.PromptHeaderMdPath(_workflowRoot),
            "# Prompt Header\n\nRead `.workflowkit/workflow/Rules.md` first. This project uses Beka Forge Workflow. JSON/JSONL under `.workflowkit/` is the source of truth. Follow the document and log formats before editing.\n\n");
        WriteIfMissing(WorkflowLayout.ImplementationLogMdPath(_workflowRoot),
            "# Implementation Log\n\n");
        WriteIfMissing(WorkflowLayout.FixLogMdPath(_workflowRoot),
            "# Fix Log\n\n");
        WriteIfMissing(WorkflowLayout.AuditLogMdPath(_workflowRoot),
            "# Audit Log\n\n");
        WriteIfMissing(WorkflowLayout.ReviewLogMdPath(_workflowRoot),
            "# Review Log\n\n");
        WriteIfMissing(WorkflowLayout.TestingLogMdPath(_workflowRoot),
            "# Testing Log\n\n");
        WriteIfMissing(WorkflowLayout.CurrentStatusMdPath(_workflowRoot),
            "# Current Status\n\n");
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (File.Exists(path))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
    }
}
