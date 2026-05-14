using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
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
            ? store.NextPhaseId()
            : explicitPhaseId.Trim().ToUpperInvariant();

        // Parse phase number from the new ID (PHASE-NNN → NNN).
        if (!TryParsePhaseNumber(phaseId, out var phaseNumber))
            return OperationResult.Fail("ValidationFailed",
                $"Phase ID '{phaseId}' must use the PHASE-NNN format.");

        if (store.PhaseExists(phaseId) || state.PhaseIds.Contains(phaseId, StringComparer.OrdinalIgnoreCase))
            return OperationResult.Fail("ValidationFailed",
                $"Phase '{phaseId}' already exists.");

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
                UnityTestRequirements = context.GetString("contractUnityTestRequirements") ?? string.Empty,
                ParallelizationNotes = context.GetString("contractParallelizationNotes") ?? string.Empty,
                DependsOnPhaseIds = ParseList(context.GetString("contractDependsOnPhaseIds")),
                RequiresUnityTest = context.GetBool("requiresUnityTest", defaultValue: true)
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
            Contract = contract,
            SubPhases = ParseSubPhases(context.GetString("subPhasesJson") ?? context.GetString("subPhases")),
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        store.SavePhase(phase);

        // Add to the workflow phase list.
        var phaseIds = state.PhaseIds
            .Where(id => !string.Equals(id, phaseId, StringComparison.OrdinalIgnoreCase))
            .Append(phaseId)
            .OrderBy(id => TryParsePhaseNumber(id, out var number) ? number : int.MaxValue)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var updatedState = state with
        {
            PhaseIds = phaseIds,
            CurrentPhaseId = state.CurrentPhaseId ?? phaseId,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveWorkflow(updatedState);

        // Event.
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

    private static bool TryParsePhaseNumber(string phaseId, out int phaseNumber)
    {
        phaseNumber = 0;
        return phaseId.StartsWith("PHASE-", StringComparison.OrdinalIgnoreCase)
            && phaseId.Length == 9
            && int.TryParse(phaseId[6..], out phaseNumber);
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

    private static IReadOnlyList<SubPhase> ParseSubPhases(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var subPhases = new List<SubPhase>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                var subPhaseId = GetString(element, "subPhaseId");
                var title = GetString(element, "title");
                if (string.IsNullOrWhiteSpace(subPhaseId) || string.IsNullOrWhiteSpace(title))
                    continue;

                subPhases.Add(new SubPhase
                {
                    SubPhaseId = subPhaseId,
                    Title = title,
                    Status = ParseSubPhaseStatus(GetString(element, "status")),
                    DependsOn = ParseStringArray(element, "dependsOn"),
                    Summary = GetString(element, "summary") ?? string.Empty,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
            }

            return subPhases;
        }
        catch (JsonException)
        {
            return ParseSubPhaseLines(raw);
        }
    }

    private static IReadOnlyList<SubPhase> ParseSubPhaseLines(string raw)
    {
        var subPhases = new List<SubPhase>();
        foreach (var line in raw.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('~', 4, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                continue;

            subPhases.Add(new SubPhase
            {
                SubPhaseId = parts[0],
                Title = parts[1],
                Summary = parts.Length > 2 ? parts[2] : string.Empty,
                DependsOn = parts.Length > 3
                    ? parts[3].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [],
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        return subPhases;
    }

    private static SubPhaseStatus ParseSubPhaseStatus(string? raw) =>
        Enum.TryParse<SubPhaseStatus>(raw, ignoreCase: true, out var status)
            ? status
            : SubPhaseStatus.Planned;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string[] ParseStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }
}

/// <summary>Transitions a phase to a new status using the state machine validator.</summary>
public sealed class UpdatePhaseStatusHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();

    public string OperationName => WorkflowOperations.UpdatePhaseStatus;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var targetStateName = context.GetString("state");
        if (!Enum.TryParse<PhaseState>(targetStateName, ignoreCase: true, out var targetState))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown phase state '{targetStateName}'. See PhaseState enum for valid values.");

        var blockerReason = context.GetString("blockerReason");
        var blockerId     = context.GetString("blockerId");
        var requiresUnityTest = phase.Contract?.RequiresUnityTest ?? true;

        var result = _validator.Validate(new TransitionContext
        {
            CurrentState    = phase.State,
            TargetState     = targetState,
            RequiresUnityTest = requiresUnityTest,
            BlockerReason   = blockerReason,
            BlockerId       = blockerId
        });

        if (result.IsFailure)
            return OperationResult.FromError(result.Error);

        var now = DateTimeOffset.UtcNow;
        var updatedPhase = phase with
        {
            State = targetState,
            UpdatedUtc = now,
            StartedUtc = (targetState == PhaseState.InImplementation && phase.StartedUtc is null)
                ? now : phase.StartedUtc,
            CompletedUtc = PhaseTransitionValidator.IsTerminal(targetState) ? now : phase.CompletedUtc
        };
        store.SavePhase(updatedPhase);

        // Update workflow last status.
        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with { LastStatus = targetState, UpdatedUtc = now });

        store.AppendEvent(new WorkflowEvent
        {
            EventId   = store.NextEventId(),
            EventType = "phase.status.changed",
            Actor     = context.Actor,
            PhaseId   = phaseId,
            Summary   = $"{phaseId} transitioned from {phase.State} to {targetState}"
        });

        return OperationResult.Ok(updatedPhase);
    }
}

/// <summary>Removes a planned phase from workflow state and deletes its generated phase files.</summary>
public sealed class RemovePhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.RemovePhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var workflow = store.LoadWorkflow();
        var wasRegistered = workflow.PhaseIds.Contains(phaseId, StringComparer.OrdinalIgnoreCase);
        if (!wasRegistered && !store.PhaseExists(phaseId))
        {
            var cleaned = CleanupRemovedPhaseReferences(phaseId);
            if (cleaned == 0)
                return OperationResult.Fail("NotFound", $"Phase '{phaseId}' is not registered in workflow.json.");

            return OperationResult.Ok(new { phaseId, cleanedReferenceCount = cleaned });
        }

        var phase = store.LoadPhase(phaseId);
        if (phase is not null && phase.State != PhaseState.Planned)
            return OperationResult.Fail("ValidationFailed",
                $"Only planned phases can be removed. Current state: {phase.State}.");

        var updatedPhaseIds = workflow.PhaseIds
            .Where(id => !string.Equals(id, phaseId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (updatedPhaseIds.Length == 0)
            return OperationResult.Fail("ValidationFailed", "Cannot remove the only phase in the workflow.");

        var replacementCurrentPhase = string.Equals(workflow.CurrentPhaseId, phaseId, StringComparison.OrdinalIgnoreCase)
            ? updatedPhaseIds.LastOrDefault()
            : workflow.CurrentPhaseId;

        store.DeletePhase(phaseId);
        store.SaveWorkflow(workflow with
        {
            PhaseIds = updatedPhaseIds,
            CurrentPhaseId = replacementCurrentPhase,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        var phaseMarkdownPath = WorkflowLayout.PhaseMdPath(store.WorkflowRoot, phaseId);
        if (File.Exists(phaseMarkdownPath))
            File.Delete(phaseMarkdownPath);

        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "phase.removed",
            Actor = context.Actor,
            PhaseId = phaseId,
            Summary = $"Phase {phaseId} removed from workflow planning"
        });

        return OperationResult.Ok(new
        {
            phaseId,
            removedPhaseFile = phase is not null,
            remainingPhaseCount = updatedPhaseIds.Length,
            cleanedReferenceCount = CleanupRemovedPhaseReferences(phaseId)
        });
    }

    private int CleanupRemovedPhaseReferences(string removedPhaseId)
    {
        var cleaned = 0;

        foreach (var phase in store.LoadAllPhases())
        {
            var updated = phase;

            var dependencies = RemoveId(phase.Dependencies, removedPhaseId);
            if (dependencies.Count != phase.Dependencies.Count)
            {
                updated = updated with { Dependencies = dependencies };
                cleaned++;
            }

            if (phase.Contract is not null)
            {
                var contractDependencies = RemoveId(phase.Contract.DependsOnPhaseIds, removedPhaseId);
                if (contractDependencies.Count != phase.Contract.DependsOnPhaseIds.Count)
                {
                    updated = updated with
                    {
                        Contract = phase.Contract with { DependsOnPhaseIds = contractDependencies }
                    };
                    cleaned++;
                }
            }

            var subPhases = phase.SubPhases
                .Select(sp =>
                {
                    var subDeps = RemoveId(sp.DependsOn, removedPhaseId);
                    if (subDeps.Count == sp.DependsOn.Count)
                        return sp;

                    cleaned++;
                    return sp with { DependsOn = subDeps, UpdatedUtc = DateTimeOffset.UtcNow };
                })
                .ToArray();

            if (!ReferenceEquals(updated, phase) || subPhases.Where((sp, i) => !ReferenceEquals(sp, phase.SubPhases[i])).Any())
            {
                store.SavePhase(updated with
                {
                    SubPhases = subPhases,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
            }
        }

        return cleaned;
    }

    private static IReadOnlyList<string> RemoveId(IReadOnlyList<string> values, string removedId) =>
        values
            .Where(value => !string.Equals(value, removedId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
}

/// <summary>Assigns an agent to a phase (moves to ASSIGNED_TO_IMPLEMENTATION).</summary>
public sealed class AssignPhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.AssignPhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var agentName = context.GetString("agent");
        if (!Enum.TryParse<WorkflowActor>(agentName, ignoreCase: true, out var agent))
            return OperationResult.Fail("ValidationFailed", $"Unknown agent '{agentName}'.");

        // Assign uses the normal state machine: must be in ReadyForImplementation.
        if (phase.State != PhaseState.ReadyForImplementation)
            return OperationResult.Fail("InvalidTransition",
                $"Phase must be in ReadyForImplementation to assign. Current state: {phase.State}");

        var updated = phase with
        {
            State = PhaseState.AssignedToImplementation,
            AssignedAgent = agent,
            UpdatedUtc = DateTimeOffset.UtcNow
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

/// <summary>Starts implementation (ASSIGNED_TO_IMPLEMENTATION → IN_IMPLEMENTATION).</summary>
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

        var now = DateTimeOffset.UtcNow;
        var updated = phase with
        {
            State      = PhaseState.InImplementation,
            StartedUtc = now,
            UpdatedUtc = now
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

/// <summary>Updates non-status phase metadata (title, summary, dependencies).</summary>
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

        var title = context.GetString("title");
        if (!string.IsNullOrWhiteSpace(title))
            updated = updated with { Title = title };

        var summary = context.GetString("summary");
        if (summary is not null) // allow clearing summary by passing empty string
            updated = updated with { Summary = summary };

        var dependenciesRaw = context.GetString("dependencies");
        if (dependenciesRaw is not null)
            updated = updated with { Dependencies = ParseList(dependenciesRaw) };

        var hasContractUpdates =
            context.GetString("contractObjective") is not null ||
            context.GetString("contractScope") is not null ||
            context.GetString("contractOutOfScope") is not null ||
            context.GetString("contractImplementationNotes") is not null ||
            context.GetString("contractAuditRequirements") is not null ||
            context.GetString("contractUnityTestRequirements") is not null ||
            context.GetString("contractParallelizationNotes") is not null ||
            context.GetString("contractArchitectureConstraints") is not null ||
            context.GetString("contractRequiredFilesOrAreas") is not null ||
            context.GetString("contractAcceptanceCriteria") is not null ||
            context.GetString("contractDependsOnPhaseIds") is not null ||
            context.GetString("requiresUnityTest") is not null;

        var contract = updated.Contract;
        if (contract is null && hasContractUpdates)
        {
            var objective = context.GetString("contractObjective");
            var scope = context.GetString("contractScope");
            if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(scope))
                return OperationResult.Fail("ValidationFailed",
                    "contractObjective and contractScope are required when creating a new phase contract.");

            contract = new PhaseContract
            {
                Objective = objective,
                Scope = scope,
                RequiresUnityTest = true
            };
        }

        if (contract is not null)
        {
            var contractUpdated = false;
            var nextContract = contract;

            if (context.GetString("contractObjective") is { } objective)
            {
                nextContract = nextContract with { Objective = objective };
                contractUpdated = true;
            }
            if (context.GetString("contractScope") is { } scope)
            {
                nextContract = nextContract with { Scope = scope };
                contractUpdated = true;
            }
            if (context.GetString("contractOutOfScope") is { } outOfScope)
            {
                nextContract = nextContract with { OutOfScope = outOfScope };
                contractUpdated = true;
            }
            if (context.GetString("contractImplementationNotes") is { } implementationNotes)
            {
                nextContract = nextContract with { ImplementationNotes = implementationNotes };
                contractUpdated = true;
            }
            if (context.GetString("contractAuditRequirements") is { } auditRequirements)
            {
                nextContract = nextContract with { AuditRequirements = auditRequirements };
                contractUpdated = true;
            }
            if (context.GetString("contractUnityTestRequirements") is { } unityTestRequirements)
            {
                nextContract = nextContract with { UnityTestRequirements = unityTestRequirements };
                contractUpdated = true;
            }
            if (context.GetString("contractParallelizationNotes") is { } parallelizationNotes)
            {
                nextContract = nextContract with { ParallelizationNotes = parallelizationNotes };
                contractUpdated = true;
            }
            if (context.GetString("contractArchitectureConstraints") is { } architectureConstraints)
            {
                nextContract = nextContract with { ArchitectureConstraints = ParseList(architectureConstraints) };
                contractUpdated = true;
            }
            if (context.GetString("contractRequiredFilesOrAreas") is { } requiredFilesOrAreas)
            {
                nextContract = nextContract with { RequiredFilesOrAreas = ParseList(requiredFilesOrAreas) };
                contractUpdated = true;
            }
            if (context.GetString("contractAcceptanceCriteria") is { } acceptanceCriteria)
            {
                nextContract = nextContract with { AcceptanceCriteria = ParseList(acceptanceCriteria) };
                contractUpdated = true;
            }
            if (context.GetString("contractDependsOnPhaseIds") is { } dependsOnPhaseIds)
            {
                nextContract = nextContract with { DependsOnPhaseIds = ParseList(dependsOnPhaseIds) };
                contractUpdated = true;
            }
            if (context.GetString("requiresUnityTest") is { } requiresUnityTest &&
                bool.TryParse(requiresUnityTest, out var parsedRequiresUnityTest))
            {
                nextContract = nextContract with { RequiresUnityTest = parsedRequiresUnityTest };
                contractUpdated = true;
            }

            if (contractUpdated)
                updated = updated with { Contract = nextContract };
        }

        var subPhaseId = context.GetString("subPhaseId");
        if (!string.IsNullOrWhiteSpace(subPhaseId))
        {
            var subPhaseSummary = context.GetString("subPhaseSummary");
            var subPhaseDependencies = context.GetString("subPhaseDependencies");
            var foundSubPhase = false;

            var subPhases = updated.SubPhases.Select(sp =>
            {
                if (!string.Equals(sp.SubPhaseId, subPhaseId, StringComparison.OrdinalIgnoreCase))
                    return sp;

                foundSubPhase = true;
                var next = sp;
                if (subPhaseSummary is not null)
                    next = next with { Summary = subPhaseSummary };
                if (subPhaseDependencies is not null)
                    next = next with { DependsOn = ParseList(subPhaseDependencies) };

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

    private static string[] ParseList(string raw)
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
