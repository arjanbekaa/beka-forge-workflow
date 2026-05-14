using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Markdown;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.MarkdownTests;

/// <summary>
/// Integration tests for <see cref="MarkdownSyncService"/> and the
/// <c>workflow.sync_markdown</c> operation handler.
///
/// Each test uses a fresh temp workflow root with real storage.
/// </summary>
public sealed class MarkdownSyncServiceTests : IDisposable
{
    private readonly string            _tempRoot;
    private readonly WorkflowStore     _store;
    private readonly OperationDispatcher _dispatcher;

    public MarkdownSyncServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-md-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("MarkdownTestAsset");
        _store      = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private OperationResult Dispatch(string operation,
        string? phaseId = null,
        WorkflowActor actor = WorkflowActor.Codex,
        Dictionary<string, object?>? parameters = null) =>
        _dispatcher.Dispatch(new OperationContext
        {
            Operation  = operation,
            Actor      = actor,
            PhaseId    = phaseId,
            Parameters = parameters ?? []
        });

    // ── SyncMarkdown is registered ────────────────────────────────────────────

    [Fact]
    public void SyncMarkdown_IsRegistered()
    {
        Assert.Contains(WorkflowOperations.SyncMarkdown, _dispatcher.RegisteredOperations);
    }

    // ── Initial sync on empty workflow ────────────────────────────────────────

    [Fact]
    public void SyncAll_EmptyWorkflow_CreatesAgentsMdAndWorkflowMd()
    {
        var service = new MarkdownSyncService(_store);
        var written = service.SyncAll();

        string agentsMd   = WorkflowLayout.AgentsMdPath(_tempRoot);
        string workflowMd = WorkflowLayout.WorkflowMdPath(_tempRoot);
        string rulesMd = WorkflowLayout.RulesMdPath(_tempRoot);

        Assert.True(File.Exists(agentsMd),   "AGENTS.md should be created");
        Assert.True(File.Exists(workflowMd), "workflow.md should be created");
        Assert.True(File.Exists(rulesMd), "workflow/Rules.md should be created");
    }

