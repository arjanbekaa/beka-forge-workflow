using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// Shared deterministic orchestration runtime used by handler-backed operations.
/// The runtime stays additive to the existing workflow phase model and references
/// canonical IMP/AUD/REV/VAL/FIX records instead of inventing a parallel log trail.
/// </summary>
public sealed class OrchestrationRuntimeService(WorkflowStore store)
{
    private readonly PhaseTransitionValidator _phaseValidator = new();

    public OperationResult StartSession(
        string phaseId,
        WorkflowActor actor,
        string objectiveSnapshot,
        string scopeSnapshot,
        IReadOnlyList<string> dependsOnPhaseIds,
        IReadOnlyList<string> executionLaneIds,
        OrchestrationAttemptPolicy? attemptPolicy)
    {
        if (!IsManagerActor(actor))
            return OperationResult.Fail("ValidationFailed",
                "Only a manager-capable actor can start an orchestration session.");

        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{phaseId}' not found.");

        if (PhaseTransitionValidator.IsTerminal(phase.State))
            return OperationResult.Fail("InvalidTransition",
                $"Phase '{phaseId}' is terminal ({phase.State}) and cannot start orchestration.");

        if (phase.State == PhaseState.Blocked)
            return OperationResult.Fail("Blocked",
                $"Phase '{phaseId}' is blocked and cannot start orchestration.");

        if (HasActiveSession(phaseId))
            return OperationResult.Fail("DuplicateActiveSession",
                $"Phase '{phaseId}' already has an active orchestration session.");

        var unresolvedDependencies = LoadRequiredDependencies(phase, dependsOnPhaseIds)
            .Where(static p => p.State is not PhaseState.Pass and not PhaseState.PassWithWarnings)
            .Select(static p => p.PhaseId)
            .ToArray();
        if (unresolvedDependencies.Length > 0)
            return OperationResult.Fail("DependencyNotPassed",
                $"Phase '{phaseId}' cannot start because dependencies are not passed: {string.Join(", ", unresolvedDependencies)}");

        if (phase.State == PhaseState.Planned)
        {
            var ready = TransitionPhase(phase, PhaseState.ReadyForImplementation, actor,
                "Phase moved to ReadyForImplementation by orchestration start.");
            if (ready.IsFailure)
                return OperationResult.FromError(ready.Error);

            phase = ready.Value;
        }

        if (phase.State is not PhaseState.ReadyForImplementation and not PhaseState.AssignedToImplementation)
            return OperationResult.Fail("InvalidTransition",
                $"Phase '{phaseId}' must be ReadyForImplementation or AssignedToImplementation to start orchestration. Current state: {phase.State}.");

        var workflow = store.LoadWorkflow();
        var sessionId = store.NextOrchestrationSessionId();
        var rootRunId = store.NextOrchestrationRunId();
        var now = DateTimeOffset.UtcNow;
        var policy = NormalizeAttemptPolicy(attemptPolicy);
        var contractSnapshot = new OrchestrationContractSnapshotRef
        {
            PhaseId = phase.PhaseId,
            SessionId = sessionId,
            SnapshotHash = BuildContractHash(phase),
            SourceDocumentPaths = executionLaneIds
        };

        var rootRun = new OrchestrationRun
        {
            RunId = rootRunId,
            SessionId = sessionId,
            PhaseId = phaseId,
            RootRunId = rootRunId,
            Role = OrchestrationRunRole.Implementer,
            AssignedActor = WorkflowActor.Implementer,
            RunState = OrchestrationRunState.Queued,
            Purpose = "Implement the scoped phase work and produce the canonical implementation record.",
            InputContractRef = contractSnapshot,
            AttemptNumber = 1,
            RequestedByGate = OrchestrationGateKind.Implementation.ToString(),
            StartedUtc = now,
            UpdatedUtc = now
        };

        var session = new OrchestrationSession
        {
            SessionId = sessionId,
            PhaseId = phaseId,
            WorkflowId = workflow.WorkflowId,
            ManagerActor = actor,
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ObjectiveSnapshot = string.IsNullOrWhiteSpace(objectiveSnapshot)
                ? phase.Contract?.Objective ?? phase.Title
                : objectiveSnapshot.Trim(),
            ScopeSnapshot = string.IsNullOrWhiteSpace(scopeSnapshot)
                ? phase.Contract?.Scope ?? phase.Summary
                : scopeSnapshot.Trim(),
            DependsOnPhaseIds = dependsOnPhaseIds.Count > 0 ? dependsOnPhaseIds : phase.Dependencies,
            ExecutionLaneIds = executionLaneIds,
            ActiveRunId = rootRunId,
            Attempts = new OrchestrationAttemptCounters { ImplementationAttempts = 1 },
            AttemptPolicy = policy,
            ContractSnapshot = contractSnapshot,
            StartedUtc = now,
            UpdatedUtc = now
        };

        store.SaveOrchestrationRun(rootRun);
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, rootRunId, OrchestrationEventKind.SessionCreated, actor,
            $"Started orchestration session {sessionId} for {phaseId}.",
            "Root implementer run queued.");
        AppendRunEvent(session, rootRunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued root implementer run {rootRunId}.",
            "Initial implementation attempt.");

