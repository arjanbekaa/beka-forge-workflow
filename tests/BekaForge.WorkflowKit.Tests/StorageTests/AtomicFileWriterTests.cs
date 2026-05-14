using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.StorageTests;

/// <summary>
/// Tests for AtomicFileWriter — write-to-temp-then-rename strategy.
/// </summary>
public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bfwf-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Write_CreatesFileWithCorrectContent()
    {
        var path = Path.Combine(_tempDir, "test.json");
        AtomicFileWriter.Write(path, "{\"key\":\"value\"}");

        Assert.True(File.Exists(path));
        Assert.Equal("{\"key\":\"value\"}", File.ReadAllText(path));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "overwrite.json");
        AtomicFileWriter.Write(path, "original");
        AtomicFileWriter.Write(path, "updated");

        Assert.Equal("updated", File.ReadAllText(path));
    }

    [Fact]
    public void Write_CreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDir, "subdir", "deep.json");
        AtomicFileWriter.Write(nested, "content");

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Write_DoesNotLeaveTemporaryFiles()
    {
        var path = Path.Combine(_tempDir, "state.json");
        AtomicFileWriter.Write(path, "{}");

        // After a successful write, no .tmp_ files should remain.
        var tempFiles = Directory.GetFiles(_tempDir, ".tmp_*");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public void Write_MultipleCalls_EachOverwritesPrevious()
    {
        var path = Path.Combine(_tempDir, "seq.json");
        for (int i = 1; i <= 5; i++)
        {
            AtomicFileWriter.Write(path, $"version{i}");
        }

        Assert.Equal("version5", File.ReadAllText(path));
    }

    [Fact]
    public void Write_LargeContent_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "large.json");
        var content = new string('x', 100_000);
        AtomicFileWriter.Write(path, content);

        Assert.Equal(content, File.ReadAllText(path));
    }
}
