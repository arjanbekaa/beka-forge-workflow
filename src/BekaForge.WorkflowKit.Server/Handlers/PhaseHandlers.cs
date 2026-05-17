using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Creates a new phase and appends it to the workflow.</summary>
public sealed class CreatePhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.CreatePhase;

    public OperationResult Execute(OperationContext context)
    {
        var title = context.GetString("title");
        if (string.IsNullOrWhiteSpace(title))
            return OperationResult.Fail("ValidationFailed", "Parameter 'title' is required.");

        var summary = context.GetString("summary") ?? string.Empty;

        var state = store.LoadWorkflow();
        var explicitPhaseId = context.PhaseId ?? context.GetString("phaseId");
        var phaseId = string.IsNullOrWhiteSpace(explicitPhaseId)
            ? store.NextAvailablePhaseId(state.PhaseIds)
            : explicitPhaseId.Trim().ToUpperInvariant();

        // Parse phase number from the new ID (PHASE-NNN → NNN).
        if (!TryParsePhaseNumber(phaseId, out var phaseNumber))
            return OperationResult.Fail("ValidationFailed",
                $"Phase ID '{phaseId}' must use the PHASE-NNN format.");

        if (store.PhaseExists(phaseId) || state.PhaseIds.Contains(phaseId, StringComparer.OrdinalIgnoreCase))
            return OperationResult.Fail("ValidationFailed",
                $"Phase '{phaseId}' already exists.");

        store.EnsurePhaseSequenceAtLeast(phaseNumber);

        WorkflowActor? assignedAgent = null;
        var agentName = context.GetString("assignedAgent") ?? context.GetString("agent");
        if (!string.IsNullOrWhiteSpace(agentName))
        {
            if (!Enum.TryParse<WorkflowActor>(agentName, ignoreCase: true, out var parsedAgent))
                return OperationResult.Fail("ValidationFailed", $"Unknown agent '{agentName}'.");

            assignedAgent = parsedAgent;
        }

        PhaseContract? contract = null;
        var objective = context.GetString("contractObjective") ?? context.GetString("objective");
        var scope = context.GetString("contractScope") ?? context.GetString("scope");
        if (!string.IsNullOrWhiteSpace(objective) || !string.IsNullOrWhiteSpace(scope))
        {
            if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(scope))
                return OperationResult.Fail("ValidationFailed",
                    "Both contract objective and scope are required when creating a phase contract.");

            contract = new PhaseContract
            {
                Objective = objective,
                Scope = scope,
                OutOfScope = context.GetString("contractOutOfScope") ?? context.GetString("outOfScope") ?? string.Empty,
                ArchitectureConstraints = ParseList(context.GetString("contractArchitectureConstraints")),
                RequiredFilesOrAreas = ParseList(context.GetString("contractRequiredFilesOrAreas")),
                AcceptanceCriteria = ParseList(context.GetString("contractAcceptanceCriteria")),
                ImplementationNotes = context.GetString("contractImplementationNotes") ?? string.Empty,
                AuditRequirements = context.GetString("contractAuditRequirements") ?? string.Empty,
                ValidationRequirements = context.GetString("contractValidationRequirements") ?? string.Empty,
                ParallelizationNotes = context.GetString("contractParallelizationNotes") ?? string.Empty,
                DependsOnPhaseIds = ParseList(context.GetString("contractDependsOnPhaseIds")),
                RequiresValidation = context.GetBool("requiresValidation", defaultValue: true)
            };
        }

        var phase = new Phase
        {
            PhaseId = phaseId,
            PhaseNumber = phaseNumber,
            Title = title,
            Summary = summary,
            State = PhaseState.Planned,
            AssignedAgent = assignedAgent,
            Dependencies = ParseList(context.GetString("dependencies")),
            Contract = contract
        };

        store.SavePhase(phase);

        var updatedState = state with
        {
            PhaseIds = [..state.PhaseIds.Append(phaseId).OrderBy(ParsePhaseSortNumber).ThenBy(id => id, StringComparer.OrdinalIgnoreCase)],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveWorkflow(updatedState);

        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "phase.created",
            Actor = context.Actor,
            PhaseId = phaseId,
            Summary = $"Phase {phaseId} created: {title}"
        });

        return OperationResult.Ok(phase);
    }

    private static bool TryParsePhaseNumber(string phaseId, out int number)
    {
        number = 0;
        if (!phaseId.StartsWith("PHASE-", StringComparison.OrdinalIgnoreCase))
            return false;

        var numPart = phaseId.AsSpan(6);
        return int.TryParse(numPart, out number) && number > 0;
    }

    private static string[] ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var separators = raw.Contains("||", StringComparison.Ordinal)
            ? ["||"]
            : raw.Contains('\n', StringComparison.Ordinal)
                ? ["\r\n", "\n"]
                : new[] { "," };

        return raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int ParsePhaseSortNumber(string phaseId) =>
        TryParsePhaseNumber(phaseId, out var number) ? number : int.MaxValue;
}

