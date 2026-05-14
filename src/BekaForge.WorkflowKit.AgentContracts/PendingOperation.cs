namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// A pending operation queued in the offline operation inbox.
/// This is the only planned direct offline write target for agents
/// who cannot go through the CLI or HTTP API.
///
/// Pending operations are stored as .operation.json files under
/// .workflowkit/inbox/ and processed by the process-inbox operation.
/// </summary>
public sealed record PendingOperation
{
    /// <summary>The operation to invoke. Must match a WorkflowOperations constant.</summary>
    public required string OperationName { get; init; }

    /// <summary>The actor role requesting this operation.</summary>
    public required string Actor { get; init; }

    /// <summary>Phase ID targeted by this operation, if applicable (PHASE-NNN).</summary>
    public string? PhaseId { get; init; }

    /// <summary>Arbitrary key-value parameters specific to the operation.</summary>
    public Dictionary<string, object?> Parameters { get; init; } = [];

    /// <summary>
    /// Client-generated idempotency key. If two pending operations share the same key,
    /// only the first one will be processed; subsequent ones are skipped.
    /// Use a UUID or content hash.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>UTC timestamp when this operation was queued.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional correlation metadata for tracing across systems.
    /// E.g., {"sessionId": "SES-0001", "traceId": "abc123"}.
    /// </summary>
    public Dictionary<string, string>? CorrelationMetadata { get; init; }

    /// <summary>The file name this operation is stored as (without extension).</summary>
    public string FileName => $"{IdempotencyKey}.operation";

    /// <summary>The full file path relative to .workflowkit/inbox/.</summary>
    public string FileNameWithExtension => $"{IdempotencyKey}.operation.json";
}
