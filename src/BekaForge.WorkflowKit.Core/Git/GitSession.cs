namespace BekaForge.WorkflowKit.Core.Git;

/// <summary>
/// Tracks a workflow session — a period of activity by a specific user on a specific machine.
/// Stored in .workflowkit/git/sessions.jsonl (append-only).
/// </summary>
public sealed record GitSession
{
    /// <summary>Unique session identifier (e.g. "SES-0001").</summary>
    public required string SessionId { get; init; }

    /// <summary>Workflow actor role for this session.</summary>
    public WorkflowActor Actor { get; init; } = WorkflowActor.Implementer;

    /// <summary>User name from git config (user.name).</summary>
    public required string UserName { get; init; }

    /// <summary>User email from git config (user.email).</summary>
    public required string UserEmail { get; init; }

    /// <summary>Machine name (Environment.MachineName).</summary>
    public required string MachineName { get; init; }

    /// <summary>OS platform description.</summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>Workflow phase active when this session started.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Branch name when session started.</summary>
    public string? BranchAtStart { get; init; }

    /// <summary>UTC timestamp when session started.</summary>
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when session ended (null if still active).</summary>
    public DateTimeOffset? EndedUtc { get; init; }

    /// <summary>Whether this session is still active.</summary>
    public bool IsActive => EndedUtc is null;
}
