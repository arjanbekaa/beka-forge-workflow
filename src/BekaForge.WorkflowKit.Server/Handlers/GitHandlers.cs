using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Git;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Returns the current git worktree status: branch, dirty, ahead/behind, latest commit.</summary>
public sealed class GetGitStatusHandler(GitStore gitStore, GitService gitService) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetGitStatus;

    public OperationResult Execute(OperationContext context)
    {
        var status = gitService.GetStatus();
        return OperationResult.Ok(status);
    }
}

/// <summary>Lists recent git commits with optional phase filter.</summary>
public sealed class ListGitCommitsHandler(GitService gitService) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ListGitCommits;

    public OperationResult Execute(OperationContext context)
    {
        var maxCount = context.Get<int?>("maxCount") ?? 50;
        var since = context.GetString("since");
        var branch = context.GetString("branch");
        var phaseId = context.GetString("phaseId");

        var commits = gitService.ListCommits(
            maxCount: Math.Min(maxCount, 200),
            since: since,
            branch: branch);

        // Phase filter (post-fetch, since commit message convention is simple regex)
        if (!string.IsNullOrWhiteSpace(phaseId))
        {
            var ph = phaseId.ToUpperInvariant();
            commits = commits
                .Where(c => c.PhaseId is not null &&
                            c.PhaseId.Equals(ph, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return OperationResult.Ok(new { commits, count = commits.Count });
    }
}

/// <summary>Returns git activity records from the activity ledger with optional filters.</summary>
public sealed class GetGitActivityHandler(GitStore gitStore) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetGitActivity;

    public OperationResult Execute(OperationContext context)
    {
        var maxResults = context.Get<int?>("maxResults") ?? 100;
        var phaseId = context.GetString("phaseId");
        var sessionId = context.GetString("sessionId");
        var activityType = context.GetString("activityType");

        var records = gitStore.ListActivity(
            maxResults: Math.Min(maxResults, 500),
            phaseId: phaseId,
            sessionId: sessionId,
            activityType: activityType);

        return OperationResult.Ok(new { activity = records, count = records.Count });
    }
}

/// <summary>Lists all local git branches with tracking and divergence information.</summary>
public sealed class GetGitBranchInfoHandler(GitService gitService) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetGitBranchInfo;

    public OperationResult Execute(OperationContext context)
    {
        var branches = gitService.ListBranches();
        var current = branches.FirstOrDefault(b => b.IsCurrent);

        return OperationResult.Ok(new
        {
            branches,
            currentBranch = current?.Name,
            branchCount = branches.Count
        });
    }
}

/// <summary>Returns git/workflow health warnings: dirty tree, stale phases, missing gates, unpushed work.</summary>
public sealed class GetGitHealthHandler(GitService gitService, WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetGitHealth;

    public OperationResult Execute(OperationContext context)
    {
        var warnings = new List<GitHealthWarning>();
        var status = gitService.GetStatus();

        if (!status.GitAvailable)
        {
            return OperationResult.Ok(new
            {
                gitAvailable = false,
                warnings,
                message = status.GitError ?? "Git is not available."
            });
        }

        // Warning 1: Dirty worktree
        if (status.IsDirty)
            warnings.Add(new GitHealthWarning(
                "dirty_worktree",
                "Warning",
                $"Worktree has {status.TotalChangedCount} uncommitted changes ({status.StagedCount} staged, {status.UnstagedCount} unstaged, {status.UntrackedCount} untracked)."));

        // Warning 2: Stale dirty worktree (>1 hour since last commit)
        var timeSince = gitService.TimeSinceLastCommit();
        if (status.IsDirty && timeSince.HasValue && timeSince.Value.TotalHours > 1)
            warnings.Add(new GitHealthWarning(
                "stale_dirty",
                "Caution",
                $"Worktree has been dirty for {timeSince.Value.TotalHours:F1} hours. Consider committing or stashing."));

        // Warning 3: Unpushed work
        if (status.AheadCount > 0)
            warnings.Add(new GitHealthWarning(
                "unpushed",
                "Warning",
                $"Local branch is {status.AheadCount} commit(s) ahead of remote."));

        // Warning 4: Behind remote
        if (status.BehindCount > 0)
            warnings.Add(new GitHealthWarning(
                "behind_remote",
                "Caution",
                $"Local branch is {status.BehindCount} commit(s) behind remote. Consider pulling."));

        // Warning 5: Stale active phases (no commits in 7 days)
        if (store.IsInitialized())
        {
            var workflow = store.LoadWorkflow();
            var phases = store.LoadAllPhases();
            foreach (var phase in phases)
            {
                if (phase.State == PhaseState.InImplementation && phase.StartedUtc.HasValue)
                {
                    var age = DateTimeOffset.UtcNow - phase.StartedUtc.Value;
                    if (age.TotalDays > 7)
                        warnings.Add(new GitHealthWarning(
                            "stale_phase",
                            "Warning",
                            $"Phase {phase.PhaseId} ({phase.Title}) has been in implementation for {age.TotalDays:F0} days with no recent commits."));
                }
            }
        }

        return OperationResult.Ok(new
        {
            gitAvailable = true,
            branch = status.BranchName,
            isDirty = status.IsDirty,
            aheadCount = status.AheadCount,
            behindCount = status.BehindCount,
            timeSinceLastCommitMinutes = timeSince?.TotalMinutes,
            warningCount = warnings.Count,
            warnings
        });
    }
}

/// <summary>Snapshots the current git state into the activity ledger. Append operation.</summary>
public sealed class RecordGitActivityHandler(GitService gitService, GitStore gitStore) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RecordGitActivity;

    public OperationResult Execute(OperationContext context)
    {
        if (!gitService.IsAvailable)
            return OperationResult.Fail("GitUnavailable", "Git CLI not found or not a git repository.");

        var status = gitService.GetStatus();
        var sessionId = context.GetString("sessionId");

        // Record status snapshot
        gitStore.AppendStatus(status, sessionId);

        // Record recent commits (up to 10)
        var commits = gitService.ListCommits(maxCount: 10);
        foreach (var commit in commits)
        {
            // Only record commits we haven't seen before (simple check by SHA)
            var existing = gitStore.ListActivity(maxResults: 500, activityType: "commit");
            if (!existing.Any(r => string.Equals(r.CommitSha, commit.Sha, StringComparison.OrdinalIgnoreCase)))
                gitStore.AppendCommit(commit, sessionId);
        }

        return OperationResult.Ok(new
        {
            recorded = true,
            statusSnapshot = true,
            newCommitsRecorded = commits.Count,
            timestamp = DateTimeOffset.UtcNow
        });
    }
}

/// <summary>Lightweight health warning record for the get_git_health response.</summary>
public sealed record GitHealthWarning(string Code, string Severity, string Message);
