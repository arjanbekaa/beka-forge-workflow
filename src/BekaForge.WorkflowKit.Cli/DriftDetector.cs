using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Detects workflow drift — situations where the recorded phase state has
/// diverged from what is likely happening on disk or in practice.
///
/// "Drift" categories:
/// 1. <b>Stale</b>: a phase is in an active-write state but UpdatedUtc exceeds
///    the <see cref="DefaultThreshold"/> (default: 24 h). Work may have stalled.
/// 2. <b>MissingFiles</b>: RequiredFilesOrAreas declares paths that do not exist
///    on disk (file-existence checks are performed inline via <see cref="Check"/>).
/// 3. <b>OrphanedLock</b>: the .workflowkit/work/ lock file for the phase is stale
///    (its LastUpdatedUtc exceeds <see cref="WorkSession.StaleThreshold"/>).
///
/// No I/O except for checking file existence — never mutates state.
/// </summary>
public static class DriftDetector
{
    /// <summary>Default staleness threshold for active phases.</summary>
    public static TimeSpan DefaultThreshold { get; } = TimeSpan.FromHours(24);

    /// <summary>Phase states that are considered "actively progressing" for drift purposes.</summary>
    public static readonly IReadOnlySet<PhaseState> ActiveProgressStates = new HashSet<PhaseState>
    {
        PhaseState.AssignedToImplementation,
        PhaseState.InImplementation,
        PhaseState.ReviewInProgress,
        PhaseState.TestInProgress,
        PhaseState.FixInProgress
    };

    /// <summary>Summary of drift detected for one phase.</summary>
    public sealed record DriftResult(
        string PhaseId,
        string Title,
        string State,
        bool HasDrift,
        IReadOnlyList<DriftFinding> Findings);

    /// <summary>A single drift finding.</summary>
    public sealed record DriftFinding(string Category, string Message);

    /// <summary>
    /// Checks a single phase for drift signals.
    /// </summary>
    /// <param name="phase">Phase to inspect.</param>
    /// <param name="threshold">Max age before a phase in an active state is considered stale.</param>
    /// <param name="workflowRoot">Repo root for file-existence checks (null = skip file checks).</param>
    /// <param name="lockFilePath">Path to the work-lock JSON file (null = skip lock check).</param>
    public static DriftResult Check(
        Phase phase,
        TimeSpan? threshold = null,
        string? workflowRoot = null,
        string? lockFilePath = null)
    {
        var findings = new List<DriftFinding>();
        var th = threshold ?? DefaultThreshold;

        // 1. Stale active state
        if (ActiveProgressStates.Contains(phase.State))
        {
            var age = DateTimeOffset.UtcNow - phase.UpdatedUtc;
            if (age > th)
                findings.Add(new DriftFinding("Stale",
                    $"Phase has been in {phase.State} for {age.TotalHours:F0}h " +
                    $"(threshold: {th.TotalHours:F0}h). Work may have stalled."));
        }

        // 2. File manifest drift (only when workflowRoot is provided and contract has areas)
        if (workflowRoot is not null && phase.Contract?.RequiredFilesOrAreas.Count > 0)
        {
            foreach (var area in phase.Contract.RequiredFilesOrAreas)
            {
                var (exists, isDir) = CheckPathExists(workflowRoot, area);
                if (!exists)
                    findings.Add(new DriftFinding("MissingFile",
                        $"RequiredFilesOrAreas declares '{area}' but it does not exist on disk."));
            }
        }

        // 3. Orphaned lock
        if (lockFilePath is not null)
        {
            var session = WorkSession.TryLoad(lockFilePath);
            if (session is not null && session.IsStale)
                findings.Add(new DriftFinding("OrphanedLock",
                    $"Work lock held by {session.Actor} ({session.Role}) is stale " +
                    $"(last updated {session.LastUpdatedUtc:yyyy-MM-dd HH:mm} UTC)."));
        }

        return new DriftResult(
            PhaseId:  phase.PhaseId,
            Title:    phase.Title,
            State:    phase.State.ToString(),
            HasDrift: findings.Count > 0,
            Findings: findings);
    }

    /// <summary>Checks all phases for drift signals.</summary>
    public static IReadOnlyList<DriftResult> CheckAll(
        IReadOnlyList<Phase> phases,
        TimeSpan? threshold = null,
        string? workflowRoot = null)
    {
        return phases
            .Select(p =>
            {
                string? lockPath = workflowRoot is not null
                    ? WorkflowLayout.WorkLockPath(workflowRoot, p.PhaseId)
                    : null;
                return Check(p, threshold, workflowRoot, lockPath);
            })
            .Where(r => r.HasDrift)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (bool exists, bool isDir) CheckPathExists(string root, string area)
    {
        // Normalise: replace backslashes with forward slashes
        var normalized = area.Replace('\\', '/').TrimEnd('/');

        // Try as relative path from root
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        if (Directory.Exists(full)) return (true, true);
        if (File.Exists(full))      return (true, false);

        // Try the declared path as-is (might be absolute)
        if (Path.IsPathRooted(area))
        {
            if (Directory.Exists(area)) return (true, true);
            if (File.Exists(area))      return (true, false);
        }

        return (false, false);
    }
}
