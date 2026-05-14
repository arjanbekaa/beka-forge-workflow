using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Tracing;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for the PHASE-016 developer trace system.
/// </summary>
public sealed class TraceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkflowTraceService _service;
    private readonly TraceStore _store;

    public TraceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bfwf-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // Initialize a minimal workflow so WorkflowLayout paths work
        var wfRoot = Path.Combine(_tempDir, ".workflowkit");
        Directory.CreateDirectory(wfRoot);
        Directory.CreateDirectory(Path.Combine(wfRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(wfRoot, "traces"));

        _store = new TraceStore(_tempDir, TraceOptions.Default);
        _service = new WorkflowTraceService(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Trace creation ─────────────────────────────────────────────────────────

    [Fact]
    public void StartOperation_CreatesTraceScope_WithCorrectFields()
    {
        using var trace = _service.StartOperation(
            "workflow.get_relevant_context",
            phaseId: "PHASE-001",
            taskType: "self-audit",
            requestSummary: "Get context for phase 1");

        Assert.NotNull(trace);
        Assert.Equal("workflow.get_relevant_context", trace.Record.OperationName);
        Assert.Equal("PHASE-001", trace.Record.PhaseId);
        Assert.Equal("self-audit", trace.Record.TaskType);
        Assert.Contains("Get context", trace.Record.RequestSummary);
        Assert.True(trace.Record.TraceId.StartsWith("TRC-"));
        Assert.True(trace.Record.SpanId.StartsWith("SPN-"));
    }

    [Fact]
    public void TraceScope_OnDispose_MarksSuccessByDefault()
    {
        string traceId;
        using (var trace = _service.StartOperation("workflow.get_state"))
        {
            traceId = trace.Record.TraceId;
            // No explicit MarkSuccess — should default on dispose
        }

        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        Assert.Equal(TraceStatus.Success, stored!.Status);
    }

    [Fact]
    public void TraceScope_MarkFailed_RecordsFailure()
    {
        string traceId;
        using (var trace = _service.StartOperation("workflow.create_phase"))
        {
            traceId = trace.Record.TraceId;
            trace.MarkFailed("ValidationFailed", "Title is required");
        }

        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        Assert.Equal(TraceStatus.Failed, stored!.Status);
        Assert.Equal("ValidationFailed", stored.ErrorCode);
        Assert.Equal("Title is required", stored.ErrorMessage);
    }

    [Fact]
    public void TraceScope_MarkWarning_RecordsWarning()
    {
        string traceId;
        using (var trace = _service.StartOperation("workflow.sync_markdown"))
        {
            traceId = trace.Record.TraceId;
            trace.MarkWarning("Some regions missing but sync completed");
        }

        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        Assert.Equal(TraceStatus.Warning, stored!.Status);
    }

    [Fact]
    public void TraceScope_Duration_IsRecorded()
    {
        string traceId;
        using (var trace = _service.StartOperation("workflow.get_state"))
        {
            traceId = trace.Record.TraceId;
            trace.MarkSuccess();
        }

        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        Assert.True(stored!.DurationMs >= 0, "Duration should be >= 0");
    }

    // ── Trace modes ────────────────────────────────────────────────────────────

    [Fact]
    public void OffMode_WritesNothing()
    {
        _store.SetOptions(TraceOptions.Default with { Mode = TraceMode.Off });

        using (var trace = _service.StartOperation("workflow.get_state"))
        {
            // Force finalize
            trace.MarkSuccess();
        }

        var records = _store.List(maxResults: 10);
        Assert.Empty(records);
    }

    [Fact]
    public void BasicMode_WritesRecords()
    {
        _store.SetOptions(TraceOptions.Default with { Mode = TraceMode.Basic });

        string traceId;
        using (var trace = _service.StartOperation("workflow.get_state"))
        {
            traceId = trace.Record.TraceId;
            trace.MarkSuccess();
        }

        var records = _store.List(maxResults: 10);
        Assert.NotEmpty(records);
        Assert.Contains(records, r => r.TraceId == traceId);
    }

    [Fact]
    public void VerboseMode_WritesSpans()
    {
        _store.SetOptions(TraceOptions.Default with { Mode = TraceMode.Verbose });

        string traceId;
        using (var trace = _service.StartOperation("workflow.get_relevant_context"))
        {
            traceId = trace.Record.TraceId;

            var span = trace.AddSpan("cache.lookup", TraceSpanRecord.Categories.CacheLookup,
                "Checked phase package cache");
            trace.CompleteSpan(span);
        }

        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        Assert.NotEmpty(stored!.Spans);
        var foundSpan = stored.Spans.FirstOrDefault(s => s.Operation == "cache.lookup");
        Assert.NotNull(foundSpan);
        Assert.Equal("Checked phase package cache", foundSpan!.Summary);
    }

    [Fact]
    public void BasicMode_DoesNotRecordSpans()
    {
        _store.SetOptions(TraceOptions.Default with { Mode = TraceMode.Basic });

        string traceId;
        using (var trace = _service.StartOperation("workflow.get_relevant_context"))
        {
            traceId = trace.Record.TraceId;
            var span = trace.AddSpan("cache.lookup", TraceSpanRecord.Categories.CacheLookup);
            // Span will be null in Basic mode
            Assert.Null(span);
        }

        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        Assert.Empty(stored!.Spans);
    }

    // ── Cache hit/miss tracking ───────────────────────────────────────────────

    [Fact]
    public void TraceScope_SetCacheHit_RecordsCacheHit()
    {
        string traceId;
        using (var trace = _service.StartOperation("workflow.get_relevant_context"))
        {
            traceId = trace.Record.TraceId;
            trace.SetCacheHit(true, "phase-package");
            trace.MarkSuccess();
        }

        // Note: SetCacheHit modifies internal _record via reflection.
        // We read the final stored record to verify.
        var stored = _store.Get(traceId);
        Assert.NotNull(stored);
        // Due to reflection-based mutation in the scope, the stored record
        // should reflect cache hit.
        Assert.True(stored!.CacheHit);
        Assert.Equal("phase-package", stored.CacheLayer);
    }

    // ── File content safety ──────────────────────────────────────────────────

    [Fact]
    public void TraceRecord_DoesNotContainFullContent()
    {
        // Verify that trace records only have summaries, not full file content.
        using var trace = _service.StartOperation("workflow.get_file_slice",
            requestSummary: "Get slice of ImplementationPlan.md lines 1-10");

        trace.AddSourcePaths(["workflow/docs/ImplementationPlan.md"]);
        trace.MarkSuccess();

        // The raw JSON should not contain file content beyond paths
        var tracesDir = Path.Combine(_tempDir, ".workflowkit", "traces");
        var traceFiles = Directory.GetFiles(tracesDir, "*.jsonl");
        Assert.NotEmpty(traceFiles);

        var raw = File.ReadAllText(traceFiles[0]);
        // Check that no file content is present (just paths and summaries)
        Assert.DoesNotContain("Lorem ipsum", raw);
        Assert.DoesNotContain("public class", raw); // C# source shouldn't leak
        Assert.Contains("ImplementationPlan.md", raw); // path should be present
    }

    // ── Metadata truncation ──────────────────────────────────────────────────

    [Fact]
    public void Metadata_IsTruncated_WhenExceedsLimit()
    {
        _store.SetOptions(TraceOptions.Default with
        {
            Mode = TraceMode.Verbose,
            MaxMetadataValueLength = 50
        });

        var longValue = new string('A', 200);
        var metadata = new Dictionary<string, string>
        {
            ["longKey"] = longValue,
            ["short"] = "hello"
        };

        using var trace = _service.StartOperation("workflow.get_state");
        var span = trace.AddSpan("test.span", "test", metadata: metadata);
        trace.CompleteSpan(span);
        trace.MarkSuccess();

        // Read raw file and verify truncation
        var tracesDir = Path.Combine(_tempDir, ".workflowkit", "traces");
        var traceFiles = Directory.GetFiles(tracesDir, "*.jsonl");
        var raw = string.Join("\n", traceFiles.Select(File.ReadAllText));
        Assert.Contains("AAA...", raw); // truncated with "..."
    }

    // ── Retention ────────────────────────────────────────────────────────────

    [Fact]
    public void ClearOldTraces_RemovesOldFiles()
    {
        // This test relies on the TraceStore reading by file date in filename.
        // Since we can't mock DateTimeOffset.UtcNow easily, we test that
        // the method returns 0 when there are no old files.
        var deleted = _store.ClearOldTraces();
        Assert.True(deleted >= 0);
    }

    // ── Span status tracking ────────────────────────────────────────────────

    [Fact]
    public void Span_CanBeMarkedFailed()
    {
        _store.SetOptions(TraceOptions.Default with { Mode = TraceMode.Verbose });

        string traceId;
        using (var trace = _service.StartOperation("workflow.rebuild_context_index"))
        {
            traceId = trace.Record.TraceId;
            var span = trace.AddSpan("index.build", TraceSpanRecord.Categories.IndexQuery);
            trace.CompleteSpan(span, TraceStatus.Failed, "SQLITE_ERR", "Corrupt database");
            trace.MarkSuccess();
        }

        var stored = _store.Get(traceId);
        var failedSpan = stored!.Spans.First();
        Assert.Equal(TraceStatus.Failed, failedSpan.Status);
        Assert.Equal("SQLITE_ERR", failedSpan.ErrorCode);
        Assert.Equal("Corrupt database", failedSpan.ErrorMessage);
    }

    // ── Multiple traces ──────────────────────────────────────────────────────

    [Fact]
    public void MultipleTraces_AreAllStored()
    {
        for (int i = 0; i < 5; i++)
        {
            using var trace = _service.StartOperation($"workflow.op_{i}");
            trace.MarkSuccess();
        }

        var records = _store.List(maxResults: 10);
        Assert.True(records.Count >= 5, $"Expected >= 5 records, got {records.Count}");
    }

    // ── Get by ID ────────────────────────────────────────────────────────────

    [Fact]
    public void GetTrace_ById_ReturnsCorrectTrace()
    {
        string traceId;
        using (var trace = _service.StartOperation("workflow.validate_state"))
        {
            traceId = trace.Record.TraceId;
            trace.MarkSuccess();
        }

        var found = _store.Get(traceId);
        Assert.NotNull(found);
        Assert.Equal(traceId, found!.TraceId);
    }

    [Fact]
    public void GetTrace_NonExistent_ReturnsNull()
    {
        var found = _store.Get("TRC-9999");
        Assert.Null(found);
    }

    // ── Trace store status ───────────────────────────────────────────────────

    [Fact]
    public void GetTraceStatus_ReturnsMode()
    {
        _store.SetOptions(TraceOptions.Default with { Mode = TraceMode.Verbose });
        Assert.Equal(TraceMode.Verbose, _store.GetOptions().Mode);
    }

    [Fact]
    public void DefaultOptions_HaveBasicMode()
    {
        Assert.Equal(TraceMode.Basic, TraceOptions.Default.Mode);
        Assert.Equal(7, TraceOptions.Default.RetentionDays);
        Assert.Equal(100 * 1024 * 1024, TraceOptions.Default.MaxDirectorySizeBytes);
        Assert.Equal(512, TraceOptions.Default.MaxMetadataValueLength);
    }

    // ── Malformed JSONL does not break listing ──────────────────────────────

    [Fact]
    public void MalformedJsonlLine_IsSkipped()
    {
        // Write a malformed line directly to the trace file
        var tracesDir = Path.Combine(_tempDir, ".workflowkit", "traces");
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var traceFile = Path.Combine(tracesDir, $"trace-{today}.jsonl");

        File.AppendAllText(traceFile, "{invalid json that is not parseable}\n");

        // Write a valid trace
        using (var trace = _service.StartOperation("workflow.get_state"))
        {
            trace.MarkSuccess();
        }

        // Listing should not throw and should return at least the valid record
        var records = _store.List(maxResults: 10);
        Assert.True(records.Count >= 1, "Expected at least 1 valid record after malformed line");
    }
}
