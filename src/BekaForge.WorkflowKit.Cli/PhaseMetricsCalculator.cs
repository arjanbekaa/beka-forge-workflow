using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Computes cross-phase lifecycle metrics from phase snapshots and event logs.
///
/// Because the state machine only records UpdatedUtc (not per-state entry times),
/// exact per-state dwell times are derived from the event log when available.
/// If events are not available for a phase, only high-level timings are reported.
///
/// No I/O — all inputs are provided by the caller.
/// </summary>
public static class PhaseMetricsCalculator
{
    /// <summary>High-level summary of one phase's lifecycle metrics.</summary>
    public sealed record PhaseMetrics(
        string PhaseId,
        string Title,
        string CurrentState,
        bool IsComplete,
        bool IsFailed,
        TimeSpan? QueueTime,      // creation → InImplementation
        TimeSpan? CycleTime,      // InImplementation → terminal
        TimeSpan? TotalAge,       // creation → now (or terminal)
        int ReopenCount,
        int BlockerCount,
        int FixCycleCount);

    /// <summary>Aggregate metrics across all phases.</summary>
    public sealed record AggregateMetrics(
        int TotalPhases,
        int PassedPhases,
        int FailedPhases,
        int ActivePhases,
        int PlannedPhases,
        TimeSpan? AverageCycleTime,
        TimeSpan? MedianCycleTime,
        IReadOnlyList<string> BottleneckStates,
        IReadOnlyList<PhaseMetrics> PhaseDetails);

    /// <summary>Compute metrics for all phases.</summary>
    public static AggregateMetrics Compute(
        IReadOnlyList<Phase> phases,
        IReadOnlyList<WorkflowEvent> events)
    {
        var phaseDetails = phases.Select(p => ComputePhase(p, events)).ToList();

        var cycleTimes = phaseDetails
            .Where(m => m.CycleTime.HasValue)
            .Select(m => m.CycleTime!.Value)
            .OrderBy(t => t)
            .ToList();

        TimeSpan? avgCycle = cycleTimes.Count > 0
            ? TimeSpan.FromTicks((long)cycleTimes.Average(t => t.Ticks))
            : null;
        TimeSpan? medCycle = cycleTimes.Count > 0
            ? cycleTimes[cycleTimes.Count / 2]
            : null;

        // Bottleneck states = states currently held by the most phases
        var stateCounts = phases
            .GroupBy(p => p.State)
            .OrderByDescending(g => g.Count())
            .Where(g => g.Key is not (PhaseState.Pass or PhaseState.PassWithWarnings
                or PhaseState.Planned or PhaseState.FailedValidation
                or PhaseState.FailedArchitecture or PhaseState.FailedCompile))
            .Take(3)
            .Select(g => $"{g.Key} ({g.Count()} phase{(g.Count() == 1 ? "" : "s")})")
            .ToList();

        return new AggregateMetrics(
            TotalPhases:     phases.Count,
            PassedPhases:    phases.Count(p => p.State is PhaseState.Pass or PhaseState.PassWithWarnings),
            FailedPhases:    phases.Count(p => p.State is PhaseState.FailedValidation or PhaseState.FailedArchitecture or PhaseState.FailedCompile),
            ActivePhases:    phases.Count(p => !PhaseTransitionValidator.IsTerminal(p.State) && p.State != PhaseState.Planned),
            PlannedPhases:   phases.Count(p => p.State == PhaseState.Planned),
            AverageCycleTime: avgCycle,
            MedianCycleTime:  medCycle,
            BottleneckStates: stateCounts,
            PhaseDetails:    phaseDetails);
    }

    /// <summary>Compute metrics for a single phase.</summary>
    public static PhaseMetrics ComputePhase(Phase phase, IReadOnlyList<WorkflowEvent> allEvents)
    {
        var phaseEvents = allEvents
            .Where(e => string.Equals(e.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Timestamp)
            .ToList();

        bool isComplete = phase.State is PhaseState.Pass or PhaseState.PassWithWarnings;
        bool isFailed   = phase.State is PhaseState.FailedValidation or PhaseState.FailedArchitecture or PhaseState.FailedCompile;

        // Queue time: creation → first InImplementation event
        var firstImplEvent = phaseEvents
            .FirstOrDefault(e => e.Summary.Contains("InImplementation", StringComparison.OrdinalIgnoreCase)
                               || e.EventType == "phase.started");
        TimeSpan? queueTime = firstImplEvent is not null
            ? firstImplEvent.Timestamp - phase.CreatedUtc
            : (phase.StartedUtc.HasValue ? phase.StartedUtc.Value - phase.CreatedUtc : null);

        // Cycle time: first InImplementation → terminal (or now for in-progress)
        DateTimeOffset? startedAt = firstImplEvent?.Timestamp ?? phase.StartedUtc;
        DateTimeOffset? endedAt   = isComplete || isFailed ? phase.CompletedUtc ?? phase.UpdatedUtc : null;
        TimeSpan? cycleTime       = startedAt.HasValue
            ? (endedAt ?? DateTimeOffset.UtcNow) - startedAt.Value
            : null;

        // Total age
        var totalAge = (isComplete || isFailed ? phase.CompletedUtc ?? phase.UpdatedUtc : DateTimeOffset.UtcNow)
                       - phase.CreatedUtc;

        int reopenCount   = phaseEvents.Count(e => e.EventType == "phase.reopened");
        int blockerCount  = phaseEvents.Count(e => e.EventType == "blocker.recorded");
        int fixCycleCount = phaseEvents.Count(e => e.Summary.Contains("FixInProgress", StringComparison.OrdinalIgnoreCase)
                                                  || e.Summary.Contains("RequiresFix", StringComparison.OrdinalIgnoreCase));

        return new PhaseMetrics(
            PhaseId:       phase.PhaseId,
            Title:         phase.Title,
            CurrentState:  phase.State.ToString(),
            IsComplete:    isComplete,
            IsFailed:      isFailed,
            QueueTime:     queueTime,
            CycleTime:     cycleTime,
            TotalAge:      totalAge,
            ReopenCount:   reopenCount,
            BlockerCount:  blockerCount,
            FixCycleCount: fixCycleCount);
    }

    /// <summary>Formats a nullable TimeSpan as a human-readable string.</summary>
    public static string FormatDuration(TimeSpan? duration) =>
        duration switch
        {
            null                              => "—",
            { TotalDays: >= 1 } t            => $"{t.TotalDays:F1}d",
            { TotalHours: >= 1 } t           => $"{t.TotalHours:F1}h",
            { TotalMinutes: >= 1 } t         => $"{t.TotalMinutes:F0}m",
            { } t                            => $"{t.TotalSeconds:F0}s"
        };
}
