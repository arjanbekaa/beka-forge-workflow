using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class ValidateChangeSetHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ValidateChangeSet;

    public OperationResult Execute(OperationContext context)
    {
        var load = WorkflowChangeSetService.Load(context, store.WorkflowRoot);
        if (!load.Success)
            return OperationResult.Fail(load.ErrorCode!, load.Message!);

        var report = WorkflowChangeSetService.Validate(load.ChangeSet!, load.FilePath!, store);
        return OperationResult.Ok(report);
    }
}

public sealed class ApplyChangeSetHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.ApplyChangeSet;

    public OperationResult Execute(OperationContext context)
    {
        var load = WorkflowChangeSetService.Load(context, store.WorkflowRoot);
        if (!load.Success)
            return OperationResult.Fail(load.ErrorCode!, load.Message!);

        var changeSet = load.ChangeSet!;
        var dryRun = context.GetBool("dryRun") || string.Equals(changeSet.Mode, "dry-run", StringComparison.OrdinalIgnoreCase);
        var syncMarkdown = context.GetBool("syncMarkdown");
        var report = WorkflowChangeSetService.Apply(changeSet, load.FilePath!, store, context.Actor, dryRun, syncMarkdown);
        return OperationResult.Ok(report);
    }
}

internal static class WorkflowChangeSetService
{
    private const string CurrentSchemaVersion = "1.0";
    private const string RefPrefix = "$ref:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyDictionary<string, string> OperationMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["createPhase"] = WorkflowOperations.CreatePhase,
            ["setNextAction"] = WorkflowOperations.SetNextAction,
            ["syncMarkdown"] = WorkflowOperations.SyncMarkdown
        };

    private static readonly IReadOnlyDictionary<string, string> CreatePhaseParameterAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["architectureConstraints"] = "contractArchitectureConstraints",
            ["requiredFilesOrAreas"] = "contractRequiredFilesOrAreas",
            ["acceptanceCriteria"] = "contractAcceptanceCriteria",
            ["implementationNotes"] = "contractImplementationNotes",
            ["auditRequirements"] = "contractAuditRequirements",
            ["validationRequirements"] = "contractValidationRequirements",
            ["parallelizationNotes"] = "contractParallelizationNotes",
            ["dependsOnPhaseIds"] = "contractDependsOnPhaseIds",
            ["executionLanesJson"] = "contractExecutionLanesJson",
            ["requiresValidation"] = "requiresValidation"
        };

    public static ChangeSetLoadResult Load(OperationContext context, string workflowRoot)
    {
        var rawPath = context.GetString("filePath") ?? context.GetString("file") ?? context.GetString("path");
        if (string.IsNullOrWhiteSpace(rawPath))
            return ChangeSetLoadResult.Fail("ValidationFailed", "Parameter 'file' is required.");

        var filePath = Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Combine(workflowRoot, rawPath));

        if (!File.Exists(filePath))
            return ChangeSetLoadResult.Fail("FileNotFound", $"ChangeSet file not found: {filePath}");

        try
        {
            var json = File.ReadAllText(filePath);
            var changeSet = JsonSerializer.Deserialize<WorkflowChangeSet>(json, JsonOptions);
            if (changeSet is null)
                return ChangeSetLoadResult.Fail("InvalidJson", "ChangeSet file did not contain a JSON object.");

            return ChangeSetLoadResult.Ok(filePath, changeSet);
        }
        catch (JsonException ex)
        {
            return ChangeSetLoadResult.Fail("InvalidJson", $"Invalid ChangeSet JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ChangeSetLoadResult.Fail("FileReadFailed", $"Could not read ChangeSet file: {ex.Message}");
        }
    }

    public static WorkflowChangeSetValidationReport Validate(
        WorkflowChangeSet changeSet,
        string filePath,
        WorkflowStore store)
    {
        var issues = new List<WorkflowChangeSetIssue>();
        var warnings = new List<WorkflowChangeSetIssue>();
        var previews = new List<WorkflowChangeSetOperationPreview>();
        var knownRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.Equals(changeSet.SchemaVersion, CurrentSchemaVersion, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue("UnsupportedSchemaVersion",
                $"schemaVersion must be '{CurrentSchemaVersion}'. Actual: '{changeSet.SchemaVersion}'."));
        }

        if (changeSet.Operations.Count == 0)
            issues.Add(Issue("MissingOperations", "ChangeSet must include at least one operation."));

        for (var i = 0; i < changeSet.Operations.Count; i++)
        {
            var operation = changeSet.Operations[i];
            var type = operation.Type?.Trim();

            if (string.IsNullOrWhiteSpace(type))
            {
                issues.Add(Issue("MissingOperationType", "Operation type is required.", i, operation));
                continue;
            }

            if (IsRawFileMutation(type))
            {
                issues.Add(Issue("RawFileMutationRejected",
                    $"Operation type '{type}' is not allowed. ChangeSets cannot write files directly.", i, operation));
                continue;
            }

            if (!OperationMap.TryGetValue(type, out var workflowOperation))
            {
                issues.Add(Issue("UnknownOperationType",
                    $"Unknown ChangeSet operation type '{type}'. Allowed: {string.Join(", ", OperationMap.Keys)}.", i, operation));
                continue;
            }

            ValidateReferences(operation, i, knownRefs, store, issues);
            ValidateRequiredParameters(operation, i, store, issues);

            if (!string.IsNullOrWhiteSpace(operation.RefId))
            {
                if (!knownRefs.TryAdd(operation.RefId.Trim(), RefKindFor(type)))
                {
                    issues.Add(Issue("DuplicateRefId",
                        $"Duplicate refId '{operation.RefId}'. RefIds must be unique.", i, operation));
                }
            }

            previews.Add(new WorkflowChangeSetOperationPreview
            {
                OperationIndex = i,
                Type = type,
                RefId = operation.RefId,
                OperationName = workflowOperation,
                Summary = PreviewSummary(type, operation),
                WouldWrite = workflowOperation != WorkflowOperations.ValidateChangeSet,
                ResolvedParameters = NormalizeParameters(operation, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), forPreview: true)
            });
        }

        return new WorkflowChangeSetValidationReport
        {
            FilePath = filePath,
            Title = changeSet.Title,
            Description = changeSet.Description,
            Issues = issues,
            Warnings = warnings,
            OperationPreviews = previews
        };
    }

    public static WorkflowChangeSetApplyReport Apply(
        WorkflowChangeSet changeSet,
        string filePath,
        WorkflowStore store,
        WorkflowActor actor,
        bool dryRun,
        bool syncMarkdown)
    {
        var validation = Validate(changeSet, filePath, store);
        if (!validation.IsValid || dryRun)
        {
            return new WorkflowChangeSetApplyReport
            {
                DryRun = dryRun,
                Applied = false,
                Validation = validation,
                Warnings = validation.Warnings
            };
        }

        var dispatcher = new OperationDispatcher(store);
        var createdIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var applied = new List<WorkflowChangeSetAppliedOperation>();
        var warnings = new List<WorkflowChangeSetIssue>();

        for (var i = 0; i < changeSet.Operations.Count; i++)
        {
            var operation = changeSet.Operations[i];
            var type = operation.Type!.Trim();
            var workflowOperation = OperationMap[type];
            var parameters = NormalizeParameters(operation, createdIds, forPreview: false);
            var phaseId = ExtractPhaseId(parameters);

            var result = dispatcher.Dispatch(new OperationContext
            {
                Operation = workflowOperation,
                Actor = actor,
                PhaseId = phaseId,
                Parameters = parameters
            });

            if (!result.Success)
            {
                return new WorkflowChangeSetApplyReport
                {
                    DryRun = false,
                    Applied = false,
                    Validation = validation,
                    AppliedOperations = applied,
                    CreatedIds = createdIds,
                    Warnings = [..warnings, Issue(result.ErrorCode ?? "ApplyFailed", result.Message ?? "ChangeSet apply failed.", i, operation)]
                };
            }

            var createdId = ExtractCreatedId(type, result.Data);
            if (!string.IsNullOrWhiteSpace(operation.RefId) && !string.IsNullOrWhiteSpace(createdId))
                createdIds[operation.RefId.Trim()] = createdId;

            applied.Add(new WorkflowChangeSetAppliedOperation
            {
                OperationIndex = i,
                Type = type,
                RefId = operation.RefId,
                OperationName = workflowOperation,
                CreatedId = createdId,
                Success = true,
                Message = result.Message
            });
        }

        if (syncMarkdown)
        {
            var syncResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.SyncMarkdown,
                Actor = actor,
                Parameters = new Dictionary<string, object?>()
            });

            if (!syncResult.Success)
            {
                warnings.Add(Issue("MarkdownSyncWarning",
                    syncResult.Message ?? "Markdown sync failed after ChangeSet apply."));
            }
            else
            {
                applied.Add(new WorkflowChangeSetAppliedOperation
                {
                    OperationIndex = changeSet.Operations.Count,
                    Type = "syncMarkdown",
                    OperationName = WorkflowOperations.SyncMarkdown,
                    Success = true,
                    Message = syncResult.Message
                });
            }
        }

        return new WorkflowChangeSetApplyReport
        {
            DryRun = false,
            Applied = true,
            Validation = validation,
            AppliedOperations = applied,
            CreatedIds = createdIds,
            Warnings = warnings
        };
    }

    private static void ValidateRequiredParameters(
        WorkflowChangeSetOperation operation,
        int index,
        WorkflowStore store,
        List<WorkflowChangeSetIssue> issues)
    {
        var type = operation.Type!.Trim();
        var parameters = operation.Parameters;

        if (type.Equals("createPhase", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasText(parameters, "title"))
                issues.Add(Issue("MissingPhaseTitle", "createPhase requires parameters.title.", index, operation));

            if (!HasText(parameters, "objective") && !HasText(parameters, "contractObjective"))
                issues.Add(Issue("MissingContractObjective", "createPhase requires parameters.objective or parameters.contractObjective.", index, operation));

            if (!HasText(parameters, "scope") && !HasText(parameters, "contractScope"))
                issues.Add(Issue("MissingContractScope", "createPhase requires parameters.scope or parameters.contractScope.", index, operation));

            var explicitPhaseId = GetString(parameters, "phaseId");
            if (!string.IsNullOrWhiteSpace(explicitPhaseId) && store.PhaseExists(explicitPhaseId))
            {
                issues.Add(Issue("PhaseAlreadyExists",
                    $"Phase '{explicitPhaseId}' already exists.", index, operation));
            }
        }
        else if (type.Equals("setNextAction", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasText(parameters, "description"))
                issues.Add(Issue("MissingDescription", "setNextAction requires parameters.description.", index, operation));

            if (!HasText(parameters, "actor"))
                issues.Add(Issue("MissingActor", "setNextAction requires parameters.actor.", index, operation));
        }
    }

    private static void ValidateReferences(
        WorkflowChangeSetOperation operation,
        int index,
        IReadOnlyDictionary<string, string> knownRefs,
        WorkflowStore store,
        List<WorkflowChangeSetIssue> issues)
    {
        foreach (var reference in ExtractRefs(operation.Parameters.Values))
        {
            if (!knownRefs.ContainsKey(reference))
            {
                issues.Add(Issue("UnresolvedRefId",
                    $"Reference '$ref:{reference}' must point to a prior operation refId.", index, operation));
            }
        }

        foreach (var phaseReference in ExtractPhaseReferences(operation))
        {
            if (TryGetRefName(phaseReference, out var refName))
            {
                if (!knownRefs.TryGetValue(refName, out var refKind) || refKind != "phase")
                {
                    issues.Add(Issue("InvalidPhaseRef",
                        $"Phase reference '$ref:{refName}' must point to a prior createPhase operation.", index, operation));
                }

                continue;
            }

            if (!store.PhaseExists(phaseReference))
            {
                issues.Add(Issue("MissingDependency",
                    $"Referenced phase '{phaseReference}' does not exist and is not a local ref.", index, operation));
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeParameters(
        WorkflowChangeSetOperation operation,
        IReadOnlyDictionary<string, string> createdIds,
        bool forPreview)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, value) in operation.Parameters)
        {
            var key = NormalizeParameterName(operation.Type, rawKey);
            result[key] = ConvertJsonValue(key, value, createdIds, forPreview);
        }

        return result;
    }

    private static string NormalizeParameterName(string? operationType, string key)
    {
        if (operationType is not null
            && operationType.Equals("createPhase", StringComparison.OrdinalIgnoreCase)
            && CreatePhaseParameterAliases.TryGetValue(key, out var mapped))
        {
            return mapped;
        }

        return key;
    }

    private static object? ConvertJsonValue(
        string key,
        JsonElement value,
        IReadOnlyDictionary<string, string> createdIds,
        bool forPreview)
    {
        if (key.EndsWith("Json", StringComparison.OrdinalIgnoreCase))
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

        return value.ValueKind switch
        {
            JsonValueKind.String => ResolveString(value.GetString() ?? string.Empty, createdIds, forPreview),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Array => string.Join("||", value.EnumerateArray().Select(item =>
                ConvertJsonValue(key, item, createdIds, forPreview)?.ToString()).Where(item => !string.IsNullOrWhiteSpace(item))),
            JsonValueKind.Object => value.GetRawText(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private static string ResolveString(string value, IReadOnlyDictionary<string, string> createdIds, bool forPreview)
    {
        if (!TryGetRefName(value, out var refName))
            return value;

        if (createdIds.TryGetValue(refName, out var createdId))
            return createdId;

        return forPreview ? value : throw new InvalidOperationException($"Unresolved refId '{refName}'.");
    }

    private static string? ExtractPhaseId(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("phaseId", out var value) || value is null)
            return null;

        var phaseId = value.ToString();
        return string.IsNullOrWhiteSpace(phaseId) ? null : phaseId;
    }

    private static string? ExtractCreatedId(string operationType, object? data)
    {
        if (operationType.Equals("createPhase", StringComparison.OrdinalIgnoreCase) && data is Phase phase)
            return phase.PhaseId;

        if (operationType.Equals("setNextAction", StringComparison.OrdinalIgnoreCase) && data is NextAction nextAction)
            return nextAction.ActionId;

        return null;
    }

    private static string PreviewSummary(string type, WorkflowChangeSetOperation operation)
    {
        var parameters = operation.Parameters;
        return type.ToLowerInvariant() switch
        {
            "createphase" => $"Create phase '{GetString(parameters, "title") ?? "(missing title)"}'.",
            "setnextaction" => $"Set next action '{GetString(parameters, "description") ?? "(missing description)"}'.",
            "syncmarkdown" => "Regenerate markdown read models.",
            _ => type
        };
    }

    private static IEnumerable<string> ExtractPhaseReferences(WorkflowChangeSetOperation operation)
    {
        foreach (var key in new[]
        {
            "dependencies",
            "dependsOnPhaseIds",
            "contractDependsOnPhaseIds"
        })
        {
            if (TryGetProperty(operation.Parameters, key, out var value))
            {
                foreach (var item in ExtractList(value))
                    yield return item;
            }
        }

        if (operation.Type?.Equals("setNextAction", StringComparison.OrdinalIgnoreCase) == true
            && TryGetProperty(operation.Parameters, "phaseId", out var phaseIdElement))
        {
            var phaseId = GetElementString(phaseIdElement);
            if (!string.IsNullOrWhiteSpace(phaseId))
                yield return phaseId;
        }
    }

    private static IEnumerable<string> ExtractList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = GetElementString(item);
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
        else
        {
            var raw = GetElementString(element);
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var separators = raw.Contains("||", StringComparison.Ordinal)
                ? ["||"]
                : raw.Contains('\n', StringComparison.Ordinal)
                    ? ["\r\n", "\n"]
                    : new[] { "," };

            foreach (var item in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return item;
        }
    }

    private static IEnumerable<string> ExtractRefs(IEnumerable<JsonElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    if (TryGetRefName(element.GetString(), out var refName))
                        yield return refName;
                    break;
                case JsonValueKind.Array:
                    foreach (var item in ExtractRefs(element.EnumerateArray()))
                        yield return item;
                    break;
                case JsonValueKind.Object:
                    foreach (var item in ExtractRefs(element.EnumerateObject().Select(p => p.Value)))
                        yield return item;
                    break;
            }
        }
    }

    private static bool TryGetRefName(string? value, out string refName)
    {
        refName = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(RefPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        refName = value[RefPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(refName);
    }

    private static bool TryGetProperty(
        IReadOnlyDictionary<string, JsonElement> parameters,
        string key,
        out JsonElement value)
    {
        foreach (var item in parameters)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool HasText(IReadOnlyDictionary<string, JsonElement> parameters, string key) =>
        TryGetProperty(parameters, key, out var element)
        && !string.IsNullOrWhiteSpace(GetElementString(element));

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> parameters, string key) =>
        TryGetProperty(parameters, key, out var element) ? GetElementString(element) : null;

    private static string? GetElementString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            _ => null
        };

    private static bool IsRawFileMutation(string operationType)
    {
        var normalized = operationType.Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return normalized is "writefile" or "rawwritefile" or "deletefile" or "replacefile";
    }

    private static string RefKindFor(string operationType) =>
        operationType.Equals("createPhase", StringComparison.OrdinalIgnoreCase)
            ? "phase"
            : operationType.Equals("setNextAction", StringComparison.OrdinalIgnoreCase)
                ? "nextAction"
                : "none";

    private static WorkflowChangeSetIssue Issue(
        string code,
        string message,
        int? operationIndex = null,
        WorkflowChangeSetOperation? operation = null,
        string severity = "error") =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            OperationIndex = operationIndex,
            OperationType = operation?.Type,
            RefId = operation?.RefId
        };
}

internal sealed record ChangeSetLoadResult
{
    public required bool Success { get; init; }
    public string? FilePath { get; init; }
    public WorkflowChangeSet? ChangeSet { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static ChangeSetLoadResult Ok(string filePath, WorkflowChangeSet changeSet) =>
        new() { Success = true, FilePath = filePath, ChangeSet = changeSet };

    public static ChangeSetLoadResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };
}
