using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests.StorageTests;

/// <summary>
/// Tests for JsonlAppender — append-only JSONL log files.
/// </summary>
public sealed class JsonlAppenderTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlAppenderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bfwf-jsonl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string LogPath(string name) => Path.Combine(_tempDir, name);

    private WorkflowEvent MakeEvent(string id, string summary) => new()
    {
        EventId = id,
        EventType = "test.event",
        Actor = WorkflowActor.Codex,
        Summary = summary
    };

    // -- Append --------------------------------------------------------------------

    [Fact]
    public void Append_CreatesFileIfNotExists()
    {
        var path = LogPath("events.jsonl");
        JsonlAppender.Append(path, MakeEvent("EVT-001", "first"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Append_CreatesParentDirectoryIfMissing()
    {
        var path = Path.Combine(_tempDir, "subdir", "events.jsonl");
        JsonlAppender.Append(path, MakeEvent("EVT-001", "first"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Append_SingleRecord_IsReadBackCorrectly()
    {
        var path = LogPath("events.jsonl");
        var evt = MakeEvent("EVT-001", "first event");
        JsonlAppender.Append(path, evt);

        var all = JsonlAppender.ReadAll<WorkflowEvent>(path);
        Assert.Single(all);
        Assert.Equal("EVT-001", all[0].EventId);
        Assert.Equal("first event", all[0].Summary);
    }

    [Fact]
    public void Append_MultipleRecords_AllAreReadBack()
    {
        var path = LogPath("events.jsonl");

        for (int i = 1; i <= 5; i++)
            JsonlAppender.Append(path, MakeEvent($"EVT-00{i}", $"event {i}"));

        var all = JsonlAppender.ReadAll<WorkflowEvent>(path);
        Assert.Equal(5, all.Count);
        for (int i = 1; i <= 5; i++)
        {
            Assert.Equal($"EVT-00{i}", all[i - 1].EventId);
            Assert.Equal($"event {i}", all[i - 1].Summary);
        }
    }

    [Fact]
    public void Append_IsAppendOnly_PreviousLinesArePreserved()
    {
        var path = LogPath("events.jsonl");
        JsonlAppender.Append(path, MakeEvent("EVT-001", "first"));

        // Capture state after first append.
        var afterFirst = File.ReadAllText(path);
        Assert.Contains("EVT-001", afterFirst);

        // Second append must not disturb the first line.
        JsonlAppender.Append(path, MakeEvent("EVT-002", "second"));
        var afterSecond = File.ReadAllText(path);

        // Both lines must be present.
        Assert.Contains("EVT-001", afterSecond);
        Assert.Contains("EVT-002", afterSecond);

        // The first line must be exactly the same as before.
        var firstLineAfter = afterSecond.Split('\n')[0].TrimEnd('\r');
        var firstLineBefore = afterFirst.TrimEnd('\n', '\r');
        Assert.Equal(firstLineBefore, firstLineAfter);
    }

    [Fact]
    public void Append_EachRecordIsOnOneLine()
    {
        var path = LogPath("events.jsonl");
        JsonlAppender.Append(path, MakeEvent("EVT-001", "first"));
        JsonlAppender.Append(path, MakeEvent("EVT-002", "second"));

        var lines = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        Assert.Equal(2, lines.Length);
        // Each line must be valid JSON (starts with '{' and ends with '}')
        foreach (var line in lines)
        {
            Assert.StartsWith("{", line.Trim());
            Assert.EndsWith("}", line.Trim());
        }
    }

    [Fact]
    public void Append_EnumSerializedAsString()
    {
        var path = LogPath("events.jsonl");
        JsonlAppender.Append(path, MakeEvent("EVT-001", "check enum"));

        var raw = File.ReadAllText(path);
        // Actor enum should be serialized as a string (camelCase), not as a number.
        Assert.DoesNotMatch(@"""actor""\s*:\s*\d", raw);
        Assert.Contains("codex", raw); // camelCase enum
    }

    // -- ReadAll -------------------------------------------------------------------

    [Fact]
    public async Task Append_RetriesWhenFileIsTemporarilyLockedByAnotherWriter()
    {
        var path = LogPath("events.jsonl");
        JsonlAppender.Append(path, MakeEvent("EVT-001", "first"));

        using var locked = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var appendTask = Task.Run(() => JsonlAppender.Append(path, MakeEvent("EVT-002", "second")));

        await Task.Delay(100);
        locked.Dispose();
        await appendTask;

        var all = JsonlAppender.ReadAll<WorkflowEvent>(path);
        Assert.Equal(2, all.Count);
        Assert.Equal("EVT-002", all[1].EventId);
    }

    [Fact]
    public void ReadAll_FileNotExists_ReturnsEmpty()
    {
        var path = LogPath("nonexistent.jsonl");
        var result = JsonlAppender.ReadAll<WorkflowEvent>(path);
        Assert.Empty(result);
    }

    [Fact]
    public void ReadAll_EmptyFile_ReturnsEmpty()
    {
        var path = LogPath("empty.jsonl");
        File.WriteAllText(path, "");
        var result = JsonlAppender.ReadAll<WorkflowEvent>(path);
        Assert.Empty(result);
    }

    // -- Round-trip of WorkflowEvent fields ----------------------------------------

    [Fact]
    public void Append_WorkflowEvent_RoundTripsAllFields()
    {
        var path = LogPath("events.jsonl");
        var now = DateTimeOffset.UtcNow;
        var evt = new WorkflowEvent
        {
            EventId = "EVT-042",
            EventType = "phase.status.changed",
            Actor = WorkflowActor.DeepSeek,
            Timestamp = now,
            PhaseId = "PHASE-003",
            Summary = "Phase moved to PASS",
            PayloadReference = "IMP-007"
        };
        JsonlAppender.Append(path, evt);

        var loaded = JsonlAppender.ReadAll<WorkflowEvent>(path);
        Assert.Single(loaded);
        var r = loaded[0];
        Assert.Equal("EVT-042", r.EventId);
        Assert.Equal("phase.status.changed", r.EventType);
        Assert.Equal(WorkflowActor.DeepSeek, r.Actor);
        Assert.Equal("PHASE-003", r.PhaseId);
        Assert.Equal("Phase moved to PASS", r.Summary);
        Assert.Equal("IMP-007", r.PayloadReference);
    }
}
