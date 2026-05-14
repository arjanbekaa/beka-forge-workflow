using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.get_context_bundle</c>.
///
/// Returns a compact read-model that agents consume before acting:
/// workflow state, current phase, phase contract, recent logs,
/// open blockers, and pending handoffs — all keyed to the requested phase.
///
/// If no PhaseId is provided, returns the workflow-level bundle
/// (all phases, global next action, open blockers).
/// </summary>
public sealed class GetContextBundleHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetContextBundle;

    public OperationResult Execute(OperationContext context)
    {
        var state = store.LoadWorkflow();

        // Phase-specific bundle when PhaseId is provided.
        if (!string.IsNullOrWhiteSpace(context.PhaseId))
            return BuildPhaseBundle(context.PhaseId, state, context.Actor);

        // Workflow-level bundle: all phases, global next action, open blockers.
        return BuildWorkflowBundle(state);
    }

    private OperationResult BuildPhaseBundle(
        string phaseId, WorkflowState state, WorkflowActor actor)
    {
        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        // Filter logs to this phase only.
        var allImpls   = store.ReadAllImplementations();
        var allAudits  = store.ReadAllAudits();
        var allReviews = store.ReadAllReviews();
        var allTests   = store.ReadAllTests();
        var allFixes   = store.ReadAllFixes();
        var allBlockers = store.ReadAllBlockers();
        var allHandoffs = store.ReadAllHandoffs();

        var implLogs   = allImpls  .Where(r => r.PhaseId == phaseId).OrderByDescending(r => r.CreatedUtc).Take(5).ToList();
        var auditLogs  = allAudits .Where(r => r.PhaseId == phaseId).OrderByDescending(r => r.CreatedUtc).Take(5).ToList();
        var reviewLogs = allReviews.Where(r => r.PhaseId == phaseId).OrderByDescending(r => r.CreatedUtc).Take(5).ToList();
        var testLogs   = allTests  .Where(r => r.PhaseId == phaseId).OrderByDescending(r => r.CreatedUtc).Take(5).ToList();
        var fixLogs    = allFixes  .Where(r => r.PhaseId == phaseId).OrderByDescending(r => r.CreatedUtc).Take(5).ToList();

        // Open blockers for this phase.
        var phaseBlockers = allBlockers
            .Where(b => b.PhaseId == phaseId)
            .GroupBy(b => b.BlockerId)
            .Select(g => g.Last()) // Last-write wins for append-only JSONL
            .Where(b => !b.IsResolved)
            .OrderBy(b => b.BlockerId)
            .ToList();

        // Handoffs targeting this phase.
        var handoffs = allHandoffs
            .Where(h => h.PhaseId == phaseId)
            .OrderByDescending(h => h.CreatedUtc)
            .Take(5)
            .ToList();

        // Dependencies: resolve the state of phases this phase depends on.
        var dependencyPhases = new List<object>();
        if (phase.Dependencies.Count > 0)
        {
            foreach (var depId in phase.Dependencies)
            {
                var depPhase = store.LoadPhase(depId);
                if (depPhase is not null)
                {
                    dependencyPhases.Add(new
                    {
                        phaseId     = depPhase.PhaseId,
                        title       = depPhase.Title,
                        state       = depPhase.State.ToString()
                    });
                }
            }
        }

        var bundle = new
        {
            assetName       = state.AssetName,
            workflowId      = state.WorkflowId,
            phase           = new
            {
                phaseId    = phase.PhaseId,
                phaseNumber = phase.PhaseNumber,
                title      = phase.Title,
                state      = phase.State.ToString(),
                assignedAgent = phase.AssignedAgent?.ToString(),
                summary    = phase.Summary,
                dependencies = phase.Dependencies,
                dependencyPhases
            },
            contract        = phase.Contract,
            nextAction      = state.NextAction?.PhaseId == phaseId ? state.NextAction : null,
            implementationLogs = implLogs,
            auditLogs       = auditLogs,
            reviewLogs      = reviewLogs,
            testLogs        = testLogs,
            fixLogs         = fixLogs,
            openBlockers    = phaseBlockers,
            recentHandoffs  = handoffs,
            openBlockerCount = phaseBlockers.Count
        };

        return OperationResult.Ok(bundle);
    }

    private OperationResult BuildWorkflowBundle(WorkflowState state)
    {
        var phases    = store.LoadAllPhases();
        var blockers  = store.ReadAllBlockers();
        var handoffs  = store.ReadAllHandoffs();
        var events    = store.ReadAllEvents();

        var openBlockers = blockers
            .GroupBy(b => b.BlockerId)
            .Select(g => g.Last())
            .Where(b => !b.IsResolved)
            .OrderBy(b => b.BlockerId)
            .ToList();

        var recentHandoffs = handoffs
            .OrderByDescending(h => h.CreatedUtc)
            .Take(10)
            .ToList();

        var recentEvents = events
            .OrderByDescending(e => e.Timestamp)
            .Take(20)
            .ToList();

        var bundle = new
        {
            assetName         = state.AssetName,
            workflowId        = state.WorkflowId,
            currentPhaseId    = state.CurrentPhaseId,
            lastStatus        = state.LastStatus?.ToString(),
            architectureConstraints = state.ArchitectureConstraints,
            phases = phases.Select(p => new
            {
                phaseId    = p.PhaseId,
                phaseNumber = p.PhaseNumber,
                title      = p.Title,
                state      = p.State.ToString(),
                assignedAgent = p.AssignedAgent?.ToString()
            }).ToList(),
            nextAction        = state.NextAction,
            openBlockers,
            openBlockerCount  = openBlockers.Count,
            recentHandoffs,
            recentEvents
        };

        return OperationResult.Ok(bundle);
    }
}
