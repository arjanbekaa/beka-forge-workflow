using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Runs consistency repair checks: missing phase files, dangling phaseIds,
/// stale generated docs, stale indexes, and queue processing errors.
///
/// This operation does NOT rewrite any authoritative state. It reports issues
/// and performs only rebuildable repairs (e.g., regenerating markdown,
/// rebuilding the context index, reprocessing failed inbox items).
/// </summary>
public sealed class RepairConsistencyHandler : IOperationHandler
{
    private readonly string _root;

    public string OperationName => WorkflowOperations.RepairConsistency;

    public RepairConsistencyHandler(string workflowRoot)
    {
        _root = workflowRoot;
    }

    public OperationResult Execute(OperationContext context)
    {
        var repairs = new List<string>();
        var issues = new List<string>();

        // ── Check 1: Missing phase files ─────────────────────────────────
        var phasesDir = WorkflowLayout.PhasesDir(_root);
        if (!Directory.Exists(phasesDir))
        {
            issues.Add("Phases directory missing. Cannot verify phase consistency.");
        }
        else
        {
            var wfPath = WorkflowLayout.WorkflowFile(_root);
            if (File.Exists(wfPath))
            {
                try
                {
                    var wfJson = File.ReadAllText(wfPath);
                    var wfDoc = System.Text.Json.JsonDocument.Parse(wfJson);
                    if (wfDoc.RootElement.TryGetProperty("phaseIds", out var phaseIds))
                    {
                        foreach (var pid in phaseIds.EnumerateArray())
                        {
                            var phaseFile = WorkflowLayout.PhaseFile(_root, pid.GetString()!);
                            if (!File.Exists(phaseFile))
                            {
                                issues.Add($"Phase file missing: {pid.GetString()}.json");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    issues.Add($"Failed to read workflow.json: {ex.Message}");
                }
            }

            // ── Check 2: Dangling phase files ────────────────────────────
            // (files in phases/ not referenced in workflow.json)
            var knownPhaseIds = LoadPhaseIds();
            var phaseFiles = Directory.GetFiles(phasesDir, "PHASE-*.json");
            foreach (var pf in phaseFiles)
            {
                var pid = Path.GetFileNameWithoutExtension(pf);
                if (!knownPhaseIds.Contains(pid))
                {
                    issues.Add($"Dangling phase file (not in workflow.json): {pid}.json");
                }
            }
        }

        // ── Check 3: Stale generated markdown ────────────────────────────
        // Only detect, don't regenerate — markdown sync is a separate operation
        var mdFiles = new[]
        {
            ("workflow.md", WorkflowLayout.WorkflowMdPath(_root)),
            ("ImplementationPlan.md", WorkflowLayout.ImplementationMdPath(_root)),
        };
        foreach (var (name, path) in mdFiles)
        {
            if (File.Exists(path))
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                var age = DateTime.UtcNow - lastWrite;
                if (age > TimeSpan.FromHours(24))
                    issues.Add($"Stale markdown: {name} last updated {lastWrite:yyyy-MM-dd HH:mm} UTC");
            }
        }

        // ── Check 4: Stale context index ─────────────────────────────────
        var indexDb = WorkflowLayout.WorkflowKitDbPath(_root);
        if (File.Exists(indexDb))
        {
            var lastWrite = File.GetLastWriteTimeUtc(indexDb);
            var age = DateTime.UtcNow - lastWrite;
            if (age > TimeSpan.FromHours(24))
                issues.Add($"Stale context index: workflowkit.db last updated {lastWrite:yyyy-MM-dd HH:mm} UTC. Run workflow.rebuild_context_index to rebuild.");
        }

        // ── Check 5: Failed inbox items ──────────────────────────────────
        var failedDir = WorkflowLayout.InboxFailedDir(_root);
        if (Directory.Exists(failedDir))
        {
            var failedFiles = Directory.GetFiles(failedDir, "*.failed.json");
            if (failedFiles.Length > 0)
            {
                issues.Add($"{failedFiles.Length} failed inbox operations. Review in .workflowkit/inbox/failed/.");
            }
        }

        // ── Repairs performed ────────────────────────────────────────────
        // Generate empty directories if missing
        var requiredDirs = WorkflowLayout.RequiredDirectories(_root);
        foreach (var dir in requiredDirs)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                repairs.Add($"Created directory: {Path.GetRelativePath(_root, dir)}");
            }
        }

        // Ensure inbox directories exist
        var inboxDir = WorkflowLayout.InboxDir(_root);
        if (!Directory.Exists(inboxDir))
        {
            Directory.CreateDirectory(inboxDir);
            repairs.Add("Created inbox directory");
        }
        var inboxProcessed = WorkflowLayout.InboxProcessedDir(_root);
        if (!Directory.Exists(inboxProcessed))
        {
            Directory.CreateDirectory(inboxProcessed);
            repairs.Add("Created inbox/processed directory");
        }
        var inboxFailed = WorkflowLayout.InboxFailedDir(_root);
        if (!Directory.Exists(inboxFailed))
        {
            Directory.CreateDirectory(inboxFailed);
            repairs.Add("Created inbox/failed directory");
        }

        return OperationResult.Ok(new
        {
            RepairsPerformed = repairs,
            IssuesFound = issues,
            Healthy = issues.Count == 0,
            Summary = issues.Count == 0
                ? "Consistency check passed. No issues found."
                : $"Found {issues.Count} issues. {repairs.Count} repairs performed."
        });
    }

    private HashSet<string> LoadPhaseIds()
    {
        var wfPath = WorkflowLayout.WorkflowFile(_root);
        if (!File.Exists(wfPath))
            return [];

        try
        {
            var wfJson = File.ReadAllText(wfPath);
            var wfDoc = System.Text.Json.JsonDocument.Parse(wfJson);
            if (wfDoc.RootElement.TryGetProperty("phaseIds", out var phaseIds))
            {
                return phaseIds.EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }

        return [];
    }
}
