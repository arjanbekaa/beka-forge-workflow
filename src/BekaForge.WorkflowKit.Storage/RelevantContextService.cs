using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core.Records;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Builds ranked context pointers for <c>workflow.get_relevant_context</c>.
///
/// Rather than returning full content, this service returns pointers that agents
/// resolve through the existing slice APIs (get_file_slice, get_record_slice,
/// get_markdown_region, get_json_pointer_value). This avoids broad workflow scans
/// and keeps agent context compact.
///
/// Ranking is deterministic and local — no external ranking services are used.
/// Pointers are ordered by relevance score; the handler applies maxItems and
/// maxEstimatedTokens caps.
/// </summary>
public sealed class RelevantContextService
{
    private readonly string _workflowRoot;
    private readonly WorkflowStore _store;

    public RelevantContextService(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        _store = new WorkflowStore(workflowRoot);
    }

    /// <summary>
    /// Returns ranked context pointers for the given request parameters.
    /// </summary>
    /// <param name="phaseId">If provided, returns phase-specific context; otherwise workflow-level.</param>
    /// <param name="actor">Optional actor filter for handoff/task targeting.</param>
    /// <param name="taskType">Optional task type (implementation, audit, review, test, fix) to bias ranking.</param>
    /// <param name="maxItems">Maximum number of pointers to return. Default 20.</param>
    /// <param name="maxEstimatedTokens">Optional token budget cap.</param>
    public RelevantContextResult GetRelevantContext(
        string? phaseId,
        string? actor,
        string? taskType,
        int? maxItems,
        int? maxEstimatedTokens)
    {
        var warnings = new List<string>
        {
            "Do not scan the full workflow folder. Resolve pointers through slice APIs " +
            "(get_file_slice, get_record_slice, get_markdown_region, get_json_pointer_value)."
        };

        // Gather all candidate pointers.
        var candidates = new List<(ContextPointer Pointer, double Score)>();

        if (!string.IsNullOrWhiteSpace(phaseId))
        {
            var phase = _store.LoadPhase(phaseId);
            if (phase is null)
            {
                return new RelevantContextResult
                {
                    PhaseId = phaseId,
                    TaskType = taskType,
                    Pointers = [],
                    OmittedCandidates = 0,
                    Warnings = [$"Phase '{phaseId}' not found.", ..warnings]
                };
            }
            GatherPhaseContext(candidates, phase, phaseId, taskType);
        }
        else
        {
            GatherWorkflowContext(candidates);
        }

        // Sort by relevance score descending, then by estimated tokens ascending
        // (smaller items first when scores are equal).
        candidates.Sort((a, b) =>
        {
            var scoreCmp = b.Score.CompareTo(a.Score);
            if (scoreCmp != 0) return scoreCmp;
            return a.Pointer.EstimatedTokens.CompareTo(b.Pointer.EstimatedTokens);
        });

        // Apply caps.
        var effectiveMaxItems = maxItems ?? 20;
        var omitted = 0;
        var totalTokens = 0;
        var resultPointers = new List<ContextPointer>();

        foreach (var (pointer, score) in candidates)
        {
            // Skip if we've hit the item cap.
            if (resultPointers.Count >= effectiveMaxItems)
            {
                omitted++;
                continue;
            }

            // Skip if adding this pointer would exceed the token budget
            // (but always include at least one pointer).
            if (maxEstimatedTokens.HasValue &&
                pointer.EstimatedTokens > 0 &&
                totalTokens + pointer.EstimatedTokens > maxEstimatedTokens.Value &&
                resultPointers.Count > 0)
            {
                omitted++;
                continue;
            }

            resultPointers.Add(pointer);
            if (pointer.EstimatedTokens > 0)
                totalTokens += pointer.EstimatedTokens;
        }

        if (omitted > 0)
            warnings.Add($"{omitted} context pointer(s) omitted due to maxItems or maxEstimatedTokens limits.");

        return new RelevantContextResult
        {
            PhaseId = phaseId,
            TaskType = taskType,
            Pointers = resultPointers,
            OmittedCandidates = omitted,
            EstimatedTotalTokens = totalTokens > 0 ? totalTokens : -1,
            Warnings = warnings
        };
    }

    // ── Phase-level context gathering ────────────────────────────────────────────

