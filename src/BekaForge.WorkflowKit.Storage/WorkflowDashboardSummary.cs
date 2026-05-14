using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Storage;

public sealed record WorkflowDashboardSummary
{
    public required string RootPath { get; init; }
    public required string AssetName { get; init; }
    public required string WorkflowId { get; init; }
    public required string SchemaVersion { get; init; }
    public string? CurrentPhaseId { get; init; }
    public required string LastStatusDisplay { get; init; }
    public required string NextActionDescription { get; init; }
    public string? NextActionPhaseId { get; init; }
    public string? NextActionActor { get; init; }
    public Urgency NextActionUrgency { get; init; }
    public DateTimeOffset? NextActionDueDate { get; init; }
    public bool PinnedFinishNow { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public int TotalPhases { get; init; }
    public int CompletedPhases { get; init; }
    public int BlockedPhases { get; init; }
    public int FailedPhases { get; init; }
    public int OverallProgressPercent { get; init; }
    public IReadOnlyList<DashboardPhaseSummary> Phases { get; init; } = [];
    public IReadOnlyList<DashboardBlockerSummary> OpenBlockers { get; init; } = [];
    public IReadOnlyList<DashboardActivityItem> RecentEvents { get; init; } = [];
    public IReadOnlyList<DashboardActivityItem> RecentLogs { get; init; } = [];
}

public sealed record DashboardPhaseSummary
{
    public required string PhaseId { get; init; }
    public int PhaseNumber { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public PhaseState State { get; init; }
    public required string StateDisplay { get; init; }
    public required string AssignedAgent { get; init; }
    public int ProgressPercent { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public int ImplementationCount { get; init; }
    public int AuditCount { get; init; }
    public int ReviewCount { get; init; }
    public int TestCount { get; init; }
    public int FixCount { get; init; }
    public int BlockerCount { get; init; }

    /// <summary>Sub-phase breakdown, if this phase uses sub-phases.</summary>
    public IReadOnlyList<DashboardSubPhaseSummary> SubPhases { get; init; } = [];
}

/// <summary>Lightweight sub-phase entry for dashboard display.</summary>
public sealed record DashboardSubPhaseSummary
{
    public required string SubPhaseId { get; init; }
    public required string Title { get; init; }
    public required string StatusDisplay { get; init; }
    public required SubPhaseStatus Status { get; init; }
    public int ProgressPercent { get; init; }
}

public sealed record DashboardBlockerSummary
{
    public required string BlockerId { get; init; }
    public required string PhaseId { get; init; }
    public required string Reason { get; init; }
    public WorkflowActor ReportedBy { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record DashboardActivityItem
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string? PhaseId { get; init; }
    public required string Actor { get; init; }
    public required string Summary { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public string Outcome { get; init; } = string.Empty;

    public string DisplayText =>
        string.IsNullOrWhiteSpace(PhaseId)
            ? $"{Kind} {Id}: {Summary}"
            : $"{Kind} {Id} ({PhaseId}): {Summary}";
}

public static class WorkflowDashboardSummaryBuilder
{
    public static WorkflowDashboardSummary Build(WorkflowStore store)
    {
        var state = store.LoadWorkflow();
        var phases = store.LoadAllPhases()
            .OrderBy(p => p.PhaseNumber)
            .ToList();

        var openBlockers = CurrentOpenBlockers(store.ReadAllBlockers());
        var phaseRows = phases.Select(ToPhaseSummary).ToList();
        var completed = phases.Count(p => PhaseProgress.IsSuccessfulTerminal(p.State));
        var failed = phases.Count(p => PhaseProgress.IsFailedTerminal(p.State));
        var blocked = phases.Count(p => p.State == PhaseState.Blocked);
        var progress = phaseRows.Count == 0
            ? 0
            : (int)Math.Round(phaseRows.Average(p => p.ProgressPercent), MidpointRounding.AwayFromZero);

        return new WorkflowDashboardSummary
        {
            RootPath = store.WorkflowRoot,
            AssetName = state.AssetName,
            WorkflowId = state.WorkflowId,
            SchemaVersion = state.SchemaVersion,
            CurrentPhaseId = state.CurrentPhaseId,
            LastStatusDisplay = FormatState(state.LastStatus),
            NextActionDescription = FormatNextAction(state.NextAction),
            NextActionPhaseId = state.NextAction?.PhaseId,
            NextActionActor = state.NextAction?.Actor.ToString(),
            NextActionUrgency = state.NextAction?.Urgency ?? Urgency.Medium,
            NextActionDueDate = state.NextAction?.DueDate,
            PinnedFinishNow = state.NextAction?.PinnedFinishNow ?? false,
            UpdatedUtc = state.UpdatedUtc,
            TotalPhases = phases.Count,
            CompletedPhases = completed,
            BlockedPhases = blocked,
            FailedPhases = failed,
            OverallProgressPercent = progress,
            Phases = phaseRows,
            OpenBlockers = openBlockers
                .OrderByDescending(b => b.CreatedUtc)
                .Select(b => new DashboardBlockerSummary
                {
                    BlockerId = b.BlockerId,
                    PhaseId = b.PhaseId,
                    Reason = b.Reason,
                    ReportedBy = b.ReportedBy,
                    CreatedUtc = b.CreatedUtc
                })
                .ToList(),
            RecentEvents = store.ReadAllEvents()
                .OrderByDescending(e => e.Timestamp)
                .Take(10)
                .Select(e => new DashboardActivityItem
                {
                    Id = e.EventId,
                    Kind = e.EventType,
                    PhaseId = e.PhaseId,
                    Actor = e.Actor.ToString(),
                    Summary = e.Summary,
                    CreatedUtc = e.Timestamp,
                    Outcome = e.PayloadReference ?? string.Empty
                })
                .ToList(),
            RecentLogs = RecentLogs(store)
        };
    }

    private static DashboardPhaseSummary ToPhaseSummary(Phase phase) =>
        new()
        {
            PhaseId = phase.PhaseId,
            PhaseNumber = phase.PhaseNumber,
            Title = phase.Title,
            Summary = phase.Summary,
            State = phase.State,
            StateDisplay = FormatState(phase.State),
            AssignedAgent = phase.AssignedAgent?.ToString() ?? "-",
            ProgressPercent = PhaseProgress.ForPhase(phase),
            UpdatedUtc = phase.UpdatedUtc,
            ImplementationCount = phase.ImplementationLogIds.Count,
            AuditCount = phase.AuditLogIds.Count,
            ReviewCount = phase.ReviewLogIds.Count,
            TestCount = phase.TestLogIds.Count,
            FixCount = phase.FixLogIds.Count,
            BlockerCount = phase.BlockerIds.Count,
            SubPhases = phase.SubPhases.Select(sp => new DashboardSubPhaseSummary
            {
                SubPhaseId    = sp.SubPhaseId,
                Title         = sp.Title,
                StatusDisplay = sp.Status.ToString(),
                Status        = sp.Status,
                ProgressPercent = sp.Status == SubPhaseStatus.Completed ? 100 :
                                  sp.Status == SubPhaseStatus.InProgress ? 50 : 0
            }).ToList()
        };

    private static IReadOnlyList<BlockerRecord> CurrentOpenBlockers(IEnumerable<BlockerRecord> blockers)
    {
        var latest = new Dictionary<string, BlockerRecord>();
        foreach (var blocker in blockers)
            latest[blocker.BlockerId] = blocker;

        return latest.Values
            .Where(b => !b.IsResolved)
            .ToList();
    }

    private static IReadOnlyList<DashboardActivityItem> RecentLogs(WorkflowStore store)
    {
        var items = new List<DashboardActivityItem>();

        items.AddRange(store.ReadAllImplementations().Select(r => LogItem(
            r.ImplementationId, "implementation", r.PhaseId, r.Actor, r.Summary, r.CreatedUtc, r.Status.ToString())));
        items.AddRange(store.ReadAllAudits().Select(r => LogItem(
            r.AuditId, "audit", r.PhaseId, r.Actor, r.Summary, r.CreatedUtc, r.Passed ? "passed" : "failed")));
        items.AddRange(store.ReadAllReviews().Select(r => LogItem(
            r.ReviewId, "review", r.PhaseId, r.Actor, r.Summary, r.CreatedUtc, r.Passed ? "approved" : "changes requested")));
        items.AddRange(store.ReadAllTests().Select(r => LogItem(
            r.TestId, "test", r.PhaseId, r.Actor, r.Summary, r.CreatedUtc, r.Passed ? "passed" : "failed")));
        items.AddRange(store.ReadAllFixes().Select(r => LogItem(
            r.FixId, "fix", r.PhaseId, r.Actor, r.Summary, r.CreatedUtc, string.Empty)));

        return items
            .OrderByDescending(i => i.CreatedUtc)
            .Take(10)
            .ToList();
    }

    private static DashboardActivityItem LogItem(
        string id,
        string kind,
        string phaseId,
        WorkflowActor actor,
        string summary,
        DateTimeOffset createdUtc,
        string outcome) =>
        new()
        {
            Id = id,
            Kind = kind,
            PhaseId = phaseId,
            Actor = actor.ToString(),
            Summary = summary,
            CreatedUtc = createdUtc,
            Outcome = outcome
        };

    private static string FormatNextAction(NextAction? nextAction) =>
        nextAction is null
            ? "(not set)"
            : $"[{nextAction.Actor}] {nextAction.Description}";

    public static string FormatState(PhaseState? state) => state switch
    {
        null => "(none)",
        PhaseState.Pass => "PASS",
        PhaseState.PassWithWarnings => "PASS_WITH_WARNINGS",
        PhaseState.FailedArchitecture => "FAILED_ARCHITECTURE",
        PhaseState.FailedCompile => "FAILED_COMPILE",
        PhaseState.FailedTests => "FAILED_TESTS",
        PhaseState.Blocked => "BLOCKED",
        _ => state.Value.ToString()
    };

    public static string FormatUrgency(Urgency urgency) => urgency switch
    {
        Urgency.Low => "Low",
        Urgency.Medium => "Medium",
        Urgency.High => "High",
        Urgency.Critical => "Critical",
        _ => urgency.ToString()
    };
}