using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Server.Handlers;
using BekaForge.WorkflowKit.Storage;
using BekaForge.WorkflowKit.Core.Tracing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var commandLineArgs = args;
var command = commandLineArgs.Length > 0 ? commandLineArgs[0].ToLowerInvariant() : "help";

// Parse common flags
string? root = ParseFlag(commandLineArgs, "--root");
bool force = HasFlag(commandLineArgs, "--force");
int? port = ParseIntFlag(commandLineArgs, "--port");
string? phase = ParseFlag(commandLineArgs, "--phase");
string? title = ParseFlag(commandLineArgs, "--title");
string? summary = ParseFlag(commandLineArgs, "--summary");
string? notes = ParseFlag(commandLineArgs, "--notes");
string? state = ParseFlag(commandLineArgs, "--state");
string? dependencies = ParseFlag(commandLineArgs, "--dependencies");
string? objective = ParseFlag(commandLineArgs, "--objective");
string? scope = ParseFlag(commandLineArgs, "--scope");
string? outOfScope = ParseFlag(commandLineArgs, "--out-of-scope");
string? implementationNotes = ParseFlag(commandLineArgs, "--implementation-notes");
string? auditRequirements = ParseFlag(commandLineArgs, "--audit-requirements");
string? unityTestRequirements = ParseFlag(commandLineArgs, "--unity-test-requirements");
string? parallelizationNotes = ParseFlag(commandLineArgs, "--parallelization-notes");
string? architectureConstraints = ParseFlag(commandLineArgs, "--architecture-constraints");
string? requiredFilesOrAreas = ParseFlag(commandLineArgs, "--required-files-or-areas");
string? acceptanceCriteria = ParseFlag(commandLineArgs, "--acceptance-criteria");
string? dependsOnPhaseIds = ParseFlag(commandLineArgs, "--depends-on-phase-ids");
string? requiresUnityTest = ParseFlag(commandLineArgs, "--requires-unity-test");
string? subPhaseId = ParseFlag(commandLineArgs, "--sub-phase");
string? subPhaseSummary = ParseFlag(commandLineArgs, "--sub-summary");
string? subPhaseDependencies = ParseFlag(commandLineArgs, "--sub-dependencies");
string? subPhasesJson = ParseFlag(commandLineArgs, "--sub-phases-json");
string? budgetMode = ParseFlag(commandLineArgs, "--budget");
string? modeOverrides = ParseFlag(commandLineArgs, "--mode-overrides");
string? passed = ParseFlag(commandLineArgs, "--passed");
string? agent = ParseFlag(commandLineArgs, "--agent");
string? reason = ParseFlag(commandLineArgs, "--blocker-reason") ?? ParseFlag(commandLineArgs, "--reason");
string? blockerId = ParseFlag(commandLineArgs, "--blocker-id");
string? resolution = ParseFlag(commandLineArgs, "--resolution");
string? toActor = ParseFlag(commandLineArgs, "--to-actor");
string? task = ParseFlag(commandLineArgs, "--task");
string? operation = ParseFlag(commandLineArgs, "--operation");
string? targetActor = ParseFlag(commandLineArgs, "--actor");
bool jsonOutput = HasFlag(commandLineArgs, "--json");
bool plainOutput = HasFlag(commandLineArgs, "--plain");
bool watchOutput = HasFlag(commandLineArgs, "--watch");
int watchIntervalSeconds = Math.Max(ParseIntFlag(commandLineArgs, "--interval") ?? 5, 1);
var outputMode = CliRenderer.Resolve(jsonOutput, plainOutput);

// Discover root
string startDir = root ?? Directory.GetCurrentDirectory();
string? workflowRoot = DiscoverWorkflowRoot(startDir);

// â”€â”€ Command dispatch â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

