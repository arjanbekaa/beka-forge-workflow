using BekaForge.WorkflowKit.AgentContracts;
using System.Text.Json;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// Code-owned catalog of every WorkflowKit operation and its manifest metadata.
/// This is the authoritative source for operation descriptions, access levels,
/// handler mappings, and write-target safety metadata. The generated
/// .workflowkit/index/operation-manifest.json is a rebuildable read model
/// derived from this catalog.
///
/// To add a new operation:
///   1. Add the constant to WorkflowOperations.
///   2. Add a handler and register it in OperationDispatcher.
///   3. Add a manifest entry here.
///   4. Run tests to confirm manifest coverage.
/// </summary>
public static class OperationManifestCatalog
{
    /// <summary>
    /// Returns the complete list of manifest entries for all known WorkflowKit operations.
    /// Every WorkflowOperations constant MUST have an entry here.
    /// Every dispatcher-registered handler MUST have an entry here with a HandlerTypeName.
    /// </summary>
    public static IReadOnlyList<OperationManifestEntry> GetAll()
    {
        return
        [
            // -- Workflow state reads -----------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetState,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Returns the full workflow state including phase list, current phase, next action, and metadata.",
                HandlerTypeName = typeof(Handlers.GetStateHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetCurrentPhase,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Returns the current active phase with its full state, contract, and dependencies.",
                HandlerTypeName = typeof(Handlers.GetCurrentPhaseHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListPhases,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Returns all phases in the workflow, ordered by phase number.",
                HandlerTypeName = typeof(Handlers.ListPhasesHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ValidateState,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Validates workflow consistency: phase ID references, file existence, and state integrity.",
                HandlerTypeName = typeof(Handlers.ValidateStateHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetIntegrityReport,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Aggregates registry, log, evidence, mirror, read-model, and operation-metadata integrity findings.",
                HandlerTypeName = typeof(Handlers.GetIntegrityReportHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ValidateReleaseGate,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Evaluates whether any release-blocking integrity issues currently fail the release gate.",
                HandlerTypeName = typeof(Handlers.ValidateReleaseGateHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetReleaseCandidateReport,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Builds the broader release candidate report across integrity, documentation, support, packaging, and release guidance evidence.",
                HandlerTypeName = typeof(Handlers.GetReleaseCandidateReportHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ValidatePublicRelease,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Evaluates whether public release evidence is complete enough to pass a release-readiness gate.",
                HandlerTypeName = typeof(Handlers.ValidatePublicReleaseHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetDashboardSummary,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Returns a compact dashboard summary with phase status, blocker counts, and urgency cues.",
                HandlerTypeName = typeof(Handlers.GetDashboardSummaryHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetContextBundle,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Workflow state reads",
                Summary         = "Returns a compact context bundle for LLM agents: state, phase, contract, logs, blockers, handoffs.",
                HandlerTypeName = typeof(Handlers.GetContextBundleHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListPersonas,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Persona policy",
                Summary         = "Lists the safe workflow personas available from the persisted or built-in persona catalog.",
                HandlerTypeName = typeof(Handlers.ListPersonasHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetPersona,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Persona policy",
                Summary         = "Returns a single persona profile and its supported task policies.",
                HandlerTypeName = typeof(Handlers.GetPersonaHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.RecommendPersona,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Persona policy",
                Summary         = "Recommends the best-fit persona for a task using the safe persona policy catalog.",
                HandlerTypeName = typeof(Handlers.RecommendPersonaHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ValidatePersonaTask,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Persona policy",
                Summary         = "Validates whether a persona-task pairing stays within the MVP safety policy boundaries.",
                HandlerTypeName = typeof(Handlers.ValidatePersonaTaskHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.CreateDocumentationRecord,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Documentation ledger",
                Summary         = "Adds a documentation ledger record that links workflow claims to evidence, commands, and operations.",
                HandlerTypeName = typeof(Handlers.CreateDocumentationRecordHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.CreateDocumentationRecord,
                        TargetDescription  = "Appends a documentation record to the workflow-owned ledger under .workflowkit/documentation/ledger.json.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = false,
                        RequiredParameters = ["title", "summary"],
                        SuitableActors     = ["implementer", "auditor", "reviewer", "validator"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetDocumentationLedger,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Documentation ledger",
                Summary         = "Returns the documentation ledger records stored for the current workflow.",
                HandlerTypeName = typeof(Handlers.GetDocumentationLedgerHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetDocumentationDraft,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Documentation ledger",
                Summary         = "Builds a markdown documentation draft grouped by documentation claim status.",
                HandlerTypeName = typeof(Handlers.GetDocumentationDraftHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetDocumentationCoverage,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Documentation ledger",
                Summary         = "Checks whether the current public commands and operations have evidence-backed documentation coverage.",
                HandlerTypeName = typeof(Handlers.GetDocumentationCoverageHandler).FullName
            },

            // -- Phase management --------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.CreatePhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Creates a new phase, assigns a phase ID, and appends it to the workflow phase list.",
                HandlerTypeName = typeof(Handlers.CreatePhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreatePhase,
                        TargetDescription = "Creates a phase JSON file under .workflowkit/phases/ and registers it in workflow.json.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["title", "summary"],
                        SuitableActors    = ["planner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.UpdatePhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Updates non-status phase metadata: title, summary, and dependencies.",
                HandlerTypeName = typeof(Handlers.UpdatePhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.UpdatePhase,
                        TargetDescription = "Updates metadata fields in the phase JSON file under .workflowkit/phases/.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors    = ["planner"]
                    }
                ]
            },
            new()
            {
                OperationName = WorkflowOperations.RemovePhase,
                AccessLevel = OperationAccessLevel.Write,
                Category = "Phase management",
                Summary = "Removes a planned phase from workflow.json and deletes its generated phase files.",
                HandlerTypeName = typeof(Handlers.RemovePhaseHandler).FullName,
                WriteTargets =
                [
                    new()
                    {
                        OperationName = WorkflowOperations.RemovePhase,
                        TargetDescription = "Removes a planned phase from workflow.json and deletes .workflowkit/phases/PHASE-NNN.json plus workflow/phases/PHASE-NNN.md.",
                        AccessLevel = OperationAccessLevel.Write,
                        IsAppendOnly = false,
                        IsEventTracked = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors = ["planner", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.UpdatePhaseStatus,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Transitions a phase to a new state using the state machine validator.",
                HandlerTypeName = typeof(Handlers.UpdatePhaseStatusHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.UpdatePhaseStatus,
                        TargetDescription = "Updates the state field in the phase JSON file, validated by the state machine.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "state"],
                        SuitableActors    = ["planner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.DeferPhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Explicitly defers a non-terminal phase so later phases can proceed without faking lifecycle progress.",
                HandlerTypeName = typeof(Handlers.DeferPhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.DeferPhase,
                        TargetDescription  = "Writes deferred metadata on the phase JSON file and optionally moves workflow.json currentPhaseId to another phase.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["phaseId", "reason"],
                        SuitableActors     = ["planner", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.FocusPhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Moves workflow focus to a phase without mutating its lifecycle state, clearing deferred metadata if present.",
                HandlerTypeName = typeof(Handlers.FocusPhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.FocusPhase,
                        TargetDescription  = "Updates workflow.json currentPhaseId and clears deferred metadata on the target phase JSON file.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["phaseId", "reason"],
                        SuitableActors     = ["planner", "humanowner"]
                    }
                ]
            },
            // PHASE-008: Phase recovery
            new()
            {
                OperationName   = WorkflowOperations.ReopenPhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Reopens a phase that is in a terminal failure state (FailedValidation, FailedArchitecture, FailedCompile). Transitions to ReadyForImplementation. Requires a reason.",
                HandlerTypeName = typeof(Handlers.ReopenPhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.ReopenPhase,
                        TargetDescription = "Bypasses the terminal-state guard and transitions the phase to ReadyForImplementation.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "reason"],
                        SuitableActors    = ["planner", "human_owner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.AssignPhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Assigns a specific agent (Planner/Implementer) to implement a phase.",
                HandlerTypeName = typeof(Handlers.AssignPhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.AssignPhase,
                        TargetDescription = "Updates the assignedAgent field in the phase JSON file.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "agent"],
                        SuitableActors    = ["planner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.StartPhase,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Starts implementation of a phase (ASSIGNED_TO_IMPLEMENTATION → IN_IMPLEMENTATION).",
                HandlerTypeName = typeof(Handlers.StartPhaseHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.StartPhase,
                        TargetDescription = "Transitions phase state to IN_IMPLEMENTATION in the phase JSON file.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors    = ["implementer", "planner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CompleteImplementation,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase management",
                Summary         = "Completes implementation and advances to IMPLEMENTATION_LOGGED.",
                HandlerTypeName = typeof(Handlers.CompleteImplementationHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CompleteImplementation,
                        TargetDescription = "Transitions phase state to IMPLEMENTATION_LOGGED in the phase JSON file.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors    = ["implementer", "planner"]
                    }
                ]
            },

            // -- Phase contract ----------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetPhaseContract,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Phase contract",
                Summary         = "Returns the full PhaseContract JSON for a phase.",
                HandlerTypeName = typeof(Handlers.GetPhaseContractHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.SavePhaseContract,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Phase contract",
                Summary         = "Saves or updates the PhaseContract JSON for a phase.",
                HandlerTypeName = typeof(Handlers.SavePhaseContractHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.SavePhaseContract,
                        TargetDescription = "Writes the contract field in the phase JSON file under .workflowkit/phases/.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors    = ["planner"]
                    }
                ]
            },

            // -- Next action -------------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetNextAction,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Next action",
                Summary         = "Returns the current nextAction from workflow.json.",
                HandlerTypeName = typeof(Handlers.GetNextActionHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.SetNextAction,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Next action",
                Summary         = "Sets or clears the nextAction in workflow.json.",
                HandlerTypeName = typeof(Handlers.SetNextActionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.SetNextAction,
                        TargetDescription = "Updates the nextAction field in workflow.json (safe planning metadata).",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["description", "actor"],
                        SuitableActors    = ["planner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetProjectGuidance,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Project guidance docs",
                Summary         = "Reads structured project guidance used to render known limitations, extension guidance, and final review documents, plus workflow documentation policy.",
                HandlerTypeName = typeof(Handlers.GetProjectGuidanceHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.SetProjectGuidance,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Project guidance docs",
                Summary         = "Updates structured project guidance used to render known limitations, extension guidance, and final review documents, plus workflow documentation policy.",
                HandlerTypeName = typeof(Handlers.SetProjectGuidanceHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.SetProjectGuidance,
                        TargetDescription  = "Updates workflow.json project guidance fields or documentation policy and appends an event to events.jsonl.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["section", "content"],
                        SuitableActors     = ["planner", "humanowner"]
                    }
                ]
            },

            // -- Record creation ---------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.CreateImplementationLog,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Record creation",
                Summary         = "Creates an implementation log entry (IMP-) and advances phase to IMPLEMENTATION_LOGGED.",
                HandlerTypeName = typeof(Handlers.CreateImplementationLogHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateImplementationLog,
                        TargetDescription = "Appends an IMP record to implementation.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary"],
                        SuitableActors    = ["implementer", "planner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CreateAuditLog,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Record creation",
                Summary         = "Creates a self-audit log entry (AUD-) and advances phase to AUDIT_LOGGED.",
                HandlerTypeName = typeof(Handlers.CreateAuditLogHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateAuditLog,
                        TargetDescription = "Appends an AUD record to audit.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary"],
                        SuitableActors    = ["implementer", "deepseek", "auditor"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CreateReviewLog,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Record creation",
                Summary         = "Creates a review gate entry (REV-) and advances phase past the review gate.",
                HandlerTypeName = typeof(Handlers.CreateReviewLogHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateReviewLog,
                        TargetDescription = "Appends a REV record to review.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary", "passed"],
                        SuitableActors    = ["planner", "reviewer", "codex"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CreateTestLog,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Record creation",
                Summary         = "Creates a test log entry (TEST-) and advances phase to UNITY_TEST_LOGGED.",
                HandlerTypeName = typeof(Handlers.CreateTestLogHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateTestLog,
                        TargetDescription = "Appends a TEST record to test.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary"],
                        SuitableActors    = ["validator", "reviewer"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CreateFixLog,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Record creation",
                Summary         = "Creates a fix log entry (FIX-) and advances phase to FIX_LOGGED.",
                HandlerTypeName = typeof(Handlers.CreateFixLogHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateFixLog,
                        TargetDescription = "Appends a FIX record to fix.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary"],
                        SuitableActors    = ["implementer", "planner"]
                    }
                ]
            },

            new()
            {
                OperationName   = WorkflowOperations.CreateValidationLog,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Record creation",
                Summary         = "Creates a validation log entry (VAL-) with evidence and advances phase to TEST_LOGGED. Enforces honesty rules: evidence required for Passed, manual types cannot be Passed by LLM alone, skipped requires reason.",
                HandlerTypeName = typeof(Handlers.CreateValidationLogHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateValidationLog,
                        TargetDescription = "Appends a VAL record to validation.jsonl. Append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary", "validationType", "validationResult"],
                        SuitableActors    = ["validator", "implementer", "reviewer", "humanowner"]
                    }
                ]
            },

            // -- Validation commands -----------------------
            new()
            {
                OperationName   = WorkflowOperations.GetValidationPlan,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Validation commands",
                Summary         = "Returns a validation plan for a phase: what must be tested, what the agent can test, what requires the user, and exact manual steps.",
                HandlerTypeName = typeof(Handlers.GetValidationPlanHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.RequestUserValidation,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Validation commands",
                Summary         = "Creates a PendingUser validation record with manual test steps for the human owner to perform.",
                HandlerTypeName = typeof(Handlers.RequestUserValidationHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.RequestUserValidation,
                        TargetDescription = "Appends a VAL record to validation.jsonl with PendingUser result and manual steps.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors    = ["implementer", "reviewer", "validator"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CompleteUserValidation,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Validation commands",
                Summary         = "Records the human owner's result for a pending manual validation. Requires Passed/PassedWithWarnings/Failed result and evidence.",
                HandlerTypeName = typeof(Handlers.CompleteUserValidationHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CompleteUserValidation,
                        TargetDescription = "Appends a VAL record to validation.jsonl with HumanOwner actor and final result. Advances phase to TEST_LOGGED.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "validationResult", "summary"],
                        SuitableActors    = ["humanowner", "user"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.SkipValidation,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Validation commands",
                Summary         = "Records a skipped validation with reason and optional approval. Advances phase to TEST_LOGGED.",
                HandlerTypeName = typeof(Handlers.SkipValidationHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.SkipValidation,
                        TargetDescription = "Appends a VAL record to validation.jsonl with Skipped result and skip reason.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "skipReason"],
                        SuitableActors    = ["implementer", "reviewer", "validator", "humanowner"]
                    }
                ]
            },

            // -- Orchestration runtime -------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.StartOrchestrationSession,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Starts a deterministic orchestration session for one phase and queues the root implementer run.",
                HandlerTypeName = typeof(Handlers.StartOrchestrationSessionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.StartOrchestrationSession,
                        TargetDescription  = "Writes orchestration session and run JSON under .workflowkit/orchestration/ and appends orchestration run events.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["phaseId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.AdvanceOrchestrationSession,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Advances a session deterministically based on the active run, canonical workflow records, and bounded retry policy.",
                HandlerTypeName = typeof(Handlers.AdvanceOrchestrationSessionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.AdvanceOrchestrationSession,
                        TargetDescription  = "Updates orchestration session and run JSON, appends gate decisions and run events, and may advance the canonical phase state.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.PauseOrchestrationSession,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Moves an active orchestration session back to a controlled waiting state without discarding evidence.",
                HandlerTypeName = typeof(Handlers.PauseOrchestrationSessionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.PauseOrchestrationSession,
                        TargetDescription  = "Updates orchestration session and run JSON and appends a run event describing the pause.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId", "reason"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CancelOrchestrationSession,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Cancels a non-terminal orchestration session and marks the active run cancelled when applicable.",
                HandlerTypeName = typeof(Handlers.CancelOrchestrationSessionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.CancelOrchestrationSession,
                        TargetDescription  = "Updates orchestration session and run JSON and appends a cancellation event.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId", "reason"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.FocusOrchestrationSession,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Moves workflow focus to the phase owned by a session without mutating lifecycle state.",
                HandlerTypeName = typeof(Handlers.FocusOrchestrationSessionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.FocusOrchestrationSession,
                        TargetDescription  = "Updates workflow.json currentPhaseId and appends an orchestration session event.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.CreateOrchestrationRun,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Queues a manual orchestration run under a live session for operator-directed work or recovery.",
                HandlerTypeName = typeof(Handlers.CreateOrchestrationRunHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.CreateOrchestrationRun,
                        TargetDescription  = "Writes orchestration run JSON under .workflowkit/orchestration/runs/ and updates the parent session.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId", "role"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.StartOrchestrationRun,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Starts a queued orchestration run and applies the required phase-state transition for that role.",
                HandlerTypeName = typeof(Handlers.StartOrchestrationRunHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.StartOrchestrationRun,
                        TargetDescription  = "Updates orchestration run and session JSON and may advance the canonical phase state to InImplementation, ReviewInProgress, TestInProgress, or FixInProgress.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["runId"],
                        SuitableActors     = ["implementer", "auditor", "reviewer", "validator", "fixer", "workflowsystem"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.ReportOrchestrationRun,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Reports the normalized output of an orchestration run and links the canonical workflow record IDs it produced.",
                HandlerTypeName = typeof(Handlers.ReportOrchestrationRunHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.ReportOrchestrationRun,
                        TargetDescription  = "Updates orchestration run JSON and appends a run-reported event.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["runId", "summary"],
                        SuitableActors     = ["implementer", "auditor", "reviewer", "validator", "fixer", "workflowsystem"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.AcceptOrchestrationRun,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Accepts a reported orchestration run after verifying its canonical workflow records exist.",
                HandlerTypeName = typeof(Handlers.AcceptOrchestrationRunHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.AcceptOrchestrationRun,
                        TargetDescription  = "Updates orchestration run and session JSON and appends a run-accepted event.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["runId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.RejectOrchestrationRun,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Rejects a reported orchestration run while preserving the failed report in history for deterministic retry handling.",
                HandlerTypeName = typeof(Handlers.RejectOrchestrationRunHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.RejectOrchestrationRun,
                        TargetDescription  = "Updates orchestration run and session JSON and appends a run-rejected event.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["runId", "reason"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.RecordOrchestrationGateDecision,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Orchestration runtime",
                Summary         = "Appends a manual orchestration gate decision for operator-driven escalations, overrides, or recoveries.",
                HandlerTypeName = typeof(Handlers.RecordOrchestrationGateDecisionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.RecordOrchestrationGateDecision,
                        TargetDescription  = "Appends an orchestration gate decision record under .workflowkit/orchestration/ and updates the latest session decision pointer.",
                        AccessLevel        = OperationAccessLevel.Append,
                        IsAppendOnly       = true,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId", "runId", "gateKind", "decision"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.SetOrchestrationAttentionFlags,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Sets machine-readable orchestration attention flags on a session and optional run.",
                HandlerTypeName = typeof(Handlers.SetOrchestrationAttentionFlagsHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.SetOrchestrationAttentionFlags,
                        TargetDescription  = "Updates orchestration session/run JSON and synchronizes the linked phase attention snapshot.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.ClearOrchestrationAttentionFlags,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Clears one or more orchestration attention flags from a session and optional run.",
                HandlerTypeName = typeof(Handlers.ClearOrchestrationAttentionFlagsHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.ClearOrchestrationAttentionFlags,
                        TargetDescription  = "Updates orchestration session/run JSON and synchronizes the linked phase attention snapshot.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.RequestOrchestrationHumanAttention,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Orchestration runtime",
                Summary         = "Escalates a live orchestration session into a human-attention state and records the escalation reason.",
                HandlerTypeName = typeof(Handlers.RequestOrchestrationHumanAttentionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.RequestOrchestrationHumanAttention,
                        TargetDescription  = "Updates orchestration session/run JSON, synchronizes phase attention flags, and may append a gate decision.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["sessionId"],
                        SuitableActors     = ["planner", "workflowsystem", "humanowner"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetOrchestrationStatus,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Orchestration runtime",
                Summary         = "Returns the shared orchestration status snapshot for one session.",
                HandlerTypeName = typeof(Handlers.GetOrchestrationStatusHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetOrchestrationAttentionStatus,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Orchestration runtime",
                Summary         = "Returns the orchestration status snapshot with derived attention outcome for one session.",
                HandlerTypeName = typeof(Handlers.GetOrchestrationAttentionStatusHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListOrchestrationSessions,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Orchestration runtime",
                Summary         = "Lists orchestration sessions, optionally filtered by phase.",
                HandlerTypeName = typeof(Handlers.ListOrchestrationSessionsHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListOrchestrationRuns,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Orchestration runtime",
                Summary         = "Lists orchestration runs for a session in creation order.",
                HandlerTypeName = typeof(Handlers.ListOrchestrationRunsHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListOrchestrationGateDecisions,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Orchestration runtime",
                Summary         = "Lists persisted orchestration gate decisions for a session or phase.",
                HandlerTypeName = typeof(Handlers.ListOrchestrationGateDecisionsHandler).FullName
            },

            // -- Blockers ----------------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.RecordBlocker,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Blockers",
                Summary         = "Records a blocker (BLK-) against a phase or workflow.",
                HandlerTypeName = typeof(Handlers.RecordBlockerHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.RecordBlocker,
                        TargetDescription = "Appends a BLK record to blockers.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "reason"],
                        SuitableActors    = ["planner", "implementer"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.ResolveBlocker,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Blockers",
                Summary         = "Resolves an open blocker by recording resolution.",
                HandlerTypeName = typeof(Handlers.ResolveBlockerHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.ResolveBlocker,
                        TargetDescription = "Appends a resolution record to blockers.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["blockerId"],
                        SuitableActors    = ["planner", "implementer"]
                    }
                ]
            },

            // -- Handoffs ----------------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.CreateHandoff,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Handoffs",
                Summary         = "Creates a handoff record (HANDOFF-) from one actor to another.",
                HandlerTypeName = typeof(Handlers.CreateHandoffHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.CreateHandoff,
                        TargetDescription = "Appends a HANDOFF record to handoffs.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "summary", "fromActor", "toActor"],
                        SuitableActors    = ["planner", "implementer"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetHandoffs,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Handoffs",
                Summary         = "Returns all handoff records, optionally filtered by phaseId.",
                HandlerTypeName = typeof(Handlers.GetHandoffsHandler).FullName
            },

            // -- Timing / metrics --------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.RecordTimeSpent,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Timing / metrics",
                Summary         = "Records time spent (TIME-) on a phase or workflow activity.",
                HandlerTypeName = typeof(Handlers.RecordTimeSpentHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.RecordTimeSpent,
                        TargetDescription = "Appends a TIME record to timing.jsonl — append-only, never rewritten.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = false,
                        RequiredParameters = ["duration", "activity"],
                        SuitableActors    = ["planner", "implementer", "validator"]
                    }
                ]
            },

            // -- Markdown sync -----------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.SyncMarkdown,
                AccessLevel     = OperationAccessLevel.Regenerate,
                Category        = "Markdown sync",
                Summary         = "Regenerates all markdown files, merging generated regions while preserving human content.",
                HandlerTypeName = typeof(Handlers.SyncMarkdownHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.SyncMarkdown,
                        TargetDescription = "Regenerates generated regions in markdown files under workflow/. Human-written sections are preserved.",
                        AccessLevel       = OperationAccessLevel.Regenerate,
                        IsAppendOnly      = false,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },

            // -- ChangeSet import/apply ------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.ValidateChangeSet,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "ChangeSet import/apply",
                Summary         = "Loads and validates a WorkflowKit ChangeSet JSON file without writing state.",
                HandlerTypeName = typeof(Handlers.ValidateChangeSetHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ApplyChangeSet,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "ChangeSet import/apply",
                Summary         = "Applies a validated WorkflowKit ChangeSet through allowlisted existing operation handlers.",
                HandlerTypeName = typeof(Handlers.ApplyChangeSetHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.ApplyChangeSet,
                        TargetDescription  = "Applies allowlisted createPhase, setNextAction, and syncMarkdown operations through existing handler paths after full validation.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = ["file"],
                        SuitableActors     = ["planner", "implementer", "workflowsystem"]
                    }
                ]
            },

            // -- Operation manifest -----------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetOperationManifest,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Operation manifest",
                Summary         = "Returns the full operation manifest describing every WorkflowKit operation and its safety level.",
                HandlerTypeName = typeof(Handlers.GetOperationManifestHandler).FullName
            },

            // -- Tool routing / recommendations ------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.SearchOperations,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Tool routing / recommendations",
                Summary         = "Searches the operation manifest for entries matching a keyword in name, category, or summary.",
                HandlerTypeName = typeof(Handlers.SearchOperationsHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.RecommendOperation,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Tool routing / recommendations",
                Summary         = "Recommends WorkflowKit operations for a natural-language task description with safety warnings and ranked confidence.",
                HandlerTypeName = typeof(Handlers.RecommendOperationHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ExplainOperation,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Tool routing / recommendations",
                Summary         = "Returns the manifest entry (access level, category, summary) for a given operation name.",
                HandlerTypeName = typeof(Handlers.ExplainOperationHandler).FullName
            },

            // -- Context index ----------------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.RebuildContextIndex,
                AccessLevel     = OperationAccessLevel.Regenerate,
                Category        = "Context index",
                Summary         = "Rebuilds the SQLite context index from authoritative JSON/JSONL sources. Returns index health summary.",
                HandlerTypeName = typeof(Handlers.RebuildContextIndexHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.RebuildContextIndex,
                        TargetDescription = "Rebuilds .workflowkit/index/* SQLite files from authoritative JSON/JSONL sources. The index is rebuildable — not source of truth.",
                        AccessLevel       = OperationAccessLevel.Regenerate,
                        IsAppendOnly      = false,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },

            // -- Slice APIs ------------------------------------------------------
            new() { OperationName = WorkflowOperations.GetFileSlice,        AccessLevel = OperationAccessLevel.Read, Category = "Slice APIs", Summary = "Returns exact content from a file within a line range, with hash and staleness metadata.", HandlerTypeName = typeof(Handlers.GetFileSliceHandler).FullName },
            new() { OperationName = WorkflowOperations.GetRecordSlice,      AccessLevel = OperationAccessLevel.Read, Category = "Slice APIs", Summary = "Looks up a JSONL record by ID (IMP-, AUD-, REV-, TEST-, FIX-, BLK-, HANDOFF-, TIME-, EVT-).", HandlerTypeName = typeof(Handlers.GetRecordSliceHandler).FullName },
            new() { OperationName = WorkflowOperations.GetJsonPointerValue, AccessLevel = OperationAccessLevel.Read, Category = "Slice APIs", Summary = "Resolves a JSON Pointer path against a workflow or phase JSON file.", HandlerTypeName = typeof(Handlers.GetJsonPointerValueHandler).FullName },
            new() { OperationName = WorkflowOperations.GetMarkdownRegion,   AccessLevel = OperationAccessLevel.Read, Category = "Slice APIs", Summary = "Extracts a BEKAFORGE generated region from a markdown file by section name.", HandlerTypeName = typeof(Handlers.GetMarkdownRegionHandler).FullName },
            new() { OperationName = WorkflowOperations.GetFileHistory,      AccessLevel = OperationAccessLevel.Read, Category = "Slice APIs", Summary = "Returns all implementation, fix, audit, and review records referencing a given file path.", HandlerTypeName = typeof(Handlers.GetFileHistoryHandler).FullName },

            // -- Relevant context ---------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetRelevantContext,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Relevant context",
                Summary         = "Returns ranked context pointers that agents resolve through slice APIs. Preferred first operation for large agent context tasks.",
                HandlerTypeName = typeof(Handlers.GetRelevantContextHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetBudgetConfig,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Relevant context",
                Summary         = "Returns the project budget configuration and effective budget profile for context retrieval.",
                HandlerTypeName = typeof(Handlers.GetBudgetConfigHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.SetBudgetConfig,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Relevant context",
                Summary         = "Updates the project default budget mode and optional per-mode budget overrides.",
                HandlerTypeName = typeof(Handlers.SetBudgetConfigHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.SetBudgetConfig,
                        TargetDescription = "Writes .workflowkit/budget-config.json through the dispatcher-safe budget configuration handler.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = ["planner", "implementer", "workflowsystem"]
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetBudgetReport,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Relevant context",
                Summary         = "Returns the resolved budget profile and budget priority explanation for context-heavy operations.",
                HandlerTypeName = typeof(Handlers.GetBudgetReportHandler).FullName
            },

            // -- Safety validation ---------------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.ValidateOperationRequest,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Safety validation",
                Summary         = "Validates an operation request for safety: checks required parameters, actor suitability, access level, phase existence, and append-only impact. Returns safer alternatives for invalid or unsafe requests.",
                HandlerTypeName = typeof(Handlers.ValidateOperationRequestHandler).FullName
            },

            // -- Cache operations (PHASE-015) ---------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetCacheStatus,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Cache operations",
                Summary         = "Returns context cache diagnostics: hit rate, package count, memory estimate, LRU order, and configuration.",
                HandlerTypeName = typeof(Handlers.GetCacheStatusHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ClearContextCache,
                AccessLevel     = OperationAccessLevel.Regenerate,
                Category        = "Cache operations",
                Summary         = "Clears non-pinned packages from the context cache. Source data is unaffected.",
                HandlerTypeName = typeof(Handlers.ClearContextCacheHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.ClearContextCache,
                        TargetDescription = "Clears RAM cache packages. Pinned packages survive by default. Source JSON/JSONL is never touched.",
                        AccessLevel       = OperationAccessLevel.Regenerate,
                        IsAppendOnly      = false,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },

            // -- Sub-phase management ----------------------------------------
            new()
            {
                OperationName   = WorkflowOperations.UpdateSubPhaseStatus,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Sub-phase management",
                Summary         = "Transitions a sub-phase to a new status: Planned -> InProgress -> Completed. Validates dependencies.",
                HandlerTypeName = typeof(Handlers.UpdateSubPhaseStatusHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.UpdateSubPhaseStatus,
                        TargetDescription = "Updates a sub-phase status field in the phase JSON file. Validates transition rules and dependency completion.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = true,
                        RequiredParameters = ["phaseId", "subPhaseId", "status"],
                        SuitableActors    = ["implementer", "planner"]
                    }
                ]
            },

            // -- Trace operations (PHASE-016) ---------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetTraceStatus,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Trace operations",
                Summary         = "Returns trace system status: mode, file count, record count, directory size, and configuration.",
                HandlerTypeName = typeof(Handlers.GetTraceStatusHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListTraces,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Trace operations",
                Summary         = "Lists recent trace records with optional filters by phase, operation, status, and actor.",
                HandlerTypeName = typeof(Handlers.ListTracesHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetTrace,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Trace operations",
                Summary         = "Gets a single trace record by trace ID with full span detail (in verbose mode).",
                HandlerTypeName = typeof(Handlers.GetTraceHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ClearOldTraces,
                AccessLevel     = OperationAccessLevel.Regenerate,
                Category        = "Trace operations",
                Summary         = "Clears trace files older than the configured retention period. Diagnostic data only — never source of truth.",
                HandlerTypeName = typeof(Handlers.ClearOldTracesHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.ClearOldTraces,
                        TargetDescription = "Deletes trace JSONL files older than RetentionDays. Traces are diagnostics only, not source of truth.",
                        AccessLevel       = OperationAccessLevel.Regenerate,
                        IsAppendOnly      = false,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.SetTraceOptions,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Trace operations",
                Summary         = "Updates trace system options in-memory: mode (Off/Basic/Verbose), retention days, and directory size limit. Does not persist across restarts.",
                HandlerTypeName = typeof(Handlers.SetTraceOptionsHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.SetTraceOptions,
                        TargetDescription = "Updates in-memory trace options (mode, retention, size limit). Does not persist across server restarts.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = false,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },
            // -- Git activity (PHASE-018) ---------------------------------
            new()
            {
                OperationName   = WorkflowOperations.GetGitStatus,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Git activity",
                Summary         = "Returns the current git worktree status: branch name, dirty flag, staged/unstaged/untracked counts, latest commit SHA, ahead/behind remote counts.",
                HandlerTypeName = typeof(Handlers.GetGitStatusHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.ListGitCommits,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Git activity",
                Summary         = "Lists recent git commits with optional filters: maxCount, since date, branch, and phase ID (from [PHASE-NNN] convention).",
                HandlerTypeName = typeof(Handlers.ListGitCommitsHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetGitActivity,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Git activity",
                Summary         = "Returns git activity records from the append-only activity ledger with optional filters: phaseId, sessionId, activityType.",
                HandlerTypeName = typeof(Handlers.GetGitActivityHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetGitBranchInfo,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Git activity",
                Summary         = "Lists all local git branches with tracking remote, ahead/behind counts, and latest commit SHA.",
                HandlerTypeName = typeof(Handlers.GetGitBranchInfoHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetGitHealth,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Git activity",
                Summary         = "Returns git/workflow health warnings: dirty worktree, stale dirty tree, unpushed commits, behind remote, and other consistency issues.",
                HandlerTypeName = typeof(Handlers.GetGitHealthHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.RecordGitActivity,
                AccessLevel     = OperationAccessLevel.Append,
                Category        = "Git activity",
                Summary         = "Snapshots the current git state (status + recent commits) into the append-only activity ledger.",
                HandlerTypeName = typeof(Handlers.RecordGitActivityHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.RecordGitActivity,
                        TargetDescription = "Appends git status snapshot and new commit records to .workflowkit/git/activity.jsonl. Observational data only — never source of truth.",
                        AccessLevel       = OperationAccessLevel.Append,
                        IsAppendOnly      = true,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetTimeline,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Git activity",
                Summary         = "Returns a merged chronological timeline of workflow events (implementations, audits, reviews, tests, fixes) and git activity (commits, status snapshots, branch changes).",
                HandlerTypeName = typeof(Handlers.TimelineHandler).FullName
            },

            // -- Session management (PHASE-018-F) --------------------------
            new()
            {
                OperationName   = WorkflowOperations.ListSessions,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Session management",
                Summary         = "Lists recent workflow sessions with optional activeOnly filter. Each session shows user, machine, phase, and timing.",
                HandlerTypeName = typeof(Handlers.ListSessionsHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.GetCurrentSession,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Session management",
                Summary         = "Returns the current active session and identity info (user name/email from git config, machine name, platform).",
                HandlerTypeName = typeof(Handlers.GetCurrentSessionHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.EndSession,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Session management",
                Summary         = "Ends the current or specified session by recording an end-of-session record in the append-only sessions log.",
                HandlerTypeName = typeof(Handlers.EndSessionHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.EndSession,
                        TargetDescription = "Appends an end-of-session record to .workflowkit/git/sessions.jsonl. Observational data only.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = true,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = null
                    }
                ]
            },

            // -- Inbox / offline operation queue (PHASE-019) --------------------
            new()
            {
                OperationName   = WorkflowOperations.ProcessInbox,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Inbox / offline operation queue",
                Summary         = "Processes all pending operations in the offline inbox. Validates, dispatches, and moves to processed/ or failed/.",
                HandlerTypeName = typeof(Handlers.ProcessInboxHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.ProcessInbox,
                        TargetDescription = "Processes .operation.json files from .workflowkit/inbox/ through the normal dispatcher flow. Never directly edits authoritative state files.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = true,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = new[] { "implementer", "workflowsystem" }
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.GetInboxStatus,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Inbox / offline operation queue",
                Summary         = "Returns inbox status: pending count, processed count, failed count, oldest pending timestamp, and pending file list.",
                HandlerTypeName = typeof(Handlers.GetInboxStatusHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.AuditProtectedPaths,
                AccessLevel     = OperationAccessLevel.Read,
                Category        = "Inbox / offline operation queue",
                Summary         = "Audits all protected workflow paths (.workflowkit JSON/JSONL, phases, logs, indexes, generated markdown) to verify no unauthorized direct writes. Reports the only allowed write target: .workflowkit/inbox/.",
                HandlerTypeName = typeof(Handlers.AuditProtectedPathsHandler).FullName
            },
            new()
            {
                OperationName   = WorkflowOperations.RepairConsistency,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Inbox / offline operation queue",
                Summary         = "Runs consistency repair: detects missing phase files, dangling phaseIds, stale docs/indexes, and failed inbox items. Performs rebuildable repairs (directory creation) without rewriting authoritative state.",
                HandlerTypeName = typeof(Handlers.RepairConsistencyHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName     = WorkflowOperations.RepairConsistency,
                        TargetDescription = "Creates missing directories and reports issues. Never rewrites authoritative JSON/JSONL records.",
                        AccessLevel       = OperationAccessLevel.Write,
                        IsAppendOnly      = true,
                        IsEventTracked    = false,
                        RequiredParameters = null,
                        SuitableActors    = new[] { "implementer", "workflowsystem" }
                    }
                ]
            },
            new()
            {
                OperationName   = WorkflowOperations.RepairAuthoritativeIntegrity,
                AccessLevel     = OperationAccessLevel.Write,
                Category        = "Inbox / offline operation queue",
                Summary         = "Repairs a narrow set of authoritative integrity issues through a handler-backed path: duplicate audit or review IDs, stale phase evidence references created by those duplicates, and missing legacy TEST-* aliases in ValidationLogIds.",
                HandlerTypeName = typeof(Handlers.RepairAuthoritativeIntegrityHandler).FullName,
                WriteTargets    =
                [
                    new()
                    {
                        OperationName      = WorkflowOperations.RepairAuthoritativeIntegrity,
                        TargetDescription  = "Rewrites only the affected authoritative audit/review JSONL files, updates impacted phase evidence references, and records a repair artifact under .workflowkit/evidence/.",
                        AccessLevel        = OperationAccessLevel.Write,
                        IsAppendOnly       = false,
                        IsEventTracked     = true,
                        RequiredParameters = null,
                        SuitableActors     = new[] { "implementer", "workflowsystem" }
                    }
                ]
            },
        ];
    }

    /// <summary>
    /// Finds a manifest entry by operation name. Returns null if not found.
    /// Used by validation and explain handlers.
    /// </summary>
    public static OperationManifestEntry? Find(string operationName)
    {
        return GetAll().FirstOrDefault(e =>
            string.Equals(e.OperationName, operationName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns all write-capable entries (Write, Append, Regenerate) with their write targets.
    /// Used by validation to report safe write options.
    /// </summary>
    public static IReadOnlyList<OperationManifestEntry> GetWriteCapableEntries()
    {
        return GetAll()
            .Where(e => e.AccessLevel != OperationAccessLevel.Read && e.WriteTargets is { Count: > 0 })
            .ToList();
    }

    /// <summary>
    /// Generates the full OperationManifest from the code-owned catalog.
    /// Two calls with the same code version produce identical operation lists.
    /// </summary>
    public static OperationManifest Generate()
    {
        return new OperationManifest
        {
            SchemaVersion = "1.0",
            GeneratedUtc  = DateTime.UtcNow.ToString("O"),
            Operations    = GetAll()
        };
    }

    /// <summary>
    /// Exports the manifest as a rebuildable JSON file under .workflowkit/index/operation-manifest.json.
    /// This file is a generated read model — not source of truth.
    /// </summary>
    public static void ExportToFile(string workflowRoot)
    {
        var manifest = Generate();
        var path = WorkflowLayout.OperationManifestPath(workflowRoot);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        File.WriteAllText(path, json);
    }
}
