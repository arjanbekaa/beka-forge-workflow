using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Creates a new phase and appends it to the workflow.</summary>
public sealed class CreatePhaseHandler(WorkflowStore store) : IOperationHandler
{
    private static readonly JsonSerializerOptions SubPhaseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

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

        // Parse phase number from the new ID (PHASE-NNN -> NNN).
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
        var executionLanesRaw = context.GetString("contractExecutionLanesJson") ?? context.GetString("executionLanesJson");
        var wantsContract = !string.IsNullOrWhiteSpace(objective)
            || !string.IsNullOrWhiteSpace(scope)
            || executionLanesRaw is not null;
        if (wantsContract)
        {
            if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(scope))
                return OperationResult.Fail("ValidationFailed",
                    "Both contract objective and scope are required when creating a phase contract.");

            if (!TryParseExecutionLanesJson(executionLanesRaw, out var executionLanes, out var executionLaneError))
                return OperationResult.Fail("ValidationFailed", executionLaneError!);

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
                ExecutionLanes = executionLanes,
                RequiresValidation = context.GetBool("requiresValidation", defaultValue: true)
            };
        }

        if (!TryParseSubPhasesJson(context.GetString("subPhasesJson"), phaseId, out var subPhases, out var subPhaseError))
            return OperationResult.Fail("ValidationFailed", subPhaseError!);

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
            SubPhases = subPhases
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

    private static bool TryParseSubPhasesJson(
        string? raw,
        string phaseId,
        out IReadOnlyList<SubPhase> subPhases,
        out string? error)
    {
        subPhases = [];
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<SubPhaseInput>>(raw, SubPhaseJsonOptions);
            if (parsed is null)
            {
                error = "subPhasesJson must be a JSON array.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mapped = new List<SubPhase>(parsed.Count);

            foreach (var item in parsed)
            {
                var subPhaseId = item.SubPhaseId?.Trim();
                if (string.IsNullOrWhiteSpace(subPhaseId))
                {
                    error = "Each sub-phase in subPhasesJson must include subPhaseId.";
                    return false;
                }

                if (!subPhaseId.StartsWith(phaseId + "-", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Sub-phase '{subPhaseId}' must start with '{phaseId}-'.";
                    return false;
                }

                if (!seen.Add(subPhaseId))
                {
                    error = $"Duplicate sub-phase ID '{subPhaseId}' in subPhasesJson.";
                    return false;
                }

                var title = item.Title?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    error = $"Sub-phase '{subPhaseId}' must include title.";
                    return false;
                }

                var dependsOn = item.DependsOn?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                    ?? [];

                mapped.Add(new SubPhase
                {
                    SubPhaseId = subPhaseId,
                    Title = title,
                    Status = item.Status ?? SubPhaseStatus.Planned,
                    DependsOn = dependsOn,
                    Summary = item.Summary?.Trim() ?? string.Empty
                });
            }

            subPhases = mapped;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid subPhasesJson payload: {ex.Message}";
            return false;
        }
    }

    private sealed record SubPhaseInput
    {
        public string? SubPhaseId { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public SubPhaseStatus? Status { get; init; }
        public IReadOnlyList<string>? DependsOn { get; init; }
    }

    private static bool TryParseExecutionLanesJson(
        string? raw,
        out IReadOnlyList<ExecutionLane> executionLanes,
        out string? error)
    {
        executionLanes = [];
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ExecutionLaneInput>>(raw, SubPhaseJsonOptions);
            if (parsed is null)
            {
                error = "executionLanesJson must be a JSON array.";
                return false;
            }

            var seenLaneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mapped = new List<ExecutionLane>(parsed.Count);

            foreach (var item in parsed)
            {
                var laneId = item.LaneId?.Trim();
                if (string.IsNullOrWhiteSpace(laneId))
                {
                    error = "Each execution lane in executionLanesJson must include laneId.";
                    return false;
                }

                if (!seenLaneIds.Add(laneId))
                {
                    error = $"Duplicate execution lane ID '{laneId}' in executionLanesJson.";
                    return false;
                }

                var title = item.Title?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    error = $"Execution lane '{laneId}' must include title.";
                    return false;
                }

                var phaseIds = NormalizeList(item.PhaseIds);
                var subPhaseIds = NormalizeList(item.SubPhaseIds);
                if (phaseIds.Length == 0 && subPhaseIds.Length == 0)
                {
                    error = $"Execution lane '{laneId}' must include at least one phaseId or subPhaseId.";
                    return false;
                }

                mapped.Add(new ExecutionLane
                {
                    LaneId = laneId,
                    Title = title,
                    Summary = item.Summary?.Trim() ?? string.Empty,
                    PhaseIds = phaseIds,
                    SubPhaseIds = subPhaseIds,
                    DependsOnLaneIds = NormalizeList(item.DependsOnLaneIds),
                    OwnedAreas = NormalizeList(item.OwnedAreas),
                    CoordinationNotes = item.CoordinationNotes?.Trim() ?? string.Empty
                });
            }

            var knownLaneIds = mapped.Select(lane => lane.LaneId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var lane in mapped)
            {
                if (lane.DependsOnLaneIds.Contains(lane.LaneId, StringComparer.OrdinalIgnoreCase))
                {
                    error = $"Execution lane '{lane.LaneId}' cannot depend on itself.";
                    return false;
                }

                var missingDependency = lane.DependsOnLaneIds
                    .FirstOrDefault(dep => !knownLaneIds.Contains(dep));
                if (missingDependency is not null)
                {
                    error = $"Execution lane '{lane.LaneId}' depends on unknown lane '{missingDependency}'.";
                    return false;
                }
            }

            executionLanes = mapped;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid executionLanesJson payload: {ex.Message}";
            return false;
        }
    }

    private static string[] NormalizeList(IReadOnlyList<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private sealed record ExecutionLaneInput
    {
        public string? LaneId { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public IReadOnlyList<string>? PhaseIds { get; init; }
        public IReadOnlyList<string>? SubPhaseIds { get; init; }
        public IReadOnlyList<string>? DependsOnLaneIds { get; init; }
        public IReadOnlyList<string>? OwnedAreas { get; init; }
        public string? CoordinationNotes { get; init; }
    }

    private static int ParsePhaseSortNumber(string phaseId) =>
        TryParsePhaseNumber(phaseId, out var number) ? number : int.MaxValue;
}

/// <summary>Updates a phase's metadata (title, summary, contract, sub-phases).</summary>
public sealed class UpdatePhaseHandler(WorkflowStore store) : IOperationHandler
{
    private static readonly JsonSerializerOptions SubPhaseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

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

        var subPhasesJson = context.GetString("subPhasesJson");

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

        var executionLanesRaw = context.GetString("contractExecutionLanesJson") ?? context.GetString("executionLanesJson");
        if (executionLanesRaw is not null)
        {
            if (!TryParseExecutionLanesJson(executionLanesRaw, out var executionLanes, out var executionLaneError))
                return OperationResult.Fail("ValidationFailed", executionLaneError!);

            nextContract = nextContract with { ExecutionLanes = executionLanes };
            contractUpdated = true;
        }

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
        if (!string.IsNullOrWhiteSpace(subPhaseId) && subPhasesJson is not null)
            return OperationResult.Fail("ValidationFailed",
                "Provide either subPhasesJson for full sub-phase replacement or subPhaseId for a targeted update, not both.");

        if (subPhasesJson is not null)
        {
            if (!TryParseSubPhasesJson(subPhasesJson, phaseId, out var parsedSubPhases, out var subPhaseError))
                return OperationResult.Fail("ValidationFailed", subPhaseError!);

            updated = updated with { SubPhases = parsedSubPhases };
        }

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

    private static bool TryParseSubPhasesJson(
        string? raw,
        string phaseId,
        out IReadOnlyList<SubPhase> subPhases,
        out string? error)
    {
        subPhases = [];
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<SubPhaseInput>>(raw, SubPhaseJsonOptions);
            if (parsed is null)
            {
                error = "subPhasesJson must be a JSON array.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mapped = new List<SubPhase>(parsed.Count);

            foreach (var item in parsed)
            {
                var subPhaseId = item.SubPhaseId?.Trim();
                if (string.IsNullOrWhiteSpace(subPhaseId))
                {
                    error = "Each sub-phase in subPhasesJson must include subPhaseId.";
                    return false;
                }

                if (!subPhaseId.StartsWith(phaseId + "-", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Sub-phase '{subPhaseId}' must start with '{phaseId}-'.";
                    return false;
                }

                if (!seen.Add(subPhaseId))
                {
                    error = $"Duplicate sub-phase ID '{subPhaseId}' in subPhasesJson.";
                    return false;
                }

                var title = item.Title?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    error = $"Sub-phase '{subPhaseId}' must include title.";
                    return false;
                }

                var dependsOn = item.DependsOn?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                    ?? [];

                mapped.Add(new SubPhase
                {
                    SubPhaseId = subPhaseId,
                    Title = title,
                    Status = item.Status ?? SubPhaseStatus.Planned,
                    DependsOn = dependsOn,
                    Summary = item.Summary?.Trim() ?? string.Empty
                });
            }

            subPhases = mapped;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid subPhasesJson payload: {ex.Message}";
            return false;
        }
    }

    private sealed record SubPhaseInput
    {
        public string? SubPhaseId { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public SubPhaseStatus? Status { get; init; }
        public IReadOnlyList<string>? DependsOn { get; init; }
    }

    private static bool TryParseExecutionLanesJson(
        string? raw,
        out IReadOnlyList<ExecutionLane> executionLanes,
        out string? error)
    {
        executionLanes = [];
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ExecutionLaneInput>>(raw, SubPhaseJsonOptions);
            if (parsed is null)
            {
                error = "executionLanesJson must be a JSON array.";
                return false;
            }

            var seenLaneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mapped = new List<ExecutionLane>(parsed.Count);

            foreach (var item in parsed)
            {
                var laneId = item.LaneId?.Trim();
                if (string.IsNullOrWhiteSpace(laneId))
                {
                    error = "Each execution lane in executionLanesJson must include laneId.";
                    return false;
                }

                if (!seenLaneIds.Add(laneId))
                {
                    error = $"Duplicate execution lane ID '{laneId}' in executionLanesJson.";
                    return false;
                }

                var title = item.Title?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    error = $"Execution lane '{laneId}' must include title.";
                    return false;
                }

                var phaseIds = NormalizeList(item.PhaseIds);
                var subPhaseIds = NormalizeList(item.SubPhaseIds);
                if (phaseIds.Length == 0 && subPhaseIds.Length == 0)
                {
                    error = $"Execution lane '{laneId}' must include at least one phaseId or subPhaseId.";
                    return false;
                }

                mapped.Add(new ExecutionLane
                {
                    LaneId = laneId,
                    Title = title,
                    Summary = item.Summary?.Trim() ?? string.Empty,
                    PhaseIds = phaseIds,
                    SubPhaseIds = subPhaseIds,
                    DependsOnLaneIds = NormalizeList(item.DependsOnLaneIds),
                    OwnedAreas = NormalizeList(item.OwnedAreas),
                    CoordinationNotes = item.CoordinationNotes?.Trim() ?? string.Empty
                });
            }

            var knownLaneIds = mapped.Select(lane => lane.LaneId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var lane in mapped)
            {
                if (lane.DependsOnLaneIds.Contains(lane.LaneId, StringComparer.OrdinalIgnoreCase))
                {
                    error = $"Execution lane '{lane.LaneId}' cannot depend on itself.";
                    return false;
                }

                var missingDependency = lane.DependsOnLaneIds
                    .FirstOrDefault(dep => !knownLaneIds.Contains(dep));
                if (missingDependency is not null)
                {
                    error = $"Execution lane '{lane.LaneId}' depends on unknown lane '{missingDependency}'.";
                    return false;
                }
            }

            executionLanes = mapped;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid executionLanesJson payload: {ex.Message}";
            return false;
        }
    }

    private static string[] NormalizeList(IReadOnlyList<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private sealed record ExecutionLaneInput
    {
        public string? LaneId { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public IReadOnlyList<string>? PhaseIds { get; init; }
        public IReadOnlyList<string>? SubPhaseIds { get; init; }
        public IReadOnlyList<string>? DependsOnLaneIds { get; init; }
        public IReadOnlyList<string>? OwnedAreas { get; init; }
        public string? CoordinationNotes { get; init; }
    }
}

/// <summary>Completes implementation (IN_IMPLEMENTATION -> IMPLEMENTATION_LOGGED).</summary>
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
        WorkflowStatusSnapshot.UpdateWorkflowLastStatusIfCurrentPhase(store, phaseId, updated.State);

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

        // PHASE-008: Idempotency - if already in the requested state, treat as success.
        if (phase.State == targetState)
            return OperationResult.Ok(new { phaseId, state = targetState.ToString(), alreadyInState = true });

        // Check if phase has any SkippedNotPossible validation records - blocks clean Pass.
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
        WorkflowStatusSnapshot.UpdateWorkflowLastStatusIfCurrentPhase(store, phaseId, updated.State);

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
        WorkflowStatusSnapshot.UpdateWorkflowLastStatusIfCurrentPhase(store, phaseId, updated.State);

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

/// <summary>Starts a phase (ASSIGNED_TO_IMPLEMENTATION -> IN_IMPLEMENTATION).</summary>
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
        WorkflowStatusSnapshot.UpdateWorkflowLastStatusIfCurrentPhase(store, phaseId, updated.State);

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

/// <summary>Explicitly defers a phase so work can safely continue elsewhere without faking a lifecycle transition.</summary>
public sealed class DeferPhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.DeferPhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId ?? context.GetString("phaseId");
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed", "Parameter 'reason' is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        if (PhaseTransitionValidator.IsTerminal(phase.State))
            return OperationResult.Fail("InvalidTransition",
                $"Phase '{phaseId}' is already terminal ({phase.State}) and should not be deferred.");

        var continueWithPhaseId = context.GetString("continueWithPhaseId") ?? context.GetString("continueWith");
        if (!string.IsNullOrWhiteSpace(continueWithPhaseId))
        {
            if (string.Equals(phaseId, continueWithPhaseId, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("ValidationFailed",
                    "continueWithPhaseId must refer to a different phase.");

            if (store.LoadPhase(continueWithPhaseId) is null)
                return OperationResult.Fail("NotFound",
                    $"Continue-with phase '{continueWithPhaseId}' not found.");
        }

        var updatedPhase = phase with
        {
            DeferredReason = reason.Trim(),
            DeferredBy = context.Actor,
            DeferredUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var workflow = store.LoadWorkflow();
        var updatedWorkflow = workflow with
        {
            CurrentPhaseId = string.Equals(workflow.CurrentPhaseId, phaseId, StringComparison.OrdinalIgnoreCase)
                ? continueWithPhaseId
                : workflow.CurrentPhaseId,
            LastStatus = string.Equals(workflow.CurrentPhaseId, phaseId, StringComparison.OrdinalIgnoreCase)
                ? (continueWithPhaseId is not null && store.LoadPhase(continueWithPhaseId) is { } continueWithPhase
                    ? continueWithPhase.State
                    : workflow.LastStatus)
                : workflow.LastStatus,
            NextAction = ShouldClearNextAction(workflow.NextAction, phaseId)
                ? null
                : workflow.NextAction,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveWorkflow(updatedWorkflow);

        var summary = string.IsNullOrWhiteSpace(continueWithPhaseId)
            ? $"{phaseId} deferred: {reason}"
            : $"{phaseId} deferred in favor of {continueWithPhaseId}: {reason}";

        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "phase.deferred",
            Actor = context.Actor,
            PhaseId = phaseId,
            Summary = summary,
            PayloadReference = continueWithPhaseId
        });

        return OperationResult.Ok(new
        {
            phaseId,
            deferred = true,
            continueWithPhaseId,
            currentPhaseId = updatedWorkflow.CurrentPhaseId
        });
    }

    private static bool ShouldClearNextAction(NextAction? nextAction, string phaseId) =>
        nextAction is not null
        && string.Equals(nextAction.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Moves workflow focus to a phase without changing its lifecycle state.</summary>
public sealed class FocusPhaseHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.FocusPhase;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId ?? context.GetString("phaseId");
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed", "Parameter 'reason' is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var updatedPhase = phase with
        {
            DeferredReason = null,
            DeferredBy = null,
            DeferredUtc = null,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var workflow = store.LoadWorkflow();
        store.SaveWorkflow(workflow with
        {
            CurrentPhaseId = phaseId,
            LastStatus = updatedPhase.State,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "phase.focused",
            Actor = context.Actor,
            PhaseId = phaseId,
            Summary = $"{phaseId} focused: {reason}"
        });

        return OperationResult.Ok(new
        {
            phaseId,
            focused = true,
            currentPhaseId = phaseId
        });
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
            LastStatus = state.CurrentPhaseId == phaseId ? null : state.LastStatus,
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
        WorkflowStatusSnapshot.UpdateWorkflowLastStatusIfCurrentPhase(store, phaseId, updated.State);

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

internal static class WorkflowStatusSnapshot
{
    public static void UpdateWorkflowLastStatusIfCurrentPhase(WorkflowStore store, string phaseId, PhaseState state)
    {
        var workflow = store.LoadWorkflow();
        if (!string.Equals(workflow.CurrentPhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            return;

        store.SaveWorkflow(workflow with
        {
            LastStatus = state,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }
}
