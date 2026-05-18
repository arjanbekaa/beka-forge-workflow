using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Creates a validation log entry and advances phase to TEST_LOGGED.
///
/// Honesty rules (enforced here, not in the validator):
/// - Passed/PassedWithWarnings requires at least one evidence item.
/// - BrowserManual, UnityManual, HumanValidationRequired cannot be marked
///   Passed/PassedWithWarnings by a non-human actor. Use PendingUser instead.
/// - Skipped validation requires a non-empty skipReason.
/// - SkippedByUserOverride requires an approvedBy field.
/// </summary>
public sealed class CreateValidationLogHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.CreateValidationLog;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var summary = context.GetString("summary");
        if (string.IsNullOrWhiteSpace(summary))
            return OperationResult.Fail("ValidationFailed", "Parameter 'summary' is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        // Parse validation type
        var typeStr = context.GetString("validationType") ?? "AutomatedCommand";
        if (!Enum.TryParse<ValidationType>(typeStr, ignoreCase: true, out var validationType))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown validation type '{typeStr}'. Valid types: {string.Join(", ", Enum.GetNames<ValidationType>())}");

        // Parse validation result
        var resultStr = context.GetString("validationResult") ?? "Passed";
        if (!Enum.TryParse<ValidationResult>(resultStr, ignoreCase: true, out var validationResult))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown validation result '{resultStr}'. Valid results: {string.Join(", ", Enum.GetNames<ValidationResult>())}");

        // -- Honesty rule 1: Passed/PassedWithWarnings requires evidence ----------
        var evidenceItems = ParseEvidenceItems(context.GetString("evidenceItems"));
        bool isPassed = validationResult is ValidationResult.Passed or ValidationResult.PassedWithWarnings;
        var notes = context.GetString("notes") ?? string.Empty;

        if (isPassed && evidenceItems.Count == 0)
            return OperationResult.Fail("EvidenceRequired",
                "Validation cannot be marked as Passed without at least one evidence item. " +
                "Provide evidenceItems with descriptions and sources. " +
                "If you cannot provide evidence, use PendingUser result instead.");

        // -- Honesty rule 2: Manual types cannot be marked Passed by LLM ----------
        bool isManualType = validationType is ValidationType.BrowserManual
            or ValidationType.UnityManual
            or ValidationType.HumanValidationRequired;

        bool isHumanActor = context.Actor == WorkflowActor.HumanOwner
            || context.Actor == WorkflowActor.User;

        if (isPassed && isManualType && !isHumanActor)
            return OperationResult.Fail("ManualValidationRequiresHuman",
                $"Validation type '{validationType}' requires human confirmation. " +
                "An LLM agent cannot mark this as Passed. " +
                "Use PendingUser result and request the user to run validation instead.");

        // -- Honesty rule 3: Skipped requires skipReason --------------------------
        if (validationResult == ValidationResult.Skipped)
        {
            var skipReason = context.GetString("skipReason");
            if (string.IsNullOrWhiteSpace(skipReason))
                return OperationResult.Fail("SkipReasonRequired",
                    "Skipped validation requires a non-empty skipReason. " +
                    "Explain why validation is not needed or cannot be performed.");

            // SkippedNotPossible additionally requires a riskNote.
            if (validationType == ValidationType.SkippedNotPossible)
            {
                var riskNote = context.GetString("riskNote");
                if (string.IsNullOrWhiteSpace(riskNote))
                    return OperationResult.Fail("RiskNoteRequired",
                        "SkippedNotPossible requires a riskNote describing the risk accepted by skipping. " +
                        "This phase may only reach PassWithWarnings, not clean Pass.");
            }

            if (validationType == ValidationType.SkippedByUserOverride)
            {
                var approvedBy = context.GetString("approvedBy");
                if (string.IsNullOrWhiteSpace(approvedBy))
                    return OperationResult.Fail("UserOverrideRequiresApproval",
                        "SkippedByUserOverride requires an approvedBy field identifying the human owner.");
            }
        }

        // -- Honesty rule 4: PendingHumanValidation is valid for manual types -----
        // (no rejection needed — PendingHumanValidation is always allowed)

        // -- Transition check -----------------------------------------------------
        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.TestLogged,
            RequiresValidation = phase.Contract?.RequiresValidation ?? true
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var qualityFailure = LogQualityPolicy.Validate(store, WorkflowLogKind.Validation, summary, notes);
        if (qualityFailure is not null)
            return qualityFailure;

        // -- Build and append the record ------------------------------------------
        var validationId = store.NextValidationId();
        var record = new ValidationRecord
        {
            ValidationId     = validationId,
            PhaseId          = phaseId,
            Actor            = context.Actor,
            ValidationType   = validationType,
            ValidationResult = validationResult,
            Summary          = summary,
            HasWarnings      = context.GetBool("hasWarnings"),
            FailedChecks     = ParseList(context.GetString("failedChecks")),
            EvidenceItems    = evidenceItems,
            Command          = context.GetString("command"),
            ExitCode         = context.Get<int?>("exitCode"),
            ManualSteps      = ParseList(context.GetString("manualSteps")),
            SkipReason       = context.GetString("skipReason"),
            RiskNote         = context.GetString("riskNote"),
            ApprovedBy       = context.GetString("approvedBy"),
            Notes            = notes,
            CreatedUtc       = DateTimeOffset.UtcNow
        };

        store.AppendValidation(record);

        // -- Update phase ---------------------------------------------------------
        var updatedPhase = phase with
        {
            State = PhaseState.TestLogged,
            ValidationLogIds = [..phase.ValidationLogIds, validationId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        // -- Update workflow state ------------------------------------------------
        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastValidationId = validationId,
            LastStatus       = PhaseState.TestLogged,
            UpdatedUtc       = DateTimeOffset.UtcNow
        });

        // -- Append event ---------------------------------------------------------
        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "validation.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Validation logged for {phaseId}: {validationId} ({validationResult})",
            PayloadReference = validationId
        });

        return OperationResult.Ok(record);
    }

    // -- Helpers -----------------------------------------------------------------

    private static IReadOnlyList<ValidationEvidence> ParseEvidenceItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ValidationEvidence>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>
