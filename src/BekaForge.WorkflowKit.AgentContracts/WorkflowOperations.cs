namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Canonical names for all WorkflowKit operations.
/// These string values are part of the external contract and must remain stable.
/// </summary>
public static class WorkflowOperations
{
    // Workflow state reads
    public const string GetState = "workflow.get_state";
    public const string GetCurrentPhase = "workflow.get_current_phase";
    public const string ListPhases = "workflow.list_phases";
    public const string ValidateState = "workflow.validate_state";
    public const string GetIntegrityReport = "workflow.get_integrity_report";
    public const string ValidateReleaseGate = "workflow.validate_release_gate";
    public const string GetReleaseCandidateReport = "workflow.get_release_candidate_report";
    public const string ValidatePublicRelease = "workflow.validate_public_release";
    public const string GetDashboardSummary = "workflow.get_dashboard_summary";
    public const string GetContextBundle = "workflow.get_context_bundle";

    // Persona policy reads
    public const string ListPersonas = "workflow.list_personas";
    public const string GetPersona = "workflow.get_persona";
    public const string RecommendPersona = "workflow.recommend_persona";
    public const string ValidatePersonaTask = "workflow.validate_persona_task";

    // Documentation ledger
    public const string CreateDocumentationRecord = "workflow.create_documentation_record";
    public const string GetDocumentationLedger = "workflow.get_documentation_ledger";
    public const string GetDocumentationDraft = "workflow.get_documentation_draft";
    public const string GetDocumentationCoverage = "workflow.get_documentation_coverage";

    // Phase management
    public const string CreatePhase = "workflow.create_phase";
    public const string UpdatePhase = "workflow.update_phase";
    public const string RemovePhase = "workflow.remove_phase";
    public const string AssignPhase = "workflow.assign_phase";
    public const string StartPhase = "workflow.start_phase";
    public const string CompleteImplementation = "workflow.complete_implementation";
    public const string UpdatePhaseStatus = "workflow.update_phase_status";
    public const string DeferPhase = "workflow.defer_phase";
    public const string FocusPhase = "workflow.focus_phase";

    // Phase recovery (PHASE-008)
    public const string ReopenPhase = "workflow.reopen_phase";

    // Phase contract
    public const string GetPhaseContract = "workflow.get_phase_contract";
    public const string SavePhaseContract = "workflow.save_phase_contract";

    // Next action
    public const string GetNextAction = "workflow.get_next_action";
    public const string SetNextAction = "workflow.set_next_action";

    // Project guidance docs
    public const string GetProjectGuidance = "workflow.get_project_guidance";
    public const string SetProjectGuidance = "workflow.set_project_guidance";

    // Record creation
    public const string CreateImplementationLog = "workflow.create_implementation_log";
    public const string CreateAuditLog = "workflow.create_audit_log";
    public const string CreateReviewLog = "workflow.create_review_log";
    public const string CreateTestLog = "workflow.create_test_log";           // Legacy
    public const string CreateValidationLog = "workflow.create_validation_log"; // Preferred
    public const string CreateFixLog = "workflow.create_fix_log";

    // Validation commands
    public const string GetValidationPlan = "workflow.get_validation_plan";
    public const string RequestUserValidation = "workflow.request_user_validation";
    public const string CompleteUserValidation = "workflow.complete_user_validation";
    public const string SkipValidation = "workflow.skip_validation";

    // Orchestration runtime
    public const string StartOrchestrationSession = "workflow.orchestration.start_session";
    public const string AdvanceOrchestrationSession = "workflow.orchestration.advance_session";
    public const string PauseOrchestrationSession = "workflow.orchestration.pause_session";
    public const string CancelOrchestrationSession = "workflow.orchestration.cancel_session";
    public const string FocusOrchestrationSession = "workflow.orchestration.focus_session";
    public const string CreateOrchestrationRun = "workflow.orchestration.create_run";
    public const string StartOrchestrationRun = "workflow.orchestration.start_run";
    public const string ReportOrchestrationRun = "workflow.orchestration.report_run";
    public const string AcceptOrchestrationRun = "workflow.orchestration.accept_run";
    public const string RejectOrchestrationRun = "workflow.orchestration.reject_run";
    public const string RecordOrchestrationGateDecision = "workflow.orchestration.record_gate_decision";
    public const string SetOrchestrationAttentionFlags = "workflow.orchestration.set_attention_flags";
    public const string ClearOrchestrationAttentionFlags = "workflow.orchestration.clear_attention_flags";
    public const string RequestOrchestrationHumanAttention = "workflow.orchestration.request_human_attention";
    public const string GetOrchestrationStatus = "workflow.orchestration.get_status";
    public const string GetOrchestrationAttentionStatus = "workflow.orchestration.get_attention_status";
    public const string ListOrchestrationSessions = "workflow.orchestration.list_sessions";
    public const string ListOrchestrationRuns = "workflow.orchestration.list_runs";
    public const string ListOrchestrationGateDecisions = "workflow.orchestration.list_gate_decisions";

