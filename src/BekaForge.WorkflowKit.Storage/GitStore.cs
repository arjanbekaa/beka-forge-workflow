using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Git;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Append-only JSONL store for git activity records and session events.
/// All data under .workflowkit/git/ is observational/rebuildable — never source of truth.
/// Deleting these files causes no source-of-truth loss.
/// </summary>
public sealed class GitStore
{
    private readonly string _workflowRoot;
    private int _activityCounter;
    private int _sessionCounter;

    public GitStore(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        EnsureDirectories();
    }

    // -- Activity ------------------------------------------------------------

    /// <summary>Records a git commit into the activity log.</summary>
    public void AppendCommit(GitCommit commit, string? sessionId = null)
    {
        var record = new GitActivityRecord
        {
            ActivityId = NextActivityId(),
            ActivityType = "commit",
            SessionId = sessionId,
            CommitSha = commit.Sha,
            CommitMessage = commit.Message,
            AuthorName = commit.AuthorName,
            AuthorEmail = commit.AuthorEmail,
            BranchName = commit.BranchName,
            PhaseId = commit.PhaseId,
            FilesChanged = commit.FilesChanged,
            FilesAdded = commit.FilesAdded,
            FilesDeleted = commit.FilesDeleted,
            RecordedUtc = DateTimeOffset.UtcNow
        };

        Append(WorkflowLayout.GitActivityLog(_workflowRoot), record);
    }

    /// <summary>Records a git status snapshot into the activity log.</summary>
    public void AppendStatus(GitStatus status, string? sessionId = null)
    {
        var record = new GitActivityRecord
        {
            ActivityId = NextActivityId(),
            ActivityType = "status_snapshot",
            SessionId = sessionId,
            BranchName = status.BranchName,
            IsDirty = status.IsDirty,
            StagedCount = status.StagedCount,
            UnstagedCount = status.UnstagedCount,
            UntrackedCount = status.UntrackedCount,
            AheadCount = status.AheadCount,
            BehindCount = status.BehindCount,
            RecordedUtc = DateTimeOffset.UtcNow
        };

        Append(WorkflowLayout.GitActivityLog(_workflowRoot), record);
    }

    /// <summary>Records a branch change event into the activity log.</summary>
    public void AppendBranchChange(string fromBranch, string toBranch, string? sessionId = null)
    {
        var record = new GitActivityRecord
        {
            ActivityId = NextActivityId(),
            ActivityType = "branch_change",
            SessionId = sessionId,
            BranchName = toBranch,
            Metadata = new Dictionary<string, string>
            {
                ["fromBranch"] = fromBranch,
                ["toBranch"] = toBranch
            },
            RecordedUtc = DateTimeOffset.UtcNow
        };

        Append(WorkflowLayout.GitActivityLog(_workflowRoot), record);
    }

    /// <summary>Lists recent git activity records.</summary>
    public IReadOnlyList<GitActivityRecord> ListActivity(int maxResults = 100,
        DateTimeOffset? since = null, string? phaseId = null, string? sessionId = null,
        string? activityType = null)
    {
        var path = WorkflowLayout.GitActivityLog(_workflowRoot);
        if (!File.Exists(path)) return [];

        var all = JsonlAppender.ReadAll<GitActivityRecord>(path);

        var filtered = all.AsEnumerable();

        if (since.HasValue)
            filtered = filtered.Where(r => r.RecordedUtc >= since.Value);

        if (!string.IsNullOrWhiteSpace(phaseId))
            filtered = filtered.Where(r =>
                string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(sessionId))
            filtered = filtered.Where(r =>
                string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(activityType))
            filtered = filtered.Where(r =>
                string.Equals(r.ActivityType, activityType, StringComparison.OrdinalIgnoreCase));

        return filtered
            .OrderByDescending(r => r.RecordedUtc)
            .Take(Math.Min(maxResults, 500))
            .ToList();
    }

    /// <summary>Returns the total number of activity records.</summary>
    public int GetActivityCount()
    {
        var path = WorkflowLayout.GitActivityLog(_workflowRoot);
        if (!File.Exists(path)) return 0;
        return JsonlAppender.ReadAll<GitActivityRecord>(path).Count;
    }

    // -- Sessions ------------------------------------------------------------

    /// <summary>Starts a new session and appends it to the sessions log.</summary>
    public GitSession StartSession(SessionIdentity identity, WorkflowActor actor,
        string? phaseId = null, string? branchAtStart = null)
    {
        var session = new GitSession
        {
            SessionId = NextSessionId(),
            Actor = actor,
            UserName = identity.UserName,
            UserEmail = identity.UserEmail,
            MachineName = identity.MachineName,
            Platform = identity.Platform,
            PhaseId = phaseId,
            BranchAtStart = branchAtStart,
            StartedUtc = DateTimeOffset.UtcNow
        };

        Append(WorkflowLayout.GitSessionsLog(_workflowRoot), session);
        return session;
    }

    /// <summary>Ends an active session by appending an end record.</summary>
    public void EndSession(string sessionId)
    {
        // Append an end-of-session record
        var endRecord = new GitSession
        {
            SessionId = sessionId,
            Actor = WorkflowActor.WorkflowSystem,
            UserName = "system",
            UserEmail = "system",
            MachineName = Environment.MachineName,
            StartedUtc = DateTimeOffset.UtcNow,
            EndedUtc = DateTimeOffset.UtcNow
        };

        Append(WorkflowLayout.GitSessionsLog(_workflowRoot), endRecord);
    }

