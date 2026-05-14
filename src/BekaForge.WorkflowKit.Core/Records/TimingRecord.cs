namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records time spent on a phase activity by an agent.
/// Appended to metrics/timing.jsonl.
/// Used for dashboard metrics and future tooling performance analysis.
/// </summary>
public sealed record TimingRecord
{
    /// <summary>Unique identifier in the format TIME-NNN.</summary>
    public required string TimingId { get; init; }

    /// <summary>The phase this timing record belongs to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The agent who spent the time.</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Short description of what activity was timed (e.g. "implementation", "self-audit", "fix").</summary>
    public required string Activity { get; init; }

    /// <summary>Duration of the activity.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Additional notes about the timing record.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