/// <summary>Updates a phase's metadata (title, summary, contract, sub-phases).</summary>
public sealed class UpdatePhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.UpdatePhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var updated = phase;

        // Update simple fields
        var title = context.GetString("title");
        if (title is not null) updated = updated with { Title = title };

        var summary = context.GetString("summary");
        if (summary is not null) updated = updated with { Summary = summary };

        var dependenciesRaw = context.GetString("dependencies");
        if (dependenciesRaw is not null)
            updated = updated with { Dependencies = ParseList(dependenciesRaw) };

        var agentName = context.GetString("assignedAgent") ?? context.GetString("agent");
        if (agentName is not null)
        {
            if (!Enum.TryParse<WorkflowActor>(agentName, ignoreCase: true, out var parsedAgent))
                return OperationResult.Fail("ValidationFailed", $"Unknown agent '{agentName}'.");

            updated = updated with { AssignedAgent = parsedAgent };
        }

        // Update contract fields
        var contract = phase.Contract;
        var contractUpdated = false;
        var nextContract = contract ?? new PhaseContract
        {
            Objective = string.Empty,
            Scope = string.Empty
        };

        var objective = context.GetString("contractObjective") ?? context.GetString("objective");
        if (objective is not null) { nextContract = nextContract with { Objective = objective }; contractUpdated = true; }

        var scope = context.GetString("contractScope") ?? context.GetString("scope");
        if (scope is not null) { nextContract = nextContract with { Scope = scope }; contractUpdated = true; }

        var outOfScope = context.GetString("contractOutOfScope") ?? context.GetString("outOfScope");
        if (outOfScope is not null) { nextContract = nextContract with { OutOfScope = outOfScope }; contractUpdated = true; }

        var implNotes = context.GetString("contractImplementationNotes") ?? context.GetString("implementationNotes");
        if (implNotes is not null) { nextContract = nextContract with { ImplementationNotes = implNotes }; contractUpdated = true; }

        var auditReqs = context.GetString("contractAuditRequirements") ?? context.GetString("auditRequirements");
        if (auditReqs is not null) { nextContract = nextContract with { AuditRequirements = auditReqs }; contractUpdated = true; }

        var validationReqs = context.GetString("contractValidationRequirements") ?? context.GetString("validationRequirements");
        if (validationReqs is not null) { nextContract = nextContract with { ValidationRequirements = validationReqs }; contractUpdated = true; }

        var parallelNotes = context.GetString("contractParallelizationNotes") ?? context.GetString("parallelizationNotes");
        if (parallelNotes is not null) { nextContract = nextContract with { ParallelizationNotes = parallelNotes }; contractUpdated = true; }

        var archConstraints = context.GetString("contractArchitectureConstraints") ?? context.GetString("architectureConstraints");
        if (archConstraints is not null) { nextContract = nextContract with { ArchitectureConstraints = ParseList(archConstraints) }; contractUpdated = true; }

        var requiredFiles = context.GetString("contractRequiredFilesOrAreas") ?? context.GetString("requiredFilesOrAreas");
        if (requiredFiles is not null) { nextContract = nextContract with { RequiredFilesOrAreas = ParseList(requiredFiles) }; contractUpdated = true; }

        var acceptanceCriteria = context.GetString("contractAcceptanceCriteria") ?? context.GetString("acceptanceCriteria");
        if (acceptanceCriteria is not null) { nextContract = nextContract with { AcceptanceCriteria = ParseList(acceptanceCriteria) }; contractUpdated = true; }

        var dependsOnIds = context.GetString("contractDependsOnPhaseIds") ?? context.GetString("dependsOnPhaseIds");
        if (dependsOnIds is not null) { nextContract = nextContract with { DependsOnPhaseIds = ParseList(dependsOnIds) }; contractUpdated = true; }

        var requiresVal = context.GetString("requiresValidation") ?? context.GetString("contractRequiresValidation");
        if (requiresVal is not null)
        {
            nextContract = nextContract with { RequiresValidation = context.GetBool("requiresValidation", defaultValue: true) };
            contractUpdated = true;
        }

        if (contractUpdated)
            updated = updated with { Contract = nextContract };

        // Sub-phase updates
        var subPhaseId = context.GetString("subPhaseId");
        if (!string.IsNullOrWhiteSpace(subPhaseId))
        {
            var subPhaseSummary = context.GetString("subPhaseSummary");
            var subPhaseDeps = context.GetString("subPhaseDependencies");
            var foundSubPhase = false;

            var subPhases = updated.SubPhases.Select(sp =>
            {
                if (!string.Equals(sp.SubPhaseId, subPhaseId, StringComparison.OrdinalIgnoreCase))
                    return sp;

                foundSubPhase = true;
                var next = sp;
                if (subPhaseSummary is not null)
                    next = next with { Summary = subPhaseSummary };
                if (subPhaseDeps is not null)
                    next = next with { DependsOn = ParseList(subPhaseDeps) };

                return next with { UpdatedUtc = DateTimeOffset.UtcNow };
            }).ToArray();

            if (!foundSubPhase)
                return OperationResult.Fail("NotFound", $"Sub-phase '{subPhaseId}' not found in phase '{phaseId}'.");

            updated = updated with { SubPhases = subPhases };
        }

        if (updated == phase)
            return OperationResult.Fail("ValidationFailed",
                "No changes provided. Supply phase, contract, or sub-phase update parameters.");

        updated = updated with { UpdatedUtc = DateTimeOffset.UtcNow };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.updated",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"Phase {phaseId} metadata updated"
        });

        return OperationResult.Ok(updated);
    }

    private static string[] ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var separators = raw.Contains("||", StringComparison.Ordinal)
            ? ["||"]
            : raw.Contains('\n', StringComparison.Ordinal)
                ? ["\r\n", "\n"]
                : new[] { "," };

        return raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>Completes implementation (IN_IMPLEMENTATION → IMPLEMENTATION_LOGGED).</summary>
