using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdManifest(string? root, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.GetOperationManifest
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        var manifest = (OperationManifest)result.Data!;
        var rows = manifest.Operations
            .Select(o => (o.OperationName, o.AccessLevel.ToString(), o.Category))
            .ToList();

        CliRenderer.RenderManifestTable(rows, manifest.Operations.Count, mode);

        if (mode != CliOutputMode.Rich)
        {
            Console.WriteLine();
            Console.WriteLine($"Summary: {manifest.Operations.Count(o => o.AccessLevel == OperationAccessLevel.Read)} read, " +
                              $"{manifest.Operations.Count(o => o.AccessLevel == OperationAccessLevel.Append)} append, " +
                              $"{manifest.Operations.Count(o => o.AccessLevel == OperationAccessLevel.Write)} write, " +
                              $"{manifest.Operations.Count(o => o.AccessLevel == OperationAccessLevel.Regenerate)} regenerate");
        }
    }

    internal static void CmdRecommend(string? root, string? taskText, string? phaseId, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }
        if (string.IsNullOrWhiteSpace(taskText)) { Console.Error.WriteLine("ERROR: --task is required. Example: bfwf recommend --task \"log implementation\""); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);

        var parameters = new Dictionary<string, object?> { ["task"] = taskText };
        if (!string.IsNullOrWhiteSpace(phaseId))
            parameters["phaseId"] = phaseId;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.RecommendOperation,
            PhaseId = phaseId,
            Parameters = parameters
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        var jsonOutput = JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Recommendations for: \"{taskText}\"");
        if (!string.IsNullOrWhiteSpace(phaseId)) Console.WriteLine($"  Phase: {phaseId}");
        Console.WriteLine();
        Console.WriteLine(jsonOutput);
    }

    internal static void CmdContext(string? root, string? phaseId, bool json, CliOutputMode mode = CliOutputMode.Plain, string? budgetMode = null)
    {
        if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.GetRelevantContext,
            PhaseId = phaseId,
            Parameters = new Dictionary<string, object?>
            {
                ["budgetMode"] = budgetMode ?? ""
            }
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Data is RelevantContextResult context)
            CliRenderer.RenderContext(context, mode);
        else
            Console.WriteLine(JsonSerializer.Serialize(result.Data, CreatePrettyJsonOptions()));
    }

    internal static void CmdValidateRequest(string? root, string? targetOperation, string? phaseId, string? actorName, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }
        if (string.IsNullOrWhiteSpace(targetOperation)) { Console.Error.WriteLine("ERROR: --operation is required. Example: bfwf validate-request --operation workflow.create_implementation_log --phase PHASE-001"); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);

        var actor = !string.IsNullOrWhiteSpace(actorName) ? ParseActor(actorName) : WorkflowActor.WorkflowKit;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateOperationRequest,
            PhaseId = phaseId,
            Actor = actor,
            Parameters = new Dictionary<string, object?> { ["targetOperation"] = targetOperation }
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        var jsonOutput = JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Validation for: {targetOperation}");
        if (!string.IsNullOrWhiteSpace(phaseId)) Console.WriteLine($"  Phase: {phaseId}");
        if (!string.IsNullOrWhiteSpace(actorName)) Console.WriteLine($"  Actor: {actorName}");
        Console.WriteLine();
        Console.WriteLine(jsonOutput);
    }

    internal static void CmdBudget(string? root, string? budgetMode, string? modeOverrides, bool json, CliOutputMode mode = CliOutputMode.Plain)
    {
        if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }

        var store = new WorkflowStore(root);
        var dispatcher = new OperationDispatcher(store);
        var operation = string.IsNullOrWhiteSpace(budgetMode) && string.IsNullOrWhiteSpace(modeOverrides)
            ? WorkflowOperations.GetBudgetConfig
            : WorkflowOperations.SetBudgetConfig;
        var parameters = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(budgetMode))
            parameters["mode"] = budgetMode;
        if (!string.IsNullOrWhiteSpace(modeOverrides))
            parameters["modeOverrides"] = modeOverrides;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.User,
            Parameters = parameters
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? "Unknown error", mode);
            Environment.Exit(1);
        }

        if (json)
        {
            WriteJson(result);
            return;
        }

        if (result.Data is BudgetConfigResult budget)
        {
            var action = operation == WorkflowOperations.SetBudgetConfig ? "Updated" : "Budget Configuration";
            CliRenderer.Ok($"{action} - Mode: {budget.Mode} ({budget.Source})", mode);
            Console.WriteLine();

            if (budget.Profile is not null)
            {
                Console.WriteLine($"  Max Pointers:       {budget.Profile.MaxPointers}");
                Console.WriteLine($"  Max Log Records:    {budget.Profile.MaxLogRecords}");
                Console.WriteLine($"  Max Summary Length: {budget.Profile.MaxSummaryLength}");
                Console.WriteLine($"  Include Markdown:   {budget.Profile.IncludeMarkdown}");
                Console.WriteLine($"  Include Traces:     {budget.Profile.IncludeTraces}");
                Console.WriteLine($"  Include Inline:     {budget.Profile.IncludeInlineContent}");
                Console.WriteLine($"  Token Budget Cap:   {(budget.Profile.MaxEstimatedTokens > 0 ? budget.Profile.MaxEstimatedTokens.ToString() : "unlimited")}");
                Console.WriteLine($"  Priority:           {budget.Profile.Priority}");
                Console.WriteLine($"  Description:        {budget.Profile.Description}");
            }

            if (budget.Warnings.Count > 0)
            {
                Console.WriteLine();
                foreach (var warning in budget.Warnings)
                    Console.WriteLine($"  - {warning}");
            }

            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(result.Data, CreatePrettyJsonOptions()));
        if (result.Data is not null) return;

        var config = BudgetConfig.Load(BudgetConfig.ConfigPath(root));

        CliRenderer.Ok($"Budget Configuration - Default Mode: {config.DefaultMode}", mode);
        Console.WriteLine();

        foreach (BudgetMode m in Enum.GetValues<BudgetMode>())
        {
            var profile = config.EffectiveProfile(m);
            var isDefault = m == config.DefaultMode;
            var marker = isDefault ? " [DEFAULT]" : "";
            Console.WriteLine($"  {m}{marker}:");
            Console.WriteLine($"    Max Pointers:       {profile.MaxPointers}");
            Console.WriteLine($"    Max Log Records:    {profile.MaxLogRecords}");
            Console.WriteLine($"    Max Summary Length: {profile.MaxSummaryLength}");
            Console.WriteLine($"    Include Markdown:   {profile.IncludeMarkdown}");
            Console.WriteLine($"    Include Traces:     {profile.IncludeTraces}");
            Console.WriteLine($"    Include Inline:     {profile.IncludeInlineContent}");
            Console.WriteLine($"    Token Budget Cap:   {(profile.MaxEstimatedTokens > 0 ? profile.MaxEstimatedTokens.ToString() : "unlimited")}");
            Console.WriteLine($"    Priority:           {profile.Priority}");
            Console.WriteLine($"    Description:        {profile.Description}");
            Console.WriteLine();
        }

        Console.WriteLine("Use --budget <mode> with 'bfwf context' to override the default mode.");
    }

    internal static void CmdContextInject(string? wfRoot, string? phaseId, string? roleArg, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        if (string.IsNullOrWhiteSpace(phaseId))
        {
            CliRenderer.Error("--phase is required.", mode);
            Environment.Exit(1);
        }

        if (string.IsNullOrWhiteSpace(roleArg))
        {
            CliRenderer.Error("--role is required. Valid: Planner, Implementer, Auditor, Reviewer, Validator.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var phase = store.LoadPhase(phaseId);
        if (phase is null) { CliRenderer.Error($"Phase '{phaseId}' not found.", mode); Environment.Exit(1); }

        var allPhases = store.LoadAllPhases();
        var events = store.ReadAllEvents();
        var validations = store.ReadAllValidations();

        var markdown = ContextInjector.GenerateMarkdown(phase, roleArg, allPhases, events, validations, DateTimeOffset.UtcNow);

        if (mode == CliOutputMode.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                phaseId = phaseId,
                role = roleArg,
                content = markdown,
                generatedUtc = DateTimeOffset.UtcNow.ToString("O")
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        else
        {
            Console.WriteLine(markdown);
        }
    }

    internal static void CmdRulesGenerate(string? wfRoot, string? phaseId, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var phases = store.LoadAllPhases();
        var workflow = store.LoadWorkflow();
        var now = DateTimeOffset.UtcNow;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Workflow Rules Summary");
        sb.AppendLine($"_Auto-generated by `bfwf rules generate` at {now:yyyy-MM-dd HH:mm} UTC_");
        sb.AppendLine();
        sb.AppendLine("## Read This First");
        sb.AppendLine();
        sb.AppendLine("- Canonical workflow rules live in `.workflowkit/workflow/Rules.md`.");
        sb.AppendLine("- Read that file before answering, editing files, running `bfwf`, or calling workflow tools.");
        sb.AppendLine("- Treat this file as a quick project summary only. If it ever differs from the canonical rules, follow `.workflowkit/workflow/Rules.md`.");
        sb.AppendLine();
        sb.AppendLine("## Current Workflow State");
        sb.AppendLine();
        sb.AppendLine($"- **Current phase:** {workflow.CurrentPhaseId ?? "(not set)"}");
        sb.AppendLine($"- **Total phases:** {phases.Count}");
        sb.AppendLine($"- **Passed phases:** {phases.Count(p => p.State is PhaseState.Pass or PhaseState.PassWithWarnings)}");
        sb.AppendLine($"- **Active phases:** {phases.Count(p => !PhaseTransitionValidator.IsTerminal(p.State) && p.State != PhaseState.Planned)}");
        sb.AppendLine($"- **Open blockers:** {workflow.OpenBlockerCount}");

        var activePhasesForRules = phases
            .Where(p => !PhaseTransitionValidator.IsTerminal(p.State) && p.State != PhaseState.Planned)
            .OrderBy(p => p.PhaseNumber)
            .ToList();

        sb.AppendLine();
        sb.AppendLine("## Active Coordination");
        sb.AppendLine();

        if (activePhasesForRules.Count == 0)
        {
            sb.AppendLine("_No active phases._");
            sb.AppendLine();
        }
        else
        {
            foreach (var phase in activePhasesForRules)
            {
                sb.AppendLine($"### {phase.PhaseId} - {phase.Title}");
                sb.AppendLine($"- **State:** {phase.State}");
                if (phase.Dependencies.Count > 0)
                    sb.AppendLine($"- **Dependencies:** {string.Join(", ", phase.Dependencies.Select(id => $"`{id}`"))}");
                if (phase.Contract?.DependsOnPhaseIds.Count > 0)
                    sb.AppendLine($"- **Contract order:** {string.Join(", ", phase.Contract.DependsOnPhaseIds.Select(id => $"`{id}`"))}");
                if (phase.Contract?.RequiredFilesOrAreas.Count > 0)
                    sb.AppendLine($"- **Write areas:** {string.Join(", ", phase.Contract.RequiredFilesOrAreas.Select(area => $"`{area}`"))}");
                if (phase.Contract?.ExecutionLanes.Count > 0)
                    sb.AppendLine($"- **Execution lanes:** {string.Join(", ", phase.Contract.ExecutionLanes.Select(lane => $"`{lane.LaneId}` {lane.Title}"))}");
                if (!string.IsNullOrWhiteSpace(phase.Contract?.ParallelizationNotes))
                    sb.AppendLine($"- **Parallelization notes:** {phase.Contract.ParallelizationNotes}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Quick Reminders");
        sb.AppendLine();
        sb.AppendLine("1. Use `bfwf` or the workflow HTTP API for every `.workflowkit/` write.");
        sb.AppendLine("2. Log implementation, audit, review, and validation honestly with real evidence.");
        sb.AppendLine("3. Check active write areas and dependencies before doing parallel work.");

        var outputPath = Path.Combine(wfRoot, "WORKFLOW_RULES.md");
        File.WriteAllText(outputPath, sb.ToString(), new System.Text.UTF8Encoding(false));
        CliRenderer.Ok($"Rules generated: {outputPath}", mode);
    }
}