    private void GatherPhaseContext(
        List<(ContextPointer Pointer, double Score)> candidates,
        Core.Phase phase,
        string phaseId,
        string? taskType)
    {
        var phaseMd = $"workflow/phases/{phaseId}.md";

        // 1. Phase contract — the most important pointer (score 1.0).
        if (File.Exists(Path.Combine(_workflowRoot, phaseMd)))
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "markdown_region",
                Target = $"{phaseId}.md#phase-contract",
                RelevanceScore = 1.0,
                Reason = $"Full contract for {phaseId}: objective, scope, acceptance criteria, architecture constraints.",
                EstimatedTokens = EstimateTokens(Path.Combine(_workflowRoot, phaseMd)),}, 1.0));
        }

        // 2. Required files and areas from contract (score 0.95).
        if (phase.Contract?.RequiredFilesOrAreas is { Count: > 0 } areas)
        {
            foreach (var area in areas)
            {
                candidates.Add((new ContextPointer
                {
                    PointerType = "file",
                    Target = area,
                    RelevanceScore = 0.95,
                    Reason = $"Required file or area for {phaseId}: {area}.",
                    EstimatedTokens = EstimateTokens(Path.Combine(_workflowRoot, area)),}, 0.95));
            }
        }

        // 3. Current next action — what to do right now (score 0.90).
        var state = _store.LoadWorkflow();
        if (state.NextAction?.PhaseId == phaseId)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "json_pointer",
                Target = ".workflowkit/workflow.json#/nextAction",
                RelevanceScore = 0.90,
                Reason = $"Current next action for {phaseId}: {state.NextAction.Description}.",
                EstimatedTokens = EstimateTokens(WorkflowLayout.WorkflowFile(_workflowRoot)) / 5,}, 0.90));
        }

        // 4. Latest implementation logs (score 0.85).
        var impls = _store.ReadAllImplementations()
            .Where(r => r.PhaseId == phaseId)
            .OrderByDescending(r => r.CreatedUtc)
            .Take(3)
            .ToList();

        foreach (var r in impls)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "record",
                Target = r.ImplementationId,
                RelevanceScore = 0.85,
                Reason = $"Latest implementation record for {phaseId}: {r.Summary ?? r.ImplementationId}.",
                EstimatedTokens = 200,}, 0.85));
        }

        // 5. Latest relevant logs based on taskType (score 0.80).
        //    Boost the category matching the taskType.
        GatherLogs(candidates, phaseId, "audit", 0.80, taskType == "audit" ? 0.05 : 0.0);
        GatherLogs(candidates, phaseId, "review", 0.78, taskType == "review" ? 0.05 : 0.0);
        GatherLogs(candidates, phaseId, "test", 0.76, taskType == "test" ? 0.05 : 0.0);
        GatherLogs(candidates, phaseId, "fix", 0.74, taskType == "fix" ? 0.05 : 0.0);

        // 6. Open blockers (score 0.70).
        var blockers = _store.ReadAllBlockers()
            .Where(b => b.PhaseId == phaseId)
            .GroupBy(b => b.BlockerId)
            .Select(g => g.Last())
            .Where(b => !b.IsResolved)
            .OrderBy(b => b.BlockerId)
            .ToList();

        foreach (var b in blockers)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "record",
                Target = b.BlockerId,
                RelevanceScore = 0.70,
                Reason = $"Open blocker for {phaseId}: {b.Reason ?? b.BlockerId}.",
                EstimatedTokens = 200,}, 0.70));
        }

        // 7. Handoffs targeting this phase (score 0.65).
        var handoffs = _store.ReadAllHandoffs()
            .Where(h => h.PhaseId == phaseId)
            .OrderByDescending(h => h.CreatedUtc)
            .Take(3)
            .ToList();

        foreach (var h in handoffs)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "record",
                Target = h.HandoffId,
                RelevanceScore = 0.65,
                Reason = $"Recent handoff for {phaseId}: {h.Summary ?? h.HandoffId}.",
                EstimatedTokens = 200,}, 0.65));
        }

        // 8. Generated doc regions — current status and implementation plan (score 0.60).
        candidates.Add((new ContextPointer
        {
            PointerType = "markdown_region",
            Target = "CurrentStatus.md#current-status",
            RelevanceScore = 0.60,
            Reason = "Current workflow status: phase progress, blockers, recent activity.",
            EstimatedTokens = 300,}, 0.60));

        candidates.Add((new ContextPointer
        {
            PointerType = "markdown_region",
            Target = "ImplementationPlan.md#implementation-plan",
            RelevanceScore = 0.58,
            Reason = "Implementation plan with phase roadmap and progress percentages.",
            EstimatedTokens = 300,}, 0.58));

        // 9. Dependency phases (score 0.50).
        if (phase.Dependencies.Count > 0)
        {
            foreach (var depId in phase.Dependencies)
            {
                var depPhase = _store.LoadPhase(depId);
                if (depPhase is null) continue;

                candidates.Add((new ContextPointer
                {
                    PointerType = "json_pointer",
                    Target = $".workflowkit/phases/{depId}.json#/state",
                    RelevanceScore = 0.50,
                    Reason = $"Dependency {depId} ({depPhase.Title}) — state: {depPhase.State}.",
                    EstimatedTokens = 50,}, 0.50));
            }
        }
    }

    // ── Workflow-level context gathering ─────────────────────────────────────────

    private void GatherWorkflowContext(
        List<(ContextPointer Pointer, double Score)> candidates)
    {
        var state = _store.LoadWorkflow();

        // 1. Current phase pointer (score 1.0).
        if (!string.IsNullOrWhiteSpace(state.CurrentPhaseId))
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "json_pointer",
                Target = $".workflowkit/phases/{state.CurrentPhaseId}.json",
                RelevanceScore = 1.0,
                Reason = $"Current active phase: {state.CurrentPhaseId}.",
                EstimatedTokens = EstimateTokens(
                    WorkflowLayout.PhaseFile(_workflowRoot, state.CurrentPhaseId)),}, 1.0));
        }

        // 2. Global next action (score 0.90).
        if (state.NextAction is not null)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "json_pointer",
                Target = ".workflowkit/workflow.json#/nextAction",
                RelevanceScore = 0.90,
                Reason = $"Global next action: {state.NextAction.Description}.",
                EstimatedTokens = EstimateTokens(WorkflowLayout.WorkflowFile(_workflowRoot)) / 5,}, 0.90));
        }

        // 3. All phases summary (score 0.85).
        candidates.Add((new ContextPointer
        {
            PointerType = "markdown_region",
            Target = "ImplementationPlan.md#implementation-plan",
            RelevanceScore = 0.85,
            Reason = "All phases with state, progress, and assigned agents.",
            EstimatedTokens = 300,}, 0.85));

        // 4. Open blockers (score 0.80).
        var blockers = _store.ReadAllBlockers()
            .GroupBy(b => b.BlockerId)
            .Select(g => g.Last())
            .Where(b => !b.IsResolved)
            .OrderBy(b => b.BlockerId)
            .ToList();

        foreach (var b in blockers)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "record",
                Target = b.BlockerId,
                RelevanceScore = 0.80,
                Reason = $"Open blocker: {b.Reason ?? b.BlockerId} (phase: {b.PhaseId}).",
                EstimatedTokens = 200,}, 0.80));
        }

        // 5. Recent events (score 0.70).
        var events = _store.ReadAllEvents()
            .OrderByDescending(e => e.Timestamp)
            .Take(3)
            .ToList();

        foreach (var e in events)
        {
            candidates.Add((new ContextPointer
            {
                PointerType = "record",
                Target = e.EventId,
                RelevanceScore = 0.70,
                Reason = $"Recent event: {e.Summary ?? e.EventId}.",
                EstimatedTokens = 200,}, 0.70));
        }

        // 6. Current status (score 0.60).
        candidates.Add((new ContextPointer
        {
            PointerType = "markdown_region",
            Target = "CurrentStatus.md#current-status",
            RelevanceScore = 0.60,
            Reason = "Current workflow status: phase progress, blockers, recent activity.",
            EstimatedTokens = 300,}, 0.60));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void GatherLogs(
        List<(ContextPointer Pointer, double Score)> candidates,
        string phaseId,
        string logType,
        double baseScore,
        double boost)
    {
        var score = baseScore + boost;

        switch (logType)
        {
            case "audit":
                foreach (var r in _store.ReadAllAudits()
                    .Where(r => r.PhaseId == phaseId)
                    .OrderByDescending(r => r.CreatedUtc)
                    .Take(2))
                {
                    candidates.Add((new ContextPointer
                    {
                        PointerType = "record",
                        Target = r.AuditId,
                        RelevanceScore = score,
                        Reason = $"Latest audit for {phaseId}: {r.Summary ?? r.AuditId}.",
                        EstimatedTokens = 200,}, score));
                }
                break;

            case "review":
                foreach (var r in _store.ReadAllReviews()
                    .Where(r => r.PhaseId == phaseId)
                    .OrderByDescending(r => r.CreatedUtc)
                    .Take(2))
                {
                    candidates.Add((new ContextPointer
                    {
                        PointerType = "record",
                        Target = r.ReviewId,
                        RelevanceScore = score,
                        Reason = $"Latest review for {phaseId}: {r.Summary ?? r.ReviewId}.",
                        EstimatedTokens = 200,}, score));
                }
                break;

            case "test":
                foreach (var r in _store.ReadAllTests()
                    .Where(r => r.PhaseId == phaseId)
                    .OrderByDescending(r => r.CreatedUtc)
                    .Take(2))
                {
                    candidates.Add((new ContextPointer
                    {
                        PointerType = "record",
                        Target = r.TestId,
                        RelevanceScore = score,
                        Reason = $"Latest test for {phaseId}: {r.Summary ?? r.TestId}.",
                        EstimatedTokens = 200,}, score));
                }
                break;

            case "fix":
                foreach (var r in _store.ReadAllFixes()
                    .Where(r => r.PhaseId == phaseId)
                    .OrderByDescending(r => r.CreatedUtc)
                    .Take(2))
                {
                    candidates.Add((new ContextPointer
                    {
                        PointerType = "record",
                        Target = r.FixId,
                        RelevanceScore = score,
                        Reason = $"Latest fix for {phaseId}: {r.Summary ?? r.FixId}.",
                        EstimatedTokens = 200,}, score));
                }
                break;
        }
    }

    /// <summary>
    /// Rough token estimator: ~4 characters per token for English text.
    /// Returns -1 if the file doesn't exist.
    /// </summary>
    private static int EstimateTokens(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return -1;
            var length = new FileInfo(fullPath).Length;
            return (int)(length / 4);
        }
        catch
        {
            return -1;
        }
    }
}