        return OperationResult.Ok(session);
    }

    public OperationResult PauseSession(string sessionId, WorkflowActor actor, string reason)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        if (IsTerminal(session.SessionState))
            return OperationResult.Fail("InvalidTransition",
                $"Session '{sessionId}' is terminal ({session.SessionState}) and cannot be paused.");

        var activeRun = session.ActiveRunId is null ? null : store.LoadOrchestrationRun(session.ActiveRunId);
        if (activeRun is not null && activeRun.RunState == OrchestrationRunState.InProgress)
        {
            activeRun = activeRun with
            {
                RunState = OrchestrationRunState.Dispatched,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SaveOrchestrationRun(activeRun);
        }

        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, activeRun?.RunId, OrchestrationEventKind.SessionUpdated, actor,
            $"Paused orchestration session {sessionId}.", reason);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    public OperationResult CancelSession(string sessionId, WorkflowActor actor, string reason)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        if (IsTerminal(session.SessionState))
            return OperationResult.Fail("InvalidTransition",
                $"Session '{sessionId}' is terminal ({session.SessionState}) and cannot be cancelled.");

        if (session.ActiveRunId is { } runId)
        {
            var activeRun = store.LoadOrchestrationRun(runId);
            if (activeRun is not null && !IsTerminal(activeRun.RunState))
            {
                store.SaveOrchestrationRun(activeRun with
                {
                    RunState = OrchestrationRunState.Cancelled,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                });
            }
        }

        session = session with
        {
            SessionState = OrchestrationSessionState.Cancelled,
            ActiveRunId = null,
            UpdatedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, null, OrchestrationEventKind.SessionCancelled, actor,
            $"Cancelled orchestration session {sessionId}.", reason);

        return OperationResult.Ok(session);
    }

    public OperationResult FocusSession(string sessionId, WorkflowActor actor, string reason)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        var workflow = store.LoadWorkflow();
        store.SaveWorkflow(workflow with
        {
            CurrentPhaseId = session.PhaseId,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        AppendRunEvent(session, session.ActiveRunId, OrchestrationEventKind.SessionUpdated, actor,
            $"Focused orchestration session {sessionId}.",
            string.IsNullOrWhiteSpace(reason)
                ? $"Workflow focus moved to phase {session.PhaseId}."
                : reason);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    public OperationResult CreateRun(
        string sessionId,
        WorkflowActor actor,
        OrchestrationRunRole role,
        string purpose,
        string? requestedByGate,
        string? laneId)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        if (IsTerminal(session.SessionState))
            return OperationResult.Fail("InvalidTransition",
                $"Session '{sessionId}' is terminal ({session.SessionState}) and cannot accept new runs.");

        var activeRun = session.ActiveRunId is null ? null : store.LoadOrchestrationRun(session.ActiveRunId);
        var run = CreateQueuedRun(
            session,
            role,
            requestedByGate,
            attemptNumber: ResolveManualRunAttempt(session, role),
            rootRunId: activeRun?.RootRunId ?? store.NextOrchestrationRunId(),
            parentRunId: activeRun?.RunId,
            laneId,
            string.IsNullOrWhiteSpace(purpose)
                ? $"Manual orchestration run for role {role}."
                : purpose.Trim());

        store.SaveOrchestrationRun(run);
        session = session with
        {
            ActiveRunId = run.RunId,
            SessionState = OrchestrationSessionState.WaitingForAgent,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, run.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued orchestration run {run.RunId}.",
            run.Purpose);

        return OperationResult.Ok(run);
    }

    public OperationResult RecordGateDecision(
        string sessionId,
        string runId,
        WorkflowActor actor,
        OrchestrationGateKind gateKind,
        OrchestrationDecision decision,
        string rationale,
        IReadOnlyList<string> inputRecordIds,
        PhaseState? resultingPhaseState,
        string? nextActionKind,
        AttentionFlagsSnapshot attentionFlags)
    {
        if (!AttentionFlagRules.TryValidate(attentionFlags, out var error))
            return OperationResult.Fail("ValidationFailed", $"Attention flags are invalid: {error}");

        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        var run = store.LoadOrchestrationRun(runId);
        if (run is null || !string.Equals(run.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("NotFound", $"Run '{runId}' was not found in session '{sessionId}'.");

        var record = new OrchestrationGateDecisionRecord
        {
            GateDecisionId = store.NextOrchestrationGateDecisionId(),
            SessionId = session.SessionId,
            PhaseId = session.PhaseId,
            RunId = run.RunId,
            GateKind = gateKind,
            GateAttempt = ResolveGateAttempt(session.Attempts, gateKind),
            Decision = decision,
            DecisionActor = actor,
            Rationale = string.IsNullOrWhiteSpace(rationale) ? $"{decision} recorded manually." : rationale.Trim(),
            InputRecordIds = inputRecordIds,
            ResultingPhaseState = resultingPhaseState,
            NextActionKind = nextActionKind,
            AttentionFlags = attentionFlags,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        store.AppendOrchestrationGateDecision(record);

        session = session with
        {
            LatestGateDecisionId = record.GateDecisionId,
            AttentionFlags = attentionFlags,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        SyncPhaseAttentionFlags(session.PhaseId, attentionFlags);

        return OperationResult.Ok(record);
    }

    public OperationResult SetAttentionFlags(
        string sessionId,
        WorkflowActor actor,
        AttentionFlagsSnapshot attentionFlags,
        string? runId,
        string? reason)
    {
        if (!AttentionFlagRules.TryValidate(attentionFlags, out var error))
            return OperationResult.Fail("ValidationFailed", $"Attention flags are invalid: {error}");

        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        var updatedState = ResolveAttentionState(session.SessionState, attentionFlags);
        session = session with
        {
            AttentionFlags = attentionFlags,
            SessionState = updatedState,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);

        if (!string.IsNullOrWhiteSpace(runId))
        {
            var run = store.LoadOrchestrationRun(runId);
            if (run is null || !string.Equals(run.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("NotFound", $"Run '{runId}' was not found in session '{sessionId}'.");

            store.SaveOrchestrationRun(run with
            {
                AttentionFlags = attentionFlags,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        SyncPhaseAttentionFlags(session.PhaseId, attentionFlags);
        AppendRunEvent(session, runId ?? session.ActiveRunId, OrchestrationEventKind.SessionUpdated, actor,
            $"Updated attention flags for session {sessionId}.",
            string.IsNullOrWhiteSpace(reason)
                ? $"Derived outcome: {AttentionFlagRules.DeriveOutcome(attentionFlags)}."
                : reason);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    public OperationResult ClearAttentionFlags(
        string sessionId,
        WorkflowActor actor,
        IReadOnlyList<string> flagsToClear,
        string? runId,
        string? reason)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        var updatedFlags = ClearAttentionFlags(session.AttentionFlags, flagsToClear);
        if (!AttentionFlagRules.TryValidate(updatedFlags, out var error))
            return OperationResult.Fail("ValidationFailed", $"Attention flags are invalid after clearing: {error}");

        var updatedState = ResolveClearedAttentionState(session.SessionState, updatedFlags);
        session = session with
        {
            AttentionFlags = updatedFlags,
            SessionState = updatedState,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);

        if (!string.IsNullOrWhiteSpace(runId))
        {
            var run = store.LoadOrchestrationRun(runId);
            if (run is null || !string.Equals(run.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("NotFound", $"Run '{runId}' was not found in session '{sessionId}'.");

            store.SaveOrchestrationRun(run with
            {
                AttentionFlags = ClearAttentionFlags(run.AttentionFlags, flagsToClear),
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        SyncPhaseAttentionFlags(session.PhaseId, updatedFlags);
        AppendRunEvent(session, runId ?? session.ActiveRunId, OrchestrationEventKind.SessionUpdated, actor,
            $"Cleared attention flags for session {sessionId}.",
            string.IsNullOrWhiteSpace(reason)
                ? "Attention flags cleared."
                : reason);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    public OperationResult RequestHumanAttention(
        string sessionId,
        WorkflowActor actor,
        AttentionFlagsSnapshot attentionFlags,
        string reason,
        string? runId)
    {
        if (!attentionFlags.HumanValidationRequired
            && !attentionFlags.ManualReviewRequired
            && !attentionFlags.BlockedByUser
            && !attentionFlags.BlockedByEnvironment
            && !attentionFlags.ExternalToolRequired)
        {
            return OperationResult.Fail("ValidationFailed",
                "request_human_attention requires a human-facing attention flag.");
        }

        var setResult = SetAttentionFlags(sessionId, actor, attentionFlags, runId, reason);
        if (!setResult.Success)
            return setResult;

        var session = store.LoadOrchestrationSession(sessionId)!;
        var activeRun = runId is null ? null : store.LoadOrchestrationRun(runId);
        var gateKind = activeRun is null ? OrchestrationGateKind.Stop : ParseGateKind(activeRun.RequestedByGate);
        var decision = activeRun is null
            ? null
            : AppendGateDecision(
                session,
                activeRun,
                gateKind,
                attentionFlags.BlockedByEnvironment || attentionFlags.BlockedByUser
                    ? OrchestrationDecision.Block
                    : OrchestrationDecision.RequestHuman,
                actor,
                string.IsNullOrWhiteSpace(reason) ? "Human attention requested." : reason.Trim(),
                attentionFlags.ReasonRecordIds,
                store.LoadPhase(session.PhaseId)?.State,
                attentionFlags.BlockedByEnvironment || attentionFlags.BlockedByUser ? "Unblock" : "Review",
                attentionFlags);

        if (decision is not null)
        {
            session = session with
            {
                LatestGateDecisionId = decision.GateDecisionId,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SaveOrchestrationSession(session);
        }

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    public OperationResult StartRun(string runId, WorkflowActor actor, string notes)
    {
        var run = store.LoadOrchestrationRun(runId);
        if (run is null)
            return OperationResult.Fail("NotFound", $"Orchestration run '{runId}' not found.");

        if (run.RunState is not OrchestrationRunState.Queued and not OrchestrationRunState.Dispatched)
            return OperationResult.Fail("InvalidTransition",
                $"Run '{runId}' is in state '{run.RunState}' and cannot be started.");

        var session = store.LoadOrchestrationSession(run.SessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Session '{run.SessionId}' not found.");

        var phase = store.LoadPhase(run.PhaseId);
        if (phase is null)
            return OperationResult.Fail("NotFound", $"Phase '{run.PhaseId}' not found.");

        var phaseResult = TransitionPhaseForRunStart(phase, run);
        if (phaseResult.IsFailure)
            return OperationResult.FromError(phaseResult.Error);

        run = run with
        {
            RunState = OrchestrationRunState.InProgress,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationRun(run);

        session = session with
        {
            SessionState = OrchestrationSessionState.Running,
            ActiveRunId = run.RunId,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);

        AppendRunEvent(session, run.RunId, OrchestrationEventKind.RunStarted, actor,
            $"Started orchestration run {runId}.",
            string.IsNullOrWhiteSpace(notes) ? $"Role {run.Role} is now in progress." : notes);

        return OperationResult.Ok(run);
    }

    public OperationResult ReportRun(string runId, WorkflowActor actor, string summary, IReadOnlyList<string> producedRecordIds, string notes)
    {
        var run = store.LoadOrchestrationRun(runId);
        if (run is null)
            return OperationResult.Fail("NotFound", $"Orchestration run '{runId}' not found.");

        if (run.RunState is not OrchestrationRunState.InProgress and not OrchestrationRunState.Dispatched)
            return OperationResult.Fail("InvalidTransition",
                $"Run '{runId}' is in state '{run.RunState}' and cannot be reported.");

        run = run with
        {
            RunState = OrchestrationRunState.Reported,
            ResultSummary = summary.Trim(),
            ProducedRecordIds = producedRecordIds,
            UpdatedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationRun(run);

        var session = store.LoadOrchestrationSession(run.SessionId)! with
        {
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);

        AppendRunEvent(session, run.RunId, OrchestrationEventKind.RunReported, actor,
            $"Reported orchestration run {runId}.",
            string.IsNullOrWhiteSpace(notes) ? run.ResultSummary : notes,
            run.ProducedRecordIds);

        return OperationResult.Ok(run);
    }

    public OperationResult AcceptRun(string runId, WorkflowActor actor, string notes)
    {
        var run = store.LoadOrchestrationRun(runId);
        if (run is null)
            return OperationResult.Fail("NotFound", $"Orchestration run '{runId}' not found.");

        if (run.RunState != OrchestrationRunState.Reported)
            return OperationResult.Fail("InvalidTransition",
                $"Run '{runId}' is in state '{run.RunState}' and cannot be accepted.");

        var requiredRecordCheck = ValidateAcceptedRunEvidence(run);
        if (!requiredRecordCheck.Success)
            return requiredRecordCheck;

        run = run with
        {
            RunState = OrchestrationRunState.Accepted,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationRun(run);

        var session = store.LoadOrchestrationSession(run.SessionId)! with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);

        AppendRunEvent(session, run.RunId, OrchestrationEventKind.RunAccepted, actor,
            $"Accepted orchestration run {runId}.",
            string.IsNullOrWhiteSpace(notes) ? "Run output accepted for gate progression." : notes,
            run.ProducedRecordIds);

        return OperationResult.Ok(run);
    }

    public OperationResult RejectRun(string runId, WorkflowActor actor, string reason)
    {
        var run = store.LoadOrchestrationRun(runId);
        if (run is null)
            return OperationResult.Fail("NotFound", $"Orchestration run '{runId}' not found.");

        if (run.RunState != OrchestrationRunState.Reported)
            return OperationResult.Fail("InvalidTransition",
                $"Run '{runId}' is in state '{run.RunState}' and cannot be rejected.");

        run = run with
        {
            RunState = OrchestrationRunState.Rejected,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationRun(run);

        var session = store.LoadOrchestrationSession(run.SessionId)! with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);

        AppendRunEvent(session, run.RunId, OrchestrationEventKind.RunRejected, actor,
            $"Rejected orchestration run {runId}.", reason, run.ProducedRecordIds);

        return OperationResult.Ok(run);
    }

    public OperationResult AdvanceSession(string sessionId, WorkflowActor actor, string rationale)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        if (session is null)
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        if (IsTerminal(session.SessionState))
            return OperationResult.Ok(BuildStatusSnapshot(session));

        if (string.IsNullOrWhiteSpace(session.ActiveRunId))
            return OperationResult.Fail("ValidationFailed",
                $"Session '{sessionId}' has no active run to advance.");

        var run = store.LoadOrchestrationRun(session.ActiveRunId);
        if (run is null)
            return OperationResult.Fail("NotFound",
                $"Active run '{session.ActiveRunId}' for session '{sessionId}' was not found.");

        if (run.RunState == OrchestrationRunState.Queued)
            return OperationResult.Ok(BuildStatusSnapshot(session));

        if (run.RunState is OrchestrationRunState.InProgress or OrchestrationRunState.Reported)
            return OperationResult.Ok(BuildStatusSnapshot(session));

        if (run.RunState == OrchestrationRunState.Rejected)
            return RetryRejectedRun(session, run, actor, rationale);

        if (run.RunState != OrchestrationRunState.Accepted)
            return OperationResult.Fail("InvalidTransition",
                $"Run '{run.RunId}' is in state '{run.RunState}' and cannot advance the session.");

        return run.Role switch
        {
            OrchestrationRunRole.Implementer => AdvanceImplementation(session, run, actor, rationale),
            OrchestrationRunRole.Auditor => AdvanceAudit(session, run, actor, rationale),
            OrchestrationRunRole.Reviewer => AdvanceReview(session, run, actor, rationale),
            OrchestrationRunRole.Tester => AdvanceValidation(session, run, actor, rationale),
            OrchestrationRunRole.Fixer => AdvanceFix(session, run, actor, rationale),
            _ => OperationResult.Fail("ValidationFailed",
                $"Unsupported orchestration role '{run.Role}' in runtime advance.")
        };
    }

    public OperationResult GetStatus(string sessionId)
    {
        var session = store.LoadOrchestrationSession(sessionId);
        return session is null
            ? OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.")
            : OperationResult.Ok(BuildStatusSnapshot(session));
    }

    public OperationResult ListSessions(string? phaseId)
    {
        var sessions = store.LoadAllOrchestrationSessions()
            .Where(s => string.IsNullOrWhiteSpace(phaseId)
                || string.Equals(s.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static s => s.StartedUtc)
            .ToArray();
        return OperationResult.Ok(sessions);
    }

    public OperationResult ListRuns(string sessionId)
    {
        if (!store.OrchestrationSessionExists(sessionId))
            return OperationResult.Fail("NotFound", $"Orchestration session '{sessionId}' not found.");

        var runs = store.LoadAllOrchestrationRuns()
            .Where(r => string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static r => r.StartedUtc)
            .ToArray();
        return OperationResult.Ok(runs);
    }

    public OperationResult ListGateDecisions(string? sessionId, string? phaseId)
    {
        var decisions = store.ReadAllOrchestrationGateDecisions()
            .Where(d => string.IsNullOrWhiteSpace(sessionId)
                || string.Equals(d.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .Where(d => string.IsNullOrWhiteSpace(phaseId)
                || string.Equals(d.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static d => d.CreatedUtc)
            .ToArray();
        return OperationResult.Ok(decisions);
    }

    private OperationResult RetryRejectedRun(
        OrchestrationSession session,
        OrchestrationRun rejectedRun,
        WorkflowActor actor,
        string rationale)
    {
        var role = rejectedRun.Role;
        return role switch
        {
            OrchestrationRunRole.Implementer => RetryRoleRun(
                session, rejectedRun, actor, rationale, OrchestrationGateKind.Implementation,
                rejectedRun.AttemptNumber + 1, session.AttemptPolicy.MaxImplementationAttempts,
                s => s with
                {
                    Attempts = s.Attempts with { ImplementationAttempts = rejectedRun.AttemptNumber + 1 }
                }),
            OrchestrationRunRole.Auditor => RetryRoleRun(
                session, rejectedRun, actor, rationale, OrchestrationGateKind.Audit,
                rejectedRun.AttemptNumber + 1, session.AttemptPolicy.MaxAuditAttempts,
                s => s with
                {
                    Attempts = s.Attempts with { AuditAttempts = rejectedRun.AttemptNumber + 1 }
                }),
            OrchestrationRunRole.Reviewer => RetryRoleRun(
                session, rejectedRun, actor, rationale, OrchestrationGateKind.Review,
                rejectedRun.AttemptNumber + 1, session.AttemptPolicy.MaxReviewAttempts,
                s => s with
                {
                    Attempts = s.Attempts with { ReviewAttempts = rejectedRun.AttemptNumber + 1 }
                }),
            OrchestrationRunRole.Tester => RetryRoleRun(
                session, rejectedRun, actor, rationale, OrchestrationGateKind.Validation,
                rejectedRun.AttemptNumber + 1, session.AttemptPolicy.MaxValidationAttempts,
                s => s with
                {
                    Attempts = s.Attempts with { ValidationAttempts = rejectedRun.AttemptNumber + 1 }
                }),
            OrchestrationRunRole.Fixer => RetryRoleRun(
                session, rejectedRun, actor, rationale, ParseGateKind(rejectedRun.RequestedByGate),
                rejectedRun.AttemptNumber + 1, session.AttemptPolicy.MaxFixAttempts,
                s => s with
                {
                    Attempts = s.Attempts with { FixAttempts = rejectedRun.AttemptNumber + 1 }
                }),
            _ => OperationResult.Fail("ValidationFailed",
                $"Rejected run role '{role}' is unsupported.")
        };
    }

    private OperationResult RetryRoleRun(
        OrchestrationSession session,
        OrchestrationRun rejectedRun,
        WorkflowActor actor,
        string rationale,
        OrchestrationGateKind gateKind,
        int nextAttempt,
        int maxAttempts,
        Func<OrchestrationSession, OrchestrationSession> updateAttempts)
    {
        if (nextAttempt > maxAttempts)
            return CompleteFailure(session, rejectedRun, actor, gateKind,
                string.IsNullOrWhiteSpace(rationale)
                    ? $"Run {rejectedRun.RunId} was rejected and retry budget for {gateKind} is exhausted."
                    : rationale);

        session = updateAttempts(session);
        var retryRun = CreateQueuedRun(
            session,
            rejectedRun.Role,
            rejectedRun.RequestedByGate,
            nextAttempt,
            rejectedRun.RootRunId,
            rejectedRun.RunId,
            rejectedRun.LaneId,
            rejectedRun.Purpose);
        store.SaveOrchestrationRun(retryRun);

        var decision = AppendGateDecision(session, rejectedRun, gateKind, OrchestrationDecision.Retry, actor,
            string.IsNullOrWhiteSpace(rationale)
                ? $"Run {rejectedRun.RunId} was rejected and will be retried."
                : rationale,
            rejectedRun.ProducedRecordIds);

        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = retryRun.RunId,
            LatestGateDecisionId = decision.GateDecisionId,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, retryRun.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued retry run {retryRun.RunId}.",
            $"Retry attempt {nextAttempt} for role {retryRun.Role}.");

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private OperationResult AdvanceImplementation(
        OrchestrationSession session,
        OrchestrationRun run,
        WorkflowActor actor,
        string rationale)
    {
        var phase = store.LoadPhase(run.PhaseId)!;
        if (phase.State != PhaseState.ImplementationLogged)
            return OperationResult.Fail("ValidationFailed",
                $"Implementation run '{run.RunId}' cannot advance because phase '{phase.PhaseId}' is in state '{phase.State}', not ImplementationLogged.");

        var decision = AppendGateDecision(session, run, OrchestrationGateKind.Implementation,
            OrchestrationDecision.Advance, actor,
            DefaultRationale(rationale, "Implementation accepted; queue audit."),
            run.ProducedRecordIds,
            phase.State);

        var auditRun = CreateQueuedRun(
            session,
            OrchestrationRunRole.Auditor,
            OrchestrationGateKind.Audit.ToString(),
            NextAttempt(session.Attempts.AuditAttempts, session.AttemptPolicy.MaxAuditAttempts),
            run.RootRunId,
            run.RunId,
            run.LaneId,
            "Audit the accepted implementation output and create the canonical audit record.");
        if (auditRun.AttemptNumber <= 0)
            return CompleteFailure(session, run, actor, OrchestrationGateKind.Audit,
                "Audit retry budget is exhausted before the audit gate could start.");

        store.SaveOrchestrationRun(auditRun);
        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = auditRun.RunId,
            LatestGateDecisionId = decision.GateDecisionId,
            Attempts = session.Attempts with { AuditAttempts = auditRun.AttemptNumber },
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, auditRun.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued audit run {auditRun.RunId}.",
            $"Audit attempt {auditRun.AttemptNumber}.");

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private OperationResult AdvanceAudit(
        OrchestrationSession session,
        OrchestrationRun run,
        WorkflowActor actor,
        string rationale)
    {
        var phase = store.LoadPhase(run.PhaseId)!;
        var audit = FindAuditRecord(run.ProducedRecordIds);
        if (audit is null)
            return OperationResult.Fail("ValidationFailed",
                $"Audit run '{run.RunId}' did not produce a valid AUD record.");

        if (phase.State != PhaseState.AuditLogged)
            return OperationResult.Fail("ValidationFailed",
                $"Audit run '{run.RunId}' cannot advance because phase '{phase.PhaseId}' is in state '{phase.State}', not AuditLogged.");

        if (audit.Passed)
        {
            var ready = TransitionPhase(phase, PhaseState.ReadyForReview, actor,
                "Audit passed; orchestration advanced the phase to ReadyForReview.");
            if (ready.IsFailure)
                return OperationResult.FromError(ready.Error);

            var decision = AppendGateDecision(session, run, OrchestrationGateKind.Audit,
                OrchestrationDecision.Advance, actor,
                DefaultRationale(rationale, "Audit passed; queue review."),
                run.ProducedRecordIds,
                PhaseState.ReadyForReview);

            var reviewRun = CreateQueuedRun(
                session,
                OrchestrationRunRole.Reviewer,
                OrchestrationGateKind.Review.ToString(),
                NextAttempt(session.Attempts.ReviewAttempts, session.AttemptPolicy.MaxReviewAttempts),
                run.RootRunId,
                run.RunId,
                run.LaneId,
                "Review the audited implementation output and create the canonical review record.");
            if (reviewRun.AttemptNumber <= 0)
                return CompleteFailure(session, run, actor, OrchestrationGateKind.Review,
                    "Review retry budget is exhausted before the review gate could start.");

            store.SaveOrchestrationRun(reviewRun);
            session = session with
            {
                SessionState = OrchestrationSessionState.WaitingForAgent,
                ActiveRunId = reviewRun.RunId,
                LatestGateDecisionId = decision.GateDecisionId,
                Attempts = session.Attempts with { ReviewAttempts = reviewRun.AttemptNumber },
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SaveOrchestrationSession(session);
            AppendRunEvent(session, reviewRun.RunId, OrchestrationEventKind.RunCreated, actor,
                $"Queued review run {reviewRun.RunId}.",
                $"Review attempt {reviewRun.AttemptNumber}.");

            return OperationResult.Ok(BuildStatusSnapshot(session));
        }

        if (session.Attempts.FixAttempts >= session.AttemptPolicy.MaxFixAttempts)
            return CompleteFailure(session, run, actor, OrchestrationGateKind.Audit,
                DefaultRationale(rationale, "Audit failed and fix retry budget is exhausted."));

        var phaseToFix = TransitionPhase(phase, PhaseState.FixInProgress, actor,
            "Audit failed; orchestration moved the phase into FixInProgress.");
        if (phaseToFix.IsFailure)
            return OperationResult.FromError(phaseToFix.Error);

        var fixAttempt = session.Attempts.FixAttempts + 1;
        var fixRun = CreateQueuedRun(
            session,
            OrchestrationRunRole.Fixer,
            OrchestrationGateKind.Audit.ToString(),
            fixAttempt,
            run.RootRunId,
            run.RunId,
            run.LaneId,
            "Fix the issues found by audit and create the canonical fix record.");
        store.SaveOrchestrationRun(fixRun);

        var fixDecision = AppendGateDecision(session, run, OrchestrationGateKind.Audit,
            OrchestrationDecision.RequestFix, actor,
            DefaultRationale(rationale, "Audit failed; queue fix run."),
            run.ProducedRecordIds,
            PhaseState.FixInProgress);

        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = fixRun.RunId,
            LatestGateDecisionId = fixDecision.GateDecisionId,
            Attempts = session.Attempts with { FixAttempts = fixAttempt },
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, fixRun.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued fix run {fixRun.RunId} after audit findings.",
            $"Fix attempt {fixAttempt}.", run.ProducedRecordIds);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private OperationResult AdvanceReview(
        OrchestrationSession session,
        OrchestrationRun run,
        WorkflowActor actor,
        string rationale)
    {
        var phase = store.LoadPhase(run.PhaseId)!;
        var review = FindReviewRecord(run.ProducedRecordIds);
        if (review is null)
            return OperationResult.Fail("ValidationFailed",
                $"Review run '{run.RunId}' did not produce a valid REV record.");

        if (review.Passed)
        {
            if (phase.State != PhaseState.ReviewLogged)
                return OperationResult.Fail("ValidationFailed",
                    $"Review pass requires phase '{phase.PhaseId}' to be ReviewLogged; current state is '{phase.State}'.");

            var ready = TransitionPhase(phase, PhaseState.ReadyForTest, actor,
                "Review passed; orchestration advanced the phase to ReadyForTest.");
            if (ready.IsFailure)
                return OperationResult.FromError(ready.Error);

            var decision = AppendGateDecision(session, run, OrchestrationGateKind.Review,
                OrchestrationDecision.Advance, actor,
                DefaultRationale(rationale, "Review passed; queue validation."),
                run.ProducedRecordIds,
                PhaseState.ReadyForTest);

            var validationRun = CreateQueuedRun(
                session,
                OrchestrationRunRole.Tester,
                OrchestrationGateKind.Validation.ToString(),
                NextAttempt(session.Attempts.ValidationAttempts, session.AttemptPolicy.MaxValidationAttempts),
                run.RootRunId,
                run.RunId,
                run.LaneId,
                "Validate the reviewed implementation and create the canonical validation record.");
            if (validationRun.AttemptNumber <= 0)
                return CompleteFailure(session, run, actor, OrchestrationGateKind.Validation,
                    "Validation retry budget is exhausted before the validation gate could start.");

            store.SaveOrchestrationRun(validationRun);
            session = session with
            {
                SessionState = OrchestrationSessionState.WaitingForAgent,
                ActiveRunId = validationRun.RunId,
                LatestGateDecisionId = decision.GateDecisionId,
                Attempts = session.Attempts with { ValidationAttempts = validationRun.AttemptNumber },
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SaveOrchestrationSession(session);
            AppendRunEvent(session, validationRun.RunId, OrchestrationEventKind.RunCreated, actor,
                $"Queued validation run {validationRun.RunId}.",
                $"Validation attempt {validationRun.AttemptNumber}.");

            return OperationResult.Ok(BuildStatusSnapshot(session));
        }

        if (!review.RequiresFix)
            return CompleteFailure(session, run, actor, OrchestrationGateKind.Review,
                DefaultRationale(rationale, "Review failed without a fixable path."));

        if (session.Attempts.FixAttempts >= session.AttemptPolicy.MaxFixAttempts)
            return CompleteFailure(session, run, actor, OrchestrationGateKind.Review,
                DefaultRationale(rationale, "Review required fixes and the fix retry budget is exhausted."));

        if (phase.State != PhaseState.RequiresFix)
            return OperationResult.Fail("ValidationFailed",
                $"Review fix path requires phase '{phase.PhaseId}' to be RequiresFix; current state is '{phase.State}'.");

        var fixAttempt = session.Attempts.FixAttempts + 1;
        var fixRun = CreateQueuedRun(
            session,
            OrchestrationRunRole.Fixer,
            OrchestrationGateKind.Review.ToString(),
            fixAttempt,
            run.RootRunId,
            run.RunId,
            run.LaneId,
            "Fix the issues found by review and create the canonical fix record.");
        store.SaveOrchestrationRun(fixRun);

        var fixDecision = AppendGateDecision(session, run, OrchestrationGateKind.Review,
            OrchestrationDecision.RequestFix, actor,
            DefaultRationale(rationale, "Review required fixes; queue fix run."),
            run.ProducedRecordIds,
            PhaseState.RequiresFix);

        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = fixRun.RunId,
            LatestGateDecisionId = fixDecision.GateDecisionId,
            Attempts = session.Attempts with { FixAttempts = fixAttempt },
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, fixRun.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued fix run {fixRun.RunId} after review findings.",
            $"Fix attempt {fixAttempt}.", run.ProducedRecordIds);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private OperationResult AdvanceValidation(
        OrchestrationSession session,
        OrchestrationRun run,
        WorkflowActor actor,
        string rationale)
    {
        var phase = store.LoadPhase(run.PhaseId)!;
        var validation = FindValidationRecord(run.ProducedRecordIds);
        if (validation is null)
            return OperationResult.Fail("ValidationFailed",
                $"Validation run '{run.RunId}' did not produce a valid VAL or TEST-backed validation record.");

        if (validation.ValidationResult == ValidationResult.PendingUser
            || validation.ValidationResult == ValidationResult.PendingHumanValidation)
        {
            if (phase.State is not PhaseState.TestInProgress and not PhaseState.TestLogged)
                return OperationResult.Fail("ValidationFailed",
                    $"Pending human validation requires phase '{phase.PhaseId}' to be TestInProgress or TestLogged; current state is '{phase.State}'.");

            var flags = new AttentionFlagsSnapshot
            {
                HumanValidationRequired = true,
                ReasonRecordIds = run.ProducedRecordIds
            };
            var requestHumanDecision = AppendGateDecision(session, run, OrchestrationGateKind.Validation,
                OrchestrationDecision.RequestHuman, actor,
                DefaultRationale(rationale, "Validation requires human attention."),
                run.ProducedRecordIds,
                phase.State,
                "ValidateManually",
                flags);

            session = session with
            {
                SessionState = OrchestrationSessionState.WaitingForHuman,
                AttentionFlags = flags,
                LatestGateDecisionId = requestHumanDecision.GateDecisionId,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            store.SaveOrchestrationSession(session);
            SyncPhaseAttentionFlags(session.PhaseId, flags);
            store.SaveOrchestrationRun(run with
            {
                AttentionFlags = flags,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            return OperationResult.Ok(BuildStatusSnapshot(session));
        }

        if (phase.State != PhaseState.TestLogged)
            return OperationResult.Fail("ValidationFailed",
                $"Validation advance requires phase '{phase.PhaseId}' to be TestLogged; current state is '{phase.State}'.");

        if (validation.ValidationResult is ValidationResult.Passed or ValidationResult.PassedWithWarnings
            || validation.ValidationResult == ValidationResult.Skipped)
        {
            var targetState = ResolveValidationSuccessState(validation);
            var transition = TransitionPhase(phase, targetState, actor,
                $"Validation completed; orchestration advanced the phase to {targetState}.");
            if (transition.IsFailure)
                return OperationResult.FromError(transition.Error);

            var finishDecision = AppendGateDecision(session, run, OrchestrationGateKind.Validation,
                targetState == PhaseState.Pass ? OrchestrationDecision.FinishPass : OrchestrationDecision.FinishPassWithWarnings,
                actor,
                DefaultRationale(rationale, $"Validation completed; finish session as {targetState}."),
                run.ProducedRecordIds,
                targetState);

            session = session with
            {
                SessionState = targetState == PhaseState.Pass
                    ? OrchestrationSessionState.CompletedPass
                    : OrchestrationSessionState.CompletedPassWithWarnings,
                ActiveRunId = null,
                LatestGateDecisionId = finishDecision.GateDecisionId,
                UpdatedUtc = DateTimeOffset.UtcNow,
                CompletedUtc = DateTimeOffset.UtcNow
            };
            store.SaveOrchestrationSession(session);
            return OperationResult.Ok(BuildStatusSnapshot(session));
        }

        if (session.Attempts.FixAttempts >= session.AttemptPolicy.MaxFixAttempts
            || session.Attempts.ValidationAttempts >= session.AttemptPolicy.MaxValidationAttempts)
        {
            return CompleteFailure(session, run, actor, OrchestrationGateKind.Validation,
                DefaultRationale(rationale, "Validation failed and retry budget is exhausted."));
        }

        var fixPhase = TransitionPhase(phase, PhaseState.FixInProgress, actor,
            "Validation failed; orchestration moved the phase into FixInProgress.");
        if (fixPhase.IsFailure)
            return OperationResult.FromError(fixPhase.Error);

        var fixAttempt = session.Attempts.FixAttempts + 1;
        var fixRun = CreateQueuedRun(
            session,
            OrchestrationRunRole.Fixer,
            OrchestrationGateKind.Validation.ToString(),
            fixAttempt,
            run.RootRunId,
            run.RunId,
            run.LaneId,
            "Fix the validation failure and create the canonical fix record.");
        store.SaveOrchestrationRun(fixRun);

        var fixDecision = AppendGateDecision(session, run, OrchestrationGateKind.Validation,
            OrchestrationDecision.RequestFix, actor,
            DefaultRationale(rationale, "Validation failed; queue fix run."),
            run.ProducedRecordIds,
            PhaseState.FixInProgress);

        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = fixRun.RunId,
            LatestGateDecisionId = fixDecision.GateDecisionId,
            Attempts = session.Attempts with { FixAttempts = fixAttempt },
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, fixRun.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued fix run {fixRun.RunId} after validation failure.",
            $"Fix attempt {fixAttempt}.", run.ProducedRecordIds);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private OperationResult AdvanceFix(
        OrchestrationSession session,
        OrchestrationRun run,
        WorkflowActor actor,
        string rationale)
    {
        var phase = store.LoadPhase(run.PhaseId)!;
        if (phase.State != PhaseState.FixLogged)
            return OperationResult.Fail("ValidationFailed",
                $"Fix run '{run.RunId}' cannot advance because phase '{phase.PhaseId}' is in state '{phase.State}', not FixLogged.");

        if (FindFixRecord(run.ProducedRecordIds) is null)
            return OperationResult.Fail("ValidationFailed",
                $"Fix run '{run.RunId}' did not produce a valid FIX record.");

        var implementationReady = TransitionPhase(phase, PhaseState.ImplementationLogged, actor,
            "Fix accepted; orchestration restored the phase to ImplementationLogged before rerunning audit.");
        if (implementationReady.IsFailure)
            return OperationResult.FromError(implementationReady.Error);

        var nextAuditAttempt = session.Attempts.AuditAttempts + 1;
        if (nextAuditAttempt > session.AttemptPolicy.MaxAuditAttempts)
            return CompleteFailure(session, run, actor, ParseGateKind(run.RequestedByGate),
                DefaultRationale(rationale, "Audit retry budget is exhausted after fix completion."));

        var gateKind = ParseGateKind(run.RequestedByGate);
        var decision = AppendGateDecision(session, run, gateKind, OrchestrationDecision.Retry, actor,
            DefaultRationale(rationale, "Fix accepted; rerun audit from the updated implementation baseline."),
            run.ProducedRecordIds,
            PhaseState.ImplementationLogged);

        var auditRun = CreateQueuedRun(
            session,
            OrchestrationRunRole.Auditor,
            OrchestrationGateKind.Audit.ToString(),
            nextAuditAttempt,
            run.RootRunId,
            run.RunId,
            run.LaneId,
            "Re-audit the updated implementation after the fix and create the canonical audit record.");
        store.SaveOrchestrationRun(auditRun);

        session = session with
        {
            SessionState = OrchestrationSessionState.WaitingForAgent,
            ActiveRunId = auditRun.RunId,
            LatestGateDecisionId = decision.GateDecisionId,
            Attempts = session.Attempts with { AuditAttempts = nextAuditAttempt },
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        AppendRunEvent(session, auditRun.RunId, OrchestrationEventKind.RunCreated, actor,
            $"Queued audit retry run {auditRun.RunId}.",
            $"Audit attempt {nextAuditAttempt} after fix.", run.ProducedRecordIds);

        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private OperationResult CompleteFailure(
        OrchestrationSession session,
        OrchestrationRun run,
        WorkflowActor actor,
        OrchestrationGateKind gateKind,
        string rationale)
    {
        var phase = store.LoadPhase(session.PhaseId)!;
        var flags = new AttentionFlagsSnapshot
        {
            MaxAgentAttemptsReached = true,
            ManualReviewRequired = true,
            UnresolvedRisk = true,
            ReasonRecordIds = run.ProducedRecordIds
        };
        var decision = AppendGateDecision(session, run, gateKind, OrchestrationDecision.Fail, actor,
            rationale, run.ProducedRecordIds, phase.State, "HumanReview", flags);
        session = session with
        {
            SessionState = OrchestrationSessionState.CompletedFailure,
            ActiveRunId = null,
            AttentionFlags = flags,
            LatestGateDecisionId = decision.GateDecisionId,
            UpdatedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
        store.SaveOrchestrationSession(session);
        SyncPhaseAttentionFlags(session.PhaseId, flags);
        store.SaveOrchestrationRun(run with
        {
            AttentionFlags = flags,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
        return OperationResult.Ok(BuildStatusSnapshot(session));
    }

    private WorkflowResult<Phase> TransitionPhaseForRunStart(Phase phase, OrchestrationRun run)
    {
        return run.Role switch
        {
            OrchestrationRunRole.Implementer => TransitionForImplementationStart(phase),
            OrchestrationRunRole.Auditor => phase.State == PhaseState.ImplementationLogged
                ? WorkflowResult.Ok(phase)
                : WorkflowResult.Fail<Phase>(WorkflowError.InvalidTransition(phase.State, phase.State,
                    $"Audit runs require phase '{phase.PhaseId}' to be ImplementationLogged.")),
            OrchestrationRunRole.Reviewer => phase.State switch
            {
                PhaseState.ReadyForReview => TransitionPhase(phase, PhaseState.ReviewInProgress, WorkflowActor.WorkflowSystem,
                    "Reviewer run started by orchestration."),
                PhaseState.ReviewInProgress => WorkflowResult.Ok(phase),
                _ => WorkflowResult.Fail<Phase>(WorkflowError.InvalidTransition(phase.State, PhaseState.ReviewInProgress,
                    $"Reviewer runs require phase '{phase.PhaseId}' to be ReadyForReview or ReviewInProgress."))
            },
            OrchestrationRunRole.Tester => phase.State switch
            {
                PhaseState.ReadyForTest => TransitionPhase(phase, PhaseState.TestInProgress, WorkflowActor.WorkflowSystem,
                    "Validation run started by orchestration."),
                PhaseState.TestInProgress => WorkflowResult.Ok(phase),
                _ => WorkflowResult.Fail<Phase>(WorkflowError.InvalidTransition(phase.State, PhaseState.TestInProgress,
                    $"Validation runs require phase '{phase.PhaseId}' to be ReadyForTest or TestInProgress."))
            },
            OrchestrationRunRole.Fixer => phase.State switch
            {
                PhaseState.RequiresFix => TransitionPhase(phase, PhaseState.FixInProgress, WorkflowActor.WorkflowSystem,
                    "Fix run started by orchestration."),
                PhaseState.AuditLogged => TransitionPhase(phase, PhaseState.FixInProgress, WorkflowActor.WorkflowSystem,
                    "Audit retry fix run started by orchestration."),
                PhaseState.TestLogged => TransitionPhase(phase, PhaseState.FixInProgress, WorkflowActor.WorkflowSystem,
                    "Validation retry fix run started by orchestration."),
                PhaseState.FixInProgress => WorkflowResult.Ok(phase),
                _ => WorkflowResult.Fail<Phase>(WorkflowError.InvalidTransition(phase.State, PhaseState.FixInProgress,
                    $"Fix runs require phase '{phase.PhaseId}' to be RequiresFix, AuditLogged, TestLogged, or FixInProgress."))
            },
            _ => WorkflowResult.Fail<Phase>(WorkflowError.ValidationRequired())
        };
    }

    private WorkflowResult<Phase> TransitionForImplementationStart(Phase phase)
    {
        if (phase.State == PhaseState.ReadyForImplementation)
        {
            var assigned = AssignPhase(phase, WorkflowActor.Implementer);
            if (assigned.IsFailure)
                return assigned;

            return TransitionPhase(assigned.Value, PhaseState.InImplementation, WorkflowActor.WorkflowSystem,
                "Implementation run started by orchestration.");
        }

        if (phase.State == PhaseState.AssignedToImplementation)
        {
            return TransitionPhase(phase, PhaseState.InImplementation, WorkflowActor.WorkflowSystem,
                "Implementation run started by orchestration.");
        }

        return phase.State == PhaseState.InImplementation
            ? WorkflowResult.Ok(phase)
            : WorkflowResult.Fail<Phase>(WorkflowError.InvalidTransition(phase.State, PhaseState.InImplementation,
                $"Implementation runs require phase '{phase.PhaseId}' to be ReadyForImplementation, AssignedToImplementation, or InImplementation."));
    }

    private WorkflowResult<Phase> AssignPhase(Phase phase, WorkflowActor agent)
    {
        if (phase.State == PhaseState.AssignedToImplementation && phase.AssignedAgent == agent)
            return WorkflowResult.Ok(phase);

        var validation = _phaseValidator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState = PhaseState.AssignedToImplementation,
            RequiresValidation = phase.Contract?.RequiresValidation ?? true
        });
        if (validation.IsFailure)
            return WorkflowResult.Fail<Phase>(validation.Error);

        var updated = phase with
        {
            State = PhaseState.AssignedToImplementation,
            AssignedAgent = agent,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updated);
        SaveWorkflowStatus(PhaseState.AssignedToImplementation);
        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "phase.assigned",
            Actor = WorkflowActor.WorkflowSystem,
            PhaseId = phase.PhaseId,
            Summary = $"{phase.PhaseId} assigned to {agent} by orchestration"
        });
        return WorkflowResult.Ok(updated);
    }

    private WorkflowResult<Phase> TransitionPhase(Phase phase, PhaseState targetState, WorkflowActor actor, string reason)
    {
        if (phase.State == targetState)
            return WorkflowResult.Ok(phase);

        var validation = _phaseValidator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState = targetState,
            RequiresValidation = phase.Contract?.RequiresValidation ?? true
        });
        if (validation.IsFailure)
            return WorkflowResult.Fail<Phase>(validation.Error);

        var now = DateTimeOffset.UtcNow;
        var updated = phase with
        {
            State = targetState,
            StartedUtc = targetState == PhaseState.InImplementation ? phase.StartedUtc ?? now : phase.StartedUtc,
            CompletedUtc = PhaseTransitionValidator.IsTerminal(targetState) ? now : phase.CompletedUtc,
            UpdatedUtc = now
        };
        store.SavePhase(updated);
        SaveWorkflowStatus(targetState);
        store.AppendEvent(new WorkflowEvent
        {
            EventId = store.NextEventId(),
            EventType = "phase.status.updated",
            Actor = actor,
            PhaseId = phase.PhaseId,
            Summary = $"{phase.PhaseId} status changed to {targetState}: {reason}"
        });
        return WorkflowResult.Ok(updated);
    }

    private void SaveWorkflowStatus(PhaseState targetState)
    {
        var workflow = store.LoadWorkflow();
        store.SaveWorkflow(workflow with
        {
            LastStatus = targetState,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }

    private OrchestrationRun CreateQueuedRun(
        OrchestrationSession session,
        OrchestrationRunRole role,
        string? requestedByGate,
        int attemptNumber,
        string rootRunId,
        string? parentRunId,
        string? laneId,
        string purpose)
    {
        if (attemptNumber <= 0)
            attemptNumber = 1;

        return new OrchestrationRun
        {
            RunId = store.NextOrchestrationRunId(),
            SessionId = session.SessionId,
            PhaseId = session.PhaseId,
            ParentRunId = parentRunId,
            RootRunId = rootRunId,
            LaneId = laneId,
            Role = role,
            AssignedActor = DefaultAssignedActor(role),
            RunState = OrchestrationRunState.Queued,
            Purpose = purpose,
            InputContractRef = session.ContractSnapshot,
            AttemptNumber = attemptNumber,
            RequestedByGate = requestedByGate,
            StartedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private OrchestrationGateDecisionRecord AppendGateDecision(
        OrchestrationSession session,
        OrchestrationRun run,
        OrchestrationGateKind gateKind,
        OrchestrationDecision decision,
        WorkflowActor actor,
        string rationale,
        IReadOnlyList<string> inputRecordIds,
        PhaseState? resultingPhaseState = null,
        string? nextActionKind = null,
        AttentionFlagsSnapshot? attentionFlags = null)
    {
        var record = new OrchestrationGateDecisionRecord
        {
            GateDecisionId = store.NextOrchestrationGateDecisionId(),
            SessionId = session.SessionId,
            PhaseId = session.PhaseId,
            RunId = run.RunId,
            GateKind = gateKind,
            GateAttempt = ResolveGateAttempt(session.Attempts, gateKind),
            Decision = decision,
            DecisionActor = actor,
            Rationale = rationale,
            InputRecordIds = inputRecordIds,
            ResultingPhaseState = resultingPhaseState,
            NextActionKind = nextActionKind,
            AttentionFlags = attentionFlags ?? session.AttentionFlags,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        store.AppendOrchestrationGateDecision(record);
        return record;
    }

    private static int ResolveGateAttempt(OrchestrationAttemptCounters attempts, OrchestrationGateKind gateKind) =>
        gateKind switch
        {
            OrchestrationGateKind.Implementation => Math.Max(1, attempts.ImplementationAttempts),
            OrchestrationGateKind.Audit => Math.Max(1, attempts.AuditAttempts),
            OrchestrationGateKind.Review => Math.Max(1, attempts.ReviewAttempts),
            OrchestrationGateKind.Validation => Math.Max(1, attempts.ValidationAttempts),
            _ => 1
        };

    private void AppendRunEvent(
        OrchestrationSession session,
        string? runId,
        OrchestrationEventKind eventKind,
        WorkflowActor actor,
        string summary,
        string notes,
        IReadOnlyList<string>? recordIds = null)
    {
        store.AppendOrchestrationRunEvent(new OrchestrationRunEventRecord
        {
            RunEventId = store.NextOrchestrationRunEventId(),
            SessionId = session.SessionId,
            RunId = runId,
            PhaseId = session.PhaseId,
            EventKind = eventKind,
            Actor = actor,
            Summary = summary,
            Notes = notes,
            RecordIds = recordIds ?? []
        });
    }

    private OrchestrationStatusSnapshot BuildStatusSnapshot(OrchestrationSession session)
    {
        var activeRun = session.ActiveRunId is null
            ? null
            : store.LoadOrchestrationRun(session.ActiveRunId);

        return new OrchestrationStatusSnapshot
        {
            SessionId = session.SessionId,
            PhaseId = session.PhaseId,
            SessionState = session.SessionState,
            ActiveRunId = session.ActiveRunId,
            ActiveRunRole = activeRun?.Role,
            ActiveRunState = activeRun?.RunState,
            LatestGateDecisionId = session.LatestGateDecisionId,
            AttentionFlags = session.AttentionFlags,
            DerivedAttentionOutcome = AttentionFlagRules.DeriveOutcome(session.AttentionFlags),
            Attempts = session.Attempts,
            AttemptPolicy = session.AttemptPolicy
        };
    }

    private void SyncPhaseAttentionFlags(string phaseId, AttentionFlagsSnapshot attentionFlags)
    {
        var phase = store.LoadPhase(phaseId);
        if (phase is null)
            return;

        store.SavePhase(phase with
        {
            AttentionFlags = attentionFlags,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }

    private static OrchestrationSessionState ResolveAttentionState(
        OrchestrationSessionState currentState,
        AttentionFlagsSnapshot attentionFlags)
    {
        if (currentState is OrchestrationSessionState.CompletedFailure
            or OrchestrationSessionState.CompletedPass
            or OrchestrationSessionState.CompletedPassWithWarnings
            or OrchestrationSessionState.Cancelled)
        {
            return currentState;
        }

        if (attentionFlags.BlockedByEnvironment || attentionFlags.BlockedByUser)
            return OrchestrationSessionState.Blocked;

        if (attentionFlags.HumanValidationRequired || attentionFlags.ManualReviewRequired || attentionFlags.ExternalToolRequired)
            return OrchestrationSessionState.WaitingForHuman;

        return OrchestrationSessionState.WaitingForAgent;
    }

    private static OrchestrationSessionState ResolveClearedAttentionState(
        OrchestrationSessionState currentState,
        AttentionFlagsSnapshot attentionFlags)
    {
        if (AttentionFlagRules.HasAny(attentionFlags))
            return ResolveAttentionState(currentState, attentionFlags);

        return currentState is OrchestrationSessionState.WaitingForHuman or OrchestrationSessionState.Blocked
            ? OrchestrationSessionState.WaitingForAgent
            : currentState;
    }

    private static AttentionFlagsSnapshot ClearAttentionFlags(
        AttentionFlagsSnapshot snapshot,
        IReadOnlyList<string> flagsToClear)
    {
        if (flagsToClear.Count == 0
            || flagsToClear.Any(static name => string.Equals(name, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return new AttentionFlagsSnapshot();
        }

        bool keep(string flagName) =>
            !flagsToClear.Any(name => string.Equals(name, flagName, StringComparison.OrdinalIgnoreCase));

        return snapshot with
        {
            HumanValidationRequired = keep("HumanValidationRequired") && snapshot.HumanValidationRequired,
            TestsNotRunnable = keep("TestsNotRunnable") && snapshot.TestsNotRunnable,
            ManualReviewRequired = keep("ManualReviewRequired") && snapshot.ManualReviewRequired,
            ExternalToolRequired = keep("ExternalToolRequired") && snapshot.ExternalToolRequired,
            MaxAgentAttemptsReached = keep("MaxAgentAttemptsReached") && snapshot.MaxAgentAttemptsReached,
            UnresolvedRisk = keep("UnresolvedRisk") && snapshot.UnresolvedRisk,
            BlockedByUser = keep("BlockedByUser") && snapshot.BlockedByUser,
            BlockedByEnvironment = keep("BlockedByEnvironment") && snapshot.BlockedByEnvironment,
            ReasonRecordIds = snapshot.ReasonRecordIds
        };
    }

    private static int ResolveManualRunAttempt(OrchestrationSession session, OrchestrationRunRole role) =>
        role switch
        {
            OrchestrationRunRole.Implementer => Math.Max(1, session.Attempts.ImplementationAttempts),
            OrchestrationRunRole.Auditor => Math.Max(1, session.Attempts.AuditAttempts),
            OrchestrationRunRole.Reviewer => Math.Max(1, session.Attempts.ReviewAttempts),
            OrchestrationRunRole.Tester => Math.Max(1, session.Attempts.ValidationAttempts),
            OrchestrationRunRole.Fixer => Math.Max(1, session.Attempts.FixAttempts),
            _ => 1
        };

    private bool HasActiveSession(string phaseId) =>
        store.LoadAllOrchestrationSessions().Any(s =>
            string.Equals(s.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)
            && !IsTerminal(s.SessionState));

    private IReadOnlyList<Phase> LoadRequiredDependencies(Phase phase, IReadOnlyList<string> dependsOnPhaseIds)
    {
        var ids = dependsOnPhaseIds.Count > 0 ? dependsOnPhaseIds : phase.Contract?.DependsOnPhaseIds ?? phase.Dependencies;
        return ids
            .Select(store.LoadPhase)
            .Where(static p => p is not null)
            .Cast<Phase>()
            .ToArray();
    }

    private static OrchestrationAttemptPolicy NormalizeAttemptPolicy(OrchestrationAttemptPolicy? attemptPolicy)
    {
        var policy = attemptPolicy ?? new OrchestrationAttemptPolicy();
        return new OrchestrationAttemptPolicy
        {
            MaxImplementationAttempts = Math.Max(1, policy.MaxImplementationAttempts),
            MaxAuditAttempts = Math.Max(1, policy.MaxAuditAttempts),
            MaxReviewAttempts = Math.Max(1, policy.MaxReviewAttempts),
            MaxValidationAttempts = Math.Max(1, policy.MaxValidationAttempts),
            MaxFixAttempts = Math.Max(1, policy.MaxFixAttempts)
        };
    }

    private static bool IsManagerActor(WorkflowActor actor) =>
        actor is WorkflowActor.Planner or WorkflowActor.WorkflowSystem or WorkflowActor.HumanOwner;

    private static bool IsTerminal(OrchestrationSessionState state) =>
        state is OrchestrationSessionState.CompletedPass
            or OrchestrationSessionState.CompletedPassWithWarnings
            or OrchestrationSessionState.CompletedFailure
            or OrchestrationSessionState.Cancelled;

    private static bool IsTerminal(OrchestrationRunState state) =>
        state is OrchestrationRunState.Accepted
            or OrchestrationRunState.Rejected
            or OrchestrationRunState.Blocked
            or OrchestrationRunState.Cancelled;

    private static WorkflowActor DefaultAssignedActor(OrchestrationRunRole role) =>
        role switch
        {
            OrchestrationRunRole.Implementer => WorkflowActor.Implementer,
            OrchestrationRunRole.Auditor => WorkflowActor.Auditor,
            OrchestrationRunRole.Reviewer => WorkflowActor.Reviewer,
            OrchestrationRunRole.Tester => WorkflowActor.Validator,
            OrchestrationRunRole.Fixer => WorkflowActor.Fixer,
            _ => WorkflowActor.WorkflowSystem
        };

    private OperationResult ValidateAcceptedRunEvidence(OrchestrationRun run)
    {
        if (run.ProducedRecordIds.Count == 0)
            return OperationResult.Fail("ValidationFailed",
                $"Run '{run.RunId}' produced no workflow record IDs.");

        foreach (var recordId in run.ProducedRecordIds)
        {
            if (!RecordExists(recordId, run.PhaseId))
                return OperationResult.Fail("ValidationFailed",
                    $"Run '{run.RunId}' references missing workflow record '{recordId}'.");
        }

        var expectedPrefix = run.Role switch
        {
            OrchestrationRunRole.Implementer => "IMP-",
            OrchestrationRunRole.Auditor => "AUD-",
            OrchestrationRunRole.Reviewer => "REV-",
            OrchestrationRunRole.Tester => null,
            OrchestrationRunRole.Fixer => "FIX-",
            _ => null
        };

        if (run.Role == OrchestrationRunRole.Tester)
        {
            if (!run.ProducedRecordIds.Any(id => id.StartsWith("VAL-", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult.Fail("ValidationFailed",
                    $"Validation run '{run.RunId}' must reference a VAL-* or TEST-* record.");
            }
        }
        else if (expectedPrefix is not null && !run.ProducedRecordIds.Any(id =>
                     id.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Fail("ValidationFailed",
                $"Run '{run.RunId}' must reference at least one {expectedPrefix} record.");
        }

        return OperationResult.Ok(run);
    }

    private bool RecordExists(string recordId, string phaseId) =>
        recordId[..Math.Min(recordId.Length, 4)].ToUpperInvariant() switch
        {
            "IMP-" => store.ReadAllImplementations()
                .Any(r => string.Equals(r.ImplementationId, recordId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)),
            "AUD-" => store.ReadAllAudits()
                .Any(r => string.Equals(r.AuditId, recordId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)),
            "REV-" => store.ReadAllReviews()
                .Any(r => string.Equals(r.ReviewId, recordId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)),
            "VAL-" => store.ReadAllValidations()
                .Any(r => string.Equals(r.ValidationId, recordId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)),
            "TEST" => store.ReadAllTests()
                .Any(r => string.Equals(r.TestId, recordId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)),
            "FIX-" => store.ReadAllFixes()
                .Any(r => string.Equals(r.FixId, recordId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

    private AuditRecord? FindAuditRecord(IReadOnlyList<string> recordIds)
    {
        var ids = recordIds
            .Where(id => id.StartsWith("AUD-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Count == 0
            ? null
            : store.ReadAllAudits().LastOrDefault(r => ids.Contains(r.AuditId));
    }

    private ReviewRecord? FindReviewRecord(IReadOnlyList<string> recordIds)
    {
        var ids = recordIds
            .Where(id => id.StartsWith("REV-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Count == 0
            ? null
            : store.ReadAllReviews().LastOrDefault(r => ids.Contains(r.ReviewId));
    }

    private ValidationRecord? FindValidationRecord(IReadOnlyList<string> recordIds)
    {
        var validationIds = recordIds
            .Where(id => id.StartsWith("VAL-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (validationIds.Count > 0)
            return store.ReadAllValidations().LastOrDefault(r => validationIds.Contains(r.ValidationId));

        var legacyIds = recordIds
            .Where(id => id.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (legacyIds.Count == 0)
            return null;

        var legacy = store.ReadAllTests().LastOrDefault(r => legacyIds.Contains(r.TestId));
        return legacy is null
            ? null
            : new ValidationRecord
            {
                ValidationId = legacy.TestId,
                PhaseId = legacy.PhaseId,
                Actor = legacy.Actor,
                ValidationType = ValidationType.AutomatedCommand,
                ValidationResult = legacy.Passed
                    ? (legacy.HasWarnings ? ValidationResult.PassedWithWarnings : ValidationResult.Passed)
                    : ValidationResult.Failed,
                Summary = legacy.Summary,
                HasWarnings = legacy.HasWarnings,
                Notes = legacy.Notes,
                CreatedUtc = legacy.CreatedUtc
            };
    }

    private FixRecord? FindFixRecord(IReadOnlyList<string> recordIds)
    {
        var ids = recordIds
            .Where(id => id.StartsWith("FIX-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Count == 0
            ? null
            : store.ReadAllFixes().LastOrDefault(r => ids.Contains(r.FixId));
    }

    private static PhaseState ResolveValidationSuccessState(ValidationRecord validation)
    {
        if (validation.ValidationResult == ValidationResult.PassedWithWarnings
            || validation.HasWarnings
            || validation.ValidationType == ValidationType.SkippedNotPossible)
        {
            return PhaseState.PassWithWarnings;
        }

        if (validation.ValidationResult == ValidationResult.Skipped
            && validation.ValidationType == ValidationType.SkippedNotPossible)
        {
            return PhaseState.PassWithWarnings;
        }

        return PhaseState.Pass;
    }

    private static int NextAttempt(int currentAttemptCount, int maxAttempts)
    {
        var next = currentAttemptCount + 1;
        return next > maxAttempts ? -1 : next;
    }

    private static string DefaultRationale(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string BuildContractHash(Phase phase)
    {
        var contract = phase.Contract;
        if (contract is null)
            return phase.PhaseId;

        return string.Join("|",
            contract.Objective,
            contract.Scope,
            contract.OutOfScope,
            string.Join(",", contract.AcceptanceCriteria),
            string.Join(",", contract.RequiredFilesOrAreas));
    }

    private static OrchestrationGateKind ParseGateKind(string? raw) =>
        Enum.TryParse<OrchestrationGateKind>(raw, ignoreCase: true, out var gate)
            ? gate
            : OrchestrationGateKind.Stop;
}