public sealed class CompleteImplementationHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();

    public string OperationName => WorkflowOperations.CompleteImplementation;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var result = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.ImplementationLogged
        });
        if (result.IsFailure)
            return OperationResult.FromError(result.Error);

        var updated = phase with
        {
            State      = PhaseState.ImplementationLogged,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.implementation.completed",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"{phaseId} implementation complete"
        });

        return OperationResult.Ok(updated);
    }
}

/// <summary>Updates phase status with an explicit target state.</summary>
public sealed class UpdatePhaseStatusHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();

    public string OperationName => WorkflowOperations.UpdatePhaseStatus;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var targetStateStr = context.GetString("state") ?? context.GetString("targetState");
        if (string.IsNullOrWhiteSpace(targetStateStr))
            return OperationResult.Fail("ValidationFailed", "Parameter 'state' is required.");

        if (!Enum.TryParse<PhaseState>(targetStateStr, ignoreCase: true, out var targetState))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown state '{targetStateStr}'. Valid states: {string.Join(", ", Enum.GetNames<PhaseState>())}");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        // PHASE-008: Idempotency — if already in the requested state, treat as success.
        if (phase.State == targetState)
            return OperationResult.Ok(new { phaseId, state = targetState.ToString(), alreadyInState = true });

        // Check if phase has any SkippedNotPossible validation records — blocks clean Pass.
        bool hasSkippedNotPossible = false;
        if (targetState == PhaseState.Pass)
        {
            var validations = store.ReadAllValidations()
                .Where(v => v.PhaseId == phaseId)
                .ToList();
            hasSkippedNotPossible = validations.Any(v => v.ValidationType == ValidationType.SkippedNotPossible);
        }

        var result = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = targetState,
            RequiresValidation = phase.Contract?.RequiresValidation ?? true,
            BlockerReason = context.GetString("blockerReason"),
            BlockerId     = context.GetString("blockerId"),
            HasSkippedNotPossible = hasSkippedNotPossible
        });
        if (result.IsFailure)
            return OperationResult.FromError(result.Error);

        var updated = phase with
        {
            State      = targetState,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.status.updated",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"{phaseId} status changed to {targetState}"
        });

        return OperationResult.Ok(updated);
    }
}

