namespace BekaForge.WorkflowKit.Core.Git;

/// <summary>
/// Represents a single Git commit from the local repository log.
/// No full file content — paths only. Metadata is truncated.
/// </summary>
public sealed record GitCommit
{
    /// <summary>Full SHA-1 hash of the commit.</summary>
    public required string Sha { get; init; }

    /// <summary>Short SHA (first 7-8 characters).</summary>
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    /// <summary>Author name from git config.</summary>
    public required string AuthorName { get; init; }

    /// <summary>Author email from git config.</summary>
    public required string AuthorEmail { get; init; }

    /// <summary>Author date (UTC).</summary>
    public DateTimeOffset AuthorDateUtc { get; init; }

    /// <summary>Commit message subject line (first line, truncated at 200 chars).</summary>
    public required string Message { get; init; }

    /// <summary>Full commit message (truncated at 1000 chars).</summary>
    public string FullMessage { get; init; } = string.Empty;

    /// <summary>Branch name this commit was recorded on, if known.</summary>
    public string? BranchName { get; init; }

    /// <summary>Parent commit SHAs (empty for initial commit).</summary>
    public IReadOnlyList<string> ParentShas { get; init; } = [];

    /// <summary>File paths changed in this commit (no content, just paths).</summary>
    public IReadOnlyList<string> FilesChanged { get; init; } = [];

    /// <summary>Phase ID extracted from commit message [PHASE-NNN] convention, if present.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Number of files added in this commit.</summary>
    public int FilesAdded { get; init; }

    /// <summary>Number of files deleted in this commit.</summary>
    public int FilesDeleted { get; init; }

    /// <summary>UTC timestamp when this commit was recorded into the activity ledger.</summary>
    public DateTimeOffset RecordedUtc { get; init; } = DateTimeOffset.UtcNow;
}
