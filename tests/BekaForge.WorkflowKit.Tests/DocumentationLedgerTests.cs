using System.Text.Json;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class DocumentationLedgerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly OperationDispatcher _dispatcher;

    public DocumentationLedgerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-doc-ledger-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("Documentation Ledger Tests");
        _dispatcher = new OperationDispatcher(new WorkflowStore(_tempRoot));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void CreateDocumentationRecord_ThenGetLedger_RoundTrips()
    {
        var create = Dispatch(
            WorkflowOperations.CreateDocumentationRecord,
            new()
            {
                ["title"] = "Integrity reporting",
                ["summary"] = "Integrity reporting is available through the CLI and operations.",
                ["status"] = "Verified",
                ["claims"] = "Integrity report aggregates authoritative and mirror drift findings.",
                ["evidenceIds"] = "VAL-001",
                ["relatedOperationNames"] = $"{WorkflowOperations.GetIntegrityReport},{WorkflowOperations.ValidateReleaseGate}",
                ["relatedCommands"] = "integrity,release-gate"
            });

        Assert.True(create.Success, create.Message);
        var record = Assert.IsType<DocumentationLedgerRecord>(create.Data);
        Assert.Equal("DOC-001", record.RecordId);

        var ledger = Dispatch(WorkflowOperations.GetDocumentationLedger);
        Assert.True(ledger.Success, ledger.Message);
        var payload = Assert.IsType<DocumentationLedgerResult>(ledger.Data);
        Assert.Single(payload.Records);
        Assert.Equal("Integrity reporting", payload.Records[0].Title);
    }

    [Fact]
    public void CreateDocumentationRecord_VerifiedWithoutEvidence_Fails()
    {
        var result = Dispatch(
            WorkflowOperations.CreateDocumentationRecord,
            new()
            {
                ["title"] = "Bad record",
                ["summary"] = "This should fail.",
                ["status"] = "Verified"
            });

        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
    }

    [Fact]
    public void GetDocumentationDraft_GroupsRecordsByStatus()
    {
        Assert.True(Dispatch(
            WorkflowOperations.CreateDocumentationRecord,
            new()
            {
                ["title"] = "Persona policy",
                ["summary"] = "Persona policy MVP exists.",
                ["status"] = "Verified",
                ["evidenceIds"] = "VAL-001",
                ["relatedCommands"] = "personas",
                ["relatedOperationNames"] = WorkflowOperations.ListPersonas
            }).Success);

        Assert.True(Dispatch(
            WorkflowOperations.CreateDocumentationRecord,
            new()
            {
                ["title"] = "Experimental release notes",
                ["summary"] = "Release notes generation remains planned.",
                ["status"] = "Planned"
            }).Success);

        var draft = Dispatch(WorkflowOperations.GetDocumentationDraft);
        Assert.True(draft.Success, draft.Message);
        var payload = Assert.IsType<DocumentationDraftResult>(draft.Data);
        Assert.Contains("## Verified behavior", payload.Markdown);
        Assert.Contains("## Planned behavior", payload.Markdown);
    }

    [Fact]
    public void GetDocumentationCoverage_FlagsMissingCoverage()
    {
        var result = Dispatch(WorkflowOperations.GetDocumentationCoverage);
        Assert.True(result.Success, result.Message);

        var report = Assert.IsType<DocumentationCoverageReport>(result.Data);
        Assert.True(report.Summary.BlockingIssueCount > 0);
        Assert.Contains(report.Issues, issue => issue.Code == "MissingOperationDocumentation");
        Assert.Contains(report.Issues, issue => issue.Code == "MissingCommandDocumentation");
    }

    [Fact]
    public void DocumentationOperations_AppearInManifestAndMcpMapping()
    {
        Assert.Contains(OperationManifestCatalog.GetAll(), entry => entry.OperationName == WorkflowOperations.CreateDocumentationRecord);
        Assert.Contains(OperationManifestCatalog.GetAll(), entry => entry.OperationName == WorkflowOperations.GetDocumentationCoverage);

        var tools = BekaForge.WorkflowKit.Mcp.McpToolMapping.GetAllTools();
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.CreateDocumentationRecord);
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.GetDocumentationDraft);
    }

    private OperationResult Dispatch(string operation, Dictionary<string, object?>? parameters = null)
    {
        return _dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            Parameters = parameters ?? new Dictionary<string, object?>()
        });
    }
}
