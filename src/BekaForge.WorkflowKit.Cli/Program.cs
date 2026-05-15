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

Program.CommandLineArgs = args;
var commandLineArgs = Program.CommandLineArgs;
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
string? validationRequirements = ParseFlag(commandLineArgs, "--validation-requirements")
    ?? ParseFlag(commandLineArgs, "--unity-test-requirements"); // legacy alias
string? parallelizationNotes = ParseFlag(commandLineArgs, "--parallelization-notes");
string? architectureConstraints = ParseFlag(commandLineArgs, "--architecture-constraints");
string? requiredFilesOrAreas = ParseFlag(commandLineArgs, "--required-files-or-areas");
string? acceptanceCriteria = ParseFlag(commandLineArgs, "--acceptance-criteria");
string? dependsOnPhaseIds = ParseFlag(commandLineArgs, "--depends-on-phase-ids");
string? requiresValidation = ParseFlag(commandLineArgs, "--requires-validation");
string? subPhaseId = ParseFlag(commandLineArgs, "--sub-phase");
string? subPhaseSummary = ParseFlag(commandLineArgs, "--sub-summary");
string? subPhaseDependencies = ParseFlag(commandLineArgs, "--sub-dependencies");
string? subPhasesJson = ParseFlag(commandLineArgs, "--sub-phases-json");
string? budgetMode = ParseFlag(commandLineArgs, "--budget");
string? modeOverrides = ParseFlag(commandLineArgs, "--mode-overrides");
string? passed = ParseFlag(commandLineArgs, "--passed");
string? recommendations = ParseFlag(commandLineArgs, "--recommendations");
string? needs = ParseFlag(commandLineArgs, "--needs");   // PHASE-003: phase list filter
bool stuck = HasFlag(commandLineArgs, "--stuck");         // PHASE-003: phase list --stuck filter
string? role = ParseFlag(commandLineArgs, "--role");      // PHASE-003: preflight role
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

// -- Command dispatch ------------------------------------------------------------

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
        CmdLog(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, summary, notes, passed, recommendations);
        break;

    case "validation":
        CmdValidation(commandLineArgs, workflowRoot);
        break;

    case "phase":
        CmdPhase(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, title, state, reason, agent, summary,
            dependencies, objective, scope, outOfScope, implementationNotes, auditRequirements, validationRequirements,
            parallelizationNotes, architectureConstraints, requiredFilesOrAreas, acceptanceCriteria, dependsOnPhaseIds,
            requiresValidation, subPhaseId, subPhaseSummary, subPhaseDependencies, subPhasesJson,
            jsonOutput, outputMode, watchOutput, watchIntervalSeconds, needs, stuck);
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
        if (commandLineArgs.Length > 1 && string.Equals(commandLineArgs[1], "inject", StringComparison.OrdinalIgnoreCase))
            CmdContextInject(workflowRoot, phase, role, outputMode);
        else
            CmdContext(workflowRoot, phase, jsonOutput, outputMode, budgetMode);
        break;

    case "budget":
        CmdBudget(workflowRoot, budgetMode, modeOverrides, jsonOutput, outputMode);
        break;

    case "validate-request":
        CmdValidateRequest(workflowRoot, operation, phase, targetActor, jsonOutput, outputMode);
        break;

    case "work":
        CmdWork(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, role, jsonOutput, outputMode);
        break;

    case "metrics":
        CmdMetrics(commandLineArgs.Length > 1 ? commandLineArgs[1] : "", workflowRoot, phase, jsonOutput, outputMode);
        break;

    case "rules":
        if (commandLineArgs.Length > 1 && string.Equals(commandLineArgs[1], "generate", StringComparison.OrdinalIgnoreCase))
            CmdRulesGenerate(workflowRoot, phase, outputMode);
        else
        {
            Console.Error.WriteLine("Usage: bfwf rules generate [--phase PHASE-NNN]");
            Environment.Exit(2);
        }
        break;

    case "next":
        CmdNext(workflowRoot, phase, jsonOutput, outputMode);
        break;

    case "preflight":
        CmdPreflight(workflowRoot, phase, role, jsonOutput, outputMode);
        break;

    case "doctor":
        CmdDoctor(workflowRoot, jsonOutput, outputMode, HasFlag(commandLineArgs, "--strict"));
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

