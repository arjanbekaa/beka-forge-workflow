using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>Creates an implementation log entry and advances phase to IMPLEMENTATION_LOGGED.</summary>
public sealed class CreateImplementationLogHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.CreateImplementationLog;

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

        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.ImplementationLogged
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var impId = store.NextImplementationId();
        var record = new ImplementationRecord
        {
            ImplementationId = impId,
            PhaseId          = phaseId,
            Actor            = context.Actor,
            Summary          = summary,
            Status           = PhaseState.ImplementationLogged,
            Notes            = context.GetString("notes") ?? string.Empty,
            CreatedUtc       = DateTimeOffset.UtcNow
        };

        store.AppendImplementation(record);

        var updatedPhase = phase with
        {
            State = PhaseState.ImplementationLogged,
            ImplementationLogIds = [..phase.ImplementationLogIds, impId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastImplementationId = impId,
            LastStatus           = PhaseState.ImplementationLogged,
            UpdatedUtc           = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "implementation.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Implementation logged for {phaseId}: {impId}",
            PayloadReference = impId
        });

        return OperationResult.Ok(record);
    }
}

/// <summary>Creates a self-audit log entry (DeepSeek) and advances to AUDIT_LOGGED.</summary>
public sealed class CreateAuditLogHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.CreateAuditLog;

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

        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.AuditLogged
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var audId = store.NextAuditId();
        var passed = context.GetBool("passed", defaultValue: true);
        var record = new AuditRecord
        {
            AuditId    = audId,
            PhaseId    = phaseId,
            Actor      = context.Actor,
            Summary    = summary,
            Passed     = passed,
            Notes      = context.GetString("notes") ?? string.Empty,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        store.AppendAudit(record);

        var updatedPhase = phase with
        {
            State = PhaseState.AuditLogged,
            AuditLogIds = [..phase.AuditLogIds, audId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastAuditId = audId,
            LastStatus  = PhaseState.AuditLogged,
            UpdatedUtc  = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "audit.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Audit logged for {phaseId}: {audId}",
            PayloadReference = audId
        });

        return OperationResult.Ok(record);
    }
}

/// <summary>Creates a Codex review log entry and advances to CODEX_REVIEW_LOGGED.</summary>
public sealed class CreateReviewLogHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.CreateReviewLog;

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

        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.CodexReviewLogged
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var revId = store.NextReviewId();
        var passed = context.GetBool("passed", defaultValue: true);
        var record = new ReviewRecord
        {
            ReviewId     = revId,
            PhaseId      = phaseId,
            Actor        = context.Actor,
            Summary      = summary,
            Passed       = passed,
            RequiresFix  = context.GetBool("requiresFix"),
            Notes        = context.GetString("notes") ?? string.Empty,
            CreatedUtc   = DateTimeOffset.UtcNow
        };

        store.AppendReview(record);

        var updatedPhase = phase with
        {
            State = PhaseState.CodexReviewLogged,
            ReviewLogIds = [..phase.ReviewLogIds, revId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "review.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Codex review logged for {phaseId}: {revId}",
            PayloadReference = revId
        });

        return OperationResult.Ok(record);
    }
}

/// <summary>Creates a Unity test log entry.</summary>
public sealed class CreateTestLogHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.CreateTestLog;

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

        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.UnityTestLogged
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var testId = store.NextTestId();
        var passed = context.GetBool("passed", defaultValue: true);
        var record = new TestRecord
        {
            TestId      = testId,
            PhaseId     = phaseId,
            Actor       = context.Actor,
            Summary     = summary,
            Passed      = passed,
            HasWarnings = context.GetBool("hasWarnings"),
            Notes       = context.GetString("notes") ?? string.Empty,
            CreatedUtc  = DateTimeOffset.UtcNow
        };

        store.AppendTest(record);

        var updatedPhase = phase with
        {
            State = PhaseState.UnityTestLogged,
            TestLogIds = [..phase.TestLogIds, testId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastTestId = testId,
            LastStatus = PhaseState.UnityTestLogged,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "test.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Unity test logged for {phaseId}: {testId}",
            PayloadReference = testId
        });

        return OperationResult.Ok(record);
    }
}

/// <summary>Creates a fix log entry and advances to FIX_LOGGED.</summary>
public sealed class CreateFixLogHandler(WorkflowStore store) : IOperationHandler
{
    private readonly PhaseTransitionValidator _validator = new();
    public string OperationName => WorkflowOperations.CreateFixLog;

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

        var transitionResult = _validator.Validate(new TransitionContext
        {
            CurrentState = phase.State,
            TargetState  = PhaseState.FixLogged
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var fixId = store.NextFixId();
        var record = new FixRecord
        {
            FixId      = fixId,
            PhaseId    = phaseId,
            Actor      = context.Actor,
            Summary    = summary,
            Notes      = context.GetString("notes") ?? string.Empty,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        store.AppendFix(record);

        var updatedPhase = phase with
        {
            State = PhaseState.FixLogged,
            FixLogIds = [..phase.FixLogIds, fixId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastFixId  = fixId,
            LastStatus = PhaseState.FixLogged,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "fix.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Fix logged for {phaseId}: {fixId}",
            PayloadReference = fixId
        });

        return OperationResult.Ok(record);
    }
}