switch (command)
{
    case "init":
        CmdInit(startDir, commandLineArgs.Length > 1 ? commandLineArgs[1] : "", force);
        break;

    case "server":
        CmdServer(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", startDir, port ?? 0);
        break;

    case "status":
        CmdStatus(workflowRoot, jsonOutput, outputMode, watchOutput, watchIntervalSeconds);
        break;

    case "validate":
        CmdValidate(workflowRoot, jsonOutput);
        break;

    case "sync-markdown":
        CmdSyncMarkdown(workflowRoot);
        break;

    case "log":
        CmdLog(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, summary, notes, passed);
        break;

    case "phase":
        CmdPhase(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, title, state, reason, agent, summary,
            dependencies, objective, scope, outOfScope, implementationNotes, auditRequirements, unityTestRequirements,
            parallelizationNotes, architectureConstraints, requiredFilesOrAreas, acceptanceCriteria, dependsOnPhaseIds,
            requiresUnityTest, subPhaseId, subPhaseSummary, subPhaseDependencies, subPhasesJson,
            jsonOutput, outputMode, watchOutput, watchIntervalSeconds);
        break;

    case "blocker":
        CmdBlocker(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, reason, blockerId, resolution);
        break;

    case "git":
        CmdGit(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, jsonOutput);
        break;

    case "session":
        CmdSession(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, jsonOutput);
        break;

    case "timeline":
        CmdTimeline(workflowRoot, jsonOutput, outputMode);
        break;

    case "trace":
        CmdTrace(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, jsonOutput, outputMode);
        break;

    case "manifest":
        CmdManifest(workflowRoot, jsonOutput, outputMode);
        break;

    case "recommend":
        CmdRecommend(workflowRoot, task, phase, jsonOutput, outputMode);
        break;

    case "context":
        CmdContext(workflowRoot, phase, jsonOutput, outputMode, budgetMode);
        break;

    case "budget":
        CmdBudget(workflowRoot, budgetMode, modeOverrides, jsonOutput, outputMode);
        break;

    case "validate-request":
        CmdValidateRequest(workflowRoot, operation, phase, targetActor, jsonOutput, outputMode);
        break;

    case "doctor":
        CmdDoctor(workflowRoot, jsonOutput, outputMode);
        break;

    case "mcp":
        CmdMcp(workflowRoot, commandLineArgs);
        break;

    case "install-agent-rules":
        CmdInstallAgentRules(workflowRoot);
        break;

    case "index-health":
        CmdIndexHealth(workflowRoot, jsonOutput, outputMode);
        break;

    case "cache-status":
        CmdCacheStatus(workflowRoot, jsonOutput, outputMode);
        break;

    case "cache-clear":
        CmdCacheClear(workflowRoot);
        break;

    case "sub-phase":
        CmdSubPhase(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, commandLineArgs.Length > 2 ? commandLineArgs[2] : "");
        break;

    case "process-inbox":
        CmdProcessInbox(workflowRoot, jsonOutput, outputMode);
        break;

    case "inbox-status":
        CmdInboxStatus(workflowRoot, jsonOutput, outputMode);
        break;

    case "audit-paths":
        CmdAuditPaths(workflowRoot, jsonOutput);
        break;

    case "repair":
        CmdRepair(workflowRoot, jsonOutput);
        break;

    case "tui":
        // PHASE-023: Interactive Terminal Dashboard TUI
        TuiCommand.Run(startDir, watchIntervalSeconds, allowAncestorDiscovery: root is null);
        break;

    case "help":
    default:
        PrintHelp();
        break;
}


// â”€â”€ PHASE-019 Inbox Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

void CmdProcessInbox(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var ctx = new OperationContext { Operation = WorkflowOperations.ProcessInbox, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        WriteJson(result);
        return;
    }

    if (result.Success && result.Data is ProcessInboxResult inboxResult)
    {
        CliRenderer.RenderProcessInbox(inboxResult, mode);
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

void CmdInboxStatus(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var ctx = new OperationContext { Operation = WorkflowOperations.GetInboxStatus, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        WriteJson(result);
        return;
    }

    if (result.Success && result.Data is InboxStatus status)
    {
        CliRenderer.RenderInboxStatus(
            status.PendingCount, status.ProcessedCount, status.FailedCount,
            status.InboxAvailable, status.OldestPendingUtc?.UtcDateTime, status.PendingFiles, mode);
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

void CmdAuditPaths(string? wfRoot, bool json)
{
    if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var ctx = new OperationContext { Operation = WorkflowOperations.AuditProtectedPaths, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
        return;
    }

    if (result.Success && result.Data is ProtectedPathAuditResult auditResult)
    {
        Console.WriteLine($"Protected path audit: {(auditResult.AllProtected ? "PASS" : "FAIL")}");
        Console.WriteLine($"  Summary: {auditResult.Summary}");
        foreach (var entry in auditResult.Paths)
        {
            var icon = entry.IsProtected
                ? (entry.Status == "ok" || entry.Status == "integrity_ok" ? "  [OK]" : "  [!]")
                : "  [--]";
            Console.WriteLine($"{icon} {entry.Path} ({entry.Status})");
        }
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

void CmdRepair(string? wfRoot, bool json)
{
    if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var ctx = new OperationContext { Operation = WorkflowOperations.RepairConsistency, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
        return;
    }

    if (result.Success)
    {
        Console.WriteLine($"Repair results: {result.Data}");
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

// â”€â”€ PHASE-018 Git, Session, Timeline, Trace Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

void CmdGit(string? subCommand, string? wfRoot, bool json)
{
    if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);

    var (operation, label) = subCommand?.ToLowerInvariant() switch
    {
        "status" => (WorkflowOperations.GetGitStatus, "Git Status"),
        "commits" => (WorkflowOperations.ListGitCommits, "Git Commits"),
        "health" => (WorkflowOperations.GetGitHealth, "Git Health"),
        "record" => (WorkflowOperations.RecordGitActivity, "Git Activity Recorded"),
        _ => (null, null)
    };

    if (operation is null)
    {
        Console.Error.WriteLine("Usage: bfwf git <status|commits|health|record>");
        Environment.Exit(2);
    }

    var ctx = new OperationContext { Operation = operation, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
        return;
    }

    if (result.Success)
    {
        Console.WriteLine($"{label}:");
        var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(jsonOutput);
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

void CmdSession(string? subCommand, string? wfRoot, bool json)
{
    if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);

    var (operation, label) = subCommand?.ToLowerInvariant() switch
    {
        "list" => (WorkflowOperations.ListSessions, "Sessions"),
        "current" => (WorkflowOperations.GetCurrentSession, "Current Session"),
        "end" => (WorkflowOperations.EndSession, "Session Ended"),
        _ => (null, null)
    };

    if (operation is null)
    {
        Console.Error.WriteLine("Usage: bfwf session <list|current|end> [--session-id SES-0001]");
        Environment.Exit(2);
    }

    var ctx = new OperationContext { Operation = operation, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
        return;
    }

    if (result.Success)
    {
        Console.WriteLine($"{label}:");
        var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(jsonOutput);
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

void CmdTimeline(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);

    var ctx = new OperationContext { Operation = WorkflowOperations.GetTimeline, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        WriteJson(result);
        return;
    }

    if (result.Success)
    {
        var root = ToJsonElement(result.Data);
        var entries = root.TryGetProperty("entries", out var entriesElement)
            ? JsonSerializer.Deserialize<List<TimelineEntry>>(entriesElement.GetRawText()) ?? []
            : [];
        var count = root.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : entries.Count;
        CliRenderer.RenderTimeline(entries, count, mode);
    }
    else
    {
        CliRenderer.Error(result.Message ?? "Unknown error", mode);
        Environment.Exit(1);
    }
}

void CmdTrace(string? subCommand, string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (wfRoot is null) { CliRenderer.Error("Not in a Beka Forge Workflow project.", mode); Environment.Exit(1); }
    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);

    var (operation, label) = subCommand?.ToLowerInvariant() switch
    {
        "status" => (WorkflowOperations.GetTraceStatus, "Trace Status"),
        "list" => (WorkflowOperations.ListTraces, "Trace List"),
        "get" => (WorkflowOperations.GetTrace, "Trace Detail"),
        "clear" => (WorkflowOperations.ClearOldTraces, "Traces Cleared"),
        "set-options" => (WorkflowOperations.SetTraceOptions, "Trace Options Updated"),
        _ => (null, null)
    };

    if (operation is null)
    {
        CliRenderer.Error("Usage: bfwf trace <status|list|get|clear|set-options>", mode);
        Environment.Exit(2);
    }

    var ctx = new OperationContext { Operation = operation, Actor = WorkflowActor.Implementer };
    var result = dispatcher.Dispatch(ctx);

    if (json)
    {
        WriteJson(result);
        return;
    }

    if (result.Success)
    {
        switch (subCommand?.ToLowerInvariant())
        {
            case "status":
                var statusRoot = ToJsonElement(result.Data);
                CliRenderer.RenderTraceStatus(
                    GetString(statusRoot, "Mode", "unknown"),
                    GetInt32(statusRoot, "RetentionDays"),
                    GetInt64(statusRoot, "MaxDirectorySizeBytes"),
                    GetInt32(statusRoot, "FileCount"),
                    GetInt32(statusRoot, "RecordCount"),
                    GetInt64(statusRoot, "DirectorySizeBytes"),
                    GetBool(statusRoot, "IsEnabled"),
                    mode);
                break;
            case "list":
                var listRoot = ToJsonElement(result.Data);
                var traces = listRoot.TryGetProperty("traces", out var tracesElement)
                    ? JsonSerializer.Deserialize<List<TraceRecord>>(tracesElement.GetRawText()) ?? []
                    : [];
                var count = listRoot.TryGetProperty("count", out var traceCountElement) ? traceCountElement.GetInt32() : traces.Count;
                CliRenderer.RenderTraceList(traces, count, mode);
                break;
            default:
                Console.WriteLine($"{label}:");
                Console.WriteLine(JsonSerializer.Serialize(result.Data, CreatePrettyJsonOptions()));
                break;
        }
    }
    else
    {
        CliRenderer.Error(result.Message ?? "Unknown error", mode);
        Environment.Exit(1);
    }
}

// â”€â”€ Command Implementations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

void CmdInit(string rootPath, string assetName, bool overwrite)
{
    if (string.IsNullOrWhiteSpace(assetName))
    {
        Console.Error.WriteLine("ERROR: Asset name is required. Usage: bfwf init \"Asset Name\" [--root <path>] [--force]");
        Environment.Exit(1);
    }

    var init = new WorkflowInitializer(rootPath);
    if (init.IsInitialized() && !overwrite)
    {
        Console.Error.WriteLine($"ERROR: Workflow already exists at '{rootPath}'. Use --force to overwrite.");
        Environment.Exit(1);
    }

    try
    {
        var state = init.Initialize(assetName, force: overwrite);
        Console.WriteLine("Beka Forge Workflow initialized.");
        Console.WriteLine($"  Asset:     {state.AssetName}");
        Console.WriteLine($"  ID:        {state.WorkflowId}");
        Console.WriteLine($"  Root:      {state.RootPath}");
        Console.WriteLine($"  Created:   {state.CreatedUtc:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"  Schema:    {state.SchemaVersion}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        Environment.Exit(1);
    }
}

void CmdServer(string subCmd, string rootPath, int serverPort)
{
    switch (subCmd.ToLowerInvariant())
    {
        case "start":
            var effectivePort = serverPort > 0 ? serverPort : LocalServerBootstrap.DefaultPort;
            var effectiveRoot = !string.IsNullOrWhiteSpace(root) ? root : rootPath;
            var (fileName, arguments) = LocalServerBootstrap.GetServerLaunchCommand(effectiveRoot, effectivePort);

            Console.WriteLine($"Starting Beka Forge Workflow server on http://localhost:{effectivePort}");
            Console.WriteLine($"  Root: {effectiveRoot}");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                Console.Error.WriteLine("ERROR: Failed to start Beka Forge Workflow server.");
                Environment.Exit(1);
            }

            Console.WriteLine($"  PID:  {proc.Id}");
            Console.WriteLine($"  URL:  http://localhost:{effectivePort}");
            break;

        case "stop":
            Console.WriteLine("Send POST /api/shutdown to the server, or press Ctrl+C in the server terminal.");
            break;

        case "status":
            if (workflowRoot is not null && WorkflowLayout.IsInitialized(workflowRoot))
                Console.WriteLine($"Beka Forge Workflow is initialized at: {workflowRoot}");
            else
                Console.WriteLine("No Beka Forge Workflow project is initialized in this directory tree.");
            break;

        default:
            Console.Error.WriteLine($"Unknown server sub-command: {subCmd}");
            Console.Error.WriteLine("Usage: bfwf server start|stop|status [--root <path>] [--port <num>]");
            Environment.Exit(1);
            break;
    }
}

void CmdStatus(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain, bool watch = false, int watchIntervalSeconds = 5)
{
    if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
    {
        CliRenderer.Warn("No Beka Forge Workflow project is initialized here. Run 'bfwf init \"Asset Name\"' first.", mode);
        if (json) Environment.Exit(1);
        return;
    }

    if (watch && json)
    {
        CliRenderer.Error("--watch is only supported for rich/plain status output.", mode);
        Environment.Exit(2);
    }

    if (json)
    {
        WriteJson(new WorkflowStore(wfRoot).LoadWorkflow());
        return;
    }

    if (watch)
    {
        RunWatchLoop(() => RenderStatusView(wfRoot, mode), watchIntervalSeconds);
        return;
    }

    RenderStatusView(wfRoot, mode);
}

void RenderStatusView(string wfRoot, CliOutputMode mode)
{
    var store = new WorkflowStore(wfRoot);
    var state = store.LoadWorkflow();

    var nextActionText = state.NextAction is not null
        ? $"[{state.NextAction.Actor}] {state.NextAction.Description}"
        : "(not set)";

    CliRenderer.RenderStatus(
        state.AssetName, state.WorkflowId, state.CurrentPhaseId,
        state.LastStatus?.ToString(), nextActionText, state.OpenBlockerCount,
        state.PhaseIds.Count, state.UpdatedUtc.UtcDateTime, mode);

    var phases = store.LoadAllPhases();
    var phaseRows = phases
        .OrderBy(p => p.PhaseNumber)
        .Select(p => (
            PhaseId: p.PhaseId,
            Title: p.Title,
            State: p.State.ToString(),
            Progress: PhaseProgress.ForPhase(p),
            Blockers: GetOpenBlockerCount(store, p.PhaseId)))
        .ToList();

    if (phaseRows.Count > 0)
    {
        Console.WriteLine();
        CliRenderer.RenderPhaseList(phaseRows, mode);
    }

    if (!string.IsNullOrWhiteSpace(state.CurrentPhaseId))
    {
        var currentPhase = phases.FirstOrDefault(p => string.Equals(p.PhaseId, state.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
        if (currentPhase is not null)
        {
            Console.WriteLine();
            RenderPhaseCard(store, currentPhase, state, mode);
        }
    }
}

void RenderPhaseView(string wfRoot, string phaseId, CliOutputMode mode)
{
    var store = new WorkflowStore(wfRoot);
    var phase = store.LoadPhase(phaseId);
    if (phase is null)
    {
        CliRenderer.Error($"Phase '{phaseId}' not found.", mode);
        Environment.Exit(1);
    }

    var workflow = store.LoadWorkflow();
    RenderPhaseCard(store, phase, workflow, mode);
}

void RenderPhaseCard(WorkflowStore store, Phase phase, WorkflowState workflow, CliOutputMode mode)
{
    var nextAction = string.Equals(workflow.NextAction?.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase)
        ? workflow.NextAction?.Description
        : null;

    var subPhases = phase.SubPhases
        .Select(sp => (sp.SubPhaseId, sp.Title, sp.Status.ToString()))
        .ToList();

    CliRenderer.RenderPhaseCard(
        phase.PhaseId,
        phase.Title,
        phase.State.ToString(),
        PhaseProgress.ForPhase(phase),
        nextAction,
        phase.Dependencies,
        subPhases,
        GetOpenBlockerCount(store, phase.PhaseId),
        mode);
}

int GetOpenBlockerCount(WorkflowStore store, string phaseId) =>
    store.ReadAllBlockers()
        .Where(b => string.Equals(b.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
        .GroupBy(b => b.BlockerId)
        .Select(g => g.Last())
        .Count(b => !b.IsResolved);

void RunWatchLoop(Action render, int intervalSeconds)
{
    while (true)
    {
        try
        {
            Console.Clear();
        }
        catch
        {
        }

        render();
        Console.WriteLine();
        Console.WriteLine($"Watching... refresh every {intervalSeconds}s. Press Ctrl+C to stop.");
        Thread.Sleep(TimeSpan.FromSeconds(intervalSeconds));
    }
}

void CmdValidate(string? wfRoot, bool json)
{
    if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
    {
        Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
        Environment.Exit(1);
    }

    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.ValidateState
    });

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
        return;
    }

    if (result.Success && result.Data is not null)
    {
        // Data is an anonymous object with 'valid' and 'issues'
        Console.WriteLine("VALID");
    }
    else
    {
        Console.Error.WriteLine("Validation failed.");
        if (result.Message is not null)
            Console.Error.WriteLine(result.Message);
        Environment.Exit(1);
    }
}

void CmdSyncMarkdown(string? wfRoot)
{
    if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
    {
        Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
        Environment.Exit(1);
    }

    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.SyncMarkdown
    });

    if (result.Success)
    {
        Console.WriteLine(result.Message ?? "Markdown synced.");
    }
    else
    {
        Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
        Environment.Exit(1);
    }
}

void CmdLog(string logType, string? wfRoot, string? phaseId, string? logSummary, string? logNotes, string? passedStr)
{
    if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
    {
        Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
        Environment.Exit(1);
    }

    if (string.IsNullOrWhiteSpace(phaseId))
    {
        Console.Error.WriteLine("ERROR: --phase is required.");
        Environment.Exit(1);
    }

    if (string.IsNullOrWhiteSpace(logSummary))
    {
        Console.Error.WriteLine("ERROR: --summary is required.");
        Environment.Exit(1);
    }

    string operationName = logType.ToLowerInvariant() switch
    {
        "implementation" => WorkflowOperations.CreateImplementationLog,
        "audit"          => WorkflowOperations.CreateAuditLog,
        "review"         => WorkflowOperations.CreateReviewLog,
        "test"           => WorkflowOperations.CreateTestLog,
        "fix"            => WorkflowOperations.CreateFixLog,
        _ => throw new ArgumentException($"Unknown log type: {logType}. Use: implementation, audit, review, test, or fix.")
    };

    var parameters = new Dictionary<string, object?>
    {
        ["summary"] = logSummary
    };

    if (!string.IsNullOrWhiteSpace(logNotes))
        parameters["notes"] = logNotes;

    if (!string.IsNullOrWhiteSpace(passedStr) && bool.TryParse(passedStr, out var p))
        parameters["passed"] = p;

    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);
    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = operationName,
        PhaseId = phaseId,
        Actor = WorkflowActor.User,
        Parameters = parameters
    });

    if (result.Success)
        Console.WriteLine("OK");
    else
    {
        Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
        Environment.Exit(1);
    }
}

void CmdPhase(string subCmd, string? wfRoot, string? phaseId, string? title, string? targetState, string? blockerReason, string? agentName,
    string? phaseSummary, string? dependencies, string? objective, string? scope, string? outOfScope,
    string? implementationNotes, string? auditRequirements, string? unityTestRequirements, string? parallelizationNotes,
    string? architectureConstraints, string? requiredFilesOrAreas, string? acceptanceCriteria, string? dependsOnPhaseIds,
    string? requiresUnityTest, string? subPhaseId, string? subPhaseSummary, string? subPhaseDependencies, string? subPhasesJson,
    bool json, CliOutputMode mode, bool watch, int watchIntervalSeconds)
{
    if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
    {
        CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
        Environment.Exit(1);
    }

    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);

    switch (subCmd.ToLowerInvariant())
    {
        case "create":
            if (string.IsNullOrWhiteSpace(title))
            {
                CliRenderer.Error("--title is required.", mode);
                Environment.Exit(1);
            }

            var createParameters = new Dictionary<string, object?> { ["title"] = title };
            AddIfPresent(createParameters, "summary", phaseSummary);
            AddIfPresent(createParameters, "assignedAgent", agentName);
            AddIfPresent(createParameters, "dependencies", dependencies);
            AddIfPresent(createParameters, "contractObjective", objective);
            AddIfPresent(createParameters, "contractScope", scope);
            AddIfPresent(createParameters, "contractOutOfScope", outOfScope);
            AddIfPresent(createParameters, "contractImplementationNotes", implementationNotes);
            AddIfPresent(createParameters, "contractAuditRequirements", auditRequirements);
            AddIfPresent(createParameters, "contractUnityTestRequirements", unityTestRequirements);
            AddIfPresent(createParameters, "contractParallelizationNotes", parallelizationNotes);
            AddIfPresent(createParameters, "contractArchitectureConstraints", architectureConstraints);
            AddIfPresent(createParameters, "contractRequiredFilesOrAreas", requiredFilesOrAreas);
            AddIfPresent(createParameters, "contractAcceptanceCriteria", acceptanceCriteria);
            AddIfPresent(createParameters, "contractDependsOnPhaseIds", dependsOnPhaseIds);
            AddIfPresent(createParameters, "requiresUnityTest", requiresUnityTest);
            AddIfPresent(createParameters, "subPhasesJson", subPhasesJson);

            var createResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.CreatePhase,
                PhaseId = phaseId,
                Actor = WorkflowActor.User,
                Parameters = createParameters
            });

            if (json)
            {
                WriteJson(createResult);
                return;
            }

            if (createResult.Success)
                CliRenderer.Ok($"Phase created.", mode);
            else
            {
                CliRenderer.Error($"{createResult.ErrorCode}: {createResult.Message}", mode);
                Environment.Exit(1);
            }
            break;

        case "show":
            var effectivePhaseId = phaseId ?? store.LoadWorkflow().CurrentPhaseId;
            if (string.IsNullOrWhiteSpace(effectivePhaseId))
            {
                CliRenderer.Error("No phase selected. Use --phase PHASE-NNN or set a current phase first.", mode);
                Environment.Exit(1);
            }

            if (watch && json)
            {
                CliRenderer.Error("--watch is only supported for rich/plain phase output.", mode);
                Environment.Exit(2);
            }

            if (watch)
            {
                RunWatchLoop(() => RenderPhaseView(wfRoot, effectivePhaseId, mode), watchIntervalSeconds);
                return;
            }

            if (json)
            {
                var phaseJson = store.LoadPhase(effectivePhaseId);
                if (phaseJson is null)
                {
                    CliRenderer.Error($"Phase '{effectivePhaseId}' not found.", mode);
                    Environment.Exit(1);
                }

                WriteJson(phaseJson);
                return;
            }

            RenderPhaseView(wfRoot, effectivePhaseId, mode);
            break;

        case "list":
            var allPhases = store.LoadAllPhases()
                .OrderBy(p => p.PhaseNumber)
                .ToList();

            if (json)
            {
                WriteJson(allPhases);
                return;
            }

            CliRenderer.RenderPhaseList(
                allPhases.Select(p => (
                    PhaseId: p.PhaseId,
                    Title: p.Title,
                    State: p.State.ToString(),
                    Progress: PhaseProgress.ForPhase(p),
                    Blockers: GetOpenBlockerCount(store, p.PhaseId))).ToList(),
                mode);
            break;

        case "status":
            if (string.IsNullOrWhiteSpace(phaseId))
            {
                CliRenderer.Error("--phase is required.", mode);
                Environment.Exit(1);
            }
            if (string.IsNullOrWhiteSpace(targetState))
            {
                CliRenderer.Error("--state is required.", mode);
                Environment.Exit(1);
            }

            var parameters = new Dictionary<string, object?> { ["state"] = targetState };
            if (!string.IsNullOrWhiteSpace(blockerReason))
                parameters["blockerReason"] = blockerReason;

            var result = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.UpdatePhaseStatus,
                PhaseId = phaseId,
                Actor = WorkflowActor.User,
                Parameters = parameters
            });

            if (result.Success)
                CliRenderer.Ok("Phase state updated.", mode);
            else
            {
                CliRenderer.Error($"{result.ErrorCode}: {result.Message}", mode);
                Environment.Exit(1);
            }
            break;

        case "assign":
            if (string.IsNullOrWhiteSpace(phaseId))
            {
                CliRenderer.Error("--phase is required.", mode);
                Environment.Exit(1);
            }
            if (string.IsNullOrWhiteSpace(agentName))
            {
                CliRenderer.Error("--agent is required.", mode);
                Environment.Exit(1);
            }

            var assignResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.AssignPhase,
                PhaseId = phaseId,
                Actor = WorkflowActor.Codex,
                Parameters = new Dictionary<string, object?> { ["agent"] = agentName }
            });

            if (assignResult.Success)
                CliRenderer.Ok("Phase assigned.", mode);
            else
            {
                CliRenderer.Error($"{assignResult.ErrorCode}: {assignResult.Message}", mode);
                Environment.Exit(1);
            }
            break;

        case "update":
            if (string.IsNullOrWhiteSpace(phaseId))
            {
                CliRenderer.Error("--phase is required.", mode);
                Environment.Exit(1);
            }

            var updateParameters = new Dictionary<string, object?>();
            AddIfPresent(updateParameters, "summary", phaseSummary);
            AddIfPresent(updateParameters, "dependencies", dependencies);
            AddIfPresent(updateParameters, "contractObjective", objective);
            AddIfPresent(updateParameters, "contractScope", scope);
            AddIfPresent(updateParameters, "contractOutOfScope", outOfScope);
            AddIfPresent(updateParameters, "contractImplementationNotes", implementationNotes);
            AddIfPresent(updateParameters, "contractAuditRequirements", auditRequirements);
            AddIfPresent(updateParameters, "contractUnityTestRequirements", unityTestRequirements);
            AddIfPresent(updateParameters, "contractParallelizationNotes", parallelizationNotes);
            AddIfPresent(updateParameters, "contractArchitectureConstraints", architectureConstraints);
            AddIfPresent(updateParameters, "contractRequiredFilesOrAreas", requiredFilesOrAreas);
            AddIfPresent(updateParameters, "contractAcceptanceCriteria", acceptanceCriteria);
            AddIfPresent(updateParameters, "contractDependsOnPhaseIds", dependsOnPhaseIds);
            AddIfPresent(updateParameters, "requiresUnityTest", requiresUnityTest);
            AddIfPresent(updateParameters, "subPhaseId", subPhaseId);
            AddIfPresent(updateParameters, "subPhaseSummary", subPhaseSummary);
            AddIfPresent(updateParameters, "subPhaseDependencies", subPhaseDependencies);

            var updateResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.UpdatePhase,
                PhaseId = phaseId,
                Actor = WorkflowActor.User,
                Parameters = updateParameters
            });

            if (updateResult.Success)
                CliRenderer.Ok($"Phase {phaseId} updated.", mode);
            else
            {
                CliRenderer.Error($"{updateResult.ErrorCode}: {updateResult.Message}", mode);
                Environment.Exit(1);
            }
            break;

        case "remove":
            if (string.IsNullOrWhiteSpace(phaseId))
            {
                CliRenderer.Error("--phase is required.", mode);
                Environment.Exit(1);
            }

            var removeResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.RemovePhase,
                PhaseId = phaseId,
                Actor = WorkflowActor.User
            });

            if (removeResult.Success)
                CliRenderer.Ok($"Phase {phaseId} removed.", mode);
            else
            {
                CliRenderer.Error($"{removeResult.ErrorCode}: {removeResult.Message}", mode);
                Environment.Exit(1);
            }
            break;

        default:
            CliRenderer.Error($"Unknown phase sub-command: {subCmd}", mode);
            Console.Error.WriteLine("Usage: bfwf phase <show|list|status|assign|update|remove> [...]");
            Environment.Exit(1);
            break;
    }
}