/// Generates a validation plan explaining what must be tested, what the agent
/// can test, what requires the user, and exact manual steps.
/// </summary>
public sealed class GetValidationPlanHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetValidationPlan;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var contract = phase.Contract;
        var requiresValidation = contract?.RequiresValidation ?? true;

        if (!requiresValidation)
        {
            return OperationResult.Ok(new
            {
                PhaseId = phaseId,
                ValidationRequired = false,
                Message = "Validation is not required for this phase. Use workflow.skip_validation to log a skipped validation with a reason."
            });
        }

        // Build the validation plan
        var validationRequirements = contract?.ValidationRequirements ?? "No specific validation requirements defined.";
        var acceptanceCriteria = contract?.AcceptanceCriteria ?? [];

        // Determine what the agent can test
        var agentCanTest = new List<string>();
        var requiresUser = new List<string>();
        var manualSteps = new List<string>();

        // Basic heuristics for determining testability
        if (!string.IsNullOrWhiteSpace(validationRequirements))
        {
            var req = validationRequirements.ToLowerInvariant();
            
            if (req.Contains("browser") || req.Contains("web") || req.Contains("ui") || req.Contains("visual"))
            {
                requiresUser.Add("Browser-based testing requires manual human verification.");
                manualSteps.Add("1. Open the application in a browser.");
                manualSteps.Add("2. Verify the UI renders correctly.");
                manualSteps.Add("3. Test all user interactions and flows.");
                manualSteps.Add("4. Report any visual glitches or broken interactions.");
            }
            else if (req.Contains("unity"))
            {
                requiresUser.Add("Unity Editor testing requires manual human verification.");
                manualSteps.Add("1. Open the project in Unity Editor.");
                manualSteps.Add("2. Enter Play Mode and test the relevant scenes.");
                manualSteps.Add("3. Check the Console for errors or warnings.");
                manualSteps.Add("4. Verify game object behavior matches acceptance criteria.");
            }
            else
            {
                agentCanTest.Add("Automated command execution: dotnet test, npm test, etc.");
                manualSteps.Add("1. Run the test command and capture output.");
                manualSteps.Add("2. Verify exit code is 0 and no tests failed.");
                manualSteps.Add("3. Provide the command output as evidence.");
            }
        }

        if (acceptanceCriteria.Count > 0)
        {
            agentCanTest.Add($"Verify {acceptanceCriteria.Count} acceptance criteria via static inspection.");
            foreach (var ac in acceptanceCriteria)
                manualSteps.Add($"- Verify: {ac}");
        }

        return OperationResult.Ok(new
        {
            PhaseId = phaseId,
            ValidationRequired = true,
            ValidationRequirements = validationRequirements,
            AcceptanceCriteria = acceptanceCriteria,
            AgentCanTest = agentCanTest.ToArray(),
            RequiresUser = requiresUser.ToArray(),
            ManualTestSteps = manualSteps.ToArray(),
            RecommendedValidationType = requiresUser.Count > 0 ? "HumanValidationRequired" : "AutomatedCommand",
            NextSteps = requiresUser.Count > 0
                ? "Use workflow.request_user_validation to ask the human owner to run these steps."
                : "Run the command and use workflow.create_validation_log with the output as evidence."
        });
    }
}