    // Blockers
    public const string RecordBlocker = "workflow.record_blocker";
    public const string ResolveBlocker = "workflow.resolve_blocker";

    // Handoffs
    public const string CreateHandoff = "workflow.create_handoff";
    public const string GetHandoffs = "workflow.get_handoffs";

    // Timing
    public const string RecordTimeSpent = "workflow.record_time_spent";

    // Markdown sync
    public const string SyncMarkdown = "workflow.sync_markdown";

    // ChangeSet import/apply
    public const string ValidateChangeSet = "workflow.validate_changeset";
    public const string ApplyChangeSet = "workflow.apply_changeset";

    // Operation manifest
    public const string GetOperationManifest = "workflow.get_operation_manifest";

    // Tool routing
    public const string SearchOperations = "workflow.search_operations";
    public const string RecommendOperation = "workflow.recommend_operation";
    public const string ExplainOperation = "workflow.explain_operation";

    // Context index
    public const string RebuildContextIndex = "workflow.rebuild_context_index";

    // Slice APIs
    public const string GetFileSlice = "workflow.get_file_slice";
    public const string GetRecordSlice = "workflow.get_record_slice";
    public const string GetJsonPointerValue = "workflow.get_json_pointer_value";
    public const string GetMarkdownRegion = "workflow.get_markdown_region";
    public const string GetFileHistory = "workflow.get_file_history";

    // Relevant context
    public const string GetRelevantContext = "workflow.get_relevant_context";

    // Safety validation
    public const string ValidateOperationRequest = "workflow.validate_operation_request";

    // Cache
    public const string GetCacheStatus = "workflow.get_cache_status";
    public const string ClearContextCache = "workflow.clear_context_cache";

    // Tracing
    public const string GetTraceStatus = "workflow.get_trace_status";
    public const string ListTraces = "workflow.list_traces";
    public const string GetTrace = "workflow.get_trace";
    public const string ClearOldTraces = "workflow.clear_old_traces";
    public const string SetTraceOptions = "workflow.set_trace_options";

    // Sub-phase management
    public const string UpdateSubPhaseStatus = "workflow.update_sub_phase_status";

    // Git activity
    public const string GetGitStatus = "workflow.get_git_status";
    public const string ListGitCommits = "workflow.list_git_commits";
    public const string GetGitActivity = "workflow.get_git_activity";
    public const string GetGitBranchInfo = "workflow.get_git_branch_info";
    public const string GetGitHealth = "workflow.get_git_health";
    public const string RecordGitActivity = "workflow.record_git_activity";
    public const string GetTimeline = "workflow.get_timeline";

    // Budget configuration
    public const string GetBudgetConfig = "workflow.get_budget_config";
    public const string SetBudgetConfig = "workflow.set_budget_config";
    public const string GetBudgetReport = "workflow.get_budget_report";

    // Sessions
    public const string ListSessions = "workflow.list_sessions";
    public const string GetCurrentSession = "workflow.get_current_session";
    public const string EndSession = "workflow.end_session";

    // Inbox / offline operation queue
    public const string ProcessInbox = "workflow.process_inbox";
    public const string GetInboxStatus = "workflow.get_inbox_status";
    public const string AuditProtectedPaths = "workflow.audit_protected_paths";
    public const string RepairConsistency = "workflow.repair_consistency";
    public const string RepairAuthoritativeIntegrity = "workflow.repair_authoritative_integrity";
}
