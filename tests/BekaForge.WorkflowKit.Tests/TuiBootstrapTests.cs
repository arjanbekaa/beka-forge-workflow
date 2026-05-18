using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class TuiBootstrapTests : IDisposable
{
    private readonly string _tempRoot;

    public TuiBootstrapTests()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath()) ?? Path.GetTempPath();
        _tempRoot = Path.Combine(driveRoot, $"bfwf-tui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void DeriveAssetName_UsesLeafDirectoryName()
    {
        var dir = Path.Combine(_tempRoot, "Example Project");
        Directory.CreateDirectory(dir);

        Assert.Equal("Example Project", TuiBootstrap.DeriveAssetName(dir));
    }

    [Fact]
    public void BuildUninitializedDetailText_IncludesFolderNameAndInitShortcut()
    {
        var dir = Path.Combine(_tempRoot, "Bootstrap Target");
        Directory.CreateDirectory(dir);

        var text = TuiBootstrap.BuildUninitializedDetailText(dir);

        Assert.Contains("Bootstrap Target", text);
        Assert.Contains("Press I to initialize", text);
        Assert.Contains("I initialize workflow", text);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("yes", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    public void ShouldInitialize_UsesYesByDefault(string? response, bool expected)
    {
        Assert.Equal(expected, TuiBootstrap.ShouldInitialize(response));
    }

    [Fact]
    public void EnsureWorkflowReady_Decline_DoesNotInitialize()
    {
        var startDir = Path.Combine(_tempRoot, "NoInit");
        Directory.CreateDirectory(startDir);

        var result = TuiBootstrap.EnsureWorkflowReady(
            startDir,
            new StringReader("n" + Environment.NewLine),
            new StringWriter());

        Assert.True(result.Cancelled);
        Assert.Null(result.WorkflowRoot);
        Assert.False(WorkflowLayout.IsInitialized(startDir));
    }

    [Fact]
    public void EnsureWorkflowReady_Accept_InitializesAndSyncsRules()
    {
        var startDir = Path.Combine(_tempRoot, "My App");
        Directory.CreateDirectory(startDir);

        var output = new StringWriter();
        var result = TuiBootstrap.EnsureWorkflowReady(
            startDir,
            new StringReader(Environment.NewLine),
            output);

        Assert.False(result.Cancelled);
        Assert.True(result.InitializedNow);
        Assert.Equal(Path.GetFullPath(startDir), result.WorkflowRoot);
        Assert.True(WorkflowLayout.IsInitialized(startDir));
        Assert.True(File.Exists(WorkflowLayout.RulesMdPath(startDir)));
        Assert.Contains("Initializing workflow", output.ToString());
        Assert.Contains("Beka Forge Workflow initialized for this folder.", output.ToString());
    }

    [Fact]
    public void InitializeWorkflowInFolder_UsesFolderNameAndIsIdempotent()
    {
        var startDir = Path.Combine(_tempRoot, "Idempotent App");
        Directory.CreateDirectory(startDir);

        var root1 = TuiBootstrap.InitializeWorkflowInFolder(startDir);
        var root2 = TuiBootstrap.InitializeWorkflowInFolder(startDir);

        Assert.Equal(Path.GetFullPath(startDir), root1);
        Assert.Equal(root1, root2);
        Assert.True(WorkflowLayout.IsInitialized(startDir));

        var store = new WorkflowStore(startDir);
        Assert.Equal("Idempotent App", store.LoadWorkflow().AssetName);
    }

    [Fact]
    public void EnsureWorkflowReady_ExistingWorkflow_DoesNotPromptAgain()
    {
        var startDir = Path.Combine(_tempRoot, "Existing App");
        Directory.CreateDirectory(startDir);
        new WorkflowInitializer(startDir).Initialize("Existing App");

        var output = new StringWriter();
        var result = TuiBootstrap.EnsureWorkflowReady(
            startDir,
            new StringReader("n" + Environment.NewLine),
            output);

        Assert.False(result.Cancelled);
        Assert.False(result.InitializedNow);
        Assert.Equal(Path.GetFullPath(startDir), result.WorkflowRoot);
        Assert.DoesNotContain("Set it up now?", output.ToString());
    }

    [Fact]
    public void EnsureWorkflowReady_ExplicitRoot_DoesNotBindToInitializedParent()
    {
        var parentRoot = Path.Combine(_tempRoot, "ParentRoot");
        Directory.CreateDirectory(parentRoot);
        new WorkflowInitializer(parentRoot).Initialize("Parent");

        var childDir = Path.Combine(parentRoot, "Child App");
        Directory.CreateDirectory(childDir);

        var result = TuiBootstrap.EnsureWorkflowReady(
            childDir,
            new StringReader(Environment.NewLine),
            new StringWriter(),
            allowAncestorDiscovery: false);

        Assert.Equal(Path.GetFullPath(childDir), result.WorkflowRoot);
        Assert.True(result.InitializedNow);
        Assert.True(WorkflowLayout.IsInitialized(childDir));
    }
}
