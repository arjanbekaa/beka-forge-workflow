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

/// <summary>Creates a self-audit log entry and advances to AUDIT_LOGGED.</summary>
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

        var auditId = store.NextAuditId();
        var passed = context.GetBool("passed", defaultValue: true);
        var issuesRaw = context.GetString("issues");
        var issues = string.IsNullOrWhiteSpace(issuesRaw)
            ? (IReadOnlyList<string>)[]
            : new List<string>(issuesRaw.Split(['\n', '\r', ';', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var recommendationsRaw = context.GetString("recommendations");
        var recommendations = string.IsNullOrWhiteSpace(recommendationsRaw)
            ? (IReadOnlyList<string>)[]
            : new List<string>(recommendationsRaw.Split(['\n', '\r', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (!passed && issues.Count == 0)
            return OperationResult.Fail("ValidationFailed",
                "A failed audit must include at least one issue.");

        var record = new AuditRecord
        {
            AuditId         = auditId,
            PhaseId         = phaseId,
            Actor           = context.Actor,
            Summary         = summary,
            Passed          = passed,
            Issues          = issues,
            Recommendations = recommendations,
            Notes           = context.GetString("notes") ?? string.Empty,
            CreatedUtc      = DateTimeOffset.UtcNow
        };

        store.AppendAudit(record);

        var updatedPhase = phase with
        {
            State = PhaseState.AuditLogged,
            AuditLogIds = [..phase.AuditLogIds, auditId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastAuditId = auditId,
            LastStatus  = PhaseState.AuditLogged,
            UpdatedUtc  = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "audit.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Self-audit logged for {phaseId}: {auditId}",
            PayloadReference = auditId
        });

        return OperationResult.Ok(record);
    }
}

/// <summary>Creates a review log entry and advances to REVIEW_LOGGED.</summary>
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
            TargetState  = PhaseState.ReviewLogged
        });
        if (transitionResult.IsFailure)
            return OperationResult.FromError(transitionResult.Error);

        var reviewId = store.NextReviewId();
        var passed = context.GetBool("passed", defaultValue: true);
        var requiresFix = context.Parameters.ContainsKey("requiresFix")
            ? context.GetBool("requiresFix")
            : !passed;
        var issuesRaw = context.GetString("issues");
        var issues = string.IsNullOrWhiteSpace(issuesRaw)
            ? (IReadOnlyList<string>)[]
            : new List<string>(issuesRaw.Split(['\n', '\r', ';', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var recommendationsRaw = context.GetString("recommendations");
        var recommendations = string.IsNullOrWhiteSpace(recommendationsRaw)
            ? (IReadOnlyList<string>)[]
            : new List<string>(recommendationsRaw.Split(['\n', '\r', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (passed && requiresFix)
            return OperationResult.Fail("ValidationFailed",
                "A passing review cannot also require fixes.");
        if (!passed && !requiresFix)
            return OperationResult.Fail("ValidationFailed",
                "A non-passing review must require fixes.");
        if (requiresFix && issues.Count == 0)
            return OperationResult.Fail("ValidationFailed",
                "A review that requires fixes must include at least one issue.");

        var record = new ReviewRecord
        {
            ReviewId        = reviewId,
            PhaseId         = phaseId,
            Actor           = context.Actor,
            Summary         = summary,
            Passed          = passed,
            Issues          = issues,
            Recommendations = recommendations,
            RequiresFix     = requiresFix,
            Notes           = context.GetString("notes") ?? string.Empty,
            CreatedUtc      = DateTimeOffset.UtcNow
        };

        store.AppendReview(record);

        var nextState = requiresFix
            ? PhaseState.RequiresFix
            : PhaseState.ReviewLogged;

        var updatedPhase = phase with
        {
            State = nextState,
            ReviewLogIds = [..phase.ReviewLogIds, reviewId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastReviewId = reviewId,
            LastStatus   = nextState,
            UpdatedUtc   = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "review.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Review logged for {phaseId}: {reviewId}",
            PayloadReference = reviewId
        });

        return OperationResult.Ok(record);
    }
}

/// <summary>Legacy handler: creates a test log entry. Prefer CreateValidationLogHandler for new code.</summary>
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
            TargetState  = PhaseState.TestLogged
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
            State = PhaseState.TestLogged,
            TestLogIds = [..phase.TestLogIds, testId],
            ValidationLogIds = [..phase.ValidationLogIds, testId],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        store.SavePhase(updatedPhase);

        var wf = store.LoadWorkflow();
        store.SaveWorkflow(wf with
        {
            LastTestId = testId,
            LastStatus = PhaseState.TestLogged,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        store.AppendEvent(new WorkflowEvent
        {
            EventId          = store.NextEventId(),
            EventType        = "test.logged",
            Actor            = context.Actor,
            PhaseId          = phaseId,
            Summary          = $"Test logged for {phaseId}: {testId} (legacy)",
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
