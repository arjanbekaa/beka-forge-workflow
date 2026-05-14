namespace BekaForge.WorkflowKit.Core.Git;

/// <summary>
/// Represents a local Git branch with tracking information.
/// </summary>
public sealed record GitBranch
{
    /// <summary>Branch name (e.g. "main", "feature/phase-018").</summary>
    public required string Name { get; init; }

    /// <summary>Whether this is the currently checked-out branch.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>Remote tracking branch name, if configured (e.g. "origin/main").</summary>
    public string? TrackingRemote { get; init; }

    /// <summary>Number of commits this branch is ahead of its tracking remote.</summary>
    public int AheadCount { get; init; }

    /// <summary>Number of commits this branch is behind its tracking remote.</summary>
    public int BehindCount { get; init; }

    /// <summary>SHA of the latest commit on this branch.</summary>
    public string? LatestCommitSha { get; init; }

    /// <summary>Whether this branch has a tracking remote configured.</summary>
    public bool HasTracking => !string.IsNullOrWhiteSpace(TrackingRemote);
}