/// <summary>Assigns an agent to a phase.</summary>
public sealed class AssignPhaseHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();

    public string OperationName => WorkflowOperations.AssignPhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var agentName = context.GetString("agent") ?? context.GetString("assignedAgent");
        if (string.IsNullOrWhiteSpace(agentName))
            return OperationResult.Fail("ValidationFailed", "Parameter 'agent' is required.");

        if (!Enum.TryParse<WorkflowActor>(agentName, ignoreCase: true, out var agent))
            return OperationResult.Fail("ValidationFailed", $"Unknown agent '{agentName}'.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var result = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.AssignedToImplementation
        });
        if (result.IsFailure)
            return OperationResult.FromError(result.Error);

        var updated = phase with
        {
            State         = PhaseState.AssignedToImplementation,
            AssignedAgent = agent,
            UpdatedUtc    = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.assigned",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"{phaseId} assigned to {agent}"
        });

        return OperationResult.Ok(updated);
    }
}

/// <summary>Starts a phase (ASSIGNED_TO_IMPLEMENTATION → IN_IMPLEMENTATION).</summary>
public sealed class StartPhaseHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();

    public string OperationName => WorkflowOperations.StartPhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var result = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.InImplementation
        });
        if (result.IsFailure)
            return OperationResult.FromError(result.Error);

        var updated = phase with
        {
            State      = PhaseState.InImplementation,
            StartedUtc = phase.StartedUtc ?? DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.started",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"{phaseId} implementation started"
        });

        return OperationResult.Ok(updated);
    }
}

/// <summary>Removes a phase from the workflow.</summary>
public sealed class RemovePhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RemovePhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        if (!store.PhaseExists(phaseId))
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        store.DeletePhase(phaseId);

        var state = store.LoadWorkflow();
        store.SaveWorkflow(state with
        {
            PhaseIds = state.PhaseIds.Where(id => id != phaseId).ToArray(),
            CurrentPhaseId = state.CurrentPhaseId == phaseId ? null : state.CurrentPhaseId,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.removed",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"Phase {phaseId} removed"
        });

        return OperationResult.Ok(new { removed = phaseId });
    }
}

/// <summary>
/// PHASE-008: Reopens a phase that is in a terminal failure state.
///
/// Only FailedValidation, FailedArchitecture, and FailedCompile phases can be
/// reopened; Pass and PassWithWarnings are intentionally not recoverable via
/// this path. A non-empty reason is required so the audit trail is informative.
///
/// The phase transitions to ReadyForImplementation, bypassing the normal
/// PhaseTransitionValidator terminal-state guard (which would otherwise block it).
/// </summary>
public sealed class ReopenPhaseHandler(WorkflowStore store) : IOperationHandler
{
    private static readonly IReadOnlySet<PhaseState> ReopenableStates = new HashSet<PhaseState>
    {
        PhaseState.FailedValidation,
        PhaseState.FailedArchitecture,
        PhaseState.FailedCompile,
        // PHASE-014: Blocked is also recoverable via reopen as a manual fallback
        // (the normal path is auto-advance on last-blocker resolve).
        PhaseState.Blocked
    };

    public string OperationName => WorkflowOperations.ReopenPhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed",
                "Parameter 'reason' is required. Explain why this phase is being reopened.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        if (!ReopenableStates.Contains(phase.State))
            return OperationResult.Fail("InvalidTransition",
                $"Phase '{phaseId}' is in state '{phase.State}' and cannot be reopened. " +
                $"Reopenable states: FailedValidation, FailedArchitecture, FailedCompile, Blocked.");

        var previousState = phase.State;
        var updated = phase with
        {
            State      = PhaseState.ReadyForImplementation,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.reopened",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"{phaseId} reopened from {previousState}: {reason}"
        });

        return OperationResult.Ok(new
        {
            phaseId,
            previousState = previousState.ToString(),
            newState      = PhaseState.ReadyForImplementation.ToString(),
            reason
        });
    }
}
