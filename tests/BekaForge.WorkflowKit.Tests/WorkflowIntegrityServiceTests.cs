using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Markdown;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class WorkflowIntegrityServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly WorkflowIntegrityService _service;

    public WorkflowIntegrityServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-integrity-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("Integrity Service Tests");
        _store = new WorkflowStore(_tempRoot);
        _service = new WorkflowIntegrityService(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private WorkflowIntegrityService CreateIntegrityService(
        WorkflowIntegrityService.OperationMetadataSnapshot operationMetadata) =>
        new(_store, operationMetadata);

    private static WorkflowIntegrityService.OperationMetadataSnapshot CreateHealthyOperationMetadataSnapshot()
    {
        return new WorkflowIntegrityService.OperationMetadataSnapshot(
            WorkflowOperationNames: ["workflow.read_sample", "workflow.write_sample"],
            DispatcherOperationNames: ["workflow.read_sample", "workflow.write_sample"],
            ManifestEntries:
            [
                new OperationManifestEntry
                {
                    OperationName = "workflow.read_sample",
                    AccessLevel = OperationAccessLevel.Read,
                    Category = "Samples",
                    Summary = "Reads sample metadata.",
                    HandlerTypeName = "ReadSampleHandler"
                },
                new OperationManifestEntry
                {
                    OperationName = "workflow.write_sample",
                    AccessLevel = OperationAccessLevel.Write,
                    Category = "Samples",
                    Summary = "Writes sample metadata.",
                    HandlerTypeName = "WriteSampleHandler",
                    WriteTargets =
                    [
                        new WriteTargetEntry
                        {
                            OperationName = "workflow.write_sample",
                            TargetDescription = "Writes the sample state.",
                            AccessLevel = OperationAccessLevel.Write,
                            IsAppendOnly = false
                        }
                    ]
                }
            ],
            RoutingRules:
            [
                new ToolRoutingRule
                {
                    IntentKeyword = "read sample",
                    OperationName = "workflow.read_sample",
                    Confidence = 1.0,
                    IsPrimary = true
                },
                new ToolRoutingRule
                {
                    IntentKeyword = "write sample",
                    OperationName = "workflow.write_sample",
                    Confidence = 1.0,
                    IsPrimary = true
                }
            ]);
    }

    [Fact]
    public void CheckPhaseRegistry_HealthyWorkflow_ReturnsNoIssues()
    {
        var report = _service.CheckPhaseRegistry();

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
    }

    [Fact]
    public void CheckPhaseRegistry_RegistryDrift_ReturnsExpectedIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One",
            State = PhaseState.ReadyForImplementation
        });
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-003",
            PhaseNumber = 3,
            Title = "Three",
            State = PhaseState.Pass
        });
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-004",
            PhaseNumber = 4,
            Title = "Orphan",
            State = PhaseState.Planned
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001", "PHASE-001", "PHASE-003"],
            CurrentPhaseId = "PHASE-003",
            LastStatus = PhaseState.InImplementation
        });

        var report = _service.CheckPhaseRegistry();

        Assert.Contains(report.Issues, issue => issue.Code == "DuplicatePhaseId");
        Assert.Contains(report.Issues, issue => issue.Code == "PhaseNumberGap");
        Assert.Contains(report.Issues, issue => issue.Code == "OrphanPhaseFile");
        Assert.Contains(report.Issues, issue => issue.Code == "CurrentPhaseStateMismatch");
        Assert.Equal(4, report.Summary.TotalIssues);
        Assert.Equal(3, report.Summary.ErrorCount);
        Assert.Equal(1, report.Summary.WarningCount);
    }

    [Fact]
    public void CheckPhaseRegistry_MissingAndMismatchedPhaseFiles_ReturnsExpectedIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002",
            PhaseNumber = 2,
            Title = "Two"
        });

        File.Delete(WorkflowLayout.PhaseFile(_tempRoot, "PHASE-002"));
        var mismatchedPhase = new Phase
        {
            PhaseId = "PHASE-007",
            PhaseNumber = 7,
            Title = "Wrong content"
        };
        File.WriteAllText(
            WorkflowLayout.PhaseFile(_tempRoot, "PHASE-005"),
            WorkflowSerializer.SerializeState(mismatchedPhase));

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-002", "PHASE-005"],
            CurrentPhaseId = "PHASE-002"
        });

        var report = _service.CheckPhaseRegistry();

        Assert.Contains(report.Issues, issue => issue.Code == "MissingPhaseFile" && issue.PhaseId == "PHASE-002");
        Assert.Contains(report.Issues, issue => issue.Code == "CurrentPhaseMissingFile" && issue.PhaseId == "PHASE-002");
        Assert.Contains(report.Issues, issue => issue.Code == "PhaseFileIdMismatch" && issue.PhaseId == "PHASE-005");
        Assert.Contains(report.Issues, issue => issue.Code == "PhaseFileNumberMismatch" && issue.PhaseId == "PHASE-005");
        Assert.Contains(report.Issues, issue => issue.Code == "PhaseNumberGap");
        Assert.Equal(5, report.Summary.TotalIssues);
        Assert.Equal(4, report.Summary.ErrorCount);
        Assert.Equal(1, report.Summary.WarningCount);
        Assert.Equal(4, report.Summary.ReleaseBlockingCount);
    }

    [Fact]
    public void CheckAppendOnlyLogs_HealthyWorkflow_ReturnsNoIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Healthy phase"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Implementer,
            Summary = "Implemented healthy slice.",
            Status = PhaseState.ImplementationLogged
        });

        _store.AppendValidation(new ValidationRecord
        {
            ValidationId = "VAL-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Validator,
            ValidationType = ValidationType.StaticInspection,
            ValidationResult = ValidationResult.Passed,
            Summary = "Validated the healthy slice.",
            EvidenceItems =
            [
                new ValidationEvidence
                {
                    Description = "Static inspection confirmed the record shape.",
                    Source = EvidenceSource.Command
                }
            ]
        });

        var report = _service.CheckAppendOnlyLogs();

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
    }

    [Fact]
    public void CheckAppendOnlyLogs_DuplicateAndReusedIds_ReturnExpectedIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002",
            PhaseNumber = 2,
            Title = "Two"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001", "PHASE-002"],
            CurrentPhaseId = "PHASE-001"
        });

        var record = new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Implementer,
            Summary = "First append.",
            Status = PhaseState.ImplementationLogged
        };
        _store.AppendImplementation(record);
        _store.AppendImplementation(record);
        _store.AppendImplementation(record with
        {
            PhaseId = "PHASE-002",
            Summary = "Reused for another phase."
        });

        var report = _service.CheckAppendOnlyLogs();

        Assert.Contains(report.Issues, issue => issue.Code == "DuplicateRecordId" && issue.RecordId == "IMP-001");
        Assert.Contains(report.Issues, issue => issue.Code == "ReusedRecordId" && issue.RecordId == "IMP-001");
        Assert.Equal(2, report.Summary.TotalIssues);
        Assert.Equal(2, report.Summary.ErrorCount);
        Assert.Equal(2, report.Summary.ReleaseBlockingCount);
    }

    [Fact]
    public void CheckAppendOnlyLogs_BlockerHistoryReuseForSamePhase_IsAllowed()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendBlocker(new BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Open blocker.",
            ReportedBy = WorkflowActor.Implementer,
            IsResolved = false
        });
        _store.AppendBlocker(new BlockerRecord
        {
            BlockerId = "BLK-001",
            PhaseId = "PHASE-001",
            Reason = "Open blocker.",
            ReportedBy = WorkflowActor.Implementer,
            IsResolved = true,
            Resolution = "Resolved."
        });

        var report = _service.CheckAppendOnlyLogs();

        Assert.DoesNotContain(report.Issues, issue => issue.RecordId == "BLK-001");
    }

    [Fact]
    public void CheckAppendOnlyLogs_MalformedAndMissingPhaseLinks_ReturnExpectedIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-010",
            PhaseId = "",
            Actor = WorkflowActor.Implementer,
            Summary = "Missing phase link.",
            Status = PhaseState.ImplementationLogged
        });
        _store.AppendAudit(new AuditRecord
        {
            AuditId = "AUD-010",
            PhaseId = "PHASE-999",
            Actor = WorkflowActor.Auditor,
            Summary = "Unknown phase link.",
            Passed = false
        });

        File.AppendAllText(
            WorkflowLayout.ReviewLog(_tempRoot),
            "{\"reviewId\":\"REV-010\",\"phaseId\":\"PHASE-001\"" + Environment.NewLine);

        var report = _service.CheckAppendOnlyLogs();

        Assert.Contains(report.Issues, issue => issue.Code == "MissingRecordPhaseLink" && issue.RecordId == "IMP-010");
        Assert.Contains(report.Issues, issue => issue.Code == "UnknownRecordPhaseLink" && issue.RecordId == "AUD-010" && issue.PhaseId == "PHASE-999");
        Assert.Contains(report.Issues, issue => issue.Code == "MalformedJsonlLine" && issue.Path == ".workflowkit/logs/review.jsonl");
        Assert.Equal(3, report.Summary.TotalIssues);
        Assert.Equal(3, report.Summary.ErrorCount);
        Assert.Equal(3, report.Summary.ReleaseBlockingCount);
    }

    [Fact]
    public void CheckAppendOnlyLogs_NullJsonlLine_ReturnsMalformedJsonlIssue()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        File.AppendAllText(
            WorkflowLayout.ReviewLog(_tempRoot),
            "null" + Environment.NewLine);

        var report = _service.CheckAppendOnlyLogs();

        var issue = Assert.Single(report.Issues);
        Assert.Equal("MalformedJsonlLine", issue.Code);
        Assert.Equal(".workflowkit/logs/review.jsonl", issue.Path);
        Assert.Equal(1, issue.LineNumber);
        Assert.Equal(1, report.Summary.TotalIssues);
        Assert.Equal(1, report.Summary.ErrorCount);
        Assert.Equal(1, report.Summary.ReleaseBlockingCount);
    }

    [Fact]
    public void CheckEvidenceReferences_HealthyWorkflow_ReturnsNoIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Implementer,
            Summary = "Implementation logged.",
            Status = PhaseState.ImplementationLogged
        });
        _store.AppendValidation(new ValidationRecord
        {
            ValidationId = "VAL-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Validator,
            ValidationType = ValidationType.StaticInspection,
            ValidationResult = ValidationResult.Passed,
            Summary = "Validation logged.",
            EvidenceItems =
            [
                new ValidationEvidence
                {
                    Description = "Validated by inspection.",
                    Source = EvidenceSource.Agent
                }
            ]
        });

        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One",
            ImplementationLogIds = ["IMP-001"],
            ValidationLogIds = ["VAL-001"]
        });

        var report = _service.CheckEvidenceReferences();

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
    }

    [Fact]
    public void CheckEvidenceReferences_MissingAndMismatchedPhaseReferences_ReturnExpectedIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One",
            ImplementationLogIds = ["IMP-404", "IMP-002"]
        });
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-002",
            PhaseNumber = 2,
            Title = "Two",
            ImplementationLogIds = ["IMP-002"]
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001", "PHASE-002"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-002",
            PhaseId = "PHASE-002",
            Actor = WorkflowActor.Implementer,
            Summary = "Owned by phase two.",
            Status = PhaseState.ImplementationLogged
        });

        var report = _service.CheckEvidenceReferences();

        Assert.Contains(report.Issues, issue => issue.Code == "MissingPhaseEvidenceReference" && issue.PhaseId == "PHASE-001" && issue.RecordId == "IMP-404");
        Assert.Contains(report.Issues, issue => issue.Code == "MismatchedPhaseEvidenceReference" && issue.PhaseId == "PHASE-001" && issue.RecordId == "IMP-002");
        Assert.Equal(2, report.Summary.TotalIssues);
        Assert.Equal(2, report.Summary.ErrorCount);
        Assert.Equal(2, report.Summary.ReleaseBlockingCount);
    }

    [Fact]
    public void CheckEvidenceReferences_OrphanedLogRecords_ReturnExpectedIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendAudit(new AuditRecord
        {
            AuditId = "AUD-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Auditor,
            Summary = "Audit logged without phase reference.",
            Passed = true
        });

        var report = _service.CheckEvidenceReferences();

        var issue = Assert.Single(report.Issues);
        Assert.Equal("OrphanLogEvidenceReference", issue.Code);
        Assert.Equal("PHASE-001", issue.PhaseId);
        Assert.Equal("AUD-001", issue.RecordId);
        Assert.Equal(".workflowkit/logs/audit.jsonl", issue.Path);
        Assert.Equal(1, issue.LineNumber);
        Assert.Equal(1, report.Summary.ReleaseBlockingCount);
    }

    [Fact]
    public void CheckEvidenceReferences_LegacyTestAliasInValidationLogIds_IsAccepted()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One",
            ValidationLogIds = ["TEST-001"]
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        _store.AppendTest(new TestRecord
        {
            TestId = "TEST-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Validator,
            Summary = "Legacy test logged.",
            Passed = true
        });

        var report = _service.CheckEvidenceReferences();

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
    }

    [Fact]
    public void CheckPhaseCompletionEvidence_PassedPhaseMissingReferences_ReturnsBlockingIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One",
            State = PhaseState.Pass
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        var report = _service.CheckPhaseCompletionEvidence();

        Assert.Equal(4, report.Summary.TotalIssues);
        Assert.All(report.Issues, issue =>
        {
            Assert.Equal("PassedPhaseMissingEvidence", issue.Code);
            Assert.True(issue.IsReleaseBlocking);
        });
    }

    [Fact]
    public void CheckPhaseRegistry_FocusedPhase_UsesFocusedPhaseStateAsLastStatus()
    {
        var dispatcher = new OperationDispatcher(_store);
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Focus target",
            State = PhaseState.ReadyForImplementation
        });

        _store.SaveWorkflow(_store.LoadWorkflow() with
        {
            PhaseIds = ["PHASE-001"]
        });

        var focus = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.FocusPhase,
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Implementer,
            Parameters = new Dictionary<string, object?> { ["reason"] = "Resume the repaired phase." }
        });

        Assert.True(focus.Success, focus.Message);
        Assert.Equal(PhaseState.ReadyForImplementation, _store.LoadWorkflow().LastStatus);
        Assert.DoesNotContain(_service.CheckPhaseRegistry().Issues, issue => issue.Code == "CurrentPhaseStateMismatch");
    }

    [Fact]
    public void CheckMarkdownMirrorDrift_StaleMarkdownMirror_ReturnsMirrorIssue()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        var markdown = new MarkdownSyncService(_store);
        markdown.SyncAll();

        var currentStatusPath = WorkflowLayout.CurrentStatusMdPath(_tempRoot);
        File.SetLastWriteTimeUtc(currentStatusPath, DateTime.UtcNow.AddMinutes(-10));

        var report = _service.CheckMarkdownMirrorDrift();

        Assert.Contains(report.Issues, issue =>
            issue.Code == "MarkdownMirrorStale"
            && issue.Path == ".workflowkit/workflow/07_Status/CurrentStatus.md"
            && issue.Category == WorkflowIntegrityCategory.MarkdownMirror
            && issue.SourceKind == WorkflowIntegritySourceKind.RebuildableMirror);
        Assert.All(report.Issues, issue => Assert.False(issue.IsReleaseBlocking));
    }

    [Fact]
    public void CheckMarkdownMirrorDrift_SyncMarkdownRefreshesUnchangedFiles()
    {
        var dispatcher = new OperationDispatcher(_store);

        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        var firstSync = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.SyncMarkdown,
            Actor = WorkflowActor.Codex
        });
        Assert.True(firstSync.Success, firstSync.Message);

        Thread.Sleep(1100);
        _store.SaveWorkflow(_store.LoadWorkflow() with { UpdatedUtc = DateTimeOffset.UtcNow });

        var secondSync = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.SyncMarkdown,
            Actor = WorkflowActor.Codex
        });
        Assert.True(secondSync.Success, secondSync.Message);

        var report = _service.CheckMarkdownMirrorDrift();
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "MarkdownMirrorStale");
    }

    [Fact]
    public void CheckReadModelStaleness_UpToDateContextIndex_ReturnsNoIssues()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        var indexBuilder = new ContextIndexBuilder(_tempRoot);
        indexBuilder.Rebuild();

        var report = _service.CheckReadModelStaleness();

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
    }

    [Fact]
    public void CheckReadModelStaleness_StaleContextIndex_ReturnsReadModelIssue()
    {
        _store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "One"
        });

        var workflow = _store.LoadWorkflow();
        _store.SaveWorkflow(workflow with
        {
            PhaseIds = ["PHASE-001"],
            CurrentPhaseId = "PHASE-001"
        });

        var indexBuilder = new ContextIndexBuilder(_tempRoot);
        indexBuilder.Rebuild();

        File.SetLastWriteTimeUtc(
            WorkflowLayout.WorkflowKitDbPath(_tempRoot),
            DateTime.UtcNow.AddMinutes(-10));

        _store.AppendImplementation(new ImplementationRecord
        {
            ImplementationId = "IMP-001",
            PhaseId = "PHASE-001",
            Actor = WorkflowActor.Implementer,
            Summary = "Makes the index stale.",
            Status = PhaseState.ImplementationLogged
        });

        var report = _service.CheckReadModelStaleness();

        var issue = Assert.Single(report.Issues);
        Assert.Equal("ReadModelStale", issue.Code);
        Assert.Equal(".workflowkit/index/workflowkit.db", issue.Path);
        Assert.Equal(WorkflowIntegrityCategory.ReadModel, issue.Category);
        Assert.Equal(WorkflowIntegritySourceKind.RebuildableMirror, issue.SourceKind);
        Assert.False(issue.IsReleaseBlocking);
    }

    [Fact]
    public void CheckOperationMetadataConsistency_HealthySnapshot_ReturnsNoIssues()
    {
        var service = CreateIntegrityService(CreateHealthyOperationMetadataSnapshot());

        var report = service.CheckOperationMetadataConsistency();

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.TotalIssues);
    }

    [Fact]
    public void CheckOperationMetadataConsistency_MetadataDrift_ReturnsExpectedIssues()
    {
        var service = CreateIntegrityService(new WorkflowIntegrityService.OperationMetadataSnapshot(
            WorkflowOperationNames: ["workflow.read_sample", "workflow.constant_only"],
            DispatcherOperationNames: ["workflow.read_sample", "workflow.dispatcher_only"],
            ManifestEntries:
            [
                new OperationManifestEntry
                {
                    OperationName = "workflow.read_sample",
                    AccessLevel = OperationAccessLevel.Read,
                    Category = "Samples",
                    Summary = "Reads sample metadata.",
                    HandlerTypeName = "ReadSampleHandler"
                },
                new OperationManifestEntry
                {
                    OperationName = "workflow.missing_target",
                    AccessLevel = OperationAccessLevel.Write,
                    Category = "Samples",
                    Summary = "Missing write-target metadata.",
                    HandlerTypeName = "MissingTargetHandler"
                },
                new OperationManifestEntry
                {
                    OperationName = "workflow.mismatch_target",
                    AccessLevel = OperationAccessLevel.Append,
                    Category = "Samples",
                    Summary = "Has mismatched write-target metadata.",
                    HandlerTypeName = "MismatchTargetHandler",
                    WriteTargets =
                    [
                        new WriteTargetEntry
                        {
                            OperationName = "workflow.other_operation",
                            TargetDescription = "Points to the wrong operation name.",
                            AccessLevel = OperationAccessLevel.Append,
                            IsAppendOnly = true
                        }
                    ]
                }
            ],
            RoutingRules:
            [
                new ToolRoutingRule
                {
                    IntentKeyword = "unknown route",
                    OperationName = "workflow.routing_only",
                    Confidence = 1.0,
                    IsPrimary = true
                }
            ]));

        var report = service.CheckOperationMetadataConsistency();

        Assert.Contains(report.Issues, issue =>
            issue.Code == "MissingManifestEntryForOperationConstant"
            && issue.EntityId == "workflow.constant_only"
            && issue.Path == "src/BekaForge.WorkflowKit.AgentContracts/WorkflowOperations.cs");
        Assert.Contains(report.Issues, issue =>
            issue.Code == "MissingManifestEntryForDispatcherOperation"
            && issue.EntityId == "workflow.dispatcher_only"
            && issue.Path == "src/BekaForge.WorkflowKit.Server/OperationDispatcher.cs");
        Assert.Contains(report.Issues, issue =>
            issue.Code == "UnknownOperationInRoutingRule"
            && issue.EntityId == "workflow.routing_only"
            && issue.Path == "src/BekaForge.WorkflowKit.Server/ToolRoutingCatalog.cs");
        Assert.Contains(report.Issues, issue =>
            issue.Code == "MissingWriteTargetMetadata"
            && issue.EntityId == "workflow.missing_target"
            && issue.Path == "src/BekaForge.WorkflowKit.Server/OperationManifestCatalog.cs");
        Assert.Contains(report.Issues, issue =>
            issue.Code == "WriteTargetOperationNameMismatch"
            && issue.EntityId == "workflow.mismatch_target"
            && issue.Path == "src/BekaForge.WorkflowKit.Server/OperationManifestCatalog.cs");
        Assert.All(report.Issues, issue =>
        {
            Assert.Equal(WorkflowIntegrityCategory.OperationMetadata, issue.Category);
            Assert.True(issue.IsReleaseBlocking);
            Assert.Equal(WorkflowIntegritySeverity.Error, issue.Severity);
        });
        Assert.Equal(5, report.Summary.TotalIssues);
        Assert.Equal(5, report.Summary.ErrorCount);
        Assert.Equal(5, report.Summary.ReleaseBlockingCount);
    }
}
