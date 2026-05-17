using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Server;

namespace BekaForge.WorkflowKit.Mcp;

/// <summary>
/// Maps WorkflowKit operations to MCP tool definitions.
/// </summary>
public static class McpToolMapping
{
    public static IReadOnlyList<McpToolDefinition> GetAllTools()
    {
        return OperationManifestCatalog.GetAll()
            .Select(entry => new McpToolDefinition
            {
                Name = OperationNameToToolName(entry.OperationName),
                Description = entry.Summary,
                InputSchema = BuildInputSchema(entry)
            })
            .ToList();
    }

    /// <summary>
    /// MCP tool names use the canonical workflow operation names directly.
    /// This is reversible and stable.
    /// </summary>
    public static string OperationNameToToolName(string operationName) => operationName;

    /// <summary>
    /// Resolves an MCP tool name to a workflow operation name.
    /// Accepts canonical names and the earlier underscore-based legacy shape.
    /// </summary>
    public static string? ToolNameToOperation(string toolName)
    {
        var all = OperationManifestCatalog.GetAll();
        var exact = all.FirstOrDefault(entry =>
            string.Equals(entry.OperationName, toolName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.OperationName;

        static string Normalize(string value) =>
            new string(value.Where(ch => ch != '.' && ch != '_').ToArray()).ToLowerInvariant();

        var normalized = Normalize(toolName);
        var match = all.SingleOrDefault(entry => Normalize(entry.OperationName) == normalized);
        return match?.OperationName;
    }

    private static McpInputSchema BuildInputSchema(OperationManifestEntry entry)
    {
        var schema = new McpInputSchema
        {
            Type = "object",
            Properties = new Dictionary<string, McpProperty>(),
            Required = new List<string>()
        };

        schema.Properties["root"] = new McpProperty
        {
            Type = "string",
            Description = "Absolute WorkflowKit project root. Must contain .workflowkit/workflow.json."
        };
        schema.Properties["projectId"] = new McpProperty
        {
            Type = "string",
            Description = "Registered project ID from the MCP project registry."
        };
        schema.Properties["actor"] = new McpProperty
        {
            Type = "string",
            Description = "Optional workflow actor name for the operation."
        };

        AddOperationParameters(entry.OperationName, schema);

        if (schema.Required.Count == 0)
            schema.Required = null;

        return schema;
    }

    private static void AddOperationParameters(string operationName, McpInputSchema schema)
    {
        void Add(string name, string type, string description, IEnumerable<string>? enumValues = null)
        {
            schema.Properties[name] = new McpProperty
            {
                Type = type,
                Description = description,
                Enum = enumValues?.ToList()
            };
        }

        void Require(params string[] names)
        {
            foreach (var name in names)
            {
                if (!schema.Required!.Contains(name, StringComparer.Ordinal))
                    schema.Required.Add(name);
            }
        }

        if (operationName is WorkflowOperations.GetCurrentPhase or
            WorkflowOperations.GetPhaseContract or
            WorkflowOperations.StartPhase or
            WorkflowOperations.CompleteImplementation or
            WorkflowOperations.UpdatePhaseStatus or
            WorkflowOperations.AssignPhase or
            WorkflowOperations.RemovePhase or
            WorkflowOperations.UpdateSubPhaseStatus)
        {
            Add("phaseId", "string", "Phase identifier, for example PHASE-026.");
        }

        switch (operationName)
        {
            case var op when op == WorkflowOperations.CreatePhase:
                Add("phaseId", "string", "Optional explicit phase identifier.");
                Add("title", "string", "Phase title.");
                Add("summary", "string", "Phase summary.");
                Add("dependencies", "string", "Comma-separated or newline-separated dependency phase IDs.");
                Add("objective", "string", "Contract objective.");
                Add("scope", "string", "Contract scope.");
                Add("outOfScope", "string", "Contract out-of-scope notes.");
                Add("implementationNotes", "string", "Implementation notes.");
                Add("auditRequirements", "string", "Audit requirements.");
                Add("validationRequirements", "string", "Validation requirements.");
                Add("parallelizationNotes", "string", "Parallelization notes.");
                Add("architectureConstraints", "string", "Architecture constraints.");
                Add("requiredFilesOrAreas", "string", "Required files or areas.");
                Add("acceptanceCriteria", "string", "Acceptance criteria.");
                Add("dependsOnPhaseIds", "string", "Contract dependency phase IDs.");
                Add("requiresValidation", "boolean", "Whether this phase requires validation.");
                Add("subPhasesJson", "string", "Sub-phase JSON payload.");
                Require("title");
                break;

            case var op when op == WorkflowOperations.UpdatePhaseStatus:
                Add("state", "string", "Target phase state.", Enum.GetNames<Core.PhaseState>());
                Require("phaseId", "state");
                break;

            case var op when op == WorkflowOperations.AssignPhase:
                Add("agent", "string", "Assigned agent or workflow role.");
                Require("phaseId", "agent");
                break;

            case var op when op == WorkflowOperations.CreateImplementationLog ||
                             op == WorkflowOperations.CreateAuditLog ||
                             op == WorkflowOperations.CreateReviewLog ||
                             op == WorkflowOperations.CreateTestLog ||
                             op == WorkflowOperations.CreateFixLog:
                Add("phaseId", "string", "Phase identifier.");
                Add("summary", "string", "Log summary.");
                Add("notes", "string", "Detailed notes.");
                Add("passed", "boolean", "Optional pass/fail gate value.");
                Add("hasWarnings", "boolean", "Optional warning flag for test logs.");
                Add("requiresFix", "boolean", "Optional fix flag for review logs.");
                if (operationName is WorkflowOperations.CreateAuditLog or WorkflowOperations.CreateReviewLog)
                {
                    Add("issues", "string", "Concrete findings — semicolon-separated. Required for failed audits and for reviews that require fixes.");
                    Add("recommendations", "string", "Quality improvement recommendations — semicolon-separated. Use these for non-blocking improvements, alternatives, or simplifications.");
                }
                Require("phaseId", "summary");
                break;

            case var op when op == WorkflowOperations.RecordBlocker:
                Add("phaseId", "string", "Phase identifier.");
                Add("reason", "string", "Blocker reason.");
                Require("phaseId", "reason");
                break;

            case var op when op == WorkflowOperations.ResolveBlocker:
                Add("blockerId", "string", "Blocker identifier.");
                Add("resolution", "string", "Resolution notes.");
                Require("blockerId");
                break;

            case var op when op == WorkflowOperations.CreateHandoff:
                Add("phaseId", "string", "Phase identifier.");
                Add("summary", "string", "Handoff summary.");
                Add("task", "string", "Alias for summary.");
                Add("toActor", "string", "Target actor.");
                Add("operationHint", "string", "Suggested next operation.");
                Require("phaseId", "toActor");
                break;

            case var op when op == WorkflowOperations.RecordTimeSpent:
                Add("phaseId", "string", "Phase identifier.");
                Add("activity", "string", "Activity label.");
                Add("durationSeconds", "number", "Positive duration in seconds.");
                Add("notes", "string", "Optional notes.");
                Require("phaseId", "activity", "durationSeconds");
                break;

            case var op when op == WorkflowOperations.SetNextAction:
                Add("phaseId", "string", "Phase identifier.");
                Add("description", "string", "Next action description.");
                Add("operationHint", "string", "Suggested workflow operation.");
                Add("urgency", "string", "Optional urgency label.", ["Low", "Medium", "High", "Critical"]);
                Require("description", "actor");
                break;

            case var op when op == WorkflowOperations.UpdateSubPhaseStatus:
                Add("phaseId", "string", "Phase identifier.");
                Add("subPhaseId", "string", "Sub-phase identifier.");
                Add("status", "string", "Target sub-phase status.", Enum.GetNames<Core.SubPhaseStatus>());
                Require("phaseId", "subPhaseId", "status");
                break;

            case var op when op == WorkflowOperations.GetRelevantContext:
                Add("phaseId", "string", "Optional phase identifier.");
                Add("task", "string", "Task description for retrieval targeting.");
                Add("budgetMode", "string", "Optional budget mode.", Enum.GetNames<Core.BudgetMode>());
                Add("maxItems", "integer", "Maximum pointers to return.");
                Add("maxEstimatedTokens", "integer", "Approximate token cap.");
                break;

            case var op when op == WorkflowOperations.GetFileSlice:
                Add("filePath", "string", "Relative file path within the project.");
                Add("startLine", "integer", "Start line, 1-based.");
                Add("endLine", "integer", "End line, 1-based.");
                Require("filePath");
                break;

            case var op when op == WorkflowOperations.GetRecordSlice:
                Add("recordType", "string", "Record type.");
                Add("recordId", "string", "Record identifier.");
                Require("recordType", "recordId");
                break;

            case var op when op == WorkflowOperations.GetJsonPointerValue:
                Add("filePath", "string", "Relative JSON file path.");
                Add("pointer", "string", "JSON Pointer path.");
                Require("filePath", "pointer");
                break;

            case var op when op == WorkflowOperations.GetMarkdownRegion:
                Add("filePath", "string", "Relative markdown file path.");
                Add("regionName", "string", "Generated region name.");
                Require("filePath", "regionName");
                break;

            case var op when op == WorkflowOperations.GetFileHistory:
                Add("filePath", "string", "Relative file path.");
                Add("maxEntries", "integer", "Maximum history entries.");
                Require("filePath");
                break;

            case var op when op == WorkflowOperations.RecommendOperation:
            case var op2 when op2 == WorkflowOperations.SearchOperations:
                Add("task", "string", "Task or query text.");
                break;

            case var op when op == WorkflowOperations.ExplainOperation:
                Add("operation", "string", "Workflow operation name.");
                Require("operation");
                break;

            case var op when op == WorkflowOperations.SetBudgetConfig:
                Add("mode", "string", "Default budget mode.", Enum.GetNames<Core.BudgetMode>());
                Add("budgetMode", "string", "Alias for mode.", Enum.GetNames<Core.BudgetMode>());
                Add("modeOverrides", "string", "JSON object containing per-mode overrides.");
                break;

            case var op when op == WorkflowOperations.GetBudgetConfig ||
                             op == WorkflowOperations.GetBudgetReport:
                Add("mode", "string", "Optional budget mode.", Enum.GetNames<Core.BudgetMode>());
                Add("phaseId", "string", "Optional phase identifier.");
                break;

            case var op when op == WorkflowOperations.SetTraceOptions:
                Add("mode", "string", "Trace mode.", Enum.GetNames<Core.Tracing.TraceMode>());
                Add("enabled", "boolean", "Alias: true maps to Basic, false maps to Off.");
                Add("retentionDays", "integer", "Trace retention in days.");
                Add("maxDirectorySizeBytes", "integer", "Maximum trace directory size in bytes.");
                break;

            case var op when op == WorkflowOperations.ListTraces:
                Add("maxResults", "integer", "Maximum traces to list.");
                Add("phaseId", "string", "Optional phase identifier.");
                Add("operation", "string", "Optional operation-name filter.");
                Add("status", "string", "Optional trace status filter.");
                Add("actor", "string", "Optional actor filter.");
                break;

            case var op when op == WorkflowOperations.GetTrace:
                Add("traceId", "string", "Trace identifier.");
                Require("traceId");
                break;

            case var op when op == WorkflowOperations.ListSessions:
                Add("maxResults", "integer", "Maximum sessions to list.");
                Add("activeOnly", "boolean", "Return only active sessions.");
                break;

            case var op when op == WorkflowOperations.EndSession:
                Add("sessionId", "string", "Optional session identifier.");
                break;

            case var op when op == WorkflowOperations.ListGitCommits:
                Add("maxCount", "integer", "Maximum commits to list.");
                Add("since", "string", "Optional since date.");
                Add("branch", "string", "Optional branch filter.");
                Add("phaseId", "string", "Optional phase filter.");
                break;

            case var op when op == WorkflowOperations.GetGitActivity:
                Add("phaseId", "string", "Optional phase filter.");
                Add("sessionId", "string", "Optional session filter.");
                Add("activityType", "string", "Optional activity-type filter.");
                break;

            case var op when op == WorkflowOperations.ProcessInbox:
                Add("operation", "string", "Optional operation name to process.");
                break;

            case var op when op == WorkflowOperations.SyncMarkdown:
                Add("force", "boolean", "Accepted for compatibility; current implementation ignores it.");
                break;

            // PHASE-020: Validation operation parameter parity
            case var op when op == WorkflowOperations.CreateValidationLog:
                Add("phaseId",              "string",  "Phase identifier.");
                Add("summary",              "string",  "Validation summary.");
                Add("validationType",       "string",  "Validation type (AutomatedCommand, StaticInspection, BrowserManual, UnityManual, UnityAutomated, HumanValidationRequired, SkippedNotNeeded, SkippedNotPossible, SkippedByUserOverride, LegacyTest).");
                Add("validationResult",     "string",  "Validation result (Passed, PassedWithWarnings, Failed, Skipped, PendingHumanValidation).");
                Add("evidenceItems",        "string",  "Raw JSON evidence array: [{\"description\":\"...\",\"source\":0,\"reference\":\"...\"}]. Source enum: 0=Agent, 1=HumanOwner, 2=Command, 3=Tool, 4=CI.");
                Add("evidenceDescription",  "string",  "Human-readable evidence description (alternative to evidenceItems raw JSON).");
                Add("evidenceSource",       "string",  "Evidence source name — one of: Agent, HumanOwner, Command, Tool, CI (alternative to evidenceItems raw JSON).", ["Agent", "HumanOwner", "Command", "Tool", "CI"]);
                Add("evidenceReference",    "string",  "Optional file path or URL reference for the evidence (alternative to evidenceItems raw JSON).");
                Add("command",              "string",  "Command that was executed (for AutomatedCommand type).");
                Add("exitCode",             "integer", "Process exit code of the executed command.");
                Add("notes",                "string",  "Additional notes.");
                Add("skipReason",           "string",  "Reason the validation was skipped (required for Skipped* types).");
                Add("riskNote",             "string",  "Risk note for PassedWithWarnings or SkippedNotPossible.");
                Add("approvedBy",           "string",  "Actor who approved the skip (for SkippedByUserOverride).");
                Add("manualSteps",          "string",  "Manual test steps description.");
                Require("phaseId", "summary", "validationType", "validationResult");
                break;

            case var op when op == WorkflowOperations.CompleteUserValidation:
                Add("phaseId",              "string",  "Phase identifier.");
                Add("validationResult",     "string",  "Validation result (Passed, PassedWithWarnings, Failed).");
                Add("summary",              "string",  "Validation summary.");
                Add("evidenceItems",        "string",  "Raw JSON evidence array (same format as CreateValidationLog).");
                Add("evidenceDescription",  "string",  "Human-readable evidence description (alternative to evidenceItems).");
                Add("evidenceSource",       "string",  "Evidence source name (alternative to evidenceItems).", ["Agent", "HumanOwner", "Command", "Tool", "CI"]);
                Add("evidenceReference",    "string",  "Optional file path or URL reference for the evidence.");
                Add("notes",                "string",  "Additional notes.");
                Add("pendingValidationId",  "string",  "Optional ID of the pending validation record being completed.");
                Require("phaseId", "validationResult", "summary");
                break;

            case var op when op == WorkflowOperations.GetValidationPlan:
                Add("phaseId", "string", "Phase identifier.");
                Require("phaseId");
                break;

            case var op when op == WorkflowOperations.RequestUserValidation:
                Add("phaseId",      "string", "Phase identifier.");
                Add("manualSteps",  "string", "Optional manual test step instructions to present to the human.");
                Require("phaseId");
                break;

            case var op when op == WorkflowOperations.SkipValidation:
                Add("phaseId",    "string", "Phase identifier.");
                Add("reason",     "string", "Reason the validation is being skipped.");
                Add("approvedBy", "string", "Optional actor who approved the skip.");
                Require("phaseId", "reason");
                break;
        }
    }
}
