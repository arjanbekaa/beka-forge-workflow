using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Core.Git;
using BekaForge.WorkflowKit.Core.Tracing;
using BekaForge.WorkflowKit.Server.Handlers;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// Protocol-neutral operation dispatcher.
///
/// Maps operation names (workflow.*) to their handler implementations.
/// HTTP endpoints, the CLI, and future MCP adapters all call this dispatcher.
/// No workflow logic lives here — logic is in Core; state is in Storage.
///
/// Every call goes through: dispatcher → handler → Core validation + Storage write.
/// </summary>
public sealed class OperationDispatcher
{
    private readonly IReadOnlyDictionary<string, IOperationHandler> _handlers;
    private readonly WorkflowStore _store;
    private readonly ContextPackageCache? _cache;
    private readonly TraceStore? _traceStore;
    private readonly WorkflowTraceService? _traceService;

    public OperationDispatcher(WorkflowStore store, ContextPackageCache? cache = null,
        WorkflowTraceService? traceService = null)
    {
        _store = store;
        _cache = cache ?? CreateDefaultCache(store);
        if (traceService is not null)
        {
            _traceService = traceService;
            _traceStore = traceService.Store as TraceStore
                ?? new TraceStore(store.WorkflowRoot, traceService.Options);
        }
        else
        {
            _traceStore = new TraceStore(store.WorkflowRoot);
            _traceService = new WorkflowTraceService(_traceStore);
        }

        _handlers = BuildHandlers(store, _cache, _traceStore, _traceService);
    }

