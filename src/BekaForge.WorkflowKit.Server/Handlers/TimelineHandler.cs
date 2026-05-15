using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Returns a merged chronological timeline of workflow events and git activity.
/// Combines implementation logs, audit logs, review logs, test logs, fix logs,
/// and git activity records into a single time-ordered feed.
/// </summary>
public sealed class TimelineHandler(WorkflowStore store, GitStore gitStore) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetTimeline;

    public OperationResult Execute(OperationContext context)
    {
        var maxResults = context.Get<int?>("maxResults") ?? 50;
        var phaseId = context.GetString("phaseId");
        var since = context.GetString("since");
        var actor = context.GetString("actor");

        var entries = new List<TimelineEntry>();

        // -- Workflow events ------------------------------------------------
        AddRecords(entries, store.ReadAllImplementations()
            .Where(r => MatchPhase(r.PhaseId, phaseId))
            .Select(r => TimelineEntry.FromImplementation(r)), since, actor);

        AddRecords(entries, store.ReadAllAudits()
            .Where(r => MatchPhase(r.PhaseId, phaseId))
            .Select(r => TimelineEntry.FromAudit(r)), since, actor);

        AddRecords(entries, store.ReadAllReviews()
            .Where(r => MatchPhase(r.PhaseId, phaseId))
            .Select(r => TimelineEntry.FromReview(r)), since, actor);

        AddRecords(entries, store.ReadAllTests()
            .Where(r => MatchPhase(r.PhaseId, phaseId))
            .Select(r => TimelineEntry.FromTest(r)), since, actor);

        AddRecords(entries, store.ReadAllFixes()
            .Where(r => MatchPhase(r.PhaseId, phaseId))
            .Select(r => TimelineEntry.FromFix(r)), since, actor);

        // -- Git activity ---------------------------------------------------
        var gitActivity = gitStore.ListActivity(
            maxResults: 200,
            phaseId: phaseId);

        foreach (var ga in gitActivity)
        {
            entries.Add(new TimelineEntry
            {
                Timestamp = ga.RecordedUtc,
                Source = "git",
                EventType = ga.ActivityType ?? "commit",
                PhaseId = ga.PhaseId,
                Actor = ga.AuthorName,
                Summary = ga.CommitMessage ?? $"Git {ga.ActivityType}",
                Detail = ga.CommitSha,
                Metadata = new Dictionary<string, string>
                {
                    ["branch"] = ga.BranchName ?? "unknown",
                    ["activityId"] = ga.ActivityId,
                    ["sessionId"] = ga.SessionId ?? ""
                }
            });
        }

        // -- Sort chronologically descending, take max ----------------------
        var result = entries
            .OrderByDescending(e => e.Timestamp)
            .Take(Math.Min(maxResults, 500))
            .ToList();

        return OperationResult.Ok(new
        {
            entries = result,
            count = result.Count,
            filters = new { maxResults, phaseId, since, actor }
        });
    }

    private static void AddRecords(List<TimelineEntry> entries, IEnumerable<TimelineEntry> records,
        string? since, string? actor)
    {
        foreach (var r in records)
        {
            if (since is not null &&
                DateTimeOffset.TryParse(since, out var sinceDt) &&
                r.Timestamp < sinceDt)
                continue;

            if (actor is not null &&
                !string.Equals(r.Actor, actor, StringComparison.OrdinalIgnoreCase))
                continue;

            entries.Add(r);
        }
    }

    private static bool MatchPhase(string? recordPhaseId, string? filterPhaseId)
    {
        if (string.IsNullOrWhiteSpace(filterPhaseId)) return true;
        return string.Equals(recordPhaseId, filterPhaseId, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// A single entry in the merged workflow + git timeline.
/// </summary>
public sealed record TimelineEntry
{
    /// <summary>UTC timestamp of the event.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Source: "workflow" or "git".</summary>
    public string Source { get; init; } = "workflow";

    /// <summary>Type of event: implementation, audit, review, test, fix, commit, status_snapshot, branch_change.</summary>
    public string EventType { get; init; } = "unknown";

    /// <summary>Record ID if from workflow (IMP-/AUD-/REV-/TEST-/FIX-), or GIT-/SES- if from git.</summary>
    public string? RecordId { get; init; }

    /// <summary>Phase ID associated with this event.</summary>
    public string? PhaseId { get; init; }

    /// <summary>Actor who performed this action.</summary>
    public string? Actor { get; init; }

    /// <summary>Human-readable summary of the event.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Additional detail (SHA, file count, etc).</summary>
    public string? Detail { get; init; }

    /// <summary>Additional metadata key-value pairs.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    public static TimelineEntry FromImplementation(ImplementationRecord r) => new()
    {
        Timestamp = r.CreatedUtc,
        Source = "workflow",
        EventType = "implementation",
        RecordId = r.ImplementationId,
        PhaseId = r.PhaseId,
        Actor = r.Actor.ToString(),
        Summary = r.Summary ?? r.Title ?? "Implementation logged"
    };

    public static TimelineEntry FromAudit(AuditRecord r) => new()
    {
        Timestamp = r.CreatedUtc,
        Source = "workflow",
        EventType = "audit",
        RecordId = r.AuditId,
        PhaseId = r.PhaseId,
        Actor = r.Actor.ToString(),
        Summary = r.Summary ?? "Audit logged"
    };

    public static TimelineEntry FromReview(ReviewRecord r) => new()
    {
        Timestamp = r.CreatedUtc,
        Source = "workflow",
        EventType = "review",
        RecordId = r.ReviewId,
        PhaseId = r.PhaseId,
        Actor = r.Actor.ToString(),
        Summary = r.Summary ?? "Review logged"
    };

    public static TimelineEntry FromTest(TestRecord r) => new()
    {
        Timestamp = r.CreatedUtc,
        Source = "workflow",
        EventType = "test",
        RecordId = r.TestId,
        PhaseId = r.PhaseId,
        Actor = r.Actor.ToString(),
        Summary = r.Summary ?? "Test logged"
    };

    public static TimelineEntry FromFix(FixRecord r) => new()
    {
        Timestamp = r.CreatedUtc,
        Source = "workflow",
        EventType = "fix",
        RecordId = r.FixId,
        PhaseId = r.PhaseId,
        Actor = r.Actor.ToString(),
        Summary = r.Summary ?? "Fix logged"
    };
}
