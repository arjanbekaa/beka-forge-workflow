using BekaForge.WorkflowKit.Dashboard.Wpf;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class ProjectRegistryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _registryPath;

    public ProjectRegistryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _registryPath = Path.Combine(_tempRoot, "projects.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void AddOrUpdate_SavesProjectByNameAndPath()
    {
        var registry = new ProjectRegistry(_registryPath);
        var projectPath = Path.Combine(_tempRoot, "ProjectA");
        Directory.CreateDirectory(projectPath);

        registry.AddOrUpdate("Project A", projectPath);

        var project = Assert.Single(registry.Load());
        Assert.Equal("Project A", project.Name);
        Assert.Equal(Path.GetFullPath(projectPath), project.Path);
    }

    [Fact]
    public void AddOrUpdate_ReplacesExistingPath()
    {
        var registry = new ProjectRegistry(_registryPath);
        var projectPath = Path.Combine(_tempRoot, "ProjectA");
        Directory.CreateDirectory(projectPath);

        registry.AddOrUpdate("Old", projectPath);
        registry.AddOrUpdate("New", projectPath);

        var project = Assert.Single(registry.Load());
        Assert.Equal("New", project.Name);
    }

    [Fact]
    public void Remove_RemovesOnlyMatchingPath()
    {
        var registry = new ProjectRegistry(_registryPath);
        var pathA = Path.Combine(_tempRoot, "A");
        var pathB = Path.Combine(_tempRoot, "B");
        Directory.CreateDirectory(pathA);
        Directory.CreateDirectory(pathB);
        registry.AddOrUpdate("A", pathA);
        registry.AddOrUpdate("B", pathB);

        registry.Remove(pathA);

        var project = Assert.Single(registry.Load());
        Assert.Equal("B", project.Name);
    }
}