    /// <summary>
    /// Dispatches the operation in the given context and returns the result.
    /// Never throws — all errors are returned as OperationResult.Fail.
    /// </summary>
    public OperationResult Dispatch(OperationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Operation))
            return OperationResult.Fail("InvalidOperation", "Operation name must not be empty.");

        if (!_handlers.TryGetValue(context.Operation, out var handler))
            return OperationResult.Fail("UnknownOperation",
                $"No handler registered for operation '{context.Operation}'. " +
                $"See WorkflowOperations for valid operation names.");

        // -- Trace wrapping --------------------------------------------------
        WorkflowTraceScope? trace = null;
        if (_traceService is not null && _traceService.IsEnabled)
        {
            trace = _traceService.StartOperation(
                context.Operation,
                phaseId: context.PhaseId,
                actor: context.Actor,
                requestSummary: SummarizeRequest(context));

            // Attach trace scope to context so handlers can add spans
            context = context with { TraceScope = trace };
        }

        try
        {
            var result = handler.Execute(context);

            if (trace is not null)
            {
                if (result.Success)
                    trace.MarkSuccess();
                else
                    trace.MarkFailed(result.ErrorCode, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            trace?.MarkFailed("InternalError", ex.Message);
            return OperationResult.Fail("InternalError",
                $"Unhandled exception in '{context.Operation}': {ex.Message}");
        }
    }

    /// <summary>Returns all registered operation names. Useful for health/capability checks.</summary>
    public IReadOnlyCollection<string> RegisteredOperations => _handlers.Keys.ToArray();

    private static string SummarizeRequest(OperationContext context)
    {
        var parts = new List<string>();
        if (context.PhaseId is not null)
            parts.Add($"phase={context.PhaseId}");
        var summary = context.GetString("summary");
        if (summary is not null)
            parts.Add(summary.Length > 80 ? summary[..80] : summary);
        var title = context.GetString("title");
        if (title is not null)
            parts.Add(title.Length > 80 ? title[..80] : title);
        var taskType = context.GetString("taskType");
        if (taskType is not null)
            parts.Add($"task={taskType}");
        return parts.Count > 0 ? string.Join(" | ", parts) : context.Operation;
    }

    private IReadOnlyDictionary<string, IOperationHandler> BuildHandlers(WorkflowStore store,
        ContextPackageCache? cache, TraceStore? traceStore, WorkflowTraceService? traceService)
    {
        var handlers = new List<IOperationHandler>
        {
            // -- Workflow state reads -----------------------------------------------
            new GetStateHandler(store),
            new GetCurrentPhaseHandler(store),
            new ListPhasesHandler(store),
            new ValidateStateHandler(store),
            new GetIntegrityReportHandler(store),
            new ValidateReleaseGateHandler(store),
            new GetReleaseCandidateReportHandler(store),
            new ValidatePublicReleaseHandler(store),
            new GetDashboardSummaryHandler(store),
            new GetContextBundleHandler(store),
            new ListPersonasHandler(store),
            new GetPersonaHandler(store),
            new RecommendPersonaHandler(store),
            new ValidatePersonaTaskHandler(store),
            new CreateDocumentationRecordHandler(store),
            new GetDocumentationLedgerHandler(store),
            new GetDocumentationDraftHandler(store),
            new GetDocumentationCoverageHandler(store),

            // -- Phase management --------------------------------------------------
            new CreatePhaseHandler(store),
            new UpdatePhaseHandler(store),
            new RemovePhaseHandler(store),
            new ReopenPhaseHandler(store),
            new UpdatePhaseStatusHandler(store),
            new DeferPhaseHandler(store),
            new FocusPhaseHandler(store),
            new AssignPhaseHandler(store),
            new StartPhaseHandler(store),
            new CompleteImplementationHandler(store),

            // -- Phase contract ----------------------------------------------------
            new GetPhaseContractHandler(store),
            new SavePhaseContractHandler(store),

            // -- Next action -------------------------------------------------------
            new GetNextActionHandler(store),
            new SetNextActionHandler(store),
            new GetProjectGuidanceHandler(store),
            new SetProjectGuidanceHandler(store),

            // -- Record creation ---------------------------------------------------
            new CreateImplementationLogHandler(store),
            new CreateAuditLogHandler(store),
            new CreateReviewLogHandler(store),
            new CreateTestLogHandler(store),
            new CreateValidationLogHandler(store),
            new CreateFixLogHandler(store),

            // -- Validation --------------------------------------------------------
            new GetValidationPlanHandler(store),
            new RequestUserValidationHandler(store),
            new CompleteUserValidationHandler(store),
            new SkipValidationHandler(store),

            // -- Orchestration runtime --------------------------------------------
            new StartOrchestrationSessionHandler(store),
            new AdvanceOrchestrationSessionHandler(store),
            new PauseOrchestrationSessionHandler(store),
            new CancelOrchestrationSessionHandler(store),
            new FocusOrchestrationSessionHandler(store),
            new CreateOrchestrationRunHandler(store),
            new StartOrchestrationRunHandler(store),
            new ReportOrchestrationRunHandler(store),
            new AcceptOrchestrationRunHandler(store),
            new RejectOrchestrationRunHandler(store),
            new RecordOrchestrationGateDecisionHandler(store),
            new SetOrchestrationAttentionFlagsHandler(store),
            new ClearOrchestrationAttentionFlagsHandler(store),
            new RequestOrchestrationHumanAttentionHandler(store),
            new GetOrchestrationStatusHandler(store),
            new GetOrchestrationAttentionStatusHandler(store),
            new ListOrchestrationSessionsHandler(store),
            new ListOrchestrationRunsHandler(store),
            new ListOrchestrationGateDecisionsHandler(store),

            // -- Blockers ----------------------------------------------------------
            new RecordBlockerHandler(store),
            new ResolveBlockerHandler(store),

            // -- Handoffs ----------------------------------------------------------
            new CreateHandoffHandler(store),
            new GetHandoffsHandler(store),

            // -- Timing ------------------------------------------------------------
            new RecordTimeSpentHandler(store),

            // -- Markdown sync -----------------------------------------------------
            new SyncMarkdownHandler(store),

            // -- ChangeSet import/apply --------------------------------------------
            new ValidateChangeSetHandler(store),
            new ApplyChangeSetHandler(store),

            // -- Operation manifest -------------------------------------------------
            new GetOperationManifestHandler(store),

            // -- Tool routing ------------------------------------------------------
            new SearchOperationsHandler(store),
            new RecommendOperationHandler(store),
            new ExplainOperationHandler(store),

            // -- Context index -----------------------------------------------------
            new RebuildContextIndexHandler(store),

            // -- Slice APIs --------------------------------------------------------
            new GetFileSliceHandler(store),
            new GetRecordSliceHandler(store),
            new GetJsonPointerValueHandler(store),
            new GetMarkdownRegionHandler(store),
            new GetFileHistoryHandler(store),

            // -- Relevant context --------------------------------------------------
            new GetRelevantContextHandler(store, cache),

            // -- Safety validation -------------------------------------------------
            new ValidateOperationRequestHandler(store),
        };

        // -- Sub-phase management -------------------------------------------------
        handlers.Add(new UpdateSubPhaseStatusHandler(store));

        // -- Git activity handlers ------------------------------------------------
        var gitService = new GitService(store.WorkflowRoot);
        var gitStore = new GitStore(store.WorkflowRoot);
        handlers.Add(new GetGitStatusHandler(gitStore, gitService));
        handlers.Add(new ListGitCommitsHandler(gitService));
        handlers.Add(new GetGitActivityHandler(gitStore));
        handlers.Add(new GetGitBranchInfoHandler(gitService));
        handlers.Add(new GetGitHealthHandler(gitService, store));
        handlers.Add(new RecordGitActivityHandler(gitService, gitStore));
        handlers.Add(new TimelineHandler(store, gitStore));

        // -- Session handlers ----------------------------------------------------
        var identity = SessionIdentity.FromGitConfig(store.WorkflowRoot);
        handlers.Add(new ListSessionsHandler(gitStore));
        handlers.Add(new GetCurrentSessionHandler(gitStore, identity));
        handlers.Add(new EndSessionHandler(gitStore));

        // -- Budget configuration ------------------------------------------------
        handlers.Add(new GetBudgetConfigHandler(store));
        handlers.Add(new SetBudgetConfigHandler(store));
        handlers.Add(new GetBudgetReportHandler(store));

        // -- Inbox / offline operation queue -------------------------------------
        handlers.Add(new ProcessInboxHandler(store.WorkflowRoot, this));
        handlers.Add(new GetInboxStatusHandler(store.WorkflowRoot));
        handlers.Add(new AuditProtectedPathsHandler(store.WorkflowRoot));
        handlers.Add(new RepairConsistencyHandler(store.WorkflowRoot));
        handlers.Add(new RepairAuthoritativeIntegrityHandler(store));

        // -- Cache operations — only when cache is available ---------------------
        if (cache is not null)
        {
            handlers.Add(new GetCacheStatusHandler(cache));
            handlers.Add(new ClearContextCacheHandler(cache));
        }

        // -- Trace operations — register when trace service is available ---------
        if (traceService is not null && traceStore is not null)
        {
            handlers.Add(new GetTraceStatusHandler(traceStore, traceService));
            handlers.Add(new ListTracesHandler(traceStore));
            handlers.Add(new GetTraceHandler(traceStore));
            handlers.Add(new ClearOldTracesHandler(traceStore));
            handlers.Add(new SetTraceOptionsHandler(traceStore));
        }

        return handlers.ToDictionary(h => h.OperationName, StringComparer.OrdinalIgnoreCase);
    }

    private static ContextPackageCache CreateDefaultCache(WorkflowStore store)
    {
        var settingsPath = Path.Combine(store.WorkflowRoot, ".workflowkit", "cache-settings.json");
        return new ContextPackageCache(CacheSettings.Load(settingsPath));
    }
}
