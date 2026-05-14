using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BekaForge.WorkflowKit.Core.Git;

/// <summary>
/// Read-only wrapper around the local git CLI.
/// All git interactions go through this service — never call git directly elsewhere.
/// No external dependencies (no libgit2sharp). Pure Process.Start wrapper.
///
/// All methods are read-only: log, status, branch, rev-list, config.
/// Never commit, push, pull, or modify the repository.
/// </summary>
public sealed class GitService
{
    private readonly string _workflowRoot;
    private readonly int _timeoutMs;

    /// <summary>
    /// Creates a GitService for a workflow root directory.
    /// </summary>
    /// <param name="workflowRoot">Path to the git repository root (must contain .git).</param>
    /// <param name="timeoutMs">Maximum time to wait for git commands (default 15s).</param>
    public GitService(string workflowRoot, int timeoutMs = 15_000)
    {
        _workflowRoot = workflowRoot;
        _timeoutMs = timeoutMs;
    }

    // ── Availability ────────────────────────────────────────────────────────

    /// <summary>Whether git is installed and the workflow root is a git repository.</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                var result = Run("rev-parse", "--git-dir");
                return result.Success;
            }
            catch
            {
                return false;
            }
        }
    }

    // ── Status ──────────────────────────────────────────────────────────────

    /// <summary>Returns the current git worktree status.</summary>
    public GitStatus GetStatus()
    {
        if (!IsAvailable)
            return GitStatus.Unavailable("Git CLI not found or not a git repository.");

        var branch = GetCurrentBranch();
        var statusResult = Run("status", "--porcelain");
        var aheadBehind = GetAheadBehind(branch);

        int staged = 0, unstaged = 0, untracked = 0;
        if (statusResult.Success)
        {
            foreach (var line in statusResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 2) continue;
                var code = line[..2];
                if (code.Contains('?')) untracked++;
                else if (code[0] != ' ') staged++;
                else unstaged++;
            }
        }

        var latestSha = GetLatestCommitSha();

        return new GitStatus
        {
            BranchName = branch,
            IsDirty = staged + unstaged + untracked > 0,
            StagedCount = staged,
            UnstagedCount = unstaged,
            UntrackedCount = untracked,
            LatestCommitSha = latestSha,
            AheadCount = aheadBehind.ahead,
            BehindCount = aheadBehind.behind,
            GitAvailable = true
        };
    }

    // ── Commits ─────────────────────────────────────────────────────────────

    /// <summary>Lists recent commits from the repository.</summary>
    public IReadOnlyList<GitCommit> ListCommits(int maxCount = 50, string? since = null,
        string? branch = null)
    {
        if (!IsAvailable) return [];

        var args = $"log --max-count={Math.Min(maxCount, 500)} " +
                   $"--format=%H|%an|%ae|%aI|%s|%P";
        if (!string.IsNullOrWhiteSpace(since))
            args += $" --since=\"{since}\"";
        if (!string.IsNullOrWhiteSpace(branch))
            args += $" {branch}";

        var result = Run(args);
        if (!result.Success) return [];

        var commits = new List<GitCommit>();
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 5) continue;

            var sha = parts[0].Trim();
            var message = parts.Length >= 5 ? parts[4].Trim() : "";
            var phaseId = ExtractPhaseId(message);
            var files = GetFilesChanged(sha);

            commits.Add(new GitCommit
            {
                Sha = sha,
                AuthorName = parts[1].Trim(),
                AuthorEmail = parts[2].Trim(),
                AuthorDateUtc = DateTimeOffset.TryParse(parts[3].Trim(), out var dt)
                    ? dt : DateTimeOffset.UtcNow,
                Message = message.Length > 200 ? message[..200] : message,
                FullMessage = GetCommitFullMessage(sha),
                BranchName = branch ?? GetCurrentBranch(),
                ParentShas = parts.Length >= 6
                    ? parts[5].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : [],
                FilesChanged = files.paths,
                FilesAdded = files.added,
                FilesDeleted = files.deleted,
                PhaseId = phaseId
            });
        }

        return commits;
    }

    // ── Branches ────────────────────────────────────────────────────────────

    /// <summary>Lists all local branches with tracking information.</summary>
    public IReadOnlyList<GitBranch> ListBranches()
    {
        if (!IsAvailable) return [];

        var result = Run("branch", "-vv");
        if (!result.Success) return [];

        var branches = new List<GitBranch>();
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var isCurrent = trimmed.StartsWith('*');
            var name = isCurrent ? trimmed[1..].Trim() : trimmed;
            // Extract branch name (first word)
            var nameEnd = name.IndexOf(' ');
            var branchName = nameEnd > 0 ? name[..nameEnd] : name;

            var tracking = "";
            var ahead = 0;
            var behind = 0;
            var sha = (string?)null;

            // Parse tracking info: "[origin/main: ahead 2, behind 1]"
            var trackingMatch = Regex.Match(trimmed, @"\[([^\]]+)\]");
            if (trackingMatch.Success)
            {
                tracking = trackingMatch.Groups[1].Value;
                var aheadMatch = Regex.Match(tracking, @"ahead\s+(\d+)");
                var behindMatch = Regex.Match(tracking, @"behind\s+(\d+)");
                if (aheadMatch.Success) ahead = int.Parse(aheadMatch.Groups[1].Value);
                if (behindMatch.Success) behind = int.Parse(behindMatch.Groups[1].Value);

                // Strip tracking details from the tracking field to get the remote name
                var colonIdx = tracking.IndexOf(':');
                tracking = colonIdx >= 0 ? tracking[..colonIdx].Trim() : tracking.Split(' ')[0];
            }

            // Extract commit SHA (last 7+ hex chars)
            var shaMatch = Regex.Match(trimmed, @"\b([0-9a-f]{7,40})\b");
            if (shaMatch.Success) sha = shaMatch.Groups[1].Value;

            branches.Add(new GitBranch
            {
                Name = branchName,
                IsCurrent = isCurrent,
                TrackingRemote = string.IsNullOrWhiteSpace(tracking) ? null : tracking,
                AheadCount = ahead,
                BehindCount = behind,
                LatestCommitSha = sha
            });
        }

        return branches;
    }

    // ── Health / Warnings ───────────────────────────────────────────────────

    /// <summary>Gets the duration since the last commit on the current branch.</summary>
    public TimeSpan? TimeSinceLastCommit()
    {
        var sha = GetLatestCommitSha();
        if (sha is null) return null;

        var result = Run("log", $"-1 --format=%aI {sha}");
        if (!result.Success) return null;

        if (DateTimeOffset.TryParse(result.Stdout.Trim(), out var dt))
            return DateTimeOffset.UtcNow - dt;

        return null;
    }

    /// <summary>Checks whether the local branch is ahead of remote.</summary>
    public (int ahead, int behind) GetAheadBehind(string? branch = null)
    {
        var b = branch ?? GetCurrentBranch();
        if (string.IsNullOrWhiteSpace(b)) return (0, 0);

        // Check if tracking remote exists
        var remoteResult = Run("rev-parse", $"--abbrev-ref {b}@{{upstream}}");
        if (!remoteResult.Success) return (0, 0);

        var aheadResult = Run("rev-list", $"--count {b}..{b}@{{upstream}}");
        var behindResult = Run("rev-list", $"--count {b}@{{upstream}}..{b}");

        int.TryParse(behindResult.Stdout.Trim(), out var ahead);
        int.TryParse(aheadResult.Stdout.Trim(), out var behind);

        // Note: rev-list A..B counts commits in B not in A
        // So {branch}..{upstream} = commits in upstream not in branch = branch is behind
        // And {upstream}..{branch} = commits in branch not in upstream = branch is ahead
        return (behind, ahead);
    }

    // ── Phase linkage ───────────────────────────────────────────────────────

    /// <summary>Lists commits that reference a specific phase ID via [PHASE-NNN] convention.</summary>
    public IReadOnlyList<GitCommit> ListCommitsByPhase(string phaseId, int maxCount = 100)
    {
        var all = ListCommits(maxCount: Math.Min(maxCount, 300));
        return all
            .Where(c => c.PhaseId is not null &&
                        c.PhaseId.Equals(phaseId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Returns the time of the last commit referencing a phase ID.</summary>
    public DateTimeOffset? GetLastCommitTimeForPhase(string phaseId)
    {
        var commits = ListCommitsByPhase(phaseId, maxCount: 1);
        return commits.FirstOrDefault()?.AuthorDateUtc;
    }

    /// <summary>Extracts a phase ID from a commit message using [PHASE-NNN] convention.</summary>
    public static string? ExtractPhaseId(string commitMessage)
    {
        if (string.IsNullOrWhiteSpace(commitMessage)) return null;
        var match = Regex.Match(commitMessage, @"\[(PHASE-\d+)\]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private string GetCurrentBranch()
    {
        var result = Run("rev-parse", "--abbrev-ref HEAD");
        return result.Success ? result.Stdout.Trim() : "unknown";
    }

    private string? GetLatestCommitSha()
    {
        var result = Run("rev-parse", "HEAD");
        return result.Success ? result.Stdout.Trim() : null;
    }

    private string GetCommitFullMessage(string sha)
    {
        var result = Run("log", $"-1 --format=%B {sha}");
        if (!result.Success) return string.Empty;
        var msg = result.Stdout.Trim();
        return msg.Length > 1000 ? msg[..1000] : msg;
    }

    private (IReadOnlyList<string> paths, int added, int deleted) GetFilesChanged(string sha)
    {
        var result = Run("diff-tree", $"--no-commit-id -r --name-status {sha}");
        if (!result.Success) return ([], 0, 0);

        var paths = new List<string>();
        var added = 0;
        var deleted = 0;

        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;
            var status = line[0];
            var path = line.TrimStart()[1..].Trim();
            // Remove tab if present (git diff-tree uses tab separator)
            var tabIdx = path.IndexOf('\t');
            if (tabIdx > 0) path = path[(tabIdx + 1)..];

            paths.Add(path);
            if (status == 'A') added++;
            else if (status == 'D') deleted++;
        }

        return (paths, added, deleted);
    }

    private GitResult Run(string command, string? arguments = null)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments is null ? command : $"{command} {arguments}",
                    WorkingDirectory = _workflowRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            var completed = process.WaitForExit(_timeoutMs);
            if (!completed)
            {
                try { process.Kill(); } catch { }
                return GitResult.Fail("Git command timed out.");
            }

            return process.ExitCode == 0
                ? GitResult.Ok(stdout)
                : GitResult.Fail(stderr?.Trim() ?? $"Git exited with code {process.ExitCode}");
        }
        catch (Exception ex)
        {
            return GitResult.Fail($"Git execution failed: {ex.Message}");
        }
    }

    // ── Result type ─────────────────────────────────────────────────────────

    private sealed record GitResult
    {
        public bool Success { get; private init; }
        public string Stdout { get; private init; } = string.Empty;
        public string? Error { get; private init; }

        public static GitResult Ok(string stdout) => new() { Success = true, Stdout = stdout };
        public static GitResult Fail(string error) => new() { Success = false, Error = error };
    }
}
