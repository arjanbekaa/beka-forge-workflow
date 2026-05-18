using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdOrchestration(
        string subCmd,
        string? wfRoot,
        string? phaseId,
        string? sessionId,
        string? runId,
        string? roleArg,
        string? objective,
        string? scope,
        string? reason,
        string? summary,
        string? notes,
        bool json,
        CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        switch ((subCmd ?? string.Empty).ToLowerInvariant())
        {
            case "start":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.StartOrchestrationSession,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.Planner,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["objectiveSnapshot"] = objective ?? string.Empty,
                        ["scopeSnapshot"] = scope ?? string.Empty,
                        ["dependsOnPhaseIds"] = dependsOnPhaseIdsFromArgs(),
                        ["executionLaneIds"] = ParseFlag(CommandLineArgs, "--lane-ids") ?? string.Empty,
                        ["maxImplementationAttempts"] = ParseIntFlag(CommandLineArgs, "--max-implementation-attempts"),
                        ["maxAuditAttempts"] = ParseIntFlag(CommandLineArgs, "--max-audit-attempts"),
                        ["maxReviewAttempts"] = ParseIntFlag(CommandLineArgs, "--max-review-attempts"),
                        ["maxValidationAttempts"] = ParseIntFlag(CommandLineArgs, "--max-validation-attempts"),
                        ["maxFixAttempts"] = ParseIntFlag(CommandLineArgs, "--max-fix-attempts")
                    }
                });
                RenderResult(result, mode, json, successMessage: "Orchestration session started.");
                break;
            }

            case "focus":
                Require(sessionId, "--session", mode);
                RenderResult(DispatchSessionAction(dispatcher, WorkflowOperations.FocusOrchestrationSession, sessionId, reason),
                    mode, json, "Orchestration session focused.");
                break;

            case "advance":
                Require(sessionId, "--session", mode);
                RenderResult(DispatchSessionAction(dispatcher, WorkflowOperations.AdvanceOrchestrationSession, sessionId, reason),
                    mode, json, "Orchestration session advanced.");
                break;

            case "pause":
                Require(sessionId, "--session", mode);
                Require(reason, "--reason", mode);
                RenderResult(DispatchSessionAction(dispatcher, WorkflowOperations.PauseOrchestrationSession, sessionId, reason),
                    mode, json, "Orchestration session paused.");
                break;

            case "cancel":
                Require(sessionId, "--session", mode);
                Require(reason, "--reason", mode);
                RenderResult(DispatchSessionAction(dispatcher, WorkflowOperations.CancelOrchestrationSession, sessionId, reason),
                    mode, json, "Orchestration session cancelled.");
                break;

            case "status":
                Require(sessionId, "--session", mode);
                RenderResult(DispatchSessionAction(dispatcher, WorkflowOperations.GetOrchestrationStatus, sessionId, null),
                    mode, json, null);
                break;

            case "attention":
                Require(sessionId, "--session", mode);
                RenderResult(DispatchSessionAction(dispatcher, WorkflowOperations.GetOrchestrationAttentionStatus, sessionId, null),
                    mode, json, null);
                break;

            case "sessions":
            {
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.ListOrchestrationSessions,
                    Actor = WorkflowActor.Planner,
                    PhaseId = phaseId
                });
                RenderResult(result, mode, json, null);
                break;
            }

            case "runs":
            {
                Require(sessionId, "--session", mode);
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.ListOrchestrationRuns,
                    Actor = WorkflowActor.Planner,
                    Parameters = new Dictionary<string, object?> { ["sessionId"] = sessionId }
                });
                RenderResult(result, mode, json, null);
                break;
            }

            case "gates":
            {
                var parameters = new Dictionary<string, object?>();
                if (!string.IsNullOrWhiteSpace(sessionId))
                    parameters["sessionId"] = sessionId;
                if (!string.IsNullOrWhiteSpace(phaseId))
                    parameters["phaseId"] = phaseId;
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.ListOrchestrationGateDecisions,
                    Actor = WorkflowActor.Planner,
                    PhaseId = phaseId,
                    Parameters = parameters
                });
                RenderResult(result, mode, json, null);
                break;
            }

            case "create-run":
            {
                Require(sessionId, "--session", mode);
                Require(roleArg, "--role", mode);
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.CreateOrchestrationRun,
                    Actor = WorkflowActor.Planner,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["sessionId"] = sessionId,
                        ["role"] = roleArg,
                        ["purpose"] = summary ?? notes ?? string.Empty,
                        ["requestedByGate"] = ParseFlag(CommandLineArgs, "--gate-kind"),
                        ["laneId"] = ParseFlag(CommandLineArgs, "--lane")
                    }
                });
                RenderResult(result, mode, json, "Orchestration run queued.");
                break;
            }

            case "start-run":
                Require(runId, "--run", mode);
                RenderResult(DispatchRunAction(dispatcher, WorkflowOperations.StartOrchestrationRun, runId, null, notes),
                    mode, json, "Orchestration run started.");
                break;

            case "report-run":
            {
                Require(runId, "--run", mode);
                Require(summary, "--summary", mode);
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.ReportOrchestrationRun,
                    Actor = WorkflowActor.WorkflowSystem,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["runId"] = runId,
                        ["summary"] = summary,
                        ["producedRecordIds"] = ParseFlag(CommandLineArgs, "--record-ids") ?? string.Empty,
                        ["notes"] = notes ?? string.Empty
                    }
                });
                RenderResult(result, mode, json, "Orchestration run reported.");
                break;
            }

            case "accept-run":
                Require(runId, "--run", mode);
                RenderResult(DispatchRunAction(dispatcher, WorkflowOperations.AcceptOrchestrationRun, runId, null, notes),
                    mode, json, "Orchestration run accepted.");
                break;

            case "reject-run":
                Require(runId, "--run", mode);
                Require(reason, "--reason", mode);
                RenderResult(DispatchRunAction(dispatcher, WorkflowOperations.RejectOrchestrationRun, runId, reason, null),
                    mode, json, "Orchestration run rejected.");
                break;

            case "set-attention":
            {
                Require(sessionId, "--session", mode);
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.SetOrchestrationAttentionFlags,
                    Actor = WorkflowActor.Planner,
                    Parameters = BuildAttentionParameters(sessionId, runId, reason)
                });
                RenderResult(result, mode, json, "Orchestration attention updated.");
                break;
            }

            case "clear-attention":
            {
                Require(sessionId, "--session", mode);
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.ClearOrchestrationAttentionFlags,
                    Actor = WorkflowActor.Planner,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["sessionId"] = sessionId,
                        ["runId"] = runId,
                        ["flags"] = ParseFlag(CommandLineArgs, "--flags") ?? "all",
                        ["reason"] = reason ?? string.Empty
                    }
                });
                RenderResult(result, mode, json, "Orchestration attention cleared.");
                break;
            }

            case "request-human":
            {
                Require(sessionId, "--session", mode);
                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.RequestOrchestrationHumanAttention,
                    Actor = WorkflowActor.Planner,
                    Parameters = BuildAttentionParameters(sessionId, runId, reason)
                });
                RenderResult(result, mode, json, "Human attention requested.");
                break;
            }

            default:
                CliRenderer.Error(
                    "Usage: bfwf orchestration <start|focus|advance|pause|cancel|status|attention|sessions|runs|gates|create-run|start-run|report-run|accept-run|reject-run|set-attention|clear-attention|request-human>",
                    mode);
                Environment.Exit(1);
                break;
        }

        string dependsOnPhaseIdsFromArgs() => ParseFlag(CommandLineArgs, "--depends-on-phase-ids") ?? string.Empty;
    }

    private static OperationResult DispatchSessionAction(
        OperationDispatcher dispatcher,
        string operation,
        string sessionId,
        string? reason)
    {
        var parameters = new Dictionary<string, object?> { ["sessionId"] = sessionId };
        if (!string.IsNullOrWhiteSpace(reason))
        {
            parameters["reason"] = reason;
            parameters["rationale"] = reason;
        }

        return dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Planner,
            Parameters = parameters
        });
    }

    private static OperationResult DispatchRunAction(
        OperationDispatcher dispatcher,
        string operation,
        string runId,
        string? reason,
        string? notes)
    {
        var parameters = new Dictionary<string, object?> { ["runId"] = runId };
        if (!string.IsNullOrWhiteSpace(reason))
            parameters["reason"] = reason;
        if (!string.IsNullOrWhiteSpace(notes))
            parameters["notes"] = notes;

        return dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Planner,
            Parameters = parameters
        });
    }

    private static Dictionary<string, object?> BuildAttentionParameters(string sessionId, string? runId, string? reason) =>
        new()
        {
            ["sessionId"] = sessionId,
            ["runId"] = runId,
            ["reason"] = reason ?? string.Empty,
            ["humanValidationRequired"] = HasFlag(CommandLineArgs, "--human-validation-required"),
            ["testsNotRunnable"] = HasFlag(CommandLineArgs, "--tests-not-runnable"),
            ["manualReviewRequired"] = HasFlag(CommandLineArgs, "--manual-review-required"),
            ["externalToolRequired"] = HasFlag(CommandLineArgs, "--external-tool-required"),
            ["maxAgentAttemptsReached"] = HasFlag(CommandLineArgs, "--max-agent-attempts-reached"),
            ["unresolvedRisk"] = HasFlag(CommandLineArgs, "--unresolved-risk"),
            ["blockedByUser"] = HasFlag(CommandLineArgs, "--blocked-by-user"),
            ["blockedByEnvironment"] = HasFlag(CommandLineArgs, "--blocked-by-environment"),
            ["reasonRecordIds"] = ParseFlag(CommandLineArgs, "--reason-record-ids") ?? string.Empty
        };

    private static void RenderResult(OperationResult result, CliOutputMode mode, bool json, string? successMessage)
    {
        if (json)
        {
            WriteJson(result.Success ? result.Data ?? new { ok = true } : new { result.ErrorCode, result.Message });
            if (!result.Success)
                Environment.Exit(1);
            return;
        }

        if (!result.Success)
        {
            CliRenderer.Error($"{result.ErrorCode}: {result.Message}", mode);
            Environment.Exit(1);
        }

        if (!string.IsNullOrWhiteSpace(successMessage))
            CliRenderer.Ok(successMessage, mode);

        WritePlain(result.Data, mode);
    }

    private static void WritePlain(object? data, CliOutputMode mode)
    {
        if (data is null)
            return;

        switch (data)
        {
            case OrchestrationStatusSnapshot status:
                Console.WriteLine($"Session: {status.SessionId}");
                Console.WriteLine($"Phase:   {status.PhaseId}");
                Console.WriteLine($"State:   {status.SessionState}");
                Console.WriteLine($"Run:     {status.ActiveRunId ?? "-"} ({status.ActiveRunRole?.ToString() ?? "-"}/{status.ActiveRunState?.ToString() ?? "-"})");
                Console.WriteLine($"Attention: {status.DerivedAttentionOutcome}");
                if (AttentionFlagRules.HasAny(status.AttentionFlags))
                    Console.WriteLine($"Flags:   {string.Join(", ", DescribeFlags(status.AttentionFlags))}");
                break;

            case OrchestrationSession session:
                Console.WriteLine($"Session: {session.SessionId}  Phase: {session.PhaseId}  State: {session.SessionState}");
                Console.WriteLine($"Active run: {session.ActiveRunId ?? "-"}");
                break;

            case OrchestrationRun run:
                Console.WriteLine($"Run: {run.RunId}  Session: {run.SessionId}");
                Console.WriteLine($"Role: {run.Role}  State: {run.RunState}  Attempt: {run.AttemptNumber}");
                Console.WriteLine($"Purpose: {run.Purpose}");
                break;

            case OrchestrationGateDecisionRecord gate:
                Console.WriteLine($"Gate decision: {gate.GateDecisionId}");
                Console.WriteLine($"Session: {gate.SessionId}  Run: {gate.RunId}");
                Console.WriteLine($"Gate: {gate.GateKind}  Decision: {gate.Decision}");
                Console.WriteLine($"Rationale: {gate.Rationale}");
                break;

            case IReadOnlyList<OrchestrationSession> sessions:
                foreach (var item in sessions)
                    Console.WriteLine($"{item.SessionId,-10} {item.PhaseId,-10} {item.SessionState,-24} {item.ActiveRunId ?? "-"}");
                break;

            case IReadOnlyList<OrchestrationRun> runs:
                foreach (var item in runs)
                    Console.WriteLine($"{item.RunId,-10} {item.Role,-12} {item.RunState,-12} attempt {item.AttemptNumber}");
                break;

            case IReadOnlyList<OrchestrationGateDecisionRecord> decisions:
                foreach (var item in decisions)
                    Console.WriteLine($"{item.GateDecisionId,-10} {item.GateKind,-14} {item.Decision,-20} {item.Rationale}");
                break;

            default:
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                break;
        }
    }

    private static IEnumerable<string> DescribeFlags(AttentionFlagsSnapshot flags)
    {
        if (flags.HumanValidationRequired) yield return "HumanValidationRequired";
        if (flags.TestsNotRunnable) yield return "TestsNotRunnable";
        if (flags.ManualReviewRequired) yield return "ManualReviewRequired";
        if (flags.ExternalToolRequired) yield return "ExternalToolRequired";
        if (flags.MaxAgentAttemptsReached) yield return "MaxAgentAttemptsReached";
        if (flags.UnresolvedRisk) yield return "UnresolvedRisk";
        if (flags.BlockedByUser) yield return "BlockedByUser";
        if (flags.BlockedByEnvironment) yield return "BlockedByEnvironment";
    }

    private static void Require(string? value, string flagName, CliOutputMode mode)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return;

        CliRenderer.Error($"{flagName} is required.", mode);
        Environment.Exit(1);
    }
}