void AddIfPresent(Dictionary<string, object?> parameters, string key, string? value)
{
    if (value is not null)
        parameters[key] = value;
}

void CmdBlocker(string subCmd, string? wfRoot, string? phaseId, string? blockerReason, string? blockId, string? blockerResolution)
{
    if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
    {
        Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
        Environment.Exit(1);
    }

    var store = new WorkflowStore(wfRoot);
    var dispatcher = new OperationDispatcher(store);

    switch (subCmd.ToLowerInvariant())
    {
        case "add":
            if (string.IsNullOrWhiteSpace(phaseId))
            {
                Console.Error.WriteLine("ERROR: --phase is required.");
                Environment.Exit(1);
            }
            if (string.IsNullOrWhiteSpace(blockerReason))
            {
                Console.Error.WriteLine("ERROR: --reason is required.");
                Environment.Exit(1);
            }

            var addResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.RecordBlocker,
                PhaseId = phaseId,
                Actor = WorkflowActor.User,
                Parameters = new Dictionary<string, object?> { ["reason"] = blockerReason }
            });

            if (addResult.Success)
                Console.WriteLine($"OK â€” blocker recorded.");
            else
            {
                Console.Error.WriteLine($"ERROR [{addResult.ErrorCode}]: {addResult.Message}");
                Environment.Exit(1);
            }
            break;

        case "resolve":
            if (string.IsNullOrWhiteSpace(blockId))
            {
                Console.Error.WriteLine("ERROR: --blocker-id is required.");
                Environment.Exit(1);
            }

            var resolveResult = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.ResolveBlocker,
                Actor = WorkflowActor.User,
                Parameters = new Dictionary<string, object?>
                {
                    ["blockerId"] = blockId,
                    ["resolution"] = blockerResolution ?? "Resolved."
                }
            });

            if (resolveResult.Success)
                Console.WriteLine("OK â€” blocker resolved.");
            else
            {
                Console.Error.WriteLine($"ERROR [{resolveResult.ErrorCode}]: {resolveResult.Message}");
                Environment.Exit(1);
            }
            break;

        default:
            Console.Error.WriteLine($"Unknown blocker sub-command: {subCmd}");
            Console.Error.WriteLine("Usage: bfwf blocker add|resolve [...]");
            Environment.Exit(1);
            break;
    }
}

