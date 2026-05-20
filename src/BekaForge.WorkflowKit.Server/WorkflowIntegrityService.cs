using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;
using System.Reflection;
using System.Text.Json;

namespace BekaForge.WorkflowKit.Server;

public sealed class WorkflowIntegrityService
{
    private const string WorkflowOperationsSourcePath = "src/BekaForge.WorkflowKit.AgentContracts/WorkflowOperations.cs";
    private const string OperationDispatcherSourcePath = "src/BekaForge.WorkflowKit.Server/OperationDispatcher.cs";
    private const string OperationManifestSourcePath = "src/BekaForge.WorkflowKit.Server/OperationManifestCatalog.cs";
    private const string ToolRoutingSourcePath = "src/BekaForge.WorkflowKit.Server/ToolRoutingCatalog.cs";

    private readonly WorkflowStore store;
    private readonly OperationMetadataSnapshot operationMetadata;

    public WorkflowIntegrityService(WorkflowStore store, OperationMetadataSnapshot? operationMetadata = null)
    {
        this.store = store;
        this.operationMetadata = operationMetadata ?? OperationMetadataSnapshot.CreateDefault(store);
    }

    private static readonly string[] MarkdownMirrorPaths =
    [
        WorkflowLayout.WorkflowMdPath(""),
        WorkflowLayout.ArchitectureMdPath(""),
        WorkflowLayout.ImplementationPlanMdPath(""),
        WorkflowLayout.MigrationNotesMdPath(""),
        WorkflowLayout.ExtractionAuditMdPath(""),
        WorkflowLayout.KnownLimitationsMdPath(""),
        WorkflowLayout.ExtensionGuideMdPath(""),
        WorkflowLayout.ConsistencyCheckMdPath(""),
        WorkflowLayout.FinalReviewMdPath(""),
        WorkflowLayout.PromptHeaderMdPath(""),
        WorkflowLayout.AuditLogMdPath(""),
        WorkflowLayout.ReviewLogMdPath(""),
        WorkflowLayout.ImplementationLogMdPath(""),
        WorkflowLayout.FixLogMdPath(""),
        WorkflowLayout.TestingLogMdPath(""),
        WorkflowLayout.CurrentStatusMdPath("")
    ];

    private static readonly AppendOnlyLogDescriptor[] AppendOnlyLogDescriptors =
    [
        CreateDescriptor(
            "implementation",
            static root => WorkflowLayout.ImplementationLog(root),
            static (ImplementationRecord record) => record.ImplementationId,
            static (ImplementationRecord record) => record.PhaseId),
        CreateDescriptor(
            "audit",
            static root => WorkflowLayout.AuditLog(root),
            static (AuditRecord record) => record.AuditId,
            static (AuditRecord record) => record.PhaseId),
        CreateDescriptor(
            "review",
            static root => WorkflowLayout.ReviewLog(root),
            static (ReviewRecord record) => record.ReviewId,
            static (ReviewRecord record) => record.PhaseId),
        CreateDescriptor(
            "validation",
            static root => WorkflowLayout.ValidationLog(root),
            static (ValidationRecord record) => record.ValidationId,
            static (ValidationRecord record) => record.PhaseId),
        CreateDescriptor(
            "test",
            static root => WorkflowLayout.TestLog(root),
            static (TestRecord record) => record.TestId,
            static (TestRecord record) => record.PhaseId),
        CreateDescriptor(
            "fix",
            static root => WorkflowLayout.FixLog(root),
            static (FixRecord record) => record.FixId,
            static (FixRecord record) => record.PhaseId),
        CreateDescriptor(
            "blocker",
            static root => WorkflowLayout.BlockersLog(root),
            static (BlockerRecord record) => record.BlockerId,
            static (BlockerRecord record) => record.PhaseId,
            allowsStateHistoryReuse: true),
        CreateDescriptor(
            "handoff",
            static root => WorkflowLayout.HandoffsLog(root),
            static (HandoffRecord record) => record.HandoffId,
            static (HandoffRecord record) => record.PhaseId),
        CreateDescriptor(
            "timing",
            static root => WorkflowLayout.TimingLog(root),
            static (TimingRecord record) => record.TimingId,
            static (TimingRecord record) => record.PhaseId)
    ];

