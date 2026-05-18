namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Append-only orchestration event log entry for session and run lifecycle changes.
/// </summary>
public sealed record OrchestrationRunEventRecord
{
    public required string RunEventId { get; init; }
    public required string SessionId { get; init; }
    public string? RunId { get; init; }
    public required string PhaseId { get; init; }
    public required OrchestrationEventKind EventKind { get; init; }
    public required WorkflowActor Actor { get; init; }
    public required string Summary { get; init; }
    public string Notes { get; init; } = string.Empty;
    public IReadOnlyList<string> RecordIds { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