    /// <summary>Lists recent sessions.</summary>
    public IReadOnlyList<GitSession> ListSessions(int maxResults = 20)
    {
        var path = WorkflowLayout.GitSessionsLog(_workflowRoot);
        if (!File.Exists(path)) return [];

        var all = JsonlAppender.ReadAll<GitSession>(path);

        // Collect unique sessions, preferring the latest record per SessionId
        var latest = new Dictionary<string, GitSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in all.OrderBy(s => s.StartedUtc))
        {
            if (!string.IsNullOrWhiteSpace(s.SessionId))
                latest[s.SessionId] = s;
        }

        return latest.Values
            .OrderByDescending(s => s.StartedUtc)
            .Take(Math.Min(maxResults, 50))
            .ToList();
    }

    /// <summary>Gets the most recent session, if any.</summary>
    public GitSession? GetCurrentSession()
    {
        var sessions = ListSessions(1);
        return sessions.FirstOrDefault(s => s.IsActive);
    }

    /// <summary>Returns the total number of session records.</summary>
    public int GetSessionCount()
    {
        var path = WorkflowLayout.GitSessionsLog(_workflowRoot);
        if (!File.Exists(path)) return 0;
        return JsonlAppender.ReadAll<GitSession>(path).Count;
    }

    // -- Health --------------------------------------------------------------

    /// <summary>Returns basic health stats for observability.</summary>
    public GitStoreHealth GetHealth()
    {
        return new GitStoreHealth
        {
            ActivityCount = GetActivityCount(),
            SessionCount = GetSessionCount(),
            ActivityLogSizeBytes = GetFileSize(WorkflowLayout.GitActivityLog(_workflowRoot)),
            SessionsLogSizeBytes = GetFileSize(WorkflowLayout.GitSessionsLog(_workflowRoot)),
            HasActivityLog = File.Exists(WorkflowLayout.GitActivityLog(_workflowRoot)),
            HasSessionsLog = File.Exists(WorkflowLayout.GitSessionsLog(_workflowRoot))
        };
    }

    // -- Private helpers -----------------------------------------------------

    private void Append<T>(string path, T record)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        JsonlAppender.Append(path, record);
    }

    private string NextActivityId()
    {
        var id = Interlocked.Increment(ref _activityCounter);
        return $"GIT-{id:D4}";
    }

    private string NextSessionId()
    {
        var id = Interlocked.Increment(ref _sessionCounter);
        return $"SES-{id:D4}";
    }

    private void EnsureDirectories()
    {
        var gitDir = WorkflowLayout.GitDir(_workflowRoot);
        if (!Directory.Exists(gitDir))
            Directory.CreateDirectory(gitDir);
    }

    private static long GetFileSize(string path)
    {
        if (!File.Exists(path)) return 0;
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}

/// <summary>
/// Single git activity record stored in activity.jsonl.
/// </summary>
public sealed record GitActivityRecord
{
    /// <summary>Unique activity record ID (GIT-NNNN).</summary>
    public required string ActivityId { get; init; }

    /// <summary>Type of activity: commit, status_snapshot, branch_change.</summary>
    public required string ActivityType { get; init; }

    /// <summary>Session ID this activity belongs to, if any.</summary>
    public string? SessionId { get; init; }

    /// <summary>Commit SHA (for commit type).</summary>
    public string? CommitSha { get; init; }

    /// <summary>Commit message subject (for commit type).</summary>
    public string? CommitMessage { get; init; }

    /// <summary>Author name (for commit type).</summary>
    public string? AuthorName { get; init; }

    /// <summary>Author email (for commit type).</summary>
    public string? AuthorEmail { get; init; }

    /// <summary>Current branch name.</summary>
    public string? BranchName { get; init; }

    /// <summary>Phase ID linked to this activity, if any.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Whether the worktree was dirty at snapshot time.</summary>
    public bool IsDirty { get; init; }

    /// <summary>Number of staged files (status_snapshot).</summary>
    public int StagedCount { get; init; }

    /// <summary>Number of unstaged files (status_snapshot).</summary>
    public int UnstagedCount { get; init; }

    /// <summary>Number of untracked files (status_snapshot).</summary>
    public int UntrackedCount { get; init; }

    /// <summary>Number of commits ahead of remote.</summary>
    public int AheadCount { get; init; }

    /// <summary>Number of commits behind remote.</summary>
    public int BehindCount { get; init; }

    /// <summary>File paths changed (commit type). No content, just paths.</summary>
    public IReadOnlyList<string> FilesChanged { get; init; } = [];

    /// <summary>Number of files added (commit type).</summary>
    public int FilesAdded { get; init; }

    /// <summary>Number of files deleted (commit type).</summary>
    public int FilesDeleted { get; init; }

    /// <summary>Additional key-value metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset RecordedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Health summary for the git activity store.
/// </summary>
public sealed record GitStoreHealth
{
    /// <summary>Number of activity records stored.</summary>
    public int ActivityCount { get; init; }

    /// <summary>Number of session records stored.</summary>
    public int SessionCount { get; init; }

    /// <summary>Size of activity.jsonl in bytes.</summary>
    public long ActivityLogSizeBytes { get; init; }

    /// <summary>Size of sessions.jsonl in bytes.</summary>
    public long SessionsLogSizeBytes { get; init; }

    /// <summary>Whether activity.jsonl exists.</summary>
    public bool HasActivityLog { get; init; }

    /// <summary>Whether sessions.jsonl exists.</summary>
    public bool HasSessionsLog { get; init; }
}