void CmdManifest(string? root, bool json, CliOutputMode mode = CliOutputMode.Plain)
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

void CmdRecommend(string? root, string? taskText, string? phaseId, bool json, CliOutputMode mode = CliOutputMode.Plain)
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
        Operation  = WorkflowOperations.RecommendOperation,
        PhaseId    = phaseId,
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

    var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"Recommendations for: \"{taskText}\"");
    if (!string.IsNullOrWhiteSpace(phaseId)) Console.WriteLine($"  Phase: {phaseId}");
    Console.WriteLine();
    Console.WriteLine(jsonOutput);
}

void CmdContext(string? root, string? phaseId, bool json, CliOutputMode mode = CliOutputMode.Plain, string? budgetMode = null)
{
    if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }

    var store = new WorkflowStore(root);
    var dispatcher = new OperationDispatcher(store);

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.GetRelevantContext,
        PhaseId   = phaseId,
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

void CmdValidateRequest(string? root, string? targetOperation, string? phaseId, string? actorName, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }
    if (string.IsNullOrWhiteSpace(targetOperation)) { Console.Error.WriteLine("ERROR: --operation is required. Example: bfwf validate-request --operation workflow.create_implementation_log --phase PHASE-001"); Environment.Exit(1); }

    var store = new WorkflowStore(root);
    var dispatcher = new OperationDispatcher(store);

    var actor = !string.IsNullOrWhiteSpace(actorName) ? ParseActor(actorName) : WorkflowActor.WorkflowKit;

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.ValidateOperationRequest,
        PhaseId   = phaseId,
        Actor     = actor,
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

    var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"Validation for: {targetOperation}");
    if (!string.IsNullOrWhiteSpace(phaseId)) Console.WriteLine($"  Phase: {phaseId}");
    if (!string.IsNullOrWhiteSpace(actorName)) Console.WriteLine($"  Actor: {actorName}");
    Console.WriteLine();
    Console.WriteLine(jsonOutput);
}

