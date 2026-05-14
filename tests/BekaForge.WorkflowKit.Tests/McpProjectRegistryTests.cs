using BekaForge.WorkflowKit.Mcp;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class McpProjectRegistryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _registryFilePath;
    private readonly string _projectRoot;

    public McpProjectRegistryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-mcp-reg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _registryFilePath = Path.Combine(_tempRoot, "mcp-registry.json");
        _projectRoot = Path.Combine(_tempRoot, "ProjectA");
        new WorkflowInitializer(_projectRoot).Initialize("Registry Test Project");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Add_ExplicitFilePath_SavesAndResolvesProject()
    {
        var registry = new ProjectRegistry(_registryFilePath);

        registry.Add("project-a", _projectRoot);

        Assert.Equal(Path.GetFullPath(_projectRoot), registry.Resolve("project-a"));
        Assert.True(File.Exists(_registryFilePath));
        Assert.Equal(_registryFilePath, registry.RegistryPath);
    }

    [Fact]
    public void Add_WorkflowRoot_LocatesRegistryUnderWorkflowKitFolder()
    {
        var registry = new ProjectRegistry(_tempRoot);

        registry.Add("project-a", _projectRoot);

        var expectedPath = Path.Combine(_tempRoot, WorkflowLayout.WorkflowKitDir, "mcp-registry.json");
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(expectedPath, registry.RegistryPath);
    }

    [Fact]
    public void IsValidWorkflowRoot_RecognizesInitializedProject()
    {
        Assert.True(ProjectRegistry.IsValidWorkflowRoot(_projectRoot));
        Assert.False(ProjectRegistry.IsValidWorkflowRoot(_tempRoot));
    }
}
