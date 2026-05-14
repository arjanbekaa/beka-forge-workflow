namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// An immutable event record appended to events.jsonl for every meaningful operation.
/// The event log is append-only and is never modified after creation.
/// </summary>
public sealed record WorkflowEvent
{
    /// <summary>Unique event identifier in the format EVT-NNN.</summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Short machine-readable event type string.
    /// Examples: "phase.created", "phase.status.changed", "implementation.logged", "blocker.recorded".
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>The agent or actor who caused this event.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>ID of the phase this event relates to, if applicable.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Human-readable summary of what happened.</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Optional reference to the ID of the record payload (e.g. IMP-001, AUD-001).
    /// Use this to look up the full record in the appropriate JSONL log file.
    /// </summary>
    public string? PayloadReference { get; init; }
}
