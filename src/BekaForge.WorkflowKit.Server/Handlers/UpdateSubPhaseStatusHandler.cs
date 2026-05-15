using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Updates the status of a sub-phase within a parent phase.
/// Only allowed when the parent phase is in an active non-terminal state.
/// </summary>
public sealed class UpdateSubPhaseStatusHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.UpdateSubPhaseStatus;

    private static readonly HashSet<PhaseState> AllowedParentStates =
    [
        PhaseState.Planned,
        PhaseState.ReadyForImplementation,
        PhaseState.AssignedToImplementation,
        PhaseState.InImplementation,
        PhaseState.ImplementationLogged,
        PhaseState.AuditLogged,
        PhaseState.ReadyForReview,
        PhaseState.ReviewInProgress,
        PhaseState.ReviewLogged,
        PhaseState.RequiresFix,
        PhaseState.FixInProgress,
        PhaseState.FixLogged,
        PhaseState.ReadyForTest,
        PhaseState.TestInProgress,
        PhaseState.TestLogged,
        PhaseState.Blocked
    ];

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
            return OperationResult.Fail("ValidationFailed", "Parameter 'status' is required.");

        if (!Enum.TryParse<SubPhaseStatus>(statusStr, ignoreCase: true, out var status))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown sub-phase status '{statusStr}'. Valid: Planned, InProgress, Completed, Blocked, Deferred");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        if (!AllowedParentStates.Contains(phase.State))
            return OperationResult.Fail("ValidationFailed",
                $"Cannot update sub-phase status when parent phase is in {phase.State}.");

        var found = false;
        var updatedSubPhases = phase.SubPhases.Select(sp =>
        {
            if (!string.Equals(sp.SubPhaseId, subPhaseId, StringComparison.OrdinalIgnoreCase))
                return sp;

            found = true;
            return sp with { Status = status, UpdatedUtc = DateTimeOffset.UtcNow };
        }).ToArray();

        if (!found)
            return OperationResult.Fail("NotFound",
                $"Sub-phase '{subPhaseId}' not found in phase '{phaseId}'.");

        var updated = phase with
        {
            SubPhases = updatedSubPhases,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "subphase.status.updated",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"Sub-phase {subPhaseId} status changed to {status} in {phaseId}"
        });

        return OperationResult.Ok(new { newStatus = status.ToString(), subPhaseId });
    }
}
