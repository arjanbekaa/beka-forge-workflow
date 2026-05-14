namespace BekaForge.WorkflowKit.Core.Git;

/// <summary>
/// Snapshot of the current Git worktree status.
/// </summary>
public sealed record GitStatus
{
    /// <summary>Current branch name.</summary>
    public required string BranchName { get; init; }

    /// <summary>Whether the worktree has uncommitted changes (modified, staged, or untracked files).</summary>
    public bool IsDirty { get; init; }

    /// <summary>Number of staged (added) files.</summary>
    public int StagedCount { get; init; }

    /// <summary>Number of unstaged (modified but not added) files.</summary>
    public int UnstagedCount { get; init; }

    /// <summary>Number of untracked files.</summary>
    public int UntrackedCount { get; init; }

    /// <summary>Total number of changed files (staged + unstaged + untracked).</summary>
    public int TotalChangedCount => StagedCount + UnstagedCount + UntrackedCount;

    /// <summary>SHA of the latest commit on the current branch.</summary>
    public string? LatestCommitSha { get; init; }

    /// <summary>Short SHA of the latest commit.</summary>
    public string? LatestCommitShortSha => LatestCommitSha?.Length >= 7 ? LatestCommitSha[..7] : LatestCommitSha;

    /// <summary>Number of commits local is ahead of remote.</summary>
    public int AheadCount { get; init; }

    /// <summary>Number of commits local is behind remote.</summary>
    public int BehindCount { get; init; }

    /// <summary>Whether the local branch is ahead of its tracking remote.</summary>
    public bool IsAhead => AheadCount > 0;

    /// <summary>Whether the local branch is behind its tracking remote.</summary>
    public bool IsBehind => BehindCount > 0;

    /// <summary>UTC timestamp when this status was recorded.</summary>
    public DateTimeOffset RecordedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether Git was available when this status was captured.</summary>
    public bool GitAvailable { get; init; } = true;

    /// <summary>Error message if Git was not available.</summary>
    public string? GitError { get; init; }

    /// <summary>Creates a status indicating Git is not available.</summary>
    public static GitStatus Unavailable(string? error = null) => new()
    {
        BranchName = "unknown",
        GitAvailable = false,
        GitError = error ?? "Git CLI not found in PATH."
    };
}
