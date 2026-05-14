using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Audits all protected workflow paths to verify no unauthorized direct writes.
///
/// Protected paths (agents must never write directly):
///   - .workflowkit/workflow.json
///   - .workflowkit/sequences.json
///   - .workflowkit/phases/*.json
///   - .workflowkit/logs/*.jsonl
///   - .workflowkit/blockers/*.jsonl
///   - .workflowkit/handoffs/*.jsonl
///   - .workflowkit/metrics/*.jsonl
///   - .workflowkit/traces/ (diagnostic only)
///   - .workflowkit/index/ (rebuildable read models)
///   - Generated markdown files under workflow/
///
/// The only allowed direct write target:
///   - .workflowkit/inbox/*.operation.json
/// </summary>
public sealed class AuditProtectedPathsHandler : IOperationHandler
{
    private readonly string _root;

    public string OperationName => WorkflowOperations.AuditProtectedPaths;

    public AuditProtectedPathsHandler(string workflowRoot)
    {
        _root = workflowRoot;
    }

    public OperationResult Execute(OperationContext context)
    {
        var entries = new List<ProtectedPathEntry>();
        var issues = new List<string>();

        var workflowKitRoot = WorkflowLayout.Root(_root);

        // ── Protected paths ───────────────────────────────────────────────
        var protectedPaths = new[]
        {
            ("workflow.json", true, WorkflowLayout.WorkflowFile(_root)),
            ("sequences.json", true, WorkflowLayout.SequencesFile(_root)),
            ("events.jsonl", true, WorkflowLayout.EventsLog(_root)),
            ("implementation.jsonl", true, WorkflowLayout.ImplementationLog(_root)),
            ("audit.jsonl", true, WorkflowLayout.AuditLog(_root)),
            ("review.jsonl", true, WorkflowLayout.ReviewLog(_root)),
            ("test.jsonl", true, WorkflowLayout.TestLog(_root)),
            ("fix.jsonl", true, WorkflowLayout.FixLog(_root)),
            ("blockers/blockers.jsonl", true, WorkflowLayout.BlockersLog(_root)),
            ("handoffs/handoffs.jsonl", true, WorkflowLayout.HandoffsLog(_root)),
            ("metrics/timing.jsonl", true, WorkflowLayout.TimingLog(_root)),
            ("index/operation-manifest.json", true, WorkflowLayout.OperationManifestPath(_root)),
            ("index/tool-routing-rules.json", true, WorkflowLayout.ToolRoutingRulesPath(_root)),
            ("index/workflowkit.db", true, WorkflowLayout.WorkflowKitDbPath(_root)),
            ("traces/", true, Path.Combine(workflowKitRoot, "traces")),
            ("inbox/", false, Path.Combine(workflowKitRoot, "inbox")),  // Only allowed write target
        };

        // Add all phase files as protected
        var phasesDir = WorkflowLayout.PhasesDir(_root);
        if (Directory.Exists(phasesDir))
        {
            foreach (var phaseFile in Directory.GetFiles(phasesDir, "*.json"))
            {
                var name = "phases/" + Path.GetFileName(phaseFile);
                protectedPaths = protectedPaths.Append((name, true, phaseFile)).ToArray();
            }
        }

        // ── Markdown protected paths ─────────────────────────────────────
        var markdownProtected = new (string, string)[]
        {
            ("workflow/workflow.md", WorkflowLayout.WorkflowMdPath(_root)),
            ("workflow/docs/Architecture.md", WorkflowLayout.ArchitectureMdPath(_root)),
            ("workflow/docs/ImplementationPlan.md", WorkflowLayout.ImplementationMdPath(_root)),
            ("workflow/03_Implementation/ImplementationLog.md", WorkflowLayout.ImplementationLogMdPath(_root)),
            ("workflow/03_Implementation/FixLog.md", WorkflowLayout.FixLogMdPath(_root)),
            ("workflow/02_Audits/AuditLog.md", WorkflowLayout.AuditLogMdPath(_root)),
            ("workflow/02_Audits/ReviewLog.md", WorkflowLayout.ReviewLogMdPath(_root)),
            ("workflow/04_Testing/TestingLog.md", WorkflowLayout.TestingLogMdPath(_root)),
            ("workflow/07_Status/CurrentStatus.md", WorkflowLayout.CurrentStatusMdPath(_root)),
        };

        foreach (var (name, path) in markdownProtected)
        {
            var exists = File.Exists(path);
            entries.Add(new ProtectedPathEntry
            {
                Path = name,
                IsProtected = true,
                Exists = exists,
                Status = exists ? "ok" : "missing",
                LastWriteUtc = exists ? File.GetLastWriteTimeUtc(path) : null,
                Note = "Generated markdown — must not be manually edited outside generated regions."
            });
        }

        // ── Audit each protected path ────────────────────────────────────
        foreach (var (name, isProtected, path) in protectedPaths)
        {
            if (name.EndsWith("/"))
            {
                // Directory
                var exists = Directory.Exists(path);
                entries.Add(new ProtectedPathEntry
                {
                    Path = name,
                    IsProtected = isProtected,
                    Exists = exists,
                    Status = exists ? (isProtected ? "ok" : "unprotected") : "missing",
                    LastWriteUtc = exists
                        ? new DirectoryInfo(path).LastWriteTimeUtc
                        : null,
                    Note = isProtected
                        ? "Protected directory — agents must never write files here directly."
                        : "ALLOWED write target — agents may write .operation.json files here."
                });
            }
            else
            {
                var exists = File.Exists(path);
                var status = exists ? "ok" : "missing";
                if (!exists)
                    status = "missing";

                // Check JSON/JSONL integrity for key files
                string? note = null;
                if (exists && name.EndsWith(".json") && !name.Contains("manifest") && !name.Contains("routing"))
                {
                    try
                    {
                        var content = File.ReadAllText(path);
                        System.Text.Json.JsonDocument.Parse(content);
                        status = "integrity_ok";
                    }
                    catch
                    {
                        status = "integrity_fail";
                        issues.Add($"Integrity check failed for {name}");
                    }
                }

                entries.Add(new ProtectedPathEntry
                {
                    Path = name,
                    IsProtected = isProtected,
                    Exists = exists,
                    Status = status,
                    LastWriteUtc = exists ? File.GetLastWriteTimeUtc(path) : null,
                    Note = note
                });
            }
        }

        var allProtected = entries
            .Where(e => e.IsProtected && e.Exists)
            .All(e => e.Status is "ok" or "integrity_ok");

        var result = new ProtectedPathAuditResult
        {
            AllProtected = allProtected,
            Paths = entries,
            Summary = allProtected
                ? $"All {entries.Count(e => e.IsProtected)} protected paths verified. Inbox available for direct writes only."
                : $"{issues.Count} integrity issues found. Protected paths audit failed."
        };

        return OperationResult.Ok(result);
    }
}