    [Fact]
    public void SyncAll_EmptyWorkflow_CreatesFileFirstDocumentationLayout()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        Assert.True(File.Exists(WorkflowLayout.ArchitectureMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.ImplementationMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.MigrationNotesMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.ExtractionAuditMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.KnownLimitationsMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.ExtensionGuideMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.ConsistencyCheckMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.FinalReviewMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.PromptHeaderMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.ImplementationLogMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.FixLogMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.AuditLogMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.ReviewLogMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.TestingLogMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.CurrentStatusMdPath(_tempRoot)));
    }

    [Fact]
    public void SyncAll_EmptyWorkflow_AgentsMdContainsGeneratedRegion()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.AgentsMdPath(_tempRoot));
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.AgentsRoles), content);
        Assert.Contains(MarkdownRegion.End(MarkdownRegion.AgentsRoles),   content);
        Assert.Contains("workflow/Rules.md", content);
        Assert.DoesNotContain("BekaWorkflowSystemPrompt.md", content);
    }

    [Fact]
    public void SyncAll_EmptyWorkflow_RulesMdContainsCanonicalInstructions()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.RulesMdPath(_tempRoot));
        Assert.Contains("Read this `workflow/Rules.md` file completely.", content);
        Assert.Contains("All writes to `.workflowkit/` must go through CLI commands (`bfwf`)", content);
        Assert.Contains("Root agent files such as `AGENTS.md` are user-owned.", content);
    }

    [Fact]
    public void SyncAll_EmptyWorkflow_WorkflowMdContainsAssetName()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.WorkflowMdPath(_tempRoot));
        Assert.Contains("MarkdownTestAsset", content);
    }

    [Fact]
    public void SyncAll_CurrentStatusMdContainsProgress()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Phase A" });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.CurrentStatusMdPath(_tempRoot));
        Assert.Contains("Overall progress", content);
        Assert.Contains("PHASE-001", content);
    }

    [Fact]
    public void SyncAll_PromptHeader_InstructsAgentsToReadSystemPrompt()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.PromptHeaderMdPath(_tempRoot));
        Assert.Contains("Read `workflow/Rules.md` first", content);
        Assert.Contains(".workflowkit/", content);
    }

    [Fact]
    public void SyncAll_CompatibilityPrompt_PointsToRulesFile()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.BekaWorkflowSystemPromptPath(_tempRoot));
        Assert.Contains("canonical Beka Forge Workflow instructions now live in `workflow/Rules.md`", content);
        Assert.Contains("Read `workflow/Rules.md` first", content);
    }

    [Fact]
    public void SyncAll_ImplementationLog_UsesStrictEntryFormat()
    {
        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-002",
            Actor = WorkflowActor.Codex,
            Title = "Short implementation title",
            Summary = "What was implemented",
            Status = PhaseState.ImplementationLogged,
            FilesModified = ["src/Foo.cs"],
            Details = ["Concrete change"],
            RelatedFixIds = ["FIX-001"],
            ValidationBuild = "passed",
            ValidationTests = "passed",
            ValidationManualCheck = "notRun",
            Notes = "Any caveats"
        });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.ImplementationLogMdPath(_tempRoot));
        Assert.Contains("## IMP-001 - PHASE-002 - Short implementation title", content);
        Assert.Contains("**Date:**", content);
        Assert.Contains("**Phase:** PHASE-002", content);
        Assert.Contains("**Related fixes:** FIX-001", content);
        Assert.Contains("- Build: passed", content);
    }

    [Fact]
    public void SyncAll_FixLog_UsesStrictEntryFormat()
    {
        _store.AppendFix(new FixRecord
        {
            FixId = "FIX-001",
            PhaseId = "PHASE-002",
            Actor = WorkflowActor.Codex,
            Title = "Fix title",
            Summary = "What changed",
            Problem = "What was wrong",
            RelatedReviewId = "REV-001",
            FilesModified = ["src/Foo.cs"],
            Verification = "Build passed"
        });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.FixLogMdPath(_tempRoot));
        Assert.Contains("## FIX-001 - PHASE-002 - Fix title", content);
        Assert.Contains("**Fixes review:** REV-001", content);
        Assert.Contains("### Problem", content);
        Assert.Contains("Build passed", content);
    }

    // ── Per-phase markdown generation ─────────────────────────────────────────

    [Fact]
    public void SyncAll_WithOnePhase_CreatesPhaseMdFile()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Domain Model" });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string phaseMd = WorkflowLayout.PhaseMdPath(_tempRoot, "PHASE-001");
        Assert.True(File.Exists(phaseMd), "PHASE-001.md should be created");
    }

    [Fact]
    public void SyncAll_PhaseMd_ContainsAllRequiredRegions()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Domain Model" });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.PhaseMdPath(_tempRoot, "PHASE-001"));

        // All six phase-level regions must be present.
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.PhaseContract),     content);
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.ImplementationLog), content);
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.AuditLog),          content);
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.ReviewLog),         content);
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.TestingLog),        content);
        Assert.Contains(MarkdownRegion.Begin(MarkdownRegion.CurrentStatus),     content);
    }

    [Fact]
    public void SyncAll_PhaseMd_ShowsPhaseTitle()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "My Domain" });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string content = File.ReadAllText(WorkflowLayout.PhaseMdPath(_tempRoot, "PHASE-001"));
        Assert.Contains("My Domain", content);
    }

    // ── Human content is preserved ────────────────────────────────────────────

    [Fact]
    public void SyncAll_HumanContentOutsideRegion_IsPreserved()
    {
        // Write a markdown file that has human text outside any markers.
        string phaseMdPath = WorkflowLayout.PhaseMdPath(_tempRoot, "PHASE-001");
        Directory.CreateDirectory(Path.GetDirectoryName(phaseMdPath)!);

        const string humanText = "# My Hand-Written Heading\n\nThis is my personal notes.\n\n";
        File.WriteAllText(phaseMdPath, humanText);

        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Domain" });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        string result = File.ReadAllText(phaseMdPath);

        Assert.Contains("# My Hand-Written Heading", result);
        Assert.Contains("This is my personal notes.", result);
    }

    [Fact]
    public void SyncAll_HumanTextBetweenRegions_IsPreserved()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Phase" });

        // First sync to create the file with generated regions.
        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        // Add human text after the phase-contract region.
        string phaseMdPath = WorkflowLayout.PhaseMdPath(_tempRoot, "PHASE-001");
        string existing    = File.ReadAllText(phaseMdPath);
        string contractEnd = MarkdownRegion.End(MarkdownRegion.PhaseContract);
        string withHuman   = existing.Replace(contractEnd,
            contractEnd + "\n\n> ⚡ Important note from me.\n\n");
        File.WriteAllText(phaseMdPath, withHuman);

        // Second sync — human text must survive.
        service.SyncAll();
        string result = File.ReadAllText(phaseMdPath);

        Assert.Contains("> ⚡ Important note from me.", result);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public void SyncAll_CalledTwice_SecondCallWritesNothing()
    {
        var service = new MarkdownSyncService(_store);
        service.SyncAll(); // warm-up

        var written = service.SyncAll(); // should be idempotent
        Assert.Empty(written);
    }

    // ── HumanSectionPreserver unit tests ──────────────────────────────────────

    [Fact]
    public void Preserver_EmptyFile_AppendsSections()
    {
        var preserver = new HumanSectionPreserver();
        var sections  = new Dictionary<string, string>
        {
            ["my-section"] = "Hello world"
        };

        string result = preserver.Merge(string.Empty, sections);

        Assert.Contains(MarkdownRegion.Begin("my-section"), result);
        Assert.Contains("Hello world",                      result);
        Assert.Contains(MarkdownRegion.End("my-section"),   result);
    }

    [Fact]
    public void Preserver_ExistingRegion_ReplacesGeneratedContent()
    {
        var preserver = new HumanSectionPreserver();

        string initial = MarkdownRegion.Wrap("my-section", "Old content");

        string result = preserver.Merge(initial, new Dictionary<string, string>
        {
            ["my-section"] = "New content"
        });

        Assert.Contains("New content", result);
        Assert.DoesNotContain("Old content", result);
    }

    [Fact]
    public void Preserver_HumanTextBeforeMarker_IsUntouched()
    {
        var preserver = new HumanSectionPreserver();

        string initial = "# Title\n\nHuman preamble.\n\n"
                       + MarkdownRegion.Wrap("sec", "Generated");

        string result = preserver.Merge(initial, new Dictionary<string, string>
        {
            ["sec"] = "Updated"
        });

        Assert.StartsWith("# Title", result);
        Assert.Contains("Human preamble.", result);
        Assert.Contains("Updated",         result);
    }

    [Fact]
    public void Preserver_NoSections_ReturnsOriginalContent()
    {
        var preserver = new HumanSectionPreserver();
        const string original = "# Title\n\nSome human text.";

        string result = preserver.Merge(original, new Dictionary<string, string>());

        Assert.Equal(original, result);
    }

    [Fact]
    public void Preserver_UnknownSection_IsIgnored()
    {
        var preserver = new HumanSectionPreserver();

        // File has "region-a" but we're only supplying "region-b" data.
        string initial = MarkdownRegion.Wrap("region-a", "Alpha");

        string result = preserver.Merge(initial, new Dictionary<string, string>
        {
            ["region-b"] = "Beta"
        });

        // region-a is preserved; region-b is appended.
        Assert.Contains("Alpha",  result);
        Assert.Contains("Beta",   result);
    }

    // ── Human-authored content preservation through full sync cycle ───────────

    [Fact]
    public void SyncAll_HumanCanaryNote_OutsideGeneratedRegions_IsPreserved()
    {
        // Pre-setup: create a phase and an implementation record
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Phase 1",
            State = PhaseState.InImplementation
        });
        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.DeepSeek,
            Summary = "Initial implementation",
            Status = PhaseState.ImplementationLogged
        });

        var service = new MarkdownSyncService(_store);
        service.SyncAll();

        // Inject a human canary note OUTSIDE the generated regions
        string agentsMdPath = WorkflowLayout.AgentsMdPath(_tempRoot);
        string existing = File.ReadAllText(agentsMdPath);
        string canaryNote = "\n<!-- HUMAN CANARY: do not delete this line -->\n> 🐦 This is a human-authored note that must survive every sync.\n";
        string agentsEndMarker = MarkdownRegion.End(MarkdownRegion.AgentsRoles);
        string withCanary = existing.Replace(agentsEndMarker,
            agentsEndMarker + canaryNote);
        File.WriteAllText(agentsMdPath, withCanary);

        // Also inject canary in ImplementationPlan.md
        string implPlanPath = WorkflowLayout.ImplementationMdPath(_tempRoot);
        if (File.Exists(implPlanPath))
        {
            string implPlanContent = File.ReadAllText(implPlanPath);
            string implPlanEndMarker = MarkdownRegion.End(MarkdownRegion.ImplementationPlan);
            string implCanary = implPlanContent.Replace(implPlanEndMarker,
                implPlanEndMarker + "\n\n> 📋 Human planning note: review features before Phase 7.\n");
            File.WriteAllText(implPlanPath, implCanary);
        }

        // Force a state change to trigger regeneration
        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            CurrentPhaseId = "PHASE-001"
        });

        // Run a full sync cycle
        var written = service.SyncAll();

        // Assert: canary notes survived
        string resultAgents = File.ReadAllText(agentsMdPath);
        Assert.Contains("🐦 This is a human-authored note that must survive every sync.", resultAgents);
        Assert.Contains("HUMAN CANARY: do not delete this line", resultAgents);

        if (File.Exists(implPlanPath))
        {
            string resultImplPlan = File.ReadAllText(implPlanPath);
            Assert.Contains("📋 Human planning note: review features before Phase 7.", resultImplPlan);
        }
    }

    // ── Dispatcher integration ────────────────────────────────────────────────

    [Fact]
    public void DispatchSyncMarkdown_EmptyWorkflow_ReturnsSuccess()
    {
        var result = Dispatch(WorkflowOperations.SyncMarkdown);
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void DispatchSyncMarkdown_WithPhase_CreatesPhaseMd()
    {
        Dispatch(WorkflowOperations.CreatePhase, parameters: new() { ["title"] = "Alpha" });
        Dispatch(WorkflowOperations.SyncMarkdown);

        string phaseMd = WorkflowLayout.PhaseMdPath(_tempRoot, "PHASE-001");
        Assert.True(File.Exists(phaseMd));
    }
}