    private static readonly EvidenceReferenceDescriptor[] EvidenceReferenceDescriptors =
    [
        new(
            "implementation",
            "ImplementationLogIds",
            static phase => phase.ImplementationLogIds,
            [AppendOnlyLogDescriptors[0]],
            static phase => phase.ImplementationLogIds,
            true),
        new(
            "audit",
            "AuditLogIds",
            static phase => phase.AuditLogIds,
            [AppendOnlyLogDescriptors[1]],
            static phase => phase.AuditLogIds,
            true),
        new(
            "review",
            "ReviewLogIds",
            static phase => phase.ReviewLogIds,
            [AppendOnlyLogDescriptors[2]],
            static phase => phase.ReviewLogIds,
            true),
        new(
            "validation",
            "ValidationLogIds",
            static phase => phase.ValidationLogIds,
            [AppendOnlyLogDescriptors[3], AppendOnlyLogDescriptors[4]],
            static phase => phase.ValidationLogIds,
            true),
        new(
            "test",
            "TestLogIds",
            static phase => phase.TestLogIds,
            [AppendOnlyLogDescriptors[4]],
            static phase => phase.TestLogIds.Concat(phase.ValidationLogIds),
            false),
        new(
            "fix",
            "FixLogIds",
            static phase => phase.FixLogIds,
            [AppendOnlyLogDescriptors[5]],
            static phase => phase.FixLogIds,
            true),
        new(
            "blocker",
            "BlockerIds",
            static phase => phase.BlockerIds,
            [AppendOnlyLogDescriptors[6]],
            static phase => phase.BlockerIds,
            true),
        new(
            "handoff",
            "HandoffIds",
            static phase => phase.HandoffIds,
            [AppendOnlyLogDescriptors[7]],
            static phase => phase.HandoffIds,
            true)
    ];

    public WorkflowIntegrityReport CheckPhaseRegistry()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();
        var workflowPath = RelativePath(WorkflowLayout.WorkflowFile(store.WorkflowRoot));
        var phasesDir = WorkflowLayout.PhasesDir(store.WorkflowRoot);