/// <summary>
/// Records a request for the human owner to perform validation steps.
/// Creates a PendingUser validation record with manual steps.
/// </summary>
public sealed class RequestUserValidationHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.RequestUserValidation;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var manualStepsStr = context.GetString("manualSteps");
        var manualSteps = string.IsNullOrWhiteSpace(manualStepsStr)
            ? new List<string> { "Run manual validation as described in the phase contract." }
            : new List<string>(manualStepsStr.Split(new[] { '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var summary = context.GetString("summary") ?? $"Manual validation requested for {phaseId}.";

        var validationId = store.NextValidationId();
        var record = new ValidationRecord
        {
            ValidationId     = validationId,
            PhaseId          = phaseId,
            Actor            = context.Actor,
            ValidationType   = ValidationType.HumanValidationRequired,
            ValidationResult = ValidationResult.PendingUser,
            Summary          = summary,
            ManualSteps      = manualSteps,
            Notes            = "Awaiting human owner validation. " +
                               "Use 'bfwf validation log' to record the result when complete.",
            CreatedUtc       = DateTimeOffset.UtcNow
        };

        store.AppendValidation(record);

        // Update phase to TestInProgress
        if (phase.State == PhaseState.ReadyForTest)
        {
            var updatedPhase = phase with
            {
                State = PhaseState.TestInProgress,
                ValidationLogIds = [..phase.ValidationLogIds, validationId],
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SavePhase(updatedPhase);
        }
        else
        {
            var updatedPhase = phase with
            {
                ValidationLogIds = [..phase.ValidationLogIds, validationId],
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SavePhase(updatedPhase);
        }

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "validation.user_requested",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"User validation requested for {phaseId}: {validationId}",
            PayloadReference = validationId
        });

        var activeSession = ValidationHandlerOrchestrationLookup.FindActiveOrchestrationSession(store, phaseId);
        if (activeSession is not null)
        {
            _runtime.SetAttentionFlags(
                activeSession.SessionId,
                context.Actor,
                new AttentionFlagsSnapshot
                {
                    HumanValidationRequired = true,
                    ReasonRecordIds = [validationId]
                },
                activeSession.ActiveRunId,
                $"Manual validation requested for {phaseId}.");
        }

        // Build a user-friendly request message
        var requestMessage = new System.Text.StringBuilder();
        requestMessage.AppendLine($"## Human Validation Required — {phaseId}");
        requestMessage.AppendLine();
        requestMessage.AppendLine($"**Phase:** {phase.Title}");
        requestMessage.AppendLine($"**Requested by:** {context.Actor}");
        requestMessage.AppendLine();
        requestMessage.AppendLine("### Manual Test Steps");
        requestMessage.AppendLine();
        int stepNum = 1;
        foreach (var step in manualSteps)
        {
            requestMessage.AppendLine($"{stepNum}. {step}");
            stepNum++;
        }
        requestMessage.AppendLine();
        requestMessage.AppendLine("### After Testing");
        requestMessage.AppendLine();
        requestMessage.AppendLine("Run one of these commands to record the result:");
        requestMessage.AppendLine();
        requestMessage.AppendLine($"  bfwf validation log --phase {phaseId} --type HumanValidationRequired --result Passed --evidence '[{{\"description\":\"Manual test completed\",\"source\":\"HumanOwner\"}}]' --summary \"All manual tests passed.\"");
        requestMessage.AppendLine();
        requestMessage.AppendLine($"  bfwf validation log --phase {phaseId} --type HumanValidationRequired --result Failed --evidence '[{{\"description\":\"Manual test failed\",\"source\":\"HumanOwner\"}}]' --summary \"Test failed: ...\"");

        return OperationResult.Ok(new
        {
            ValidationRecord = record,
            UserMessage = requestMessage.ToString()
        });
    }
}

/// <summary>
/// Records a skipped validation with reason and approval.
/// </summary>
public sealed class SkipValidationHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.SkipValidation;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var skipReason = context.GetString("skipReason") ?? context.GetString("reason");
        if (string.IsNullOrWhiteSpace(skipReason))
            return OperationResult.Fail("SkipReasonRequired",
                "Skipped validation requires a non-empty skipReason.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        var validationTypeStr = context.GetString("validationType") ?? "SkippedNotNeeded";
        if (!Enum.TryParse<ValidationType>(validationTypeStr, ignoreCase: true, out var validationType))
            validationType = ValidationType.SkippedNotNeeded;

        var riskNote = context.GetString("riskNote");
        if (validationType == ValidationType.SkippedNotPossible && string.IsNullOrWhiteSpace(riskNote))
            return OperationResult.Fail("RiskNoteRequired",
                "SkippedNotPossible requires a riskNote describing the risk accepted by skipping. " +
                "This phase may only reach PassWithWarnings, not clean Pass.");

        var approvedBy = context.GetString("approvedBy");
        if (validationType == ValidationType.SkippedByUserOverride && string.IsNullOrWhiteSpace(approvedBy))
            return OperationResult.Fail("UserOverrideRequiresApproval",
                "SkippedByUserOverride requires an approvedBy field identifying the human owner.");

        var summary = context.GetString("summary") ?? $"Validation skipped for {phaseId}: {skipReason}";

        // -- Transition check -----------------------------------------------------
        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.TestLogged,
            RequiresValidation = phase.Contract?.RequiresValidation ?? true
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var validationId = store.NextValidationId();
        var record = new ValidationRecord
        {
            ValidationId     = validationId,
            PhaseId          = phaseId,
            Actor            = context.Actor,
            ValidationType   = validationType,
            ValidationResult = ValidationResult.Skipped,
            Summary          = summary,
            SkipReason       = skipReason,
            RiskNote         = riskNote,
            ApprovedBy       = approvedBy,
            Notes            = context.GetString("notes") ?? string.Empty,
            CreatedUtc       = DateTimeOffset.UtcNow
        };

        store.AppendValidation(record);

        var updatedPhase = phase with
        {
            State = PhaseState.TestLogged,
            ValidationLogIds = [..phase.ValidationLogIds, validationId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastValidationId = validationId,
            LastStatus       = PhaseState.TestLogged,
            UpdatedUtc       = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "validation.skipped",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Validation skipped for {phaseId}: {validationId} — {skipReason}",
            PayloadReference = validationId
        });

        var activeSession = ValidationHandlerOrchestrationLookup.FindActiveOrchestrationSession(store, phaseId);
        if (activeSession is not null)
        {
            if (validationType == ValidationType.SkippedNotPossible)
            {
                _runtime.SetAttentionFlags(
                    activeSession.SessionId,
                    context.Actor,
                    new AttentionFlagsSnapshot
                    {
                        UnresolvedRisk = true,
                        ReasonRecordIds = [validationId]
                    },
                    activeSession.ActiveRunId,
                    $"Validation skipped as not possible for {phaseId}.");
            }
            else
            {
                _runtime.ClearAttentionFlags(
                    activeSession.SessionId,
                    context.Actor,
                    ["all"],
                    activeSession.ActiveRunId,
                    $"Validation skipped for {phaseId}; attention flags cleared.");
            }
        }

        return OperationResult.Ok(record);
    }
}