void CmdBudget(string? root, string? budgetMode, string? modeOverrides, bool json, CliOutputMode mode = CliOutputMode.Plain)
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

    // Plain/rich output
    CliRenderer.Ok($"Budget Configuration â€” Default Mode: {config.DefaultMode}", mode);
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

void CmdIndexHealth(string? root, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (root is null) { CliRenderer.Error("No workflow found. Run from a workflow directory or use --root.", mode); Environment.Exit(1); }

    var store = new WorkflowStore(root);
    var dispatcher = new OperationDispatcher(store);

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.RebuildContextIndex
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

    var indexDir = Path.Combine(root, ".workflowkit", "index");
    var files = Directory.Exists(indexDir)
        ? Directory.GetFiles(indexDir)
            .Select(file => new FileInfo(file))
            .Select(info => (info.Name, info.Length, info.LastWriteTime))
            .ToList()
        : [];
    var health = result.Data as IndexHealth ?? throw new InvalidOperationException("Expected IndexHealth result.");
    CliRenderer.RenderIndexHealth(health, files, mode);

    Console.WriteLine();
    Console.WriteLine("Note: .workflowkit/index/* is a rebuildable read model â€” not source of truth.");
}

void CmdCacheStatus(string? root, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    if (root is null) { CliRenderer.Error("No workflow found.", mode); Environment.Exit(1); }

    // Try the server first � it owns the single shared cache for this project
    var serverStatus = LocalServerBootstrap.GetStatus(root);
    if (serverStatus.IsRunning)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = http.PostAsync(
                $"http://localhost:{LocalServerBootstrap.DefaultPort}/api/workflow/workflow.get_cache_status",
                content).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                var jsonStr = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (json)
                {
                    Console.WriteLine(jsonStr);
                    return;
                }
                using var doc = JsonDocument.Parse(jsonStr);
                var data = doc.RootElement.GetProperty("data");
                var pkgCount = data.GetProperty("packageCount").GetInt32();
                var hitRate = data.GetProperty("hitRate").GetDouble();
                Console.WriteLine($"Server cache: {pkgCount} pkgs, hit rate {hitRate:P0}");
                return;
            }
        }
        catch { /* fall through to local dispatcher */ }
    }

    var store = new WorkflowStore(root);
    var settingsPath = Path.Combine(root, ".workflowkit", "cache-settings.json");
    var settingsFileExists = File.Exists(settingsPath);
    var settings = CacheSettings.Load(settingsPath);
    var cache = new ContextPackageCache(settings);
    var dispatcher = new OperationDispatcher(store, cache);

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.GetCacheStatus
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

    if (result.Data is CacheDiagnostics diagnostics)
        CliRenderer.RenderCacheStatus(diagnostics, settings, settingsFileExists, mode);
    else
        Console.WriteLine(JsonSerializer.Serialize(result.Data, CreatePrettyJsonOptions()));
}

