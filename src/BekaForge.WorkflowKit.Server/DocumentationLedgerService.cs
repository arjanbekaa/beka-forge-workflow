using System.Linq;
using System.Text;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

internal sealed class DocumentationLedgerService(WorkflowStore store)
{
    private static readonly string[] RequiredPublicCommands =
    [
        "integrity",
        "release-gate",
        "personas",
        "persona",
        "recommend-persona",
        "validate-persona-task",
        "doc add",
        "doc ledger",
        "doc draft",
        "doc coverage",
        "release-report",
        "validate-public-release"
    ];

    private static readonly string[] RequiredPublicOperations =
    [
        WorkflowOperations.GetIntegrityReport,
        WorkflowOperations.ValidateReleaseGate,
        WorkflowOperations.ListPersonas,
        WorkflowOperations.GetPersona,
        WorkflowOperations.RecommendPersona,
        WorkflowOperations.ValidatePersonaTask,
        WorkflowOperations.CreateDocumentationRecord,
        WorkflowOperations.GetDocumentationLedger,
        WorkflowOperations.GetDocumentationDraft,
        WorkflowOperations.GetDocumentationCoverage,
        WorkflowOperations.GetReleaseCandidateReport,
        WorkflowOperations.ValidatePublicRelease
    ];

    public DocumentationLedgerRecord CreateRecord(
        string title,
        string summary,
        DocumentationClaimStatus status,
        IReadOnlyList<string> claims,
        IReadOnlyList<string> evidenceIds,
        IReadOnlyList<string> relatedPhaseIds,
        IReadOnlyList<string> relatedOperationNames,
        IReadOnlyList<string> relatedCommands,
        IReadOnlyList<string> keywords,
        string notes)
    {
        var existing = store.LoadDocumentationLedger();
        var record = new DocumentationLedgerRecord
        {
            RecordId = NextRecordId(existing),
            Title = title.Trim(),
            Summary = summary.Trim(),
            Status = status,
            Claims = claims,
            EvidenceIds = evidenceIds,
            RelatedPhaseIds = relatedPhaseIds,
            RelatedOperationNames = relatedOperationNames,
            RelatedCommands = relatedCommands,
            Keywords = keywords,
            Notes = notes.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
            VerifiedUtc = status == DocumentationClaimStatus.Verified ? DateTimeOffset.UtcNow : null
        };

        store.SaveDocumentationLedger(existing.Concat([record]).ToList());
        return record;
    }

