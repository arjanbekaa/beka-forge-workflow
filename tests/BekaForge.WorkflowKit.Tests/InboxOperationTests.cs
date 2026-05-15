using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for PHASE-019: Handler-Only Writes and Offline Operation Queue.
/// Covers pending operation schema, inbox processing pipeline,
/// idempotency, protected path audit, and consistency repair.
/// </summary>
public sealed class InboxOperationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;
    private readonly JsonSerializerOptions _jsonOptions;

    public InboxOperationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-inbox-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("InboxTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -- Helpers ----------------------------------------------------------

    private OperationContext Ctx(string operation, string? phaseId = null) =>
        new() { Operation = operation, Actor = WorkflowActor.Codex, PhaseId = phaseId };

    private string CreatePendingOperationFile(string operationName, string idempotencyKey,
        string actor = "implementer", string? phaseId = null,
        Dictionary<string, object?>? parameters = null)
    {
        var inboxDir = WorkflowLayout.InboxDir(_tempRoot);
        Directory.CreateDirectory(inboxDir);

        var pending = new PendingOperation
        {
            OperationName = operationName,
            Actor = actor,
            PhaseId = phaseId,
            Parameters = parameters ?? [],
            IdempotencyKey = idempotencyKey,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        var path = Path.Combine(inboxDir, $"{idempotencyKey}.operation.json");
        var json = JsonSerializer.Serialize(pending, _jsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    private int CountJsonlRecords(string path) =>
        File.Exists(path) ? File.ReadAllLines(path).Length : 0;

    // -- PendingOperation DTO ---------------------------------------------

    [Fact]
    public void PendingOperation_Construction_HasRequiredFields()
    {
        var op = new PendingOperation
        {
            OperationName = "workflow.get_state",
            Actor = "implementer",
            IdempotencyKey = "idem-001"
        };

        Assert.Equal("workflow.get_state", op.OperationName);
        Assert.Equal("implementer", op.Actor);
        Assert.Equal("idem-001", op.IdempotencyKey);
        Assert.NotEqual(default, op.CreatedUtc);
    }

    [Fact]
    public void PendingOperation_FileName_UsesIdempotencyKey()
    {
        var op = new PendingOperation
        {
            OperationName = "workflow.get_state",
            Actor = "implementer",
            IdempotencyKey = "abc-123"
        };

        Assert.Equal("abc-123.operation", op.FileName);
        Assert.Equal("abc-123.operation.json", op.FileNameWithExtension);
    }

    // -- InboxStatus DTO --------------------------------------------------

    [Fact]
    public void InboxStatus_Construction_HasRequiredFields()
    {
        var status = new InboxStatus
        {
            PendingCount = 3,
            ProcessedCount = 5,
            FailedCount = 1,
            OldestPendingUtc = DateTimeOffset.UtcNow.AddHours(-1),
            InboxAvailable = true,
            PendingFiles = new[] { "id1.operation.json", "id2.operation.json" }
        };

        Assert.Equal(3, status.PendingCount);
        Assert.Equal(5, status.ProcessedCount);
        Assert.Equal(1, status.FailedCount);
        Assert.True(status.InboxAvailable);
        Assert.Equal(2, status.PendingFiles.Count);
    }

    // -- Inbox processor: GetStatus ---------------------------------------

    [Fact]
    public void InboxProcessor_GetStatus_ReportsPendingCount()
    {
        CreatePendingOperationFile("workflow.get_state", "key-001");
        CreatePendingOperationFile("workflow.get_current_phase", "key-002");

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var status = processor.GetStatus();

        Assert.Equal(2, status.PendingCount);
        Assert.True(status.InboxAvailable);
        Assert.Equal(2, status.PendingFiles.Count);
    }

    [Fact]
    public void InboxProcessor_GetStatus_ReportsEmptyInbox()
    {
        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var status = processor.GetStatus();

        Assert.Equal(0, status.PendingCount);
        Assert.Equal(0, status.ProcessedCount);
        Assert.Equal(0, status.FailedCount);
    }

    // -- Inbox processor: ProcessAll - valid operation ---------------------

    [Fact]
    public void ProcessAll_ValidReadOperation_Succeeds()
    {
        CreatePendingOperationFile("workflow.get_state", "valid-001");

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var result = processor.ProcessAll();

        Assert.True(result.Processed);
        Assert.Equal(1, result.TotalPending);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);

        // Pending file should be gone
        var inboxDir = WorkflowLayout.InboxDir(_tempRoot);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.operation.json"));

        // Processed file should exist
        var processedDir = WorkflowLayout.InboxProcessedDir(_tempRoot);
        Assert.True(Directory.Exists(processedDir));
        var processed = Directory.GetFiles(processedDir, "*.processed.json");
        Assert.Single(processed);
    }

    // -- Inbox processor: ProcessAll - idempotency -------------------------

    [Fact]
    public void ProcessAll_DuplicateIdempotencyKey_SkipsSecond()
    {
        CreatePendingOperationFile("workflow.get_state", "dup-key");

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var firstResult = processor.ProcessAll();
        CreatePendingOperationFile("workflow.get_current_phase", "dup-key");
        var secondResult = processor.ProcessAll();

        Assert.True(firstResult.Processed);
        Assert.Equal(1, firstResult.TotalPending);
        Assert.Equal(1, firstResult.Succeeded);
        Assert.Equal(0, firstResult.Failed);
        Assert.Equal(0, firstResult.Skipped);

        Assert.True(secondResult.Processed);
        Assert.Equal(1, secondResult.TotalPending);
        Assert.Equal(0, secondResult.Succeeded);
        Assert.Equal(0, secondResult.Failed);
        Assert.Equal(1, secondResult.Skipped);

        // No pending files remain
        var inboxDir = WorkflowLayout.InboxDir(_tempRoot);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.operation.json"));
    }

    // -- Inbox processor: ProcessAll - failed operation --------------------

    [Fact]
    public void ProcessAll_UnknownOperation_Fails()
    {
        CreatePendingOperationFile("workflow.nonexistent_op", "bad-op");

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var result = processor.ProcessAll();

        Assert.True(result.Processed);
        Assert.Equal(1, result.TotalPending);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);

        // Failed file should exist
        var failedDir = WorkflowLayout.InboxFailedDir(_tempRoot);
        var failed = Directory.GetFiles(failedDir, "*.failed.json");
        Assert.Single(failed);

        // Verify failed content
        var failedJson = File.ReadAllText(failed[0]);
        var failedOp = JsonSerializer.Deserialize<FailedOperation>(failedJson, _jsonOptions);
        Assert.NotNull(failedOp);
        Assert.Equal("UnknownOperation", failedOp!.ErrorCode);
        Assert.Equal("validation", failedOp.FailureStage);
    }

    // -- Inbox processor: ProcessAll - malformed JSON ---------------------

    [Fact]
    public void ProcessAll_MalformedJson_Skips()
    {
        var inboxDir = WorkflowLayout.InboxDir(_tempRoot);
        Directory.CreateDirectory(inboxDir);
        File.WriteAllText(Path.Combine(inboxDir, "bad.operation.json"), "not valid json {{{");

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var result = processor.ProcessAll();

        Assert.True(result.Processed);
        Assert.Equal(1, result.TotalPending);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Skipped);

        // Bad file should be deleted and evidence preserved
        Assert.Empty(Directory.GetFiles(inboxDir, "*.operation.json"));
        var failedDir = WorkflowLayout.InboxFailedDir(_tempRoot);
        var failed = Directory.GetFiles(failedDir, "*.failed.json");
        Assert.Single(failed);
        var failedJson = File.ReadAllText(failed[0]);
        var failedOp = JsonSerializer.Deserialize<FailedOperation>(failedJson, _jsonOptions);
        Assert.NotNull(failedOp);
        Assert.Equal("deserialization", failedOp!.FailureStage);
    }

    [Fact]
    public void ProcessAll_MismatchedUnsafeIdempotencyKey_FailsValidationAndStaysInFailedDir()
    {
        var inboxDir = WorkflowLayout.InboxDir(_tempRoot);
        Directory.CreateDirectory(inboxDir);

        var pending = new PendingOperation
        {
            OperationName = "workflow.get_state",
            Actor = "implementer",
            IdempotencyKey = "..\\..\\pwned",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(pending, _jsonOptions);
        File.WriteAllText(Path.Combine(inboxDir, "queue.operation.json"), json);

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var result = processor.ProcessAll();

        Assert.True(result.Processed);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);

        Assert.False(File.Exists(Path.Combine(WorkflowLayout.Root(_tempRoot), "pwned.failed.json")));
        Assert.False(File.Exists(Path.Combine(WorkflowLayout.InboxDir(_tempRoot), "pwned.failed.json")));
        Assert.Single(Directory.GetFiles(WorkflowLayout.InboxFailedDir(_tempRoot), "*.failed.json"));
    }

    [Fact]
    public void ProcessAll_MissingRequiredParameters_FailsBeforeDispatch()
    {
        var phaseId = _store.LoadWorkflow().CurrentPhaseId;
        var implementationLogPath = WorkflowLayout.ImplementationLog(_tempRoot);
        var recordCountBefore = CountJsonlRecords(implementationLogPath);

        CreatePendingOperationFile(
            WorkflowOperations.CreateImplementationLog,
            "missing-summary",
            phaseId: phaseId,
            parameters: new Dictionary<string, object?> { ["title"] = "Only title" });

        var processor = new InboxProcessor(_tempRoot, _dispatcher);
        var result = processor.ProcessAll();

        Assert.True(result.Processed);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);

        var failedDir = WorkflowLayout.InboxFailedDir(_tempRoot);
        var failed = Directory.GetFiles(failedDir, "*.failed.json");
        Assert.Single(failed);
        var failedJson = File.ReadAllText(failed[0]);
        var failedOp = JsonSerializer.Deserialize<FailedOperation>(failedJson, _jsonOptions);
        Assert.NotNull(failedOp);
        Assert.Equal("OperationValidationFailed", failedOp!.ErrorCode);
        Assert.Equal("validation", failedOp.FailureStage);
        Assert.Equal(recordCountBefore, CountJsonlRecords(implementationLogPath));
    }

    // -- Protected path audit ----------------------------------------------

    [Fact]
    public void AuditProtectedPaths_ReturnsAllProtected()
    {
        var ctx = Ctx(WorkflowOperations.AuditProtectedPaths);
        var result = _dispatcher.Dispatch(ctx);

        Assert.True(result.Success);
        var auditResult = Assert.IsType<ProtectedPathAuditResult>(result.Data);
        Assert.NotNull(auditResult);

        // Should report all protected since we just initialized
        Assert.True(auditResult.AllProtected);

        // Should have entries for key protected paths
        var pathNames = auditResult.Paths.Select(p => p.Path).ToList();
        Assert.Contains("workflow.json", pathNames);
        Assert.Contains("events.jsonl", pathNames);
        Assert.Contains("blockers/blockers.jsonl", pathNames);

        // All protected paths should have IsProtected = true
        Assert.All(auditResult.Paths.Where(p => p.IsProtected), p =>
            Assert.True(p.Status is "ok" or "integrity_ok" or "missing"));
    }

    [Fact]
    public void AuditProtectedPaths_InboxIsNotProtected()
    {
        var ctx = Ctx(WorkflowOperations.AuditProtectedPaths);
        var result = _dispatcher.Dispatch(ctx);

        Assert.True(result.Success);
        var auditResult = Assert.IsType<ProtectedPathAuditResult>(result.Data);

        var inboxEntry = auditResult.Paths.FirstOrDefault(p => p.Path == "inbox/");
        Assert.NotNull(inboxEntry);
        Assert.False(inboxEntry.IsProtected);
    }

    // -- Consistency repair ------------------------------------------------

    [Fact]
    public void RepairConsistency_ReturnsHealthyForCleanWorkflow()
    {
        var ctx = Ctx(WorkflowOperations.RepairConsistency);
        var result = _dispatcher.Dispatch(ctx);

        Assert.True(result.Success);
        // Result is an anonymous object
        var data = result.Data!.ToString();
        Assert.Contains("Healthy", data);
    }

    [Fact]
    public void RepairConsistency_CreatesMissingDirectories()
    {
        // Delete the inbox directory to test repair
        var inboxDir = WorkflowLayout.InboxDir(_tempRoot);
        if (Directory.Exists(inboxDir))
            Directory.Delete(inboxDir, recursive: true);

        Assert.False(Directory.Exists(inboxDir));

        var ctx = Ctx(WorkflowOperations.RepairConsistency);
        var result = _dispatcher.Dispatch(ctx);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(inboxDir));
        Assert.True(Directory.Exists(Path.Combine(inboxDir, "processed")));
        Assert.True(Directory.Exists(Path.Combine(inboxDir, "failed")));
    }

    // -- WorkflowLayout inbox paths ----------------------------------------

    [Fact]
    public void WorkflowLayout_InboxPaths_AreUnderWorkflowKit()
    {
        var inboxPath = WorkflowLayout.InboxDir(_tempRoot);
        var processedPath = WorkflowLayout.InboxProcessedDir(_tempRoot);
        var failedPath = WorkflowLayout.InboxFailedDir(_tempRoot);

        Assert.Contains(".workflowkit", inboxPath);
        Assert.Contains("inbox", inboxPath);
        Assert.EndsWith("processed", processedPath);
        Assert.EndsWith("failed", failedPath);
    }

    // -- Operation manifest constant coverage ------------------------------

    [Fact]
    public void WorkflowOperations_HasInboxConstants()
    {
        Assert.Equal("workflow.process_inbox", WorkflowOperations.ProcessInbox);
        Assert.Equal("workflow.get_inbox_status", WorkflowOperations.GetInboxStatus);
        Assert.Equal("workflow.audit_protected_paths", WorkflowOperations.AuditProtectedPaths);
        Assert.Equal("workflow.repair_consistency", WorkflowOperations.RepairConsistency);
    }

    // -- Manifest contains inbox entries -----------------------------------

    [Fact]
    public void Manifest_HasInboxEntries()
    {
        var entries = OperationManifestCatalog.GetAll();
        var inboxOps = entries.Where(e =>
            e.OperationName == WorkflowOperations.ProcessInbox ||
            e.OperationName == WorkflowOperations.GetInboxStatus ||
            e.OperationName == WorkflowOperations.AuditProtectedPaths ||
            e.OperationName == WorkflowOperations.RepairConsistency
        ).ToList();

        Assert.Equal(4, inboxOps.Count);

        foreach (var entry in inboxOps)
        {
            Assert.NotNull(entry.HandlerTypeName);
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
        }
    }

    // -- Dispatcher registers inbox handlers -------------------------------

    [Fact]
    public void Dispatcher_RegistersInboxHandlers()
    {
        var registered = _dispatcher.RegisteredOperations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(WorkflowOperations.ProcessInbox, registered);
        Assert.Contains(WorkflowOperations.GetInboxStatus, registered);
        Assert.Contains(WorkflowOperations.AuditProtectedPaths, registered);
        Assert.Contains(WorkflowOperations.RepairConsistency, registered);
    }

    // -- ProcessInbox dispatch ---------------------------------------------

    [Fact]
    public void Dispatch_ProcessInbox_Succeeds()
    {
        CreatePendingOperationFile("workflow.get_state", "dispatch-test");

        var ctx = Ctx(WorkflowOperations.ProcessInbox);
        var result = _dispatcher.Dispatch(ctx);

        Assert.True(result.Success);
        var inboxResult = Assert.IsType<ProcessInboxResult>(result.Data);
        Assert.Equal(1, inboxResult.Succeeded);
    }

    // -- GetInboxStatus dispatch -------------------------------------------

    [Fact]
    public void Dispatch_GetInboxStatus_Succeeds()
    {
        CreatePendingOperationFile("workflow.get_state", "status-test");

        var ctx = Ctx(WorkflowOperations.GetInboxStatus);
        var result = _dispatcher.Dispatch(ctx);

        Assert.True(result.Success);
        var status = Assert.IsType<InboxStatus>(result.Data);
        Assert.Equal(1, status.PendingCount);
    }

    // -- FailedOperation DTO -----------------------------------------------

    [Fact]
    public void FailedOperation_Construction_HasRequiredFields()
    {
        var op = new FailedOperation
        {
            OperationName = "workflow.bad_op",
            Actor = "implementer",
            IdempotencyKey = "fail-001",
            ErrorCode = "UnknownOperation",
            ErrorMessage = "Operation not found.",
            FailureStage = "validation"
        };

        Assert.Equal("workflow.bad_op", op.OperationName);
        Assert.Equal("validation", op.FailureStage);
    }

    // -- ProcessedOperation DTO --------------------------------------------

    [Fact]
    public void ProcessedOperation_Construction_HasRequiredFields()
    {
        var op = new ProcessedOperation
        {
            OperationName = "workflow.get_state",
            Actor = "implementer",
            IdempotencyKey = "proc-001",
            DispatchSuccess = true,
            ResultSummary = "OK"
        };

        Assert.True(op.DispatchSuccess);
        Assert.Equal("OK", op.ResultSummary);
    }
}