void PrintHelp()
{
    Console.WriteLine("Beka Forge Workflow CLI");
    Console.WriteLine();
    Console.WriteLine("Usage: bfwf <command> [options]");
    Console.WriteLine("Future alias: bfk (planned, not implemented yet)");
    Console.WriteLine();
    Console.WriteLine("Project Setup ------------------------------------------");
    Console.WriteLine("  bfwf init \"Asset Name\" [--root <path>] [--force]");
    Console.WriteLine("  bfwf install-agent-rules [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Project Status ----------------------------------------");
    Console.WriteLine("  bfwf status [--root <path>] [--plain] [--watch] [--interval 5]");
    Console.WriteLine("  bfwf tui    [--root <path>] [--interval 5]   (interactive dashboard; auto-setup/start)");
    Console.WriteLine("  bfwf validate [--root <path>]");
    Console.WriteLine("  bfwf doctor [--root <path>] [--json] [--strict]");
    Console.WriteLine("  bfwf next [--phase PHASE-NNN] [--root <path>] [--json]");
    Console.WriteLine("  bfwf preflight --phase PHASE-NNN --role Implementer|Auditor|Reviewer|Validator [--json]");
    Console.WriteLine("  bfwf metrics [--phase PHASE-NNN] [--json]");
    Console.WriteLine("  bfwf metrics bottleneck [--json]");
    Console.WriteLine("  bfwf work begin --phase PHASE-NNN --role <role> [--force]");
    Console.WriteLine("  bfwf work end   [--phase PHASE-NNN] [--force]");
    Console.WriteLine("  bfwf work list  [--active-only]");
    Console.WriteLine("  bfwf mcp [--root <path>]     (MCP stdio host; omit --root for global mode)");
    Console.WriteLine();
    Console.WriteLine("Phases & Logs -----------------------------------------");
    Console.WriteLine("  bfwf phase create --title \"...\" [--phase PHASE-NNN] [--summary \"...\"]");
    Console.WriteLine("  bfwf phase show [--phase PHASE-NNN] [--watch] [--interval 5]");
    Console.WriteLine("  bfwf phase list [--needs audit|review|validation|fix] [--stuck]");
    Console.WriteLine("  bfwf phase check-conflicts [--phase PHASE-NNN]");
    Console.WriteLine("  bfwf phase reopen --phase PHASE-NNN --reason \"...\"    (recovery: FailedValidation/Architecture/Compile/Blocked â†’ ReadyForImplementation)");
    Console.WriteLine("  bfwf phase drift-check [--phase PHASE-NNN] [--threshold-hours 24]");
    Console.WriteLine("  bfwf phase manifest [--phase PHASE-NNN]");
    Console.WriteLine("  bfwf phase contract show --phase PHASE-NNN [--json]");
    Console.WriteLine("  bfwf phase contract save --phase PHASE-NNN --objective \"...\" --scope \"...\" [--out-of-scope \"...\"] [--criteria \"a,b\"] [--constraints \"x,y\"] [--required-files \"src/Foo,src/Bar\"] [--notes \"...\"] [--audit-requirements \"...\"] [--validation-requirements \"...\"]");
    Console.WriteLine("  bfwf phase update --phase PHASE-NNN [--summary \"...\"] [--dependencies \"PHASE-001,PHASE-002\"]");
    Console.WriteLine("  bfwf phase remove --phase PHASE-NNN");
    Console.WriteLine("  bfwf phase status --phase PHASE-NNN --state <PhaseState> [--reason \"...\"]");
    Console.WriteLine("  bfwf phase assign --phase PHASE-NNN --agent <Planner|Implementer|Auditor|...>");
    Console.WriteLine("  bfwf sub-phase update --phase PHASE-NNN --sub-phase PHASE-NNN-A <status>");
    Console.WriteLine("  bfwf log implementation --phase PHASE-NNN --summary \"...\" [--notes \"...\"]");
    Console.WriteLine("  bfwf log audit --phase PHASE-NNN --summary \"...\" [--passed true/false] [--recommendations \"rec1; rec2\"]");
    Console.WriteLine("  bfwf log review --phase PHASE-NNN --summary \"...\" [--passed true/false] [--recommendations \"rec1; rec2\"]");
    Console.WriteLine("  bfwf log test --phase PHASE-NNN --summary \"...\" [--passed true/false]");
    Console.WriteLine("  bfwf log fix --phase PHASE-NNN --summary \"...\"");
    Console.WriteLine();
    Console.WriteLine("Validation --------------------------------------------");
    Console.WriteLine("  bfwf validation plan --phase PHASE-NNN");
    Console.WriteLine("  bfwf validation run  --phase PHASE-NNN --command \"<cmd>\" [--timeout 120] [--advance] [--summary \"...\"]");
    Console.WriteLine("  bfwf validation log  --phase PHASE-NNN --type <type> --result <result> --summary \"...\"");
    Console.WriteLine("                        [--evidence '<json>']  (raw JSON array: [{\"description\":\"...\",\"source\":0,\"reference\":\"...\"}])");
    Console.WriteLine("                        [--evidence-description \"...\" --evidence-source <Agent|HumanOwner|Command|Tool|CI> --evidence-reference \"...\"]");
    Console.WriteLine("  bfwf validation request-user --phase PHASE-NNN [--manual-steps \"...\"]");
    Console.WriteLine("  bfwf validation complete-user --phase PHASE-NNN --result Passed --summary \"...\"");
    Console.WriteLine("                        [--evidence '<json>' | --evidence-description \"...\" --evidence-source HumanOwner]");
    Console.WriteLine("  bfwf validation skip --phase PHASE-NNN --reason \"...\" [--approved-by HumanOwner]");
    Console.WriteLine();
    Console.WriteLine("Blockers ----------------------------------------------");
    Console.WriteLine("  bfwf blocker add --phase PHASE-NNN --reason \"...\"");
    Console.WriteLine("  bfwf blocker resolve --blocker-id BLK-NNN [--resolution \"...\"]");
    Console.WriteLine();
    Console.WriteLine("Server ------------------------------------------------");
    Console.WriteLine("  bfwf server start [--root <path>] [--port <num>]");
    Console.WriteLine("  bfwf server stop");
    Console.WriteLine("  bfwf server status [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Operations --------------------------------------------");
    Console.WriteLine("  bfwf manifest [--root <path>]");
    Console.WriteLine("  bfwf recommend --task \"...\" [--phase PHASE-NNN] [--root <path>]");
    Console.WriteLine("  bfwf context [--phase PHASE-NNN] [--root <path>]");
    Console.WriteLine("  bfwf context inject --phase PHASE-NNN --role Implementer|Auditor|Reviewer|Validator [--json]");
    Console.WriteLine("  bfwf rules generate  [--root <path>]    (generates WORKFLOW_RULES.md)");
    Console.WriteLine("  bfwf budget [--budget Low|Medium|High|Full] [--mode-overrides <json>] [--root <path>]");
    Console.WriteLine("  bfwf validate-request --operation \"...\" [--phase PHASE-NNN] [--actor <name>]");
    Console.WriteLine();
    Console.WriteLine("Inbox -------------------------------------------------");
    Console.WriteLine("  bfwf process-inbox [--root <path>] [--plain]");
    Console.WriteLine("  bfwf inbox-status [--root <path>] [--plain]");
    Console.WriteLine("  bfwf audit-paths [--root <path>]");
    Console.WriteLine("  bfwf repair [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Git & Sessions ----------------------------------------");
    Console.WriteLine("  bfwf git status [--root <path>]");
    Console.WriteLine("  bfwf git commits [--max 50] [--phase PHASE-NNN] [--since \"2026-01-01\"]");
    Console.WriteLine("  bfwf git health");
    Console.WriteLine("  bfwf git record [--session-id SES-0001]");
    Console.WriteLine("  bfwf session list [--active-only]");
    Console.WriteLine("  bfwf session current");
    Console.WriteLine("  bfwf session end [--session-id SES-0001]");
    Console.WriteLine("  bfwf timeline [--max 50] [--phase PHASE-NNN] [--since \"2026-01-01\"] [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Cache -------------------------------------------------");
    Console.WriteLine("  bfwf cache-status [--root <path>] [--plain]");
    Console.WriteLine("  bfwf cache-clear [--root <path>]");
    Console.WriteLine();
    Console.WriteLine("Diagnostics -------------------------------------------");
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

partial class Program
{
    // Static field populated from the top-level entry point so partial class
    // methods in other files can access the original command-line args.
    internal static string[] CommandLineArgs { get; set; } = [];

    // -- Helpers ---------------------------------------------------------------------

    internal static void WriteJson(object? value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, CreateCompactJsonOptions()));

    internal static JsonElement ToJsonElement(object? value) =>
        JsonSerializer.SerializeToElement(value, CreateCompactJsonOptions());

    internal static JsonSerializerOptions CreateCompactJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    internal static JsonSerializerOptions CreatePrettyJsonOptions() => new()
    {
        WriteIndented = true
    };

    internal static string GetString(JsonElement element, string propertyName, string fallback = "")
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

    internal static int GetInt32(JsonElement element, string propertyName)
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

    internal static long GetInt64(JsonElement element, string propertyName)
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

    internal static bool GetBool(JsonElement element, string propertyName)
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

    internal static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
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

    internal static string? ParseFlag(string[] values, string flag)
    {
        for (int i = 0; i < values.Length - 1; i++)
        {
            if (string.Equals(values[i], flag, StringComparison.OrdinalIgnoreCase))
                return values[i + 1];
        }
        return null;
    }

    internal static bool HasFlag(string[] values, string flag) =>
        values.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    internal static int? ParseIntFlag(string[] values, string flag)
    {
        var val = ParseFlag(values, flag);
        return val is not null && int.TryParse(val, out var n) ? n : null;
    }

    internal static string? DiscoverWorkflowRoot(string startDir)
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

    internal static WorkflowActor ParseActor(string name) => name?.ToLowerInvariant() switch
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

    internal static void CmdMcp(string? wfRoot, string[] cmdArgs)
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
            // (it's just planning metadata - doesn't need a real .workflowkit)
            globalRegistry = new BekaForge.WorkflowKit.Mcp.ProjectRegistry(globalRegistryPath);
        }
        catch
        {
            globalRegistry = null;
        }

        var host = new BekaForge.WorkflowKit.Mcp.McpHost(effectiveRoot, globalRegistry);
        host.Run();
    }
}