void CmdCacheClear(string? root)
{
    if (root is null) { Console.Error.WriteLine("ERROR: No workflow found."); Environment.Exit(1); }

    var store = new WorkflowStore(root);
    var settings = CacheSettings.Load(Path.Combine(root, ".workflowkit", "cache-settings.json"));
    var cache = new ContextPackageCache(settings);
    var dispatcher = new OperationDispatcher(store, cache);

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.ClearContextCache
    });

    if (!result.Success)
    {
        Console.Error.WriteLine($"ERROR: {result.Message}");
        Environment.Exit(1);
    }

    var jsonOutput = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine("Cache Cleared");
    Console.WriteLine("------------");
    Console.WriteLine(jsonOutput);
}

void CmdSubPhase(string subCmd, string? root, string? phaseId, string? subPhaseId)
{
    if (root is null) { Console.Error.WriteLine("ERROR: No workflow found."); Environment.Exit(1); }
    if (string.IsNullOrWhiteSpace(phaseId)) { Console.Error.WriteLine("ERROR: --phase is required."); Environment.Exit(1); }
    if (string.IsNullOrWhiteSpace(subPhaseId)) { Console.Error.WriteLine("ERROR: sub-phase ID is required."); Environment.Exit(1); }

    var store = new WorkflowStore(root);
    var dispatcher = new OperationDispatcher(store);

    var result = dispatcher.Dispatch(new OperationContext
    {
        Operation = WorkflowOperations.UpdateSubPhaseStatus,
        PhaseId   = phaseId,
        Parameters = new Dictionary<string, object?>
        {
            ["subPhaseId"] = subPhaseId,
            ["status"] = subCmd
        }
    });

    if (!result.Success)
    {
        Console.Error.WriteLine($"ERROR: {result.Message}");
        Environment.Exit(1);
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

// â”€â”€ PHASE-020 Stub Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

void CmdDoctor(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain)
{
    var diag = new Dictionary<string, object?>();
    var issues = new List<string>();

    // 1. SDK check
    try
    {
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        var sdkVersion = proc?.StandardOutput.ReadToEnd().Trim();
        proc?.WaitForExit();
        diag["sdkVersion"] = sdkVersion ?? "unknown";
        diag["sdkOk"] = proc?.ExitCode == 0;
    }
    catch
    {
        diag["sdkVersion"] = null;
        diag["sdkOk"] = false;
        issues.Add("dotnet SDK not found or broken");
    }

    // 2. Git check
    try
    {
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        var gitVersion = proc?.StandardOutput.ReadToEnd().Trim();
        proc?.WaitForExit();
        diag["gitVersion"] = gitVersion ?? "unknown";
        diag["gitOk"] = proc?.ExitCode == 0;
    }
    catch
    {
        diag["gitVersion"] = null;
        diag["gitOk"] = false;
        issues.Add("git not found");
    }

    // 3. Workflow state
    if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
    {
        var store = new WorkflowStore(wfRoot);
        var state = store.LoadWorkflow();
        diag["workflowRoot"] = wfRoot;
        diag["workflowInitialized"] = true;
        diag["currentPhase"] = state.CurrentPhaseId;
        diag["openBlockers"] = state.OpenBlockerCount;
        diag["phaseCount"] = state.PhaseIds.Count;
    }
    else
    {
        diag["workflowInitialized"] = false;
        if (wfRoot is not null)
            issues.Add($"No .workflowkit found at {wfRoot}");
    }

    // 4. Inbox status
    if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
    {
        try
        {
            var store = new WorkflowStore(wfRoot);
            var dispatcher = new OperationDispatcher(store);
            var inboxResult = dispatcher.Dispatch(new OperationContext { Operation = WorkflowOperations.GetInboxStatus, Actor = WorkflowActor.Implementer });
            if (inboxResult.Success && inboxResult.Data is InboxStatus istatus)
            {
                diag["inboxPending"] = istatus.PendingCount;
                diag["inboxFailed"] = istatus.FailedCount;
                diag["inboxAvailable"] = istatus.InboxAvailable;
            }
        }
        catch { issues.Add("inbox status check failed"); }
    }

    // 5. Index health
    if (wfRoot is not null)
    {
        var indexDir = Path.Combine(wfRoot, ".workflowkit", "index");
        diag["indexExists"] = Directory.Exists(indexDir);
        if (Directory.Exists(indexDir))
        {
            var indexFiles = Directory.GetFiles(indexDir);
            diag["indexFileCount"] = indexFiles.Length;
            diag["indexTotalBytes"] = indexFiles.Sum(f => new FileInfo(f).Length);
        }
    }

    // 6. Trace status
    if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
    {
        try
        {
            var store = new WorkflowStore(wfRoot);
            var dispatcher = new OperationDispatcher(store);
            var traceResult = dispatcher.Dispatch(new OperationContext { Operation = WorkflowOperations.GetTraceStatus, Actor = WorkflowActor.Implementer });
            diag["traceAvailable"] = traceResult.Success;
        }
        catch { diag["traceAvailable"] = false; }
    }

    // 7. Cache settings
    if (wfRoot is not null)
    {
        var cachePath = Path.Combine(wfRoot, ".workflowkit", "cache-settings.json");
        diag["cacheSettingsExist"] = File.Exists(cachePath);
        if (File.Exists(cachePath))
        {
            try
            {
                var settings = CacheSettings.Load(cachePath);
                diag["cacheMaxPackages"] = settings.MaxPackageCount;
                diag["cacheMaxMemory"] = settings.MaxMemoryEstimateBytes;
            }
            catch { issues.Add("cache settings load failed"); }
        }
    }

    // 8. Dashboard (WPF - Windows only)
    diag["dashboardAvailable"] = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
    if (!(diag["dashboardAvailable"]?.Equals(true) == true))
        diag["dashboardNote"] = "WPF dashboard is Windows-only; CLI is cross-platform";

    diag["issues"] = issues;
    diag["healthy"] = issues.Count == 0;

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(diag,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
        if (!(diag["healthy"]?.Equals(true) == true)) Environment.Exit(1);
        return;
    }

    CliRenderer.RenderDoctor(
        sdkOk:                diag["sdkOk"]?.Equals(true) == true,
        sdkVersion:           diag["sdkVersion"]?.ToString(),
        gitOk:                diag["gitOk"]?.Equals(true) == true,
        gitVersion:           diag["gitVersion"]?.ToString(),
        workflowInitialized:  diag["workflowInitialized"]?.Equals(true) == true,
        currentPhase:         diag.ContainsKey("currentPhase") ? diag["currentPhase"]?.ToString() : null,
        openBlockers:         diag.ContainsKey("openBlockers") ? Convert.ToInt32(diag["openBlockers"]) : 0,
        indexExists:          diag["indexExists"]?.Equals(true) == true,
        indexFileCount:       diag.ContainsKey("indexFileCount") ? Convert.ToInt32(diag["indexFileCount"]) : 0,
        indexBytes:           diag.ContainsKey("indexTotalBytes") ? Convert.ToInt64(diag["indexTotalBytes"]) : 0,
        cacheSettingsExist:   diag["cacheSettingsExist"]?.Equals(true) == true,
        maxPackages:          diag.ContainsKey("cacheMaxPackages") ? (int?)Convert.ToInt32(diag["cacheMaxPackages"]) : null,
        dashboardAvailable:   diag["dashboardAvailable"]?.Equals(true) == true,
        issues:               issues,
        mode:                 mode);

    if (!(diag["healthy"]?.Equals(true) == true))
        Environment.Exit(1);
}

void CmdInstallAgentRules(string? wfRoot)
{
    if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }

    var sourceDir = wfRoot;
    var targetDir = Path.Combine(wfRoot, ".deepseek");
    var instructionsDir = Path.Combine(targetDir, "instructions");

    var filesToCopy = new (string Source, string DestRelative)[]
    {
        (Path.Combine(sourceDir, "AGENTS.md"), "AGENTS.md"),
        (Path.Combine(sourceDir, "workflow", "Rules.md"), Path.Combine("instructions", "Rules.md")),
        (Path.Combine(sourceDir, "BekaWorkflowSystemPrompt.md"), "BekaWorkflowSystemPrompt.md"),
    };

    Directory.CreateDirectory(targetDir);
    Directory.CreateDirectory(instructionsDir);

    var copied = 0;
    foreach (var (src, destRel) in filesToCopy)
    {
        if (!File.Exists(src))
        {
            Console.WriteLine($"  SKIP: {Path.GetFileName(src)} not found at project root.");
            continue;
        }

        var dest = Path.Combine(targetDir, destRel);
        File.Copy(src, dest, overwrite: true);
        Console.WriteLine($"  COPY: {Path.GetFileName(src)} -> {dest}");
        copied++;
    }

    // Ensure .deepseek directory exists in target
    var deepseekDir = Path.Combine(targetDir);
    if (!Directory.Exists(deepseekDir))
        Directory.CreateDirectory(deepseekDir);

    Console.WriteLine();
    Console.WriteLine(copied > 0
        ? $"Installed {copied} agent rule file(s) to {targetDir}"
        : "No agent rules found to install. Ensure AGENTS.md exists at the project root.");

    if (copied == 0)
        Environment.Exit(1);
}

void PrintHelp()
{
    Console.WriteLine("Beka Forge Workflow CLI");
    Console.WriteLine();
    Console.WriteLine("Usage: bfwf <command> [options]");
    Console.WriteLine("Future alias: bfk (planned, not implemented yet)");
    Console.WriteLine();
    Console.WriteLine("Project Setup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf init \"Asset Name\" [--root <path>] [--force]");
    Console.WriteLine("  bfwf install-agent-rules [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Project Status â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf status [--root <path>] [--plain] [--watch] [--interval 5]");
    Console.WriteLine("  bfwf tui    [--root <path>] [--interval 5]   (interactive dashboard; auto-setup/start)");
    Console.WriteLine("  bfwf validate [--root <path>]");
    Console.WriteLine("  bfwf doctor [--root <path>] [--json]");
    Console.WriteLine("  bfwf mcp [--root <path>]     (MCP stdio host; omit --root for global mode)");
    Console.WriteLine();
    Console.WriteLine("Phases & Logs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf phase create --title \"...\" [--phase PHASE-NNN] [--summary \"...\"]");
    Console.WriteLine("  bfwf phase show [--phase PHASE-NNN] [--watch] [--interval 5]");
    Console.WriteLine("  bfwf phase list");
    Console.WriteLine("  bfwf phase update --phase PHASE-NNN [--summary \"...\"] [--dependencies \"PHASE-001,PHASE-002\"]");
    Console.WriteLine("  bfwf phase remove --phase PHASE-NNN");
    Console.WriteLine("  bfwf phase status --phase PHASE-NNN --state <PhaseState> [--reason \"...\"]");
    Console.WriteLine("  bfwf phase assign --phase PHASE-NNN --agent <Planner|Implementer|Auditor|...>");
    Console.WriteLine("  bfwf sub-phase update --phase PHASE-NNN --sub-phase PHASE-NNN-A <status>");
    Console.WriteLine("  bfwf log implementation --phase PHASE-NNN --summary \"...\" [--notes \"...\"]");
    Console.WriteLine("  bfwf log audit --phase PHASE-NNN --summary \"...\" [--passed true/false]");
    Console.WriteLine("  bfwf log review --phase PHASE-NNN --summary \"...\" [--passed true/false]");
    Console.WriteLine("  bfwf log test --phase PHASE-NNN --summary \"...\" [--passed true/false]");
    Console.WriteLine("  bfwf log fix --phase PHASE-NNN --summary \"...\"");
    Console.WriteLine();
    Console.WriteLine("Blockers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf blocker add --phase PHASE-NNN --reason \"...\"");
    Console.WriteLine("  bfwf blocker resolve --blocker-id BLK-NNN [--resolution \"...\"]");
    Console.WriteLine();
    Console.WriteLine("Server â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf server start [--root <path>] [--port <num>]");
    Console.WriteLine("  bfwf server stop");
    Console.WriteLine("  bfwf server status [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Operations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf manifest [--root <path>]");
    Console.WriteLine("  bfwf recommend --task \"...\" [--phase PHASE-NNN] [--root <path>]");
    Console.WriteLine("  bfwf context [--phase PHASE-NNN] [--root <path>]");
    Console.WriteLine("  bfwf budget [--budget Low|Medium|High|Full] [--mode-overrides <json>] [--root <path>]");
    Console.WriteLine("  bfwf validate-request --operation \"...\" [--phase PHASE-NNN] [--actor <name>]");
    Console.WriteLine();
    Console.WriteLine("Inbox â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf process-inbox [--root <path>] [--plain]");
    Console.WriteLine("  bfwf inbox-status [--root <path>] [--plain]");
    Console.WriteLine("  bfwf audit-paths [--root <path>]");
    Console.WriteLine("  bfwf repair [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Git & Sessions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf git status [--root <path>]");
    Console.WriteLine("  bfwf git commits [--max 50] [--phase PHASE-NNN] [--since \"2026-01-01\"]");
    Console.WriteLine("  bfwf git health");
    Console.WriteLine("  bfwf git record [--session-id SES-0001]");
    Console.WriteLine("  bfwf session list [--active-only]");
    Console.WriteLine("  bfwf session current");
    Console.WriteLine("  bfwf session end [--session-id SES-0001]");
    Console.WriteLine("  bfwf timeline [--max 50] [--phase PHASE-NNN] [--since \"2026-01-01\"] [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Cache â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf cache-status [--root <path>] [--plain]");
    Console.WriteLine("  bfwf cache-clear [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Diagnostics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
    Console.WriteLine("  bfwf index-health [--root <path>] [--plain]");
    Console.WriteLine("  bfwf trace status|list|get|clear|set-options [--root <path>] [--plain]");
    Console.WriteLine("  bfwf sync-markdown [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Global flags:");
    Console.WriteLine("  --root <path>    Workflow project root (default: current directory)");
    Console.WriteLine("  --json           Machine-readable JSON output (for automation/AI agents)");
    Console.WriteLine("  --plain          Plain text output, no ANSI colours or panels");
    Console.WriteLine("  --watch          Refresh status or phase show output on an interval");
    Console.WriteLine("  --interval <n>   Watch refresh interval in seconds (default: 5)");
    Console.WriteLine();
    Console.WriteLine("Exit codes: 0 = success, 1 = error, 2 = invalid arguments");
}

// â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

void WriteJson(object? value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, CreateCompactJsonOptions()));

JsonElement ToJsonElement(object? value) =>
    JsonSerializer.SerializeToElement(value, CreateCompactJsonOptions());

JsonSerializerOptions CreateCompactJsonOptions() => new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};