    public DocumentationLedgerResult GetLedger()
    {
        var records = store.LoadDocumentationLedger()
            .OrderBy(record => record.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DocumentationLedgerResult
        {
            Records = records,
            GeneratedUtc = DateTimeOffset.UtcNow
        };
    }

    public DocumentationDraftResult BuildDraft()
    {
        var records = store.LoadDocumentationLedger()
            .OrderByDescending(static record => record.Status == DocumentationClaimStatus.Verified)
            .ThenBy(record => record.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sections = records
            .GroupBy(record => record.Status)
            .OrderBy(group => GetStatusSortOrder(group.Key))
            .Select(group => new DocumentationDraftSection
            {
                Heading = group.Key switch
                {
                    DocumentationClaimStatus.Verified => "Verified behavior",
                    DocumentationClaimStatus.Implemented => "Implemented behavior",
                    DocumentationClaimStatus.Limited => "Known limitations",
                    DocumentationClaimStatus.Deprecated => "Deprecated behavior",
                    _ => "Planned behavior"
                },
                Status = group.Key,
                Records = group.ToList()
            })
            .ToList();

        var markdown = new StringBuilder();
        markdown.AppendLine("# Documentation Draft");
        markdown.AppendLine();
        foreach (var section in sections)
        {
            markdown.AppendLine($"## {section.Heading}");
            markdown.AppendLine();
            foreach (var record in section.Records)
            {
                markdown.AppendLine($"### {record.Title}");
                markdown.AppendLine(record.Summary);
                if (record.Claims.Count > 0)
                {
                    markdown.AppendLine();
                    foreach (var claim in record.Claims)
                        markdown.AppendLine($"- {claim}");
                }

                if (record.RelatedCommands.Count > 0 || record.RelatedOperationNames.Count > 0)
                {
                    markdown.AppendLine();
                    if (record.RelatedCommands.Count > 0)
                        markdown.AppendLine($"Commands: {string.Join(", ", record.RelatedCommands)}");
                    if (record.RelatedOperationNames.Count > 0)
                        markdown.AppendLine($"Operations: {string.Join(", ", record.RelatedOperationNames)}");
                }

                markdown.AppendLine();
            }
        }

        return new DocumentationDraftResult
        {
            Markdown = markdown.ToString().TrimEnd(),
            Sections = sections,
            GeneratedUtc = DateTimeOffset.UtcNow
        };
    }

    public DocumentationCoverageReport CheckCoverage()
    {
        var issues = new List<DocumentationCoverageIssue>();
        var records = store.LoadDocumentationLedger();
        var knownOperations = OperationManifestCatalog.GetAll()
            .Select(entry => entry.OperationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (record.Status == DocumentationClaimStatus.Verified && record.EvidenceIds.Count == 0)
            {
                issues.Add(new DocumentationCoverageIssue
                {
                    Code = "VerifiedRecordMissingEvidence",
                    Severity = DocumentationCoverageSeverity.Error,
                    Message = $"Verified documentation record '{record.RecordId}' does not include evidence IDs.",
                    RecordId = record.RecordId,
                    IsBlocking = true
                });
            }

            foreach (var operationName in record.RelatedOperationNames)
            {
                if (!knownOperations.Contains(operationName))
                {
                    issues.Add(new DocumentationCoverageIssue
                    {
                        Code = "UnknownOperationClaim",
                        Severity = DocumentationCoverageSeverity.Error,
                        Message = $"Documentation record '{record.RecordId}' references unknown operation '{operationName}'.",
                        RecordId = record.RecordId,
                        OperationName = operationName,
                        IsBlocking = true
                    });
                }
            }

            foreach (var commandName in record.RelatedCommands)
            {
                if (!RequiredPublicCommands.Contains(commandName, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add(new DocumentationCoverageIssue
                    {
                        Code = "UnknownCommandClaim",
                        Severity = DocumentationCoverageSeverity.Warning,
                        Message = $"Documentation record '{record.RecordId}' references command '{commandName}' outside the current public ledger scope.",
                        RecordId = record.RecordId,
                        CommandName = commandName,
                        IsBlocking = false
                    });
                }
            }
        }

        foreach (var operationName in RequiredPublicOperations)
        {
            if (!records.Any(record => record.RelatedOperationNames.Contains(operationName, StringComparer.OrdinalIgnoreCase)))
            {
                issues.Add(new DocumentationCoverageIssue
                {
                    Code = "MissingOperationDocumentation",
                    Severity = DocumentationCoverageSeverity.Error,
                    Message = $"No documentation ledger record covers public operation '{operationName}'.",
                    OperationName = operationName,
                    IsBlocking = true
                });
            }
        }

        foreach (var commandName in RequiredPublicCommands)
        {
            if (!records.Any(record => record.RelatedCommands.Contains(commandName, StringComparer.OrdinalIgnoreCase)))
            {
                issues.Add(new DocumentationCoverageIssue
                {
                    Code = "MissingCommandDocumentation",
                    Severity = DocumentationCoverageSeverity.Error,
                    Message = $"No documentation ledger record covers public command '{commandName}'.",
                    CommandName = commandName,
                    IsBlocking = true
                });
            }
        }

        return DocumentationCoverageReport.Create(issues, DateTimeOffset.UtcNow);
    }

    private static int GetStatusSortOrder(DocumentationClaimStatus status) => status switch
    {
        DocumentationClaimStatus.Verified => 0,
        DocumentationClaimStatus.Implemented => 1,
        DocumentationClaimStatus.Limited => 2,
        DocumentationClaimStatus.Deprecated => 3,
        _ => 4
    };

    private static string NextRecordId(IReadOnlyList<DocumentationLedgerRecord> records)
    {
        var maxNumber = 0;
        foreach (var record in records)
        {
            if (!record.RecordId.StartsWith("DOC-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(record.RecordId.AsSpan(4), out var number) && number > maxNumber)
                maxNumber = number;
        }

        return $"DOC-{maxNumber + 1:000}";
    }
}
