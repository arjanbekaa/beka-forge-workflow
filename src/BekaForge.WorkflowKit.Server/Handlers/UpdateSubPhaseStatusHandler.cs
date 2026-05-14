using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Transitions a sub-phase to a new status within a parent phase.
/// Validates: phase exists, sub-phase exists, dependencies are satisfied,
/// and the transition is allowed (Planned -> InProgress -> Completed).
/// </summary>
public sealed class UpdateSubPhaseStatusHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.UpdateSubPhaseStatus;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var subPhaseId = context.GetString("subPhaseId");
        if (string.IsNullOrWhiteSpace(subPhaseId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'subPhaseId' is required.");

        var statusStr = context.GetString("status");
        if (string.IsNullOrWhiteSpace(statusStr))
            return OperationResult.Fail("ValidationFailed", "Parameter 'status' is required (Planned, InProgress, Completed, Blocked, Deferred).");

        if (!Enum.TryParse<SubPhaseStatus>(statusStr, ignoreCase: true, out var newStatus))
            return OperationResult.Fail("ValidationFailed",
                $"Invalid status '{statusStr}'. Valid values: {string.Join(", ", Enum.GetNames<SubPhaseStatus>())}.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var existingSub = phase.SubPhases.FirstOrDefault(sp =>
            string.Equals(sp.SubPhaseId, subPhaseId, StringComparison.OrdinalIgnoreCase));

        if (existingSub is null)
            return OperationResult.Fail("NotFound",
                $"Sub-phase '{subPhaseId}' not found in phase '{phaseId}'.");

        // Validate transition
        if (!IsValidTransition(existingSub.Status, newStatus))
            return OperationResult.Fail("ValidationFailed",
                $"Cannot transition sub-phase '{subPhaseId}' from '{existingSub.Status}' to '{newStatus}'.");

        // Validate dependencies
        if (newStatus == SubPhaseStatus.InProgress)
        {
            var unmetDeps = existingSub.DependsOn
                .Where(depId => !IsDependencySatisfied(phase, depId))
                .ToList();

            if (unmetDeps.Count > 0)
                return OperationResult.Fail("ValidationFailed",
                    $"Sub-phase '{subPhaseId}' depends on {string.Join(", ", unmetDeps)} which are not yet completed.");
        }

        // Update the sub-phase
        var updatedSubPhases = phase.SubPhases.Select(sp =>
            string.Equals(sp.SubPhaseId, subPhaseId, StringComparison.OrdinalIgnoreCase)
                ? sp with { Status = newStatus, UpdatedUtc = DateTimeOffset.UtcNow }
                : sp
        ).ToList();

        var updatedPhase = phase with
        {
            SubPhases = updatedSubPhases,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "sub_phase.status.changed",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"Sub-phase '{subPhaseId}' ({existingSub.Title}) -> {newStatus}"
        });

        return OperationResult.Ok(new
        {
            phaseId,
            subPhaseId,
            previousStatus = existingSub.Status.ToString(),
            newStatus = newStatus.ToString(),
            message = $"Sub-phase '{subPhaseId}' transitioned to {newStatus}."
        });
    }

    private bool IsDependencySatisfied(Phase phase, string dependencyId)
    {
        var sibling = phase.SubPhases.FirstOrDefault(sp =>
            string.Equals(sp.SubPhaseId, dependencyId, StringComparison.OrdinalIgnoreCase));
        if (sibling is not null)
            return sibling.Status is SubPhaseStatus.Completed or SubPhaseStatus.Deferred;

        var dependencyPhase = store.LoadPhase(dependencyId);
        if (dependencyPhase is null)
            return false;

        return dependencyPhase.State is not (
            PhaseState.Planned or
            PhaseState.ReadyForImplementation or
            PhaseState.AssignedToImplementation or
            PhaseState.InImplementation or
            PhaseState.Blocked or
            PhaseState.FailedArchitecture or
            PhaseState.FailedCompile or
            PhaseState.FailedTests);
    }

    private static bool IsValidTransition(SubPhaseStatus current, SubPhaseStatus next)
    {
        return (current, next) switch
        {
            (SubPhaseStatus.Planned, SubPhaseStatus.InProgress) => true,
            (SubPhaseStatus.Planned, SubPhaseStatus.Deferred) => true,
            (SubPhaseStatus.InProgress, SubPhaseStatus.Completed) => true,
            (SubPhaseStatus.InProgress, SubPhaseStatus.Blocked) => true,
            (SubPhaseStatus.Blocked, SubPhaseStatus.InProgress) => true,
            (SubPhaseStatus.Deferred, SubPhaseStatus.InProgress) => true,
            (SubPhaseStatus.Deferred, SubPhaseStatus.Planned) => true,
            _ => false
        };
    }
}
