using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.StorageTests;

/// <summary>
/// Tests for WorkflowInitializer — creates .bekaforge/ layout and initial workflow.json.
/// Each test uses an isolated temp directory that is cleaned up after the test.
/// </summary>
public sealed class WorkflowInitializerTests : IDisposable
{
    private readonly string _tempRoot;

    public WorkflowInitializerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Initialize_CreatesBekaForgeDirectory()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");
        Assert.True(Directory.Exists(WorkflowLayout.Root(_tempRoot)));
    }

    [Fact]
    public void Initialize_CreatesAllRequiredDirectories()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");

        foreach (var dir in WorkflowLayout.RequiredDirectories(_tempRoot))
        {
            Assert.True(Directory.Exists(dir), $"Expected directory to exist: {dir}");
        }
    }

    [Fact]
    public void Initialize_CreatesAllMarkdownDirectories()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");

        foreach (var dir in WorkflowLayout.RequiredMarkdownDirectories(_tempRoot))
        {
            Assert.True(Directory.Exists(dir), $"Expected markdown directory to exist: {dir}");
        }
    }

    [Fact]
    public void Initialize_CreatesStarterMarkdownFiles()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");

        Assert.True(File.Exists(WorkflowLayout.AgentsMdPath(_tempRoot)));
        Assert.Equal(Path.Combine(_tempRoot, "AGENTS.md"), WorkflowLayout.AgentsMdPath(_tempRoot));
        Assert.True(File.Exists(WorkflowLayout.ClaudeMdPath(_tempRoot)));
        Assert.Equal(Path.Combine(_tempRoot, "CLAUDE.md"), WorkflowLayout.ClaudeMdPath(_tempRoot));
        Assert.StartsWith(Path.Combine(_tempRoot, "workflow"), WorkflowLayout.WorkflowMdPath(_tempRoot));
        Assert.StartsWith(Path.Combine(_tempRoot, "workflow"), WorkflowLayout.ArchitectureMdPath(_tempRoot));
        Assert.StartsWith(Path.Combine(_tempRoot, "workflow"), WorkflowLayout.ImplementationLogMdPath(_tempRoot));
        Assert.True(File.Exists(WorkflowLayout.WorkflowMdPath(_tempRoot)));
        Assert.True(File.Exists(WorkflowLayout.RulesMdPath(_tempRoot)));
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
    public void Initialize_CreatesRulesFileAndThinAgentsPointer()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");

        var rules = File.ReadAllText(WorkflowLayout.RulesMdPath(_tempRoot));
        var agents = File.ReadAllText(WorkflowLayout.AgentsMdPath(_tempRoot));

        Assert.Contains("workflowkit-system-prompt", rules);
        Assert.Contains("workflow/Rules.md", agents);
        Assert.DoesNotContain("All `.workflowkit/` writes must go through CLI", agents);
    }

    [Fact]
    public void Initialize_CreatesWorkflowJsonFile()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");
        Assert.True(File.Exists(WorkflowLayout.WorkflowFile(_tempRoot)));
    }

    [Fact]
    public void Initialize_WorkflowJsonContainsCorrectAssetName()
    {
        var init = new WorkflowInitializer(_tempRoot);
        var state = init.Initialize("BekaForgeWeaponSystem");

        Assert.Equal("BekaForgeWeaponSystem", state.AssetName);
    }

    [Fact]
    public void Initialize_WorkflowJsonContainsSchemaVersion()
    {
        var init = new WorkflowInitializer(_tempRoot);
        var state = init.Initialize("TestAsset");

        Assert.Equal(WorkflowLayout.SchemaVersion, state.SchemaVersion);
    }

    [Fact]
    public void Initialize_WorkflowJsonContainsAbsoluteRootPath()
    {
        var init = new WorkflowInitializer(_tempRoot);
        var state = init.Initialize("TestAsset");

        Assert.Equal(Path.GetFullPath(_tempRoot), state.RootPath);
    }

    [Fact]
    public void Initialize_GeneratesUniqueWorkflowId()
    {
        var init1 = new WorkflowInitializer(_tempRoot);
        var state1 = init1.Initialize("Asset1");

        var tempRoot2 = Path.Combine(Path.GetTempPath(), $"bfwf-test-{Guid.NewGuid():N}");
        try
        {
            var init2 = new WorkflowInitializer(tempRoot2);
            var state2 = init2.Initialize("Asset2");
            Assert.NotEqual(state1.WorkflowId, state2.WorkflowId);
        }
        finally
        {
            if (Directory.Exists(tempRoot2))
                Directory.Delete(tempRoot2, recursive: true);
        }
    }

    [Fact]
    public void Initialize_WhenAlreadyInitialized_ThrowsInvalidOperation()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("FirstAsset");

        Assert.Throws<InvalidOperationException>(() => init.Initialize("SecondAsset"));
    }

    [Fact]
    public void Initialize_WithForceTrue_OverwritesExistingWorkflow()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("FirstAsset");
        var state = init.Initialize("SecondAsset", force: true);

        Assert.Equal("SecondAsset", state.AssetName);
    }

    [Fact]
    public void Initialize_WithForceTrue_PreservesExistingFiles()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("FirstAsset");

        // Place a human-written file in the root — it must survive reinit.
        var customFile = Path.Combine(_tempRoot, "NOTES.md");
        File.WriteAllText(customFile, "# Human notes");

        init.Initialize("SecondAsset", force: true);

        Assert.True(File.Exists(customFile), "Human-written files must not be deleted on reinit.");
    }

    [Fact]
    public void IsInitialized_ReturnsFalse_BeforeInit()
    {
        var init = new WorkflowInitializer(_tempRoot);
        Assert.False(init.IsInitialized());
    }

    [Fact]
    public void IsInitialized_ReturnsTrue_AfterInit()
    {
        var init = new WorkflowInitializer(_tempRoot);
        init.Initialize("TestAsset");
        Assert.True(init.IsInitialized());
    }

    [Fact]
    public void Initialize_EmptyAssetName_ThrowsArgumentException()
    {
        var init = new WorkflowInitializer(_tempRoot);
        Assert.Throws<ArgumentException>(() => init.Initialize(""));
        Assert.Throws<ArgumentException>(() => init.Initialize("   "));
    }
}
