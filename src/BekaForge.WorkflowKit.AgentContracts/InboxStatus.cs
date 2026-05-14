namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Summary of the offline operation inbox state.
/// Returned by workflow.get_inbox_status.
/// </summary>
public sealed record InboxStatus
{
    /// <summary>Number of pending operations waiting to be processed.</summary>
    public required int PendingCount { get; init; }

    /// <summary>Number of successfully processed operations.</summary>
    public required int ProcessedCount { get; init; }

    /// <summary>Number of failed operations.</summary>
    public required int FailedCount { get; init; }

    /// <summary>The oldest pending operation UTC timestamp, if any pending exist.</summary>
    public DateTimeOffset? OldestPendingUtc { get; init; }

    /// <summary>Whether the inbox directory exists and is accessible.</summary>
    public required bool InboxAvailable { get; init; }

    /// <summary>List of pending operation file names (without path).</summary>
    public required IReadOnlyList<string> PendingFiles { get; init; }
}
