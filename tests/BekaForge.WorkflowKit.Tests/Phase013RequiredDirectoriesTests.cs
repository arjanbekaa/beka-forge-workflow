using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// PHASE-013: Verifies that WorkflowLayout.RequiredDirectories includes the
/// work-session lock directory and evidence parent directory, and that
/// WorkflowInitializer pre-creates them on init.
/// </summary>
public sealed class Phase013RequiredDirectoriesTests : IDisposable
{
    private readonly string _tempRoot;

    public Phase013RequiredDirectoriesTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-p013-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void RequiredDirectories_IncludesWorkDir()
    {
        var dirs = WorkflowLayout.RequiredDirectories(_tempRoot);
        var expected = WorkflowLayout.WorkDirPath(_tempRoot);

        Assert.Contains(expected, dirs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredDirectories_IncludesEvidenceParentDir()
    {
        var dirs = WorkflowLayout.RequiredDirectories(_tempRoot);
        // Evidence parent is .workflowkit/evidence/
        var expected = Path.Combine(WorkflowLayout.Root(_tempRoot), "evidence");

        Assert.Contains(expected, dirs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowInitializer_CreatesWorkDir()
    {
        new WorkflowInitializer(_tempRoot).Initialize("TestAsset");

        var workDir = WorkflowLayout.WorkDirPath(_tempRoot);
        Assert.True(Directory.Exists(workDir),
            $"Expected work dir to exist after init: {workDir}");
    }

    [Fact]
    public void WorkflowInitializer_CreatesEvidenceParentDir()
    {
        new WorkflowInitializer(_tempRoot).Initialize("TestAsset");

        var evidenceDir = Path.Combine(WorkflowLayout.Root(_tempRoot), "evidence");
        Assert.True(Directory.Exists(evidenceDir),
            $"Expected evidence dir to exist after init: {evidenceDir}");
    }

    [Fact]
    public void RequiredDirectories_AllExistAfterInit()
    {
        new WorkflowInitializer(_tempRoot).Initialize("TestAsset");

        var missing = WorkflowLayout.RequiredDirectories(_tempRoot)
            .Where(d => !Directory.Exists(d))
            .ToList();

        Assert.Empty(missing);
    }
}
