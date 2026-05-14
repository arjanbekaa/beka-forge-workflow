namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// A successfully processed operation moved from inbox to processed.
/// Stored as .processed.json files under .workflowkit/inbox/processed/.
/// These are operational artifacts, not authoritative workflow state.
/// </summary>
public sealed record ProcessedOperation
{
    /// <summary>The original operation name.</summary>
    public required string OperationName { get; init; }

    /// <summary>The actor who submitted the operation.</summary>
    public required string Actor { get; init; }

    /// <summary>The phase ID targeted, if any.</summary>
    public string? PhaseId { get; init; }

    /// <summary>The idempotency key from the original pending operation.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>UTC timestamp when the original operation was queued.</summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>UTC timestamp when processing completed.</summary>
    public DateTimeOffset ProcessedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True if the dispatched operation returned success.</summary>
    public required bool DispatchSuccess { get; init; }

    /// <summary>Summary of the dispatch result (first 500 chars of Data or Message).</summary>
    public string? ResultSummary { get; init; }

    /// <summary>The event ID logged for this operation, if one was created.</summary>
    public string? EventId { get; init; }
}