/// <summary>
/// Completes a pending human validation request.
/// The human owner provides the result and evidence after performing manual steps.
/// Creates a new validation record that supersedes the PendingHumanValidation record.
/// </summary>
public sealed class CompleteUserValidationHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.CompleteUserValidation;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId;
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var resultStr = context.GetString("validationResult") ?? context.GetString("result");
        if (string.IsNullOrWhiteSpace(resultStr))
            return OperationResult.Fail("ValidationFailed",
                "Parameter 'validationResult' is required. Valid: Passed, PassedWithWarnings, Failed.");

        if (!Enum.TryParse<ValidationResult>(resultStr, ignoreCase: true, out var validationResult))
            return OperationResult.Fail("ValidationFailed",
                $"Unknown result '{resultStr}'. Valid: Passed, PassedWithWarnings, Failed.");

        if (validationResult == ValidationResult.PendingHumanValidation || validationResult == ValidationResult.Skipped)
            return OperationResult.Fail("ValidationFailed",
                "complete-user requires a final result: Passed, PassedWithWarnings, or Failed.");

        var summary = context.GetString("summary");
        if (string.IsNullOrWhiteSpace(summary))
            return OperationResult.Fail("ValidationFailed", "Parameter 'summary' is required.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        // Passed requires evidence.
        var evidenceItems = ParseEvidenceItems(context.GetString("evidenceItems") ?? context.GetString("evidence"));
        if (validationResult is ValidationResult.Passed or ValidationResult.PassedWithWarnings && evidenceItems.Count == 0)
            return OperationResult.Fail("EvidenceRequired",
                "Human validation result Passed/PassedWithWarnings requires at least one evidence item.");

        var validationId = store.NextValidationId();
        var pendingRef = context.GetString("pendingValidationId");

        var record = new ValidationRecord
        {
            ValidationId     = validationId,
            PhaseId          = phaseId,
            Actor            = WorkflowActor.HumanOwner,
            ValidationType   = ValidationType.HumanValidationRequired,
            ValidationResult = validationResult,
            Summary          = summary,
            EvidenceItems    = evidenceItems,
            Notes            = string.IsNullOrWhiteSpace(pendingRef)
                ? context.GetString("notes") ?? string.Empty
                : $"Completes pending validation {pendingRef}. {context.GetString("notes") ?? string.Empty}".Trim(),
            CreatedUtc       = DateTimeOffset.UtcNow
        };

        store.AppendValidation(record);

        var updatedPhase = phase with
        {
            State = PhaseState.TestLogged,
            ValidationLogIds = [..phase.ValidationLogIds, validationId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastValidationId = validationId,
            LastStatus       = PhaseState.TestLogged,
            UpdatedUtc       = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "validation.user_completed",
            Actor            = WorkflowActor.HumanOwner,
            PhaseId          = phaseId,
            Summary          = $"Human validation completed for {phaseId}: {validationId} ({validationResult})",
            PayloadReference = validationId
        });

        var activeSession = ValidationHandlerOrchestrationLookup.FindActiveOrchestrationSession(store, phaseId);
        if (activeSession is not null)
        {
            _runtime.ClearAttentionFlags(
                activeSession.SessionId,
                WorkflowActor.HumanOwner,
                ["HumanValidationRequired", "ManualReviewRequired", "ExternalToolRequired", "TestsNotRunnable", "BlockedByUser", "BlockedByEnvironment"],
                activeSession.ActiveRunId,
                $"Human validation completed for {phaseId}.");
        }

        return OperationResult.Ok(record);
    }

    private static IReadOnlyList<ValidationEvidence> ParseEvidenceItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<ValidationEvidence>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch { return []; }
    }
}

file static class ValidationHandlerOrchestrationLookup
{
    public static OrchestrationSession? FindActiveOrchestrationSession(WorkflowStore store, string phaseId) =>
        store.LoadAllOrchestrationSessions()
            .Where(s => string.Equals(s.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static s => s.UpdatedUtc)
            .FirstOrDefault(static s => s.SessionState is not OrchestrationSessionState.CompletedPass
                and not OrchestrationSessionState.CompletedPassWithWarnings
                and not OrchestrationSessionState.CompletedFailure
                and not OrchestrationSessionState.Cancelled);
}
