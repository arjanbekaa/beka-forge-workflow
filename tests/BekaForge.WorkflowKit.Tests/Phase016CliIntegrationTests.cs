using BekaForge.WorkflowKit.Storage;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// PHASE-016: CLI integration tests — exercise the CLI end-to-end via process spawn.
/// Each test spawns bfwf.dll via dotnet and verifies exit code, stdout content, and side effects.
/// </summary>
public sealed class Phase016CliIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public Phase016CliIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-p016-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string CliDllPath => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "bfwf.dll");

    private (int exitCode, string stdout, string stderr) RunCli(string args, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{CliDllPath}\" {args}",
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = workingDir ?? _tempRoot
        };

        var process = Process.Start(psi)!;

        // Read stdout/stderr concurrently to avoid deadlock when buffers fill.
        string stdout = "", stderr = "";
        var outThread = new Thread(() => stdout = process.StandardOutput.ReadToEnd());
        var errThread = new Thread(() => stderr = process.StandardError.ReadToEnd());
        outThread.Start();
        errThread.Start();

        bool exited = process.WaitForExit(60_000);
        outThread.Join(5_000);
        errThread.Join(5_000);

        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            throw new TimeoutException($"bfwf CLI timed out after 60s. args: {args}");
        }

        int exitCode = process.ExitCode;
        process.Dispose();
        return (exitCode, stdout, stderr);
    }

    private string InitWorkflow(string? subDir = null)
    {
        var wfDir = subDir is null ? _tempRoot : Path.Combine(_tempRoot, subDir);
        if (subDir is not null) Directory.CreateDirectory(wfDir);
        var (code, _, _) = RunCli($"init \"TestAsset\" --root \"{wfDir}\"");
        Assert.Equal(0, code);
        return wfDir;
    }

    // ── init ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Init_CreatesWorkflowDirectory()
    {
        var dir = Path.Combine(_tempRoot, "my-project");
        Directory.CreateDirectory(dir);

        var (code, stdout, _) = RunCli($"init \"My Asset\" --root \"{dir}\"");

        Assert.Equal(0, code);
        Assert.True(WorkflowLayout.IsInitialized(dir));
    }

    [Fact]
    public void Init_WithForce_SucceedsOnExistingProject()
    {
        var dir = InitWorkflow("force-test");

        var (code, _, _) = RunCli($"init \"My Asset\" --root \"{dir}\" --force");

        Assert.Equal(0, code);
    }

    // ── help ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Help_ExitsZeroAndContainsCoreCommands()
    {
        var (code, stdout, _) = RunCli("help");

        Assert.Equal(0, code);
        Assert.Contains("bfwf init", stdout);
        Assert.Contains("bfwf status", stdout);
        Assert.Contains("bfwf phase", stdout);
        Assert.Contains("bfwf orchestration", stdout);
        Assert.Contains("bfwf doctor", stdout);
        Assert.Contains("press I inside the TUI to initialize the current folder", stdout);
    }

    [Fact]
    public void UnknownCommand_ShowsHelp()
    {
        var (code, stdout, _) = RunCli("totally-unknown-command-xyz");

        // falls through to help (exit 0)
        Assert.Equal(0, code);
        Assert.Contains("bfwf", stdout);
    }

    // ── status ────────────────────────────────────────────────────────────────

    [Fact]
    public void Status_Plain_ShowsWorkflowName()
    {
        var dir = InitWorkflow("status-test");

        var (code, stdout, _) = RunCli($"status --root \"{dir}\" --plain");

        Assert.Equal(0, code);
        Assert.Contains("TestAsset", stdout);
    }

    [Fact]
    public void Status_Json_ReturnsValidJson()
    {
        var dir = InitWorkflow("status-json");

        var (code, stdout, _) = RunCli($"status --root \"{dir}\" --json");

        Assert.Equal(0, code);
        // Should be parseable JSON
        var doc = JsonDocument.Parse(stdout.Trim());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Status_OutsideProject_ExitsZeroWithEmptyStatus()
    {
        // When no workflow root is found, bfwf status exits 0 with a minimal (no asset name) status.
        var emptyDir = Path.Combine(_tempRoot, "no-wf");
        Directory.CreateDirectory(emptyDir);

        var (code, stdout, _) = RunCli("status --plain", workingDir: emptyDir);

        Assert.Equal(0, code);
        // Should NOT show a phase-specific project name since no project is loaded
        Assert.DoesNotContain("TestAsset", stdout);
    }

    // ── phase create / list ────────────────────────────────────────────────────

    [Fact]
    public void PhaseCreate_ThenList_ShowsPhase()
    {
        var dir = InitWorkflow("phase-list");

        RunCli($"phase create --root \"{dir}\" --title \"My Feature\"");
        var (code, stdout, _) = RunCli($"phase list --root \"{dir}\" --plain");

        Assert.Equal(0, code);
        Assert.Contains("My Feature", stdout);
    }

    [Fact]
    public void PhaseCreate_Json_ReturnsPhaseId()
    {
        var dir = InitWorkflow("phase-json");

        var (code, stdout, _) = RunCli($"phase create --root \"{dir}\" --title \"JSON Phase\" --json");

        Assert.Equal(0, code);
        var doc = JsonDocument.Parse(stdout.Trim());
        Assert.True(doc.RootElement.TryGetProperty("data", out _) ||
                    doc.RootElement.TryGetProperty("phaseId", out _) ||
                    stdout.Contains("PHASE-"));
    }

    [Fact]
    public void PhaseCreate_WithSubPhasesJson_PersistsSubPhases()
    {
        var dir = InitWorkflow("phase-subphases");
        var json = "[{\\\"subPhaseId\\\":\\\"PHASE-001-A\\\",\\\"title\\\":\\\"Rules update\\\"},{\\\"subPhaseId\\\":\\\"PHASE-001-B\\\",\\\"title\\\":\\\"Planner context\\\",\\\"dependsOn\\\":[\\\"PHASE-001-A\\\"]}]";

        var (code, _, _) = RunCli(
            $"phase create --root \"{dir}\" --title \"Planner Phase\" --sub-phases-json \"{json}\"");

        Assert.Equal(0, code);

        var store = new WorkflowStore(dir);
        var phase = store.LoadPhase("PHASE-001")!;
        Assert.Equal(2, phase.SubPhases.Count);
        Assert.Equal("PHASE-001-A", phase.SubPhases[0].SubPhaseId);
        Assert.Equal("PHASE-001-A", phase.SubPhases[1].DependsOn.Single());
    }

    [Fact]
    public void PhaseDefer_ThenFocus_UpdatesCurrentPhaseAndDeferredMetadata()
    {
        var dir = InitWorkflow("phase-defer-focus");
        RunCli($"phase create --root \"{dir}\" --title \"Phase One\"");
        RunCli($"phase create --root \"{dir}\" --title \"Phase Two\"");

        var (focusCode, _, _) = RunCli($"phase focus --root \"{dir}\" --phase PHASE-001 --reason \"Start with phase one\"");
        Assert.Equal(0, focusCode);

        var (deferCode, _, _) = RunCli($"phase defer --root \"{dir}\" --phase PHASE-001 --reason \"Continue phase two first\" --continue-with PHASE-002");
        Assert.Equal(0, deferCode);

        var store = new WorkflowStore(dir);
        Assert.Equal("PHASE-002", store.LoadWorkflow().CurrentPhaseId);
        Assert.Equal("Continue phase two first", store.LoadPhase("PHASE-001")!.DeferredReason);

        var (resumeCode, _, _) = RunCli($"phase focus --root \"{dir}\" --phase PHASE-001 --reason \"Resume the parked phase\"");
        Assert.Equal(0, resumeCode);

        Assert.Equal("PHASE-001", store.LoadWorkflow().CurrentPhaseId);
        Assert.Null(store.LoadPhase("PHASE-001")!.DeferredReason);
    }

    // ── log commands ──────────────────────────────────────────────────────────

    [Fact]
    public void LogImplementation_AdvancesPhaseState()
    {
        var dir = InitWorkflow("log-impl");
        RunCli($"phase create --root \"{dir}\" --title \"Impl Phase\"");
        RunCli($"phase status --root \"{dir}\" --phase PHASE-001 --state ReadyForImplementation");
        RunCli($"phase status --root \"{dir}\" --phase PHASE-001 --state AssignedToImplementation");
        RunCli($"phase status --root \"{dir}\" --phase PHASE-001 --state InImplementation");

        var (code, _, _) = RunCli($"log implementation --root \"{dir}\" --phase PHASE-001 --summary \"Done\"");

        Assert.Equal(0, code);

        var store = new WorkflowStore(dir);
        var phase = store.LoadPhase("PHASE-001")!;
        Assert.Equal(BekaForge.WorkflowKit.Core.PhaseState.ImplementationLogged, phase.State);
    }

    [Fact]
    public void ContextInject_PlannerRole_RendersPlannerGuidance()
    {
        var dir = InitWorkflow("context-planner");
        RunCli($"phase create --root \"{dir}\" --title \"Planner Phase\"");

        var (code, stdout, _) = RunCli($"context inject --root \"{dir}\" --phase PHASE-001 --role Planner");

        Assert.Equal(0, code);
        Assert.Contains("You are the **Planner**", stdout);
        Assert.Contains("sub-phases", stdout);
        Assert.Contains("parallel", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextInject_PlannerRole_RendersExecutionLanes()
    {
        var dir = InitWorkflow("context-planner-lanes");
        var subPhasesJson = "[{\\\"subPhaseId\\\":\\\"PHASE-001-A\\\",\\\"title\\\":\\\"Core lane\\\"},{\\\"subPhaseId\\\":\\\"PHASE-001-B\\\",\\\"title\\\":\\\"CLI lane\\\",\\\"dependsOn\\\":[\\\"PHASE-001-A\\\"]}]";
        var executionLanesJson = "[{\\\"laneId\\\":\\\"LANE-A\\\",\\\"title\\\":\\\"Core lane\\\",\\\"phaseIds\\\":[\\\"PHASE-001\\\"],\\\"subPhaseIds\\\":[\\\"PHASE-001-A\\\"],\\\"ownedAreas\\\":[\\\"src/BekaForge.WorkflowKit.Core\\\"]},{\\\"laneId\\\":\\\"LANE-B\\\",\\\"title\\\":\\\"CLI lane\\\",\\\"phaseIds\\\":[\\\"PHASE-001\\\"],\\\"subPhaseIds\\\":[\\\"PHASE-001-B\\\"],\\\"dependsOnLaneIds\\\":[\\\"LANE-A\\\"],\\\"ownedAreas\\\":[\\\"src/BekaForge.WorkflowKit.Cli\\\"]}]";

        var (createCode, _, _) = RunCli(
            $"phase create --root \"{dir}\" --title \"Planner Phase\" --objective \"Plan lanes\" --scope \"Planner guidance\" --sub-phases-json \"{subPhasesJson}\" --execution-lanes-json \"{executionLanesJson}\"");
        Assert.Equal(0, createCode);

        var (code, stdout, _) = RunCli($"context inject --root \"{dir}\" --phase PHASE-001 --role Planner");

        Assert.Equal(0, code);
        Assert.Contains("Execution lanes", stdout);
        Assert.Contains("LANE-A", stdout);
        Assert.Contains("Depends on lanes: `LANE-A`", stdout);
    }

    [Fact]
    public void OrchestrationStart_ThenAttentionStatus_ReturnsDerivedOutcome()
    {
        var dir = InitWorkflow("orchestration-attention");
        RunCli($"phase create --root \"{dir}\" --title \"Runtime Phase\" --objective \"Orchestrate\" --scope \"Test orchestration\"");
        RunCli($"phase status --root \"{dir}\" --phase PHASE-001 --state ReadyForImplementation");

        var (startCode, startOut, _) = RunCli($"orchestration start --root \"{dir}\" --phase PHASE-001 --json");
        Assert.Equal(0, startCode);
        var sessionDoc = JsonDocument.Parse(startOut.Trim());
        var sessionId = sessionDoc.RootElement.GetProperty("sessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var (attentionCode, attentionOut, _) = RunCli($"orchestration attention --root \"{dir}\" --session {sessionId} --json");
        Assert.Equal(0, attentionCode);
        var attentionDoc = JsonDocument.Parse(attentionOut.Trim());
        var outcome = attentionDoc.RootElement.GetProperty("derivedAttentionOutcome");
        Assert.True(
            (outcome.ValueKind == JsonValueKind.String && outcome.GetString() == "ReadyToContinue")
            || (outcome.ValueKind == JsonValueKind.Number && outcome.GetInt32() == 8));
    }

    [Fact]
    public void DocsSet_ThenSyncMarkdown_UpdatesKnownLimitations()
    {
        var dir = InitWorkflow("docs-guidance");

        var (setCode, _, _) = RunCli(
            $"docs set --root \"{dir}\" --section known-limitations --content \"Known limitation: release still requires HumanOwner sign-off.\"");
        Assert.Equal(0, setCode);

        var (syncCode, _, _) = RunCli($"sync-markdown --root \"{dir}\"");
        Assert.Equal(0, syncCode);

        var content = File.ReadAllText(Path.Combine(dir, ".workflowkit", "workflow", "docs", "KnownLimitations.md"));
        Assert.Contains("HumanOwner sign-off", content);
    }

    [Fact]
    public void RulesGenerate_CreatesThinRootSummaryPointingToCanonicalRules()
    {
        var dir = InitWorkflow("rules-generate");
        RunCli($"phase create --root \"{dir}\" --title \"Active Phase\" --objective \"Summarize active work\" --scope \"Root rules summary\" --required-files-or-areas \"src/Foo.cs\"");
        RunCli($"phase status --root \"{dir}\" --phase PHASE-001 --state ReadyForImplementation");
        RunCli($"phase assign --root \"{dir}\" --phase PHASE-001 --agent Implementer");
        RunCli($"phase status --root \"{dir}\" --phase PHASE-001 --state InImplementation");

        var (code, _, _) = RunCli($"rules generate --root \"{dir}\"");

        Assert.Equal(0, code);
        var content = File.ReadAllText(Path.Combine(dir, "WORKFLOW_RULES.md"));
        Assert.Contains("Canonical workflow rules live in `.workflowkit/workflow/Rules.md`.", content);
        Assert.Contains("Treat this file as a quick project summary only.", content);
        Assert.Contains("## Active Coordination", content);
        Assert.Contains("### PHASE-001 - Active Phase", content);
        Assert.DoesNotContain("State machine is enforced.", content);
    }

    [Fact]
    public void LogUnknownType_ExitsNonZero()
    {
        var dir = InitWorkflow("log-bad");
        RunCli($"phase create --root \"{dir}\" --title \"Phase\"");

        var (code, _, _) = RunCli($"log bogustype --root \"{dir}\" --phase PHASE-001 --summary \"x\"");

        Assert.NotEqual(0, code);
    }

    // ── blocker add / resolve ─────────────────────────────────────────────────

    [Fact]
    public void BlockerAdd_ThenResolve_PhaseAutoAdvances()
    {
        var dir = InitWorkflow("blocker-flow");
        RunCli($"phase create --root \"{dir}\" --title \"Blocked Phase\"");

        var (addCode, addOut, _) = RunCli($"blocker add --root \"{dir}\" --phase PHASE-001 --reason \"Waiting on API\"");
        Assert.Equal(0, addCode);

        var store = new WorkflowStore(dir);
        Assert.Equal(BekaForge.WorkflowKit.Core.PhaseState.Blocked, store.LoadPhase("PHASE-001")!.State);

        // Get blocker ID from store
        var blocker = store.ReadAllBlockers().First();

        var (resolveCode, _, _) = RunCli($"blocker resolve --root \"{dir}\" --blocker-id {blocker.BlockerId} --resolution \"Fixed\"");
        Assert.Equal(0, resolveCode);

        // Auto-advance should have fired
        Assert.Equal(BekaForge.WorkflowKit.Core.PhaseState.ReadyForImplementation, store.LoadPhase("PHASE-001")!.State);
    }

    // ── doctor ────────────────────────────────────────────────────────────────

    [Fact]
    public void Doctor_Plain_ExitsZeroAndShowsOkChecks()
    {
        var dir = InitWorkflow("doctor-test");

        var (code, stdout, _) = RunCli($"doctor --root \"{dir}\" --plain");

        Assert.Equal(0, code);
        Assert.Contains("Doctor", stdout);
        Assert.Contains("OK", stdout);
        Assert.Contains("Initialized", stdout);
    }

    [Fact]
    public void Doctor_Strict_ExitsZeroOnInitializedProject()
    {
        // --strict adds extra checks but doesn't fail a freshly initialized project
        var dir = InitWorkflow("doctor-strict");

        var (code, _, _) = RunCli($"doctor --root \"{dir}\" --strict");

        Assert.Equal(0, code);
    }

    // ── repair ────────────────────────────────────────────────────────────────

    [Fact]
    public void Repair_IsIdempotentOnHealthyProject()
    {
        var dir = InitWorkflow("repair-test");

        var (code1, _, _) = RunCli($"repair --root \"{dir}\"");
        var (code2, _, _) = RunCli($"repair --root \"{dir}\"");

        Assert.Equal(0, code1);
        Assert.Equal(0, code2);
    }

    // ── validate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ExitsZeroOnInitializedProject()
    {
        var dir = InitWorkflow("validate-test");

        var (code, _, _) = RunCli($"validate --root \"{dir}\"");

        Assert.Equal(0, code);
    }

    // ── manifest ──────────────────────────────────────────────────────────────

    [Fact]
    public void Manifest_Json_ReturnsJsonWithPhaseList()
    {
        var dir = InitWorkflow("manifest-test");
        RunCli($"phase create --root \"{dir}\" --title \"Manifest Phase\"");

        var (code, stdout, _) = RunCli($"manifest --root \"{dir}\" --json");

        Assert.Equal(0, code);
        var doc = JsonDocument.Parse(stdout.Trim());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
