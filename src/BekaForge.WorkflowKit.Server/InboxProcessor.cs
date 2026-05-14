using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// Processes pending operations in the offline operation inbox.
/// Pending items are validated before dispatch and successful/failed artifacts
/// are written under inbox/processed and inbox/failed only.
/// </summary>
public sealed class InboxProcessor
{
    private const string OperationFileSuffix = ".operation.json";
    private const string ProcessedFileSuffix = ".processed.json";
    private const string FailedFileSuffix = ".failed.json";

    private readonly string _root;
    private readonly OperationDispatcher? _dispatcher;
    private readonly JsonSerializerOptions _jsonOptions;

    public InboxProcessor(string workflowRoot, OperationDispatcher? dispatcher)
    {
        _root = workflowRoot;
        _dispatcher = dispatcher;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Processes all pending operations in the inbox.
    /// Returns a summary of results.
    /// </summary>
    public ProcessInboxResult ProcessAll(WorkflowActor actor = WorkflowActor.WorkflowKit)
    {
        var inboxDir = WorkflowLayout.InboxDir(_root);
        if (!Directory.Exists(inboxDir))
        {
            return new ProcessInboxResult
            {
                Processed = false,
                TotalPending = 0,
                Succeeded = 0,
                Failed = 0,
                Skipped = 0,
                Errors = ["Inbox directory does not exist. No pending operations to process."]
            };
        }

        var pendingFiles = Directory.GetFiles(inboxDir, $"*{OperationFileSuffix}");
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var filePath in pendingFiles)
        {
            var fileName = Path.GetFileName(filePath);
            var artifactKey = DeriveArtifactKeyFromFileName(fileName);

            try
            {
                var rawJson = File.ReadAllText(filePath);
                if (!TryDeserializePending(rawJson, out var pending, out var deserializeError))
                {
                    failed++;
                    errors.Add($"{fileName}: {deserializeError}");
                    WriteFailed(
                        artifactKey,
                        "(unreadable pending operation)",
                        "unknown",
                        null,
                        DateTimeOffset.UtcNow,
                        "InvalidPendingOperation",
                        deserializeError,
                        "deserialization");
                    SafeDelete(filePath);
                    continue;
                }

                if (!IsSafeIdempotencyKey(pending!.IdempotencyKey))
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "InvalidIdempotencyKey",
                        "IdempotencyKey must use only letters, numbers, '.', '-', or '_' and must not contain path separators.",
                        "validation");
                    SafeDelete(filePath);
                    continue;
                }

                if (!string.Equals(pending.FileNameWithExtension, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "MismatchedInboxFileName",
                        "Pending file name must match the idempotency key declared inside the JSON payload.",
                        "validation");
                    SafeDelete(filePath);
                    continue;
                }

                artifactKey = pending.IdempotencyKey;

                if (IsAlreadyProcessed(artifactKey))
                {
                    skipped++;
                    SafeDelete(filePath);
                    continue;
                }

                var manifestEntry = OperationManifestCatalog.Find(pending.OperationName);
                if (manifestEntry is null)
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "UnknownOperation",
                        $"Operation '{pending.OperationName}' is not registered in the manifest.",
                        "validation");
                    SafeDelete(filePath);
                    continue;
                }

                var requestedActor = ParseActor(pending.Actor);
                if (requestedActor is null)
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "UnknownActor",
                        $"Actor '{pending.Actor}' is not recognized.",
                        "validation");
                    SafeDelete(filePath);
                    continue;
                }

                if (_dispatcher is null)
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "NoDispatcher",
                        "InboxProcessor has no dispatcher configured.",
                        "dispatch");
                    SafeDelete(filePath);
                    continue;
                }

                var dispatchParameters = ConvertParameters(pending.Parameters);
                var validationParameters = new Dictionary<string, object?>(dispatchParameters, StringComparer.OrdinalIgnoreCase)
                {
                    ["targetOperation"] = pending.OperationName
                };

                var validationContext = new OperationContext
                {
                    Operation = WorkflowOperations.ValidateOperationRequest,
                    Actor = requestedActor.Value,
                    PhaseId = pending.PhaseId,
                    Parameters = validationParameters
                };

                var validationDispatch = _dispatcher.Dispatch(validationContext);
                if (!validationDispatch.Success || validationDispatch.Data is not OperationValidationResult validationResult)
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "ValidationDispatchFailed",
                        validationDispatch.Message ?? "Failed to validate pending operation before dispatch.",
                        "validation");
                    SafeDelete(filePath);
                    continue;
                }

                if (!validationResult.IsValid)
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "OperationValidationFailed",
                        FormatValidationIssues(validationResult.Issues),
                        "validation");
                    SafeDelete(filePath);
                    continue;
                }

                var context = new OperationContext
                {
                    Operation = pending.OperationName,
                    Actor = requestedActor.Value,
                    PhaseId = pending.PhaseId,
                    Parameters = dispatchParameters
                };

                OperationResult result;
                try
                {
                    result = _dispatcher.Dispatch(context);
                }
                catch (Exception ex)
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        "DispatchException",
                        $"Unhandled exception during dispatch: {ex.Message}",
                        "dispatch");
                    SafeDelete(filePath);
                    continue;
                }

                if (result.Success)
                {
                    succeeded++;
                    WriteProcessed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        result);
                }
                else
                {
                    failed++;
                    WriteFailed(
                        artifactKey,
                        pending.OperationName,
                        pending.Actor,
                        pending.PhaseId,
                        pending.CreatedUtc,
                        result.ErrorCode ?? "DispatchFailed",
                        result.Message ?? "Operation returned failure.",
                        "dispatch");
                }

                SafeDelete(filePath);
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{fileName}: Unexpected error: {ex.Message}");
                WriteFailed(
                    artifactKey,
                    "(unexpected pending operation failure)",
                    "unknown",
                    null,
                    DateTimeOffset.UtcNow,
                    "UnexpectedInboxError",
                    ex.Message,
                    "unknown");
                SafeDelete(filePath);
            }
        }

        return new ProcessInboxResult
        {
            Processed = true,
            TotalPending = pendingFiles.Length,
            Succeeded = succeeded,
            Failed = failed,
            Skipped = skipped,
            Errors = errors
        };
    }

    /// <summary>
    /// Returns the current inbox status without processing anything.
    /// </summary>
    public InboxStatus GetStatus()
    {
        var inboxDir = WorkflowLayout.InboxDir(_root);
        var processedDir = WorkflowLayout.InboxProcessedDir(_root);
        var failedDir = WorkflowLayout.InboxFailedDir(_root);

        var inboxAvailable = Directory.Exists(inboxDir);

        var pendingFiles = inboxAvailable
            ? Directory.GetFiles(inboxDir, $"*{OperationFileSuffix}")
            : [];
        var processedCount = Directory.Exists(processedDir)
            ? Directory.GetFiles(processedDir, $"*{ProcessedFileSuffix}").Length
            : 0;
        var failedCount = Directory.Exists(failedDir)
            ? Directory.GetFiles(failedDir, $"*{FailedFileSuffix}").Length
            : 0;

        DateTimeOffset? oldestPending = null;
        foreach (var file in pendingFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                if (TryDeserializePending(json, out var pending, out _)
                    && pending is not null
                    && (oldestPending is null || pending.CreatedUtc < oldestPending))
                {
                    oldestPending = pending.CreatedUtc;
                }
            }
            catch
            {
            }
        }

        return new InboxStatus
        {
            PendingCount = pendingFiles.Length,
            ProcessedCount = processedCount,
            FailedCount = failedCount,
            OldestPendingUtc = oldestPending,
            InboxAvailable = inboxAvailable,
            PendingFiles = pendingFiles.Select(Path.GetFileName).ToList()!
        };
    }

    private bool IsAlreadyProcessed(string artifactKey)
    {
        var processedPath = Path.Combine(WorkflowLayout.InboxProcessedDir(_root), $"{artifactKey}{ProcessedFileSuffix}");
        if (File.Exists(processedPath))
            return true;

        var failedPath = Path.Combine(WorkflowLayout.InboxFailedDir(_root), $"{artifactKey}{FailedFileSuffix}");
        return File.Exists(failedPath);
    }

    private void WriteProcessed(string artifactKey, string operationName, string actor, string? phaseId,
        DateTimeOffset createdUtc, OperationResult result)
    {
        var dir = WorkflowLayout.InboxProcessedDir(_root);
        Directory.CreateDirectory(dir);

        var processed = new ProcessedOperation
        {
            OperationName = operationName,
            Actor = actor,
            PhaseId = phaseId,
            IdempotencyKey = artifactKey,
            CreatedUtc = createdUtc,
            ProcessedUtc = DateTimeOffset.UtcNow,
            DispatchSuccess = result.Success,
            ResultSummary = Truncate(result.Data?.ToString() ?? result.Message ?? "(no data)", 500),
            EventId = null
        };

        var path = Path.Combine(dir, $"{artifactKey}{ProcessedFileSuffix}");
        var json = JsonSerializer.Serialize(processed, _jsonOptions);
        File.WriteAllText(path, json);
    }

    private void WriteFailed(string artifactKey, string operationName, string actor, string? phaseId,
        DateTimeOffset createdUtc, string errorCode, string errorMessage, string stage)
    {
        var dir = WorkflowLayout.InboxFailedDir(_root);
        Directory.CreateDirectory(dir);

        var failed = new FailedOperation
        {
            OperationName = operationName,
            Actor = actor,
            PhaseId = phaseId,
            IdempotencyKey = artifactKey,
            CreatedUtc = createdUtc,
            FailedUtc = DateTimeOffset.UtcNow,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            FailureStage = stage
        };

        var path = Path.Combine(dir, $"{artifactKey}{FailedFileSuffix}");
        var json = JsonSerializer.Serialize(failed, _jsonOptions);
        File.WriteAllText(path, json);
    }

    private bool TryDeserializePending(string json, out PendingOperation? pending, out string errorMessage)
    {
        try
        {
            pending = JsonSerializer.Deserialize<PendingOperation>(json, _jsonOptions);
            if (pending is null)
            {
                errorMessage = "Pending operation payload deserialized to null.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            pending = null;
            errorMessage = $"Could not deserialize pending operation: {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyDictionary<string, object?> ConvertParameters(
        IReadOnlyDictionary<string, object?> parameters)
    {
        return parameters.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value is JsonElement element ? ConvertJsonElement(element) : kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatValidationIssues(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
            return "Validation failed without specific issues.";

        return string.Join(" | ", issues.Select(i => $"{i.Code}: {i.Message}"));
    }

    private static string DeriveArtifactKeyFromFileName(string fileName)
    {
        var stem = fileName.EndsWith(OperationFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^OperationFileSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        var sanitized = new string(stem
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_')
            .ToArray())
            .Trim('_');

        return string.IsNullOrWhiteSpace(sanitized)
            ? $"invalid-{Guid.NewGuid():N}"
            : sanitized;
    }

    private static bool IsSafeIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!string.Equals(key, Path.GetFileName(key), StringComparison.Ordinal))
            return false;

        return key.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_');
    }

    private static void SafeDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
        }
    }

    private static WorkflowActor? ParseActor(string name) => name?.ToLowerInvariant() switch
    {
        "codex" => WorkflowActor.Codex,
        "deepseek" => WorkflowActor.DeepSeek,
        "planner" => WorkflowActor.Planner,
        "implementer" => WorkflowActor.Implementer,
        "auditor" => WorkflowActor.Auditor,
        "reviewer" => WorkflowActor.Reviewer,
        "validator" => WorkflowActor.Validator,
        "fixer" => WorkflowActor.Fixer,
        "humanowner" => WorkflowActor.HumanOwner,
        "workflowsystem" => WorkflowActor.WorkflowSystem,
        _ => null
    };

    private static object? ConvertJsonElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 3)] + "...";
}

/// <summary>
/// Result summary returned by workflow.process_inbox.
/// </summary>
public sealed record ProcessInboxResult
{
    public required bool Processed { get; init; }
    public required int TotalPending { get; init; }
    public required int Succeeded { get; init; }
    public required int Failed { get; init; }
    public required int Skipped { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}