        var phaseIds = workflow.PhaseIds ?? [];
        var phaseIdSet = new HashSet<string>(phaseIds, StringComparer.OrdinalIgnoreCase);
        foreach (var duplicatePhaseId in phaseIds
                     .GroupBy(phaseId => phaseId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(new WorkflowIntegrityIssue
            {
                Code = "DuplicatePhaseId",
                Severity = WorkflowIntegritySeverity.Error,
                Category = WorkflowIntegrityCategory.Registry,
                Message = $"workflow.json contains duplicate phase ID '{duplicatePhaseId}'.",
                Path = workflowPath,
                PhaseId = duplicatePhaseId,
                IsReleaseBlocking = true
            });
        }

        foreach (var phaseId in phaseIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var phasePath = WorkflowLayout.PhaseFile(store.WorkflowRoot, phaseId);
            if (!File.Exists(phasePath))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "MissingPhaseFile",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Registry,
                    Message = $"workflow.json references '{phaseId}' but its phase file is missing.",
                    Path = RelativePath(phasePath),
                    PhaseId = phaseId,
                    IsReleaseBlocking = true
                });
                continue;
            }

            try
            {
                var phase = store.LoadPhase(phaseId);
                if (phase is null)
                    continue;

                if (!string.Equals(phase.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new WorkflowIntegrityIssue
                    {
                        Code = "PhaseFileIdMismatch",
                        Severity = WorkflowIntegritySeverity.Error,
                        Category = WorkflowIntegrityCategory.Registry,
                        Message = $"Phase file '{phaseId}.json' contains phase ID '{phase.PhaseId}'.",
                        Path = RelativePath(phasePath),
                        PhaseId = phaseId,
                        EntityId = phase.PhaseId,
                        IsReleaseBlocking = true
                    });
                }

                if (TryParsePhaseNumber(phaseId, out var expectedPhaseNumber)
                    && phase.PhaseNumber != expectedPhaseNumber)
                {
                    issues.Add(new WorkflowIntegrityIssue
                    {
                        Code = "PhaseFileNumberMismatch",
                        Severity = WorkflowIntegritySeverity.Error,
                        Category = WorkflowIntegrityCategory.Registry,
                        Message = $"Phase file '{phaseId}.json' stores phase number {phase.PhaseNumber} instead of {expectedPhaseNumber}.",
                        Path = RelativePath(phasePath),
                        PhaseId = phaseId,
                        EntityId = phase.PhaseId,
                        IsReleaseBlocking = true
                    });
                }
            }
            catch (StorageException ex)
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "UnreadablePhaseFile",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Registry,
                    Message = ex.Message,
                    Path = RelativePath(phasePath),
                    PhaseId = phaseId,
                    IsReleaseBlocking = true
                });
            }
        }

        if (Directory.Exists(phasesDir))
        {
            foreach (var phaseFile in Directory.EnumerateFiles(phasesDir, "PHASE-*.json"))
            {
                var phaseId = Path.GetFileNameWithoutExtension(phaseFile);
                if (!phaseIdSet.Contains(phaseId))
                {
                    issues.Add(new WorkflowIntegrityIssue
                    {
                        Code = "OrphanPhaseFile",
                        Severity = WorkflowIntegritySeverity.Error,
                        Category = WorkflowIntegrityCategory.Registry,
                        Message = $"Phase file '{phaseId}.json' exists but is not listed in workflow.json.",
                        Path = RelativePath(phaseFile),
                        PhaseId = phaseId,
                        IsReleaseBlocking = true
                    });
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(workflow.CurrentPhaseId))
        {
            if (!phaseIdSet.Contains(workflow.CurrentPhaseId))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "CurrentPhaseNotInRegistry",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Registry,
                    Message = $"CurrentPhaseId '{workflow.CurrentPhaseId}' is not listed in workflow.json phaseIds.",
                    Path = workflowPath,
                    PhaseId = workflow.CurrentPhaseId,
                    IsReleaseBlocking = true
                });
            }

            var currentPhasePath = WorkflowLayout.PhaseFile(store.WorkflowRoot, workflow.CurrentPhaseId);
            if (!File.Exists(currentPhasePath))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "CurrentPhaseMissingFile",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Registry,
                    Message = $"CurrentPhaseId '{workflow.CurrentPhaseId}' does not have a phase file.",
                    Path = RelativePath(currentPhasePath),
                    PhaseId = workflow.CurrentPhaseId,
                    IsReleaseBlocking = true
                });
            }
            else if (workflow.LastStatus is not null)
            {
                try
                {
                    var currentPhase = store.LoadPhase(workflow.CurrentPhaseId);
                    if (currentPhase is not null && currentPhase.State != workflow.LastStatus)
                    {
                        issues.Add(new WorkflowIntegrityIssue
                        {
                            Code = "CurrentPhaseStateMismatch",
                            Severity = WorkflowIntegritySeverity.Error,
                            Category = WorkflowIntegrityCategory.Registry,
                            Message = $"workflow.json last status is '{workflow.LastStatus}' but current phase '{workflow.CurrentPhaseId}' is '{currentPhase.State}'.",
                            Path = workflowPath,
                            PhaseId = workflow.CurrentPhaseId,
                            IsReleaseBlocking = true
                        });
                    }
                }
                catch (StorageException)
                {
                    // Reported above as unreadable or missing.
                }
            }
        }

        var phaseNumbers = phaseIds
            .Select(ParsePhaseNumberOrNull)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .Distinct()
            .OrderBy(number => number)
            .ToArray();
        if (phaseNumbers.Length > 1)
        {
            var missingNumbers = new List<int>();
            for (var number = phaseNumbers[0]; number <= phaseNumbers[^1]; number++)
            {
                if (Array.BinarySearch(phaseNumbers, number) < 0)
                    missingNumbers.Add(number);
            }

            if (missingNumbers.Count > 0)
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "PhaseNumberGap",
                    Severity = WorkflowIntegritySeverity.Warning,
                    Category = WorkflowIntegrityCategory.Registry,
                    Message = $"workflow.json phase numbering has gaps: {string.Join(", ", missingNumbers.Select(WorkflowIdFormatter.Phase))}.",
                    Path = workflowPath,
                    SuggestedFix = "Reuse or reconcile the missing phase numbers if the gap is unintended."
                });
            }
        }

        var loadedPhases = store.LoadAllPhases();
        foreach (var duplicatePhaseNumber in loadedPhases
                     .GroupBy(phase => phase.PhaseNumber)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(new WorkflowIntegrityIssue
            {
                Code = "DuplicatePhaseNumber",
                Severity = WorkflowIntegritySeverity.Error,
                Category = WorkflowIntegrityCategory.Registry,
                Message = $"Multiple phase files declare phase number {duplicatePhaseNumber}.",
                Path = RelativePath(WorkflowLayout.PhasesDir(store.WorkflowRoot)),
                EntityId = WorkflowIdFormatter.Phase(duplicatePhaseNumber),
                IsReleaseBlocking = true
            });
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    public WorkflowIntegrityReport CheckAppendOnlyLogs()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();
        var phaseIdSet = new HashSet<string>(workflow.PhaseIds ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in AppendOnlyLogDescriptors)
        {
            ValidateAppendOnlyLog(descriptor, phaseIdSet, issues);
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    public WorkflowIntegrityReport CheckEvidenceReferences()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();
        var phases = LoadRegisteredPhases(workflow);
        var phasesById = phases.ToDictionary(static phase => phase.PhaseId, StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in EvidenceReferenceDescriptors)
        {
            var recordsById = LoadEvidenceRecords(descriptor);
            var referencesByPhaseId = phases.ToDictionary(
                static phase => phase.PhaseId,
                descriptor.BuildAcceptedReferenceSet,
                StringComparer.OrdinalIgnoreCase);

            foreach (var phase in phases)
            {
                var phasePath = RelativePath(WorkflowLayout.PhaseFile(store.WorkflowRoot, phase.PhaseId));
                foreach (var referenceId in descriptor.ReferenceSelector(phase)
                             .Where(static referenceId => !string.IsNullOrWhiteSpace(referenceId))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!recordsById.TryGetValue(referenceId, out var record))
                    {
                        issues.Add(new WorkflowIntegrityIssue
                        {
                            Code = "MissingPhaseEvidenceReference",
                            Severity = WorkflowIntegritySeverity.Error,
                            Category = WorkflowIntegrityCategory.EvidenceReference,
                            Message = $"Phase '{phase.PhaseId}' references {descriptor.RecordType} record '{referenceId}' in {descriptor.PhasePropertyName}, but no authoritative log entry exists.",
                            Path = phasePath,
                            PhaseId = phase.PhaseId,
                            RecordId = referenceId,
                            SuggestedFix = $"Restore the missing {descriptor.RecordType} log entry or remove the stale {descriptor.PhasePropertyName} reference.",
                            IsReleaseBlocking = true
                        });
                        continue;
                    }

                    if (!string.Equals(record.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new WorkflowIntegrityIssue
                        {
                            Code = "MismatchedPhaseEvidenceReference",
                            Severity = WorkflowIntegritySeverity.Error,
                            Category = WorkflowIntegrityCategory.EvidenceReference,
                            Message = $"Phase '{phase.PhaseId}' references {descriptor.RecordType} record '{referenceId}' in {descriptor.PhasePropertyName}, but that record belongs to phase '{record.PhaseId}'.",
                            Path = phasePath,
                            PhaseId = phase.PhaseId,
                            RecordId = referenceId,
                            EntityId = record.PhaseId,
                            SuggestedFix = $"Relink {referenceId} to the correct phase or remove the incorrect {descriptor.PhasePropertyName} reference.",
                            IsReleaseBlocking = true
                        });
                    }
                }
            }

            if (!descriptor.CheckLogToPhase)
                continue;

            foreach (var record in recordsById.Values)
            {
                if (!phasesById.ContainsKey(record.PhaseId))
                    continue;

                if (referencesByPhaseId.TryGetValue(record.PhaseId, out var acceptedReferences)
                    && acceptedReferences.Contains(record.RecordId))
                {
                    continue;
                }

                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "OrphanLogEvidenceReference",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.EvidenceReference,
                    Message = $"The {descriptor.RecordType} log record '{record.RecordId}' belongs to phase '{record.PhaseId}', but that phase does not reference it in {descriptor.PhasePropertyName}.",
                    Path = record.Path,
                    LineNumber = record.LineNumber,
                    PhaseId = record.PhaseId,
                    RecordId = record.RecordId,
                    SuggestedFix = $"Add '{record.RecordId}' to phase '{record.PhaseId}' {descriptor.PhasePropertyName} or repair the authoritative log entry.",
                    IsReleaseBlocking = true
                });
            }
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    public WorkflowIntegrityReport CheckPhaseCompletionEvidence()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();

        foreach (var phase in LoadRegisteredPhases(workflow))
        {
            if (phase.State is not (PhaseState.Pass or PhaseState.PassWithWarnings))
                continue;

            var phasePath = RelativePath(WorkflowLayout.PhaseFile(store.WorkflowRoot, phase.PhaseId));
            AddMissingPassedEvidenceIssue(phase, phasePath, "ImplementationLogIds", phase.ImplementationLogIds.Count > 0, issues);
            AddMissingPassedEvidenceIssue(phase, phasePath, "AuditLogIds", phase.AuditLogIds.Count > 0, issues);
            AddMissingPassedEvidenceIssue(phase, phasePath, "ReviewLogIds", phase.ReviewLogIds.Count > 0, issues);

            var hasValidationReference = phase.ValidationLogIds.Count > 0 || phase.TestLogIds.Count > 0;
            AddMissingPassedEvidenceIssue(phase, phasePath, "ValidationLogIds", hasValidationReference, issues);
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    public WorkflowIntegrityReport CheckMarkdownMirrorDrift()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();
        var authoritativeLastWriteUtc = GetLatestAuthoritativeWriteUtc();
        if (authoritativeLastWriteUtc is null)
            return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);

        foreach (var relativePath in GetMarkdownMirrorPaths(workflow))
        {
            var path = Path.Combine(store.WorkflowRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "MarkdownMirrorMissing",
                    Severity = WorkflowIntegritySeverity.Warning,
                    Category = WorkflowIntegrityCategory.MarkdownMirror,
                    SourceKind = WorkflowIntegritySourceKind.RebuildableMirror,
                    Message = $"Generated markdown mirror '{relativePath}' is missing.",
                    Path = relativePath,
                    SuggestedFix = "Run workflow.sync_markdown to regenerate markdown mirrors."
                });
                continue;
            }

            var mirrorLastWriteUtc = File.GetLastWriteTimeUtc(path);
            if (mirrorLastWriteUtc >= authoritativeLastWriteUtc.Value)
                continue;

            issues.Add(new WorkflowIntegrityIssue
            {
                Code = "MarkdownMirrorStale",
                Severity = WorkflowIntegritySeverity.Warning,
                Category = WorkflowIntegrityCategory.MarkdownMirror,
                SourceKind = WorkflowIntegritySourceKind.RebuildableMirror,
                Message = $"Generated markdown mirror '{relativePath}' is older than the latest authoritative workflow state.",
                Path = relativePath,
                SuggestedFix = "Run workflow.sync_markdown to regenerate markdown mirrors."
            });
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    public WorkflowIntegrityReport CheckReadModelStaleness()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();
        var authoritativeLastWriteUtc = GetLatestAuthoritativeWriteUtc();
        if (authoritativeLastWriteUtc is null)
            return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);

        var contextIndexPath = WorkflowLayout.WorkflowKitDbPath(store.WorkflowRoot);
        var relativePath = RelativePath(contextIndexPath);
        if (!File.Exists(contextIndexPath))
        {
            issues.Add(new WorkflowIntegrityIssue
            {
                Code = "ReadModelMissing",
                Severity = WorkflowIntegritySeverity.Warning,
                Category = WorkflowIntegrityCategory.ReadModel,
                SourceKind = WorkflowIntegritySourceKind.RebuildableMirror,
                Message = $"Rebuildable read model '{relativePath}' is missing.",
                Path = relativePath,
                SuggestedFix = "Run workflow.rebuild_context_index to rebuild the context index."
            });

            return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
        }

        if (File.GetLastWriteTimeUtc(contextIndexPath) < authoritativeLastWriteUtc.Value)
        {
            issues.Add(new WorkflowIntegrityIssue
            {
                Code = "ReadModelStale",
                Severity = WorkflowIntegritySeverity.Warning,
                Category = WorkflowIntegrityCategory.ReadModel,
                SourceKind = WorkflowIntegritySourceKind.RebuildableMirror,
                Message = $"Rebuildable read model '{relativePath}' is older than the latest authoritative workflow state.",
                Path = relativePath,
                SuggestedFix = "Run workflow.rebuild_context_index to rebuild the context index."
            });
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    public WorkflowIntegrityReport CheckOperationMetadataConsistency()
    {
        var workflow = store.LoadWorkflow();
        var issues = new List<WorkflowIntegrityIssue>();
        var manifestEntries = operationMetadata.ManifestEntries;
        var manifestOperationNames = manifestEntries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.OperationName))
            .Select(static entry => entry.OperationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var operationName in operationMetadata.WorkflowOperationNames
                     .Where(static operationName => !string.IsNullOrWhiteSpace(operationName))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (manifestOperationNames.Contains(operationName))
                continue;

            issues.Add(CreateOperationMetadataIssue(
                code: "MissingManifestEntryForOperationConstant",
                message: $"WorkflowOperations declares '{operationName}', but OperationManifestCatalog has no entry for it.",
                path: WorkflowOperationsSourcePath,
                entityId: operationName,
                suggestedFix: "Add a manifest entry for the canonical operation constant."));
        }

        foreach (var operationName in operationMetadata.DispatcherOperationNames
                     .Where(static operationName => !string.IsNullOrWhiteSpace(operationName))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (manifestOperationNames.Contains(operationName))
                continue;

            issues.Add(CreateOperationMetadataIssue(
                code: "MissingManifestEntryForDispatcherOperation",
                message: $"OperationDispatcher registers '{operationName}', but OperationManifestCatalog has no entry for it.",
                path: OperationDispatcherSourcePath,
                entityId: operationName,
                suggestedFix: "Add a manifest entry for the registered dispatcher operation."));
        }

        foreach (var rule in operationMetadata.RoutingRules)
        {
            if (string.IsNullOrWhiteSpace(rule.OperationName)
                || manifestOperationNames.Contains(rule.OperationName))
            {
                continue;
            }

            issues.Add(CreateOperationMetadataIssue(
                code: "UnknownOperationInRoutingRule",
                message: $"Routing rule '{rule.IntentKeyword}' points to unknown operation '{rule.OperationName}'.",
                path: ToolRoutingSourcePath,
                entityId: rule.OperationName,
                suggestedFix: "Point the routing rule at a manifest-defined operation or add the missing manifest entry."));
        }

        foreach (var entry in manifestEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.OperationName)
                || entry.AccessLevel == OperationAccessLevel.Read)
            {
                continue;
            }

            if (entry.WriteTargets is null || entry.WriteTargets.Count == 0)
            {
                issues.Add(CreateOperationMetadataIssue(
                    code: "MissingWriteTargetMetadata",
                    message: $"Manifest entry '{entry.OperationName}' is {entry.AccessLevel} but has no write-target metadata.",
                    path: OperationManifestSourcePath,
                    entityId: entry.OperationName,
                    suggestedFix: "Add at least one write target describing the operation's metadata-safe mutation surface."));
                continue;
            }

            foreach (var writeTarget in entry.WriteTargets)
            {
                if (string.Equals(writeTarget.OperationName, entry.OperationName, StringComparison.OrdinalIgnoreCase))
                    continue;

                issues.Add(CreateOperationMetadataIssue(
                    code: "WriteTargetOperationNameMismatch",
                    message: $"Manifest entry '{entry.OperationName}' has write target metadata for '{writeTarget.OperationName}'.",
                    path: OperationManifestSourcePath,
                    entityId: entry.OperationName,
                    suggestedFix: "Make each write target OperationName match its parent manifest entry."));
            }
        }

        return WorkflowIntegrityReport.Create(workflow.WorkflowId, issues);
    }

    private string RelativePath(string path) =>
        Path.GetRelativePath(store.WorkflowRoot, path).Replace('\\', '/');

    private static WorkflowIntegrityIssue CreateOperationMetadataIssue(
        string code,
        string message,
        string path,
        string entityId,
        string suggestedFix)
    {
        return new WorkflowIntegrityIssue
        {
            Code = code,
            Severity = WorkflowIntegritySeverity.Error,
            Category = WorkflowIntegrityCategory.OperationMetadata,
            Message = message,
            Path = path,
            EntityId = entityId,
            SuggestedFix = suggestedFix,
            IsReleaseBlocking = true
        };
    }

    private IReadOnlyList<string> GetMarkdownMirrorPaths(WorkflowState workflow)
    {
        var paths = MarkdownMirrorPaths
            .Select(path => RelativePath(Path.Combine(store.WorkflowRoot, path.TrimStart('\\', '/'))))
            .ToList();

        foreach (var phaseId in (workflow.PhaseIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(RelativePath(WorkflowLayout.PhaseMdPath(store.WorkflowRoot, phaseId)));
        }

        return paths;
    }

    private DateTime? GetLatestAuthoritativeWriteUtc()
    {
        var paths = new List<string>
        {
            WorkflowLayout.WorkflowFile(store.WorkflowRoot),
            WorkflowLayout.SequencesFile(store.WorkflowRoot),
            WorkflowLayout.EventsLog(store.WorkflowRoot),
            WorkflowLayout.ImplementationLog(store.WorkflowRoot),
            WorkflowLayout.AuditLog(store.WorkflowRoot),
            WorkflowLayout.ReviewLog(store.WorkflowRoot),
            WorkflowLayout.ValidationLog(store.WorkflowRoot),
            WorkflowLayout.TestLog(store.WorkflowRoot),
            WorkflowLayout.FixLog(store.WorkflowRoot),
            WorkflowLayout.BlockersLog(store.WorkflowRoot),
            WorkflowLayout.HandoffsLog(store.WorkflowRoot),
            WorkflowLayout.TimingLog(store.WorkflowRoot)
        };

        AddFilesIfDirectoryExists(paths, WorkflowLayout.PhasesDir(store.WorkflowRoot), "*.json");
        AddFilesIfDirectoryExists(paths, WorkflowLayout.OrchestrationSessionsDir(store.WorkflowRoot), "*.json");
        AddFilesIfDirectoryExists(paths, WorkflowLayout.OrchestrationRunsDir(store.WorkflowRoot), "*.json");
        AddFilesIfDirectoryExists(paths, WorkflowLayout.OrchestrationLogsDir(store.WorkflowRoot), "*.jsonl");

        return paths
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .Cast<DateTime?>()
            .Max();
    }

    private static void AddFilesIfDirectoryExists(List<string> paths, string directoryPath, string pattern)
    {
        if (!Directory.Exists(directoryPath))
            return;

        paths.AddRange(Directory.EnumerateFiles(directoryPath, pattern));
    }

    private IReadOnlyList<Phase> LoadRegisteredPhases(WorkflowState workflow)
    {
        var phases = new List<Phase>();
        foreach (var phaseId in (workflow.PhaseIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var phasePath = WorkflowLayout.PhaseFile(store.WorkflowRoot, phaseId);
            if (!File.Exists(phasePath))
                continue;

            try
            {
                var phase = store.LoadPhase(phaseId);
                if (phase is not null)
                    phases.Add(phase);
            }
            catch (StorageException)
            {
                // Registry validation reports unreadable phase files separately.
            }
        }

        return phases;
    }

    private Dictionary<string, EvidenceRecord> LoadEvidenceRecords(EvidenceReferenceDescriptor descriptor)
    {
        var recordsById = new Dictionary<string, EvidenceRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var logDescriptor in descriptor.LogDescriptors)
        {
            var path = logDescriptor.PathFactory(store.WorkflowRoot);
            if (!File.Exists(path))
                continue;

            var relativePath = RelativePath(path);
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ParsedAppendOnlyRecord? parsedRecord;
                try
                {
                    parsedRecord = logDescriptor.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (parsedRecord is null
                    || string.IsNullOrWhiteSpace(parsedRecord.RecordId)
                    || string.IsNullOrWhiteSpace(parsedRecord.PhaseId))
                {
                    continue;
                }

                recordsById[parsedRecord.RecordId] = new EvidenceRecord(
                    parsedRecord.RecordId,
                    parsedRecord.PhaseId,
                    relativePath,
                    lineNumber);
            }
        }

        return recordsById;
    }

    private void ValidateAppendOnlyLog(
        AppendOnlyLogDescriptor descriptor,
        HashSet<string> phaseIdSet,
        List<WorkflowIntegrityIssue> issues)
    {
        var path = descriptor.PathFactory(store.WorkflowRoot);
        if (!File.Exists(path))
            return;

        var relativePath = RelativePath(path);
        var recordsById = new Dictionary<string, List<ParsedAppendOnlyRecord>>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            ParsedAppendOnlyRecord? record;
            try
            {
                record = descriptor.Parse(line);
            }
            catch (JsonException ex)
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "MalformedJsonlLine",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Log,
                    Message = $"The {descriptor.RecordType} log contains malformed JSONL at line {lineNumber}: {ex.Message}",
                    Path = relativePath,
                    LineNumber = lineNumber,
                    SuggestedFix = "Repair or remove the malformed authoritative log line without rewriting valid history.",
                    IsReleaseBlocking = true
                });
                continue;
            }

            if (record is null)
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "MalformedJsonlLine",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Log,
                    Message = $"The {descriptor.RecordType} log contains a malformed JSONL record at line {lineNumber}.",
                    Path = relativePath,
                    LineNumber = lineNumber,
                    SuggestedFix = "Repair or remove the malformed authoritative log line without rewriting valid history.",
                    IsReleaseBlocking = true
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.RecordId))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "MissingRecordId",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Log,
                    Message = $"The {descriptor.RecordType} log contains a record without a usable ID at line {lineNumber}.",
                    Path = relativePath,
                    LineNumber = lineNumber,
                    SuggestedFix = "Restore the missing record ID or replace the malformed append-only entry through a handler-backed repair path.",
                    IsReleaseBlocking = true
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.PhaseId))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "MissingRecordPhaseLink",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Log,
                    Message = $"Record '{record.RecordId}' in the {descriptor.RecordType} log is missing its phase link.",
                    Path = relativePath,
                    LineNumber = lineNumber,
                    RecordId = record.RecordId,
                    SuggestedFix = "Restore the owning phase ID so the append-only record can be reconciled.",
                    IsReleaseBlocking = true
                });
            }
            else if (!phaseIdSet.Contains(record.PhaseId))
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "UnknownRecordPhaseLink",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Log,
                    Message = $"Record '{record.RecordId}' in the {descriptor.RecordType} log points to unknown phase '{record.PhaseId}'.",
                    Path = relativePath,
                    LineNumber = lineNumber,
                    PhaseId = record.PhaseId,
                    RecordId = record.RecordId,
                    SuggestedFix = "Create or relink the referenced phase before relying on this authoritative record.",
                    IsReleaseBlocking = true
                });
            }

            if (!recordsById.TryGetValue(record.RecordId, out var occurrences))
            {
                occurrences = [];
                recordsById.Add(record.RecordId, occurrences);
            }

            occurrences.Add(record with { LineNumber = lineNumber });
        }

        foreach (var pair in recordsById)
        {
            var occurrences = pair.Value;
            if (occurrences.Count <= 1)
                continue;

            var distinctPhaseIds = occurrences
                .Select(static occurrence => occurrence.PhaseId)
                .Where(static phaseId => !string.IsNullOrWhiteSpace(phaseId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var distinctPayloads = occurrences
                .Select(static occurrence => occurrence.RawLine)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (descriptor.AllowsStateHistoryReuse && distinctPhaseIds <= 1)
                continue;

            var lines = string.Join(", ", occurrences.Select(static occurrence => occurrence.LineNumber));
            issues.Add(new WorkflowIntegrityIssue
            {
                Code = "DuplicateRecordId",
                Severity = WorkflowIntegritySeverity.Error,
                Category = WorkflowIntegrityCategory.Log,
                Message = $"The {descriptor.RecordType} log reuses record ID '{pair.Key}' on lines {lines}.",
                Path = relativePath,
                LineNumber = occurrences[0].LineNumber,
                RecordId = pair.Key,
                SuggestedFix = "Allocate a unique append-only record ID for each entry.",
                IsReleaseBlocking = true
            });

            if (distinctPhaseIds > 1 || distinctPayloads > 1)
            {
                issues.Add(new WorkflowIntegrityIssue
                {
                    Code = "ReusedRecordId",
                    Severity = WorkflowIntegritySeverity.Error,
                    Category = WorkflowIntegrityCategory.Log,
                    Message = $"Record ID '{pair.Key}' in the {descriptor.RecordType} log maps to multiple entries or phases (lines {lines}).",
                    Path = relativePath,
                    LineNumber = occurrences[0].LineNumber,
                    RecordId = pair.Key,
                    SuggestedFix = "Repair the conflicting entries so each append-only record ID identifies exactly one record.",
                    IsReleaseBlocking = true
                });
            }
        }
    }

    private static int? ParsePhaseNumberOrNull(string phaseId) =>
        TryParsePhaseNumber(phaseId, out var phaseNumber) ? phaseNumber : null;

    private static bool TryParsePhaseNumber(string phaseId, out int phaseNumber)
    {
        phaseNumber = 0;
        if (!phaseId.StartsWith("PHASE-", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(phaseId.AsSpan(6), out phaseNumber) && phaseNumber > 0;
    }

    private static AppendOnlyLogDescriptor CreateDescriptor<TRecord>(
        string recordType,
        Func<string, string> pathFactory,
        Func<TRecord, string?> idSelector,
        Func<TRecord, string?> phaseIdSelector,
        bool allowsStateHistoryReuse = false)
    {
        return new AppendOnlyLogDescriptor(
            recordType,
            pathFactory,
            allowsStateHistoryReuse,
            line =>
            {
                var record = WorkflowSerializer.Deserialize<TRecord>(line);
                return record is null
                    ? null
                    : new ParsedAppendOnlyRecord(idSelector(record), phaseIdSelector(record), line, 0);
            });
    }

    private static void AddMissingPassedEvidenceIssue(
        Phase phase,
        string phasePath,
        string propertyName,
        bool hasEvidence,
        List<WorkflowIntegrityIssue> issues)
    {
        if (hasEvidence)
            return;

        issues.Add(new WorkflowIntegrityIssue
        {
            Code = "PassedPhaseMissingEvidence",
            Severity = WorkflowIntegritySeverity.Error,
            Category = WorkflowIntegrityCategory.EvidenceReference,
            Message = $"Phase '{phase.PhaseId}' is in terminal success state '{phase.State}' but has no evidence referenced in {propertyName}.",
            Path = phasePath,
            PhaseId = phase.PhaseId,
            SuggestedFix = $"Add the required evidence reference to {propertyName} or move the phase out of terminal success until the evidence exists.",
            IsReleaseBlocking = true
        });
    }

    private sealed record AppendOnlyLogDescriptor(
        string RecordType,
        Func<string, string> PathFactory,
        bool AllowsStateHistoryReuse,
        Func<string, ParsedAppendOnlyRecord?> Parse);

    private sealed record EvidenceReferenceDescriptor(
        string RecordType,
        string PhasePropertyName,
        Func<Phase, IReadOnlyList<string>> ReferenceSelector,
        IReadOnlyList<AppendOnlyLogDescriptor> LogDescriptors,
        Func<Phase, IEnumerable<string>> AcceptedReferenceSelector,
        bool CheckLogToPhase)
    {
        public HashSet<string> BuildAcceptedReferenceSet(Phase phase) =>
            AcceptedReferenceSelector(phase)
                .Where(static referenceId => !string.IsNullOrWhiteSpace(referenceId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ParsedAppendOnlyRecord(
        string? RecordId,
        string? PhaseId,
        string RawLine,
        int LineNumber);

    private sealed record EvidenceRecord(
        string RecordId,
        string PhaseId,
        string Path,
        int LineNumber);

    public sealed record OperationMetadataSnapshot(
        IReadOnlyCollection<string> WorkflowOperationNames,
        IReadOnlyCollection<string> DispatcherOperationNames,
        IReadOnlyList<OperationManifestEntry> ManifestEntries,
        IReadOnlyList<ToolRoutingRule> RoutingRules)
    {
        public static OperationMetadataSnapshot CreateDefault(WorkflowStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return new OperationMetadataSnapshot(
                GetWorkflowOperationNames(),
                new OperationDispatcher(store).RegisteredOperations.ToArray(),
                OperationManifestCatalog.GetAll(),
                ToolRoutingCatalog.GetRules());
        }

        private static string[] GetWorkflowOperationNames()
        {
            return typeof(WorkflowOperations)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(static field => field is { IsLiteral: true, IsInitOnly: false }
                    && field.FieldType == typeof(string))
                .Select(static field => (string)field.GetRawConstantValue()!)
                .ToArray();
        }
    }
}
