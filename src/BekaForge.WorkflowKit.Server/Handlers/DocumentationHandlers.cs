using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class CreateDocumentationRecordHandler(WorkflowStore store) : IOperationHandler
{
    private readonly DocumentationLedgerService _service = new(store);

    public string OperationName => WorkflowOperations.CreateDocumentationRecord;

    public OperationResult Execute(OperationContext context)
    {
        var title = context.GetString("title");
        var summary = context.GetString("summary");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(summary))
            return OperationResult.Fail("ValidationFailed", "Parameters 'title' and 'summary' are required.");

        if (!Enum.TryParse<DocumentationClaimStatus>(
                context.GetString("status") ?? nameof(DocumentationClaimStatus.Implemented),
                ignoreCase: true,
                out var status))
        {
            return OperationResult.Fail("ValidationFailed", "Parameter 'status' must be a valid DocumentationClaimStatus value.");
        }

        var claims = ParseList(context.GetString("claims"));
        var evidenceIds = ParseList(context.GetString("evidenceIds"));
        if (status == DocumentationClaimStatus.Verified && evidenceIds.Count == 0)
            return OperationResult.Fail("ValidationFailed", "Verified documentation records require at least one evidence ID.");

        var record = _service.CreateRecord(
            title,
            summary,
            status,
            claims,
            evidenceIds,
            ParseList(context.GetString("relatedPhaseIds") ?? context.GetString("phaseIds")),
            ParseList(context.GetString("relatedOperationNames") ?? context.GetString("operations")),
            ParseList(context.GetString("relatedCommands") ?? context.GetString("commands")),
            ParseList(context.GetString("keywords")),
            context.GetString("notes") ?? string.Empty);

        return OperationResult.Ok(record, $"Documentation record {record.RecordId} created.");
    }

    private static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class GetDocumentationLedgerHandler(WorkflowStore store) : IOperationHandler
{
    private readonly DocumentationLedgerService _service = new(store);

    public string OperationName => WorkflowOperations.GetDocumentationLedger;

    public OperationResult Execute(OperationContext context) =>
        OperationResult.Ok(_service.GetLedger());
}

public sealed class GetDocumentationDraftHandler(WorkflowStore store) : IOperationHandler
{
    private readonly DocumentationLedgerService _service = new(store);

    public string OperationName => WorkflowOperations.GetDocumentationDraft;

    public OperationResult Execute(OperationContext context) =>
        OperationResult.Ok(_service.BuildDraft());
}

public sealed class GetDocumentationCoverageHandler(WorkflowStore store) : IOperationHandler
{
    private readonly DocumentationLedgerService _service = new(store);

    public string OperationName => WorkflowOperations.GetDocumentationCoverage;

    public OperationResult Execute(OperationContext context) =>
        OperationResult.Ok(_service.CheckCoverage());
}
