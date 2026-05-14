namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// A failed operation moved from inbox to failed.
/// Stored as .failed.json files under .workflowkit/inbox/failed/.
/// Failed operations preserve evidence without corrupting authoritative state.
/// </summary>
public sealed record FailedOperation
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

    /// <summary>UTC timestamp when processing was attempted.</summary>
    public DateTimeOffset FailedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The error code from the failed dispatch.</summary>
    public required string ErrorCode { get; init; }

    /// <summary>Human-readable error message from the failed dispatch.</summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// The stage at which processing failed:
    /// "validation" — operation name or parameters invalid
    /// "dispatch" — dispatcher returned an error
    /// "unknown" — unexpected exception
    /// </summary>
    public required string FailureStage { get; init; }
}
