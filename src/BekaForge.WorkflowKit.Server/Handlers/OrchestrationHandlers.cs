using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Starts a new deterministic orchestration session for one phase.</summary>
public sealed class StartOrchestrationSessionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.StartOrchestrationSession;

    public OperationResult Execute(OperationContext context)
    {
        var phaseId = context.PhaseId ?? context.GetString("phaseId");
        if (string.IsNullOrWhiteSpace(phaseId))
            return OperationResult.Fail("ValidationFailed", "PhaseId is required.");

        var attemptPolicy = new OrchestrationAttemptPolicy
        {
            MaxImplementationAttempts = context.Get<int?>("maxImplementationAttempts") ?? 3,
            MaxAuditAttempts = context.Get<int?>("maxAuditAttempts") ?? 2,
            MaxReviewAttempts = context.Get<int?>("maxReviewAttempts") ?? 2,
            MaxValidationAttempts = context.Get<int?>("maxValidationAttempts") ?? 2,
            MaxFixAttempts = context.Get<int?>("maxFixAttempts") ?? 2
        };

        return _runtime.StartSession(
            phaseId,
            context.Actor,
            context.GetString("objectiveSnapshot") ?? string.Empty,
            context.GetString("scopeSnapshot") ?? string.Empty,
            OrchestrationHandlerParsing.ParseList(context.GetString("dependsOnPhaseIds")),
            OrchestrationHandlerParsing.ParseList(context.GetString("executionLaneIds")),
            attemptPolicy);
    }
}

/// <summary>Advances a session deterministically based on the active run and canonical workflow records.</summary>
public sealed class AdvanceOrchestrationSessionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.AdvanceOrchestrationSession;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.AdvanceSession(sessionId, context.Actor, context.GetString("rationale") ?? context.GetString("reason") ?? string.Empty);
    }
}

/// <summary>Moves a live orchestration session back to a controlled waiting state.</summary>
public sealed class PauseOrchestrationSessionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.PauseOrchestrationSession;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed", "Parameters 'sessionId' and 'reason' are required.");

        return _runtime.PauseSession(sessionId, context.Actor, reason);
    }
}

/// <summary>Cancels a live orchestration session.</summary>
public sealed class CancelOrchestrationSessionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.CancelOrchestrationSession;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed", "Parameters 'sessionId' and 'reason' are required.");

        return _runtime.CancelSession(sessionId, context.Actor, reason);
    }
}

/// <summary>Moves workflow focus to the phase owned by a session without changing lifecycle state.</summary>
public sealed class FocusOrchestrationSessionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.FocusOrchestrationSession;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.FocusSession(sessionId, context.Actor, context.GetString("reason") ?? string.Empty);
    }
}

/// <summary>Queues a manual orchestration run for a live session.</summary>
public sealed class CreateOrchestrationRunHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.CreateOrchestrationRun;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        var roleRaw = context.GetString("role");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(roleRaw))
            return OperationResult.Fail("ValidationFailed", "Parameters 'sessionId' and 'role' are required.");

        if (!Enum.TryParse<OrchestrationRunRole>(roleRaw, ignoreCase: true, out var role))
            return OperationResult.Fail("ValidationFailed", $"Unknown orchestration role '{roleRaw}'.");

        return _runtime.CreateRun(
            sessionId,
            context.Actor,
            role,
            context.GetString("purpose") ?? string.Empty,
            context.GetString("requestedByGate"),
            context.GetString("laneId"));
    }
}

