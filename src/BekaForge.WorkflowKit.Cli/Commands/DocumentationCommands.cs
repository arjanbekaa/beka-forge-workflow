using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;

partial class Program
{
    internal static void CmdDocumentation(
        string subCmd,
        string? wfRoot,
        string? title,
        string? summary,
        string? status,
        string? claims,
        string? evidenceIds,
        string? phaseId,
        string? relatedOperationNames,
        string? relatedCommands,
        string? keywords,
        string? notes,
        bool json,
        CliOutputMode mode)
    {
        switch ((subCmd ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "add":
                var addResult = DispatchDocumentationOperation(
                    wfRoot,
                    WorkflowOperations.CreateDocumentationRecord,
                    new Dictionary<string, object?>
                    {
                        ["title"] = title,
                        ["summary"] = summary,
                        ["status"] = status,
                        ["claims"] = claims,
                        ["evidenceIds"] = evidenceIds,
                        ["relatedPhaseIds"] = phaseId,
                        ["relatedOperationNames"] = relatedOperationNames,
                        ["relatedCommands"] = relatedCommands,
                        ["keywords"] = keywords,
                        ["notes"] = notes
                    },
                    mode);

                if (json)
                {
                    WriteJson(addResult.Data);
                    return;
                }

                var added = ToJsonElement(addResult.Data);
                Console.WriteLine($"Created {GetString(added, "recordId")}: {GetString(added, "title")}");
                return;

            case "ledger":
                RenderDocumentationPayload(
                    DispatchDocumentationOperation(wfRoot, WorkflowOperations.GetDocumentationLedger, null, mode).Data,
                    json,
                    "Documentation Ledger");
                return;

            case "draft":
                var draftResult = DispatchDocumentationOperation(wfRoot, WorkflowOperations.GetDocumentationDraft, null, mode);
                if (json)
                {
                    WriteJson(draftResult.Data);
                    return;
                }

                var draft = ToJsonElement(draftResult.Data);
                Console.WriteLine(GetString(draft, "markdown"));
                return;

            case "coverage":
                RenderDocumentationPayload(
                    DispatchDocumentationOperation(wfRoot, WorkflowOperations.GetDocumentationCoverage, null, mode).Data,
                    json,
                    "Documentation Coverage");
                return;

            default:
                CliRenderer.Error("Usage: bfwf doc add|ledger|draft|coverage ...", mode);
                Environment.Exit(2);
                return;
        }
    }

    private static void RenderDocumentationPayload(object? payload, bool json, string title)
    {
        if (json)
        {
            WriteJson(payload);
            return;
        }

        var element = ToJsonElement(payload);
        Console.WriteLine(title);
        Console.WriteLine(new string('=', title.Length));

        if (TryGetPropertyIgnoreCase(element, "records", out var records))
        {
            foreach (var record in records.EnumerateArray())
                Console.WriteLine($"- {GetString(record, "recordId")} {GetString(record, "title")} [{GetString(record, "status")}]");
            return;
        }

        if (TryGetPropertyIgnoreCase(element, "issues", out var issues))
        {
            foreach (var issue in issues.EnumerateArray())
                Console.WriteLine($"- {GetString(issue, "code")}: {GetString(issue, "message")}");
        }
    }

    private static OperationResult DispatchDocumentationOperation(
        string? wfRoot,
        string operation,
        IReadOnlyDictionary<string, object?>? parameters,
        CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot!);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            Parameters = parameters ?? new Dictionary<string, object?>()
        });

        if (!result.Success)
        {
            CliRenderer.Error(result.Message ?? result.ErrorCode ?? "Documentation command failed.", mode);
            Environment.Exit(1);
        }

        return result;
    }
}