JsonSerializerOptions CreatePrettyJsonOptions() => new()
{
    WriteIndented = true
};

string GetString(JsonElement element, string propertyName, string fallback = "")
{
    if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        return fallback;

    return property.ValueKind switch
    {
        JsonValueKind.String => property.GetString() ?? fallback,
        JsonValueKind.Null => fallback,
        _ => property.ToString()
    };
}

int GetInt32(JsonElement element, string propertyName)
{
    if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        return 0;

    return property.ValueKind switch
    {
        JsonValueKind.Number => property.GetInt32(),
        JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
        _ => 0
    };
}

long GetInt64(JsonElement element, string propertyName)
{
    if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        return 0;

    return property.ValueKind switch
    {
        JsonValueKind.Number => property.GetInt64(),
        JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
        _ => 0
    };
}

bool GetBool(JsonElement element, string propertyName)
{
    if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        return false;

    return property.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
        _ => false
    };
}

bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
{
    if (element.TryGetProperty(propertyName, out property))
        return true;

    foreach (var candidate in element.EnumerateObject())
    {
        if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
        {
            property = candidate.Value;
            return true;
        }
    }

    property = default;
    return false;
}

static string? ParseFlag(string[] values, string flag)
{
    for (int i = 0; i < values.Length - 1; i++)
    {
        if (string.Equals(values[i], flag, StringComparison.OrdinalIgnoreCase))
            return values[i + 1];
    }
    return null;
}