/// <summary>Starts a queued orchestration run and applies the required phase-state transition for its role.</summary>
public sealed class StartOrchestrationRunHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.StartOrchestrationRun;

    public OperationResult Execute(OperationContext context)
    {
        var runId = context.GetString("runId");
        if (string.IsNullOrWhiteSpace(runId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'runId' is required.");

        return _runtime.StartRun(runId, context.Actor, context.GetString("notes") ?? string.Empty);
    }
}

/// <summary>Reports the normalized output of a run and links the produced canonical workflow record IDs.</summary>
public sealed class ReportOrchestrationRunHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.ReportOrchestrationRun;

    public OperationResult Execute(OperationContext context)
    {
        var runId = context.GetString("runId");
        var summary = context.GetString("summary");
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(summary))
            return OperationResult.Fail("ValidationFailed", "Parameters 'runId' and 'summary' are required.");

        return _runtime.ReportRun(
            runId,
            context.Actor,
            summary,
            OrchestrationHandlerParsing.ParseList(context.GetString("producedRecordIds")),
            context.GetString("notes") ?? string.Empty);
    }
}

/// <summary>Accepts a reported run after verifying its canonical workflow records exist.</summary>
public sealed class AcceptOrchestrationRunHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.AcceptOrchestrationRun;

    public OperationResult Execute(OperationContext context)
    {
        var runId = context.GetString("runId");
        if (string.IsNullOrWhiteSpace(runId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'runId' is required.");

        return _runtime.AcceptRun(runId, context.Actor, context.GetString("notes") ?? string.Empty);
    }
}

/// <summary>Rejects a reported run and leaves the runtime to decide the retry path deterministically.</summary>
public sealed class RejectOrchestrationRunHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.RejectOrchestrationRun;

    public OperationResult Execute(OperationContext context)
    {
        var runId = context.GetString("runId");
        var reason = context.GetString("reason");
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("ValidationFailed", "Parameters 'runId' and 'reason' are required.");

        return _runtime.RejectRun(runId, context.Actor, reason);
    }
}

/// <summary>Appends a manual orchestration gate decision for operator-driven recoveries or escalations.</summary>
public sealed class RecordOrchestrationGateDecisionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.RecordOrchestrationGateDecision;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        var runId = context.GetString("runId");
        var gateKindRaw = context.GetString("gateKind");
        var decisionRaw = context.GetString("decision");
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(runId)
            || string.IsNullOrWhiteSpace(gateKindRaw)
            || string.IsNullOrWhiteSpace(decisionRaw))
        {
            return OperationResult.Fail("ValidationFailed",
                "Parameters 'sessionId', 'runId', 'gateKind', and 'decision' are required.");
        }

        if (!Enum.TryParse<OrchestrationGateKind>(gateKindRaw, ignoreCase: true, out var gateKind))
            return OperationResult.Fail("ValidationFailed", $"Unknown gate kind '{gateKindRaw}'.");
        if (!Enum.TryParse<OrchestrationDecision>(decisionRaw, ignoreCase: true, out var decision))
            return OperationResult.Fail("ValidationFailed", $"Unknown orchestration decision '{decisionRaw}'.");

        PhaseState? resultingState = null;
        var resultingStateRaw = context.GetString("resultingPhaseState");
        if (!string.IsNullOrWhiteSpace(resultingStateRaw))
        {
            if (!Enum.TryParse<PhaseState>(resultingStateRaw, ignoreCase: true, out var parsedState))
                return OperationResult.Fail("ValidationFailed", $"Unknown phase state '{resultingStateRaw}'.");
            resultingState = parsedState;
        }

        return _runtime.RecordGateDecision(
            sessionId,
            runId,
            context.Actor,
            gateKind,
            decision,
            context.GetString("rationale") ?? context.GetString("reason") ?? string.Empty,
            OrchestrationHandlerParsing.ParseList(context.GetString("inputRecordIds")),
            resultingState,
            context.GetString("nextActionKind"),
            OrchestrationHandlerParsing.ParseAttentionFlags(context));
    }
}

/// <summary>Returns the current orchestration status snapshot for one session.</summary>
public sealed class GetOrchestrationStatusHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.GetOrchestrationStatus;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.GetStatus(sessionId);
    }
}

/// <summary>Returns the orchestration status snapshot with attention emphasis.</summary>
public sealed class GetOrchestrationAttentionStatusHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.GetOrchestrationAttentionStatus;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.GetStatus(sessionId);
    }
}

/// <summary>Sets the current orchestration attention flags on a session and optional run.</summary>
public sealed class SetOrchestrationAttentionFlagsHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.SetOrchestrationAttentionFlags;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.SetAttentionFlags(
            sessionId,
            context.Actor,
            OrchestrationHandlerParsing.ParseAttentionFlags(context),
            context.GetString("runId"),
            context.GetString("reason"));
    }
}

/// <summary>Clears one or more orchestration attention flags from a session and optional run.</summary>
public sealed class ClearOrchestrationAttentionFlagsHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.ClearOrchestrationAttentionFlags;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.ClearAttentionFlags(
            sessionId,
            context.Actor,
            OrchestrationHandlerParsing.ParseList(context.GetString("flags")),
            context.GetString("runId"),
            context.GetString("reason"));
    }
}

/// <summary>Escalates a session into a human-attention state and records the reason.</summary>
public sealed class RequestOrchestrationHumanAttentionHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.RequestOrchestrationHumanAttention;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        var flags = OrchestrationHandlerParsing.ParseAttentionFlags(context);
        if (!AttentionFlagRules.HasAny(flags))
        {
            flags = flags with
            {
                ManualReviewRequired = true,
                ReasonRecordIds = OrchestrationHandlerParsing.ParseList(context.GetString("reasonRecordIds"))
            };
        }

        return _runtime.RequestHumanAttention(
            sessionId,
            context.Actor,
            flags,
            context.GetString("reason") ?? context.GetString("rationale") ?? string.Empty,
            context.GetString("runId"));
    }
}

/// <summary>Lists orchestration sessions, optionally filtered by phase.</summary>
public sealed class ListOrchestrationSessionsHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.ListOrchestrationSessions;

    public OperationResult Execute(OperationContext context) =>
        _runtime.ListSessions(context.PhaseId ?? context.GetString("phaseId"));
}

/// <summary>Lists orchestration runs for a session.</summary>
public sealed class ListOrchestrationRunsHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.ListOrchestrationRuns;

    public OperationResult Execute(OperationContext context)
    {
        var sessionId = context.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail("ValidationFailed", "Parameter 'sessionId' is required.");

        return _runtime.ListRuns(sessionId);
    }
}

/// <summary>Lists gate decisions for a session or phase.</summary>
public sealed class ListOrchestrationGateDecisionsHandler(WorkflowStore store) : IOperationHandler
{
    private readonly OrchestrationRuntimeService _runtime = new(store);
    public string OperationName => WorkflowOperations.ListOrchestrationGateDecisions;

    public OperationResult Execute(OperationContext context) =>
        _runtime.ListGateDecisions(context.GetString("sessionId"), context.PhaseId ?? context.GetString("phaseId"));
}

file static class OrchestrationHandlerParsing
{
    public static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split(["||", "\r\n", "\n", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static AttentionFlagsSnapshot ParseAttentionFlags(OperationContext context)
    {
        var reasonRecordIds = ParseList(context.GetString("reasonRecordIds"));
        return new AttentionFlagsSnapshot
        {
            HumanValidationRequired = context.GetBool("humanValidationRequired"),
            TestsNotRunnable = context.GetBool("testsNotRunnable"),
            ManualReviewRequired = context.GetBool("manualReviewRequired"),
            ExternalToolRequired = context.GetBool("externalToolRequired"),
            MaxAgentAttemptsReached = context.GetBool("maxAgentAttemptsReached"),
            UnresolvedRisk = context.GetBool("unresolvedRisk"),
            BlockedByUser = context.GetBool("blockedByUser"),
            BlockedByEnvironment = context.GetBool("blockedByEnvironment"),
            ReasonRecordIds = reasonRecordIds
        };
    }
}