static bool HasFlag(string[] values, string flag) =>
    values.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

static int? ParseIntFlag(string[] values, string flag)
{
    var val = ParseFlag(values, flag);
    return val is not null && int.TryParse(val, out var n) ? n : null;
}

static string? DiscoverWorkflowRoot(string startDir)
{
    var dir = Path.GetFullPath(startDir);
    while (true)
    {
        if (WorkflowLayout.IsInitialized(dir))
            return dir;

        var parent = Path.GetDirectoryName(dir);
        if (parent is null || parent == dir)
            return null;

        dir = parent;
    }
}

static WorkflowActor ParseActor(string name) => name?.ToLowerInvariant() switch
{
    // Legacy names (backward compatible)
    "codex" => WorkflowActor.Codex,
    "deepseek" => WorkflowActor.DeepSeek,
    "unityassistant" => WorkflowActor.UnityAssistant,
    "unitybridge" => WorkflowActor.UnityBridge,
    "user" => WorkflowActor.User,
    "workflowkit" => WorkflowActor.WorkflowKit,
    // Generic role names (preferred)
    "planner" => WorkflowActor.Planner,
    "implementer" => WorkflowActor.Implementer,
    "auditor" => WorkflowActor.Auditor,
    "reviewer" => WorkflowActor.Reviewer,
    "validator" => WorkflowActor.Validator,
    "fixer" => WorkflowActor.Fixer,
    "humanowner" => WorkflowActor.HumanOwner,
    "workflowsystem" => WorkflowActor.WorkflowSystem,
    _ => WorkflowActor.Implementer
};

void CmdMcp(string? wfRoot, string[] cmdArgs)
{
    var explicitRoot = ParseFlag(cmdArgs, "--root");
    var effectiveRoot = explicitRoot;

    if (effectiveRoot is not null &&
        !BekaForge.WorkflowKit.Mcp.ProjectRegistry.IsValidWorkflowRoot(effectiveRoot))
    {
        Console.Error.WriteLine($"[Beka Forge Workflow MCP] ERROR: Not a valid Beka Forge Workflow project: {effectiveRoot}");
        Console.Error.WriteLine("  Must contain .workflowkit/workflow.json");
        Environment.Exit(1);
    }

    // Create global registry from app data
    BekaForge.WorkflowKit.Mcp.ProjectRegistry? globalRegistry = null;
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var globalRegistryPath = Path.Combine(appData, "BekaForge", "mcp-registry.json");

    try
    {
        // Global registry uses %APPDATA%/BekaForge as its "workflow root" directory
        // (it's just planning metadata â€” doesn't need a real .workflowkit)
        globalRegistry = new BekaForge.WorkflowKit.Mcp.ProjectRegistry(globalRegistryPath);
    }
    catch
    {
        globalRegistry = null;
    }

    var host = new BekaForge.WorkflowKit.Mcp.McpHost(effectiveRoot, globalRegistry);
    host.Run();
}