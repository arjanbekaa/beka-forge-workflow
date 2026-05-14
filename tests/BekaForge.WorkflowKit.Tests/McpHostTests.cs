using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Mcp;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class McpHostTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _projectRoot;
    private readonly string _secondProjectRoot;
    private readonly string _globalRegistryFile;
    private readonly ProjectRegistry _globalRegistry;
    private readonly McpHost _globalHost;

    public McpHostTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-mcp-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _projectRoot = Path.Combine(_tempRoot, "ProjectA");
        new WorkflowInitializer(_projectRoot).Initialize("MCP Host Test");
        _secondProjectRoot = Path.Combine(_tempRoot, "ProjectB");
        new WorkflowInitializer(_secondProjectRoot).Initialize("Second MCP Host Test");

        _globalRegistryFile = Path.Combine(_tempRoot, "global-mcp-registry.json");
        _globalRegistry = new ProjectRegistry(_globalRegistryFile);
        _globalRegistry.Add("project-a", _projectRoot);
        _globalRegistry.Add("project-b", _secondProjectRoot);
        _globalHost = new McpHost(null, _globalRegistry);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Initialize_ReturnsServerInfo()
    {
        var response = _globalHost.ProcessRequest(new JsonRpcRequest
        {
            Id = 1,
            Method = "initialize"
        });

        Assert.NotNull(response);
        Assert.Null(response!.Error);
        var result = response.Result!.Value;
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("BekaForge WorkflowKit MCP", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public void ToolsCall_ReadOperation_ByRoot_ReturnsStructuredPayload()
    {
        var response = SendToolCall(_globalHost, WorkflowOperations.GetState, new Dictionary<string, object?>
        {
            ["root"] = _projectRoot
        });

        var payload = ReadToolPayload(response);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal(WorkflowOperations.GetState, payload.GetProperty("operation").GetString());
        Assert.Equal(Path.GetFullPath(_projectRoot), payload.GetProperty("root").GetString());
        Assert.Equal("MCP Host Test", payload.GetProperty("data").GetProperty("assetName").GetString());
    }

    [Fact]
    public void ToolsCall_ReadOperation_ByProjectId_UsesGlobalRegistry()
    {
        var response = SendToolCall(_globalHost, WorkflowOperations.GetState, new Dictionary<string, object?>
        {
            ["projectId"] = "project-a"
        });

        var payload = ReadToolPayload(response);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal(Path.GetFullPath(_projectRoot), payload.GetProperty("root").GetString());
    }

    [Fact]
    public void ToolsCall_ReadOperation_MultipleProjects_ResolveDistinctRoots()
    {
        var first = ReadToolPayload(SendToolCall(_globalHost, WorkflowOperations.GetState, new Dictionary<string, object?>
        {
            ["projectId"] = "project-a"
        }));
        var second = ReadToolPayload(SendToolCall(_globalHost, WorkflowOperations.GetState, new Dictionary<string, object?>
        {
            ["projectId"] = "project-b"
        }));

        Assert.Equal("MCP Host Test", first.GetProperty("data").GetProperty("assetName").GetString());
        Assert.Equal("Second MCP Host Test", second.GetProperty("data").GetProperty("assetName").GetString());
        Assert.NotEqual(first.GetProperty("root").GetString(), second.GetProperty("root").GetString());
    }

    [Fact]
    public void ToolsCall_UnknownProjectId_ReturnsJsonRpcError()
    {
        var response = SendToolCall(_globalHost, WorkflowOperations.GetState, new Dictionary<string, object?>
        {
            ["projectId"] = "missing-project"
        });

        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
        Assert.Contains("Unknown projectId", response.Error.Message);
    }

    [Fact]
    public void ToolsCall_MissingParams_ReturnsJsonRpcError()
    {
        var response = _globalHost.ProcessRequest(new JsonRpcRequest
        {
            Id = 1,
            Method = "tools/call"
        });

        Assert.NotNull(response);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response!.Error!.Code);
        Assert.Contains("Missing tool call parameters", response.Error.Message);
    }

    [Fact]
    public void ToolsCall_UnknownTool_ReturnsJsonRpcError()
    {
        var response = SendToolCall(_globalHost, "workflow.not_real", new Dictionary<string, object?>
        {
            ["root"] = _projectRoot
        });

        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
        Assert.Contains("Unknown tool", response.Error.Message);
    }

    [Fact]
    public void ToolsCall_WithoutRootOrProjectId_InGlobalMode_ReturnsJsonRpcError()
    {
        var response = SendToolCall(_globalHost, WorkflowOperations.GetState, new Dictionary<string, object?>());

        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
        Assert.Contains("Missing 'root' or 'projectId'", response.Error.Message);
    }

    [Fact]
    public void ToolsCall_WriteOperation_NormalizesBudgetModeAlias()
    {
        var response = SendToolCall(_globalHost, WorkflowOperations.SetBudgetConfig, new Dictionary<string, object?>
        {
            ["root"] = _projectRoot,
            ["budgetMode"] = "High"
        });

        var payload = ReadToolPayload(response);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal("High", payload.GetProperty("data").GetProperty("mode").GetString());
    }

    [Fact]
    public void ToolsCall_WriteOperation_NormalizesTaskAliasForHandoff()
    {
        var store = new WorkflowStore(_projectRoot);
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Phase One",
            State = PhaseState.InImplementation
        });

        var response = SendToolCall(_globalHost, WorkflowOperations.CreateHandoff, new Dictionary<string, object?>
        {
            ["root"] = _projectRoot,
            ["phaseId"] = "PHASE-001",
            ["toActor"] = "Codex",
            ["task"] = "Review the MCP adapter"
        });

        var payload = ReadToolPayload(response);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal("Review the MCP adapter", payload.GetProperty("data").GetProperty("summary").GetString());
    }

    [Fact]
    public void ToolsCall_WriteOperation_NormalizesLegacySubPhaseStatus()
    {
        var store = new WorkflowStore(_projectRoot);
        store.SavePhase(new Phase
        {
            PhaseId = "PHASE-001",
            PhaseNumber = 1,
            Title = "Phase One",
            SubPhases =
            [
                new SubPhase
                {
                    SubPhaseId = "PHASE-001-A",
                    Title = "First",
                    Status = SubPhaseStatus.Planned
                }
            ]
        });

        var response = SendToolCall(_globalHost, WorkflowOperations.UpdateSubPhaseStatus, new Dictionary<string, object?>
        {
            ["root"] = _projectRoot,
            ["phaseId"] = "PHASE-001",
            ["subPhaseId"] = "PHASE-001-A",
            ["status"] = "in_progress"
        });

        var payload = ReadToolPayload(response);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal("InProgress", payload.GetProperty("data").GetProperty("newStatus").GetString());
    }

    [Fact]
    public void ToolsList_ExactlyMatchesOperationManifest()
    {
        var response = _globalHost.ProcessRequest(new JsonRpcRequest
        {
            Id = 1,
            Method = "tools/list"
        });

        Assert.NotNull(response);
        Assert.Null(response!.Error);
        var tools = response.Result!.Value.GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var operations = OperationManifestCatalog.GetAll()
            .Select(entry => entry.OperationName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(operations, tools);
    }

    private static JsonRpcResponse SendToolCall(McpHost host, string toolName, Dictionary<string, object?> arguments)
    {
        var request = new JsonRpcRequest
        {
            Id = 1,
            Method = "tools/call",
            Params = JsonSerializer.SerializeToElement(new
            {
                name = toolName,
                arguments
            })
        };

        return host.ProcessRequest(request)!;
    }

    private static JsonElement ReadToolPayload(JsonRpcResponse response)
    {
        Assert.Null(response.Error);
        var result = response.Result!.Value;
        var content = result.GetProperty("content");
        var text = content[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));
        return JsonDocument.Parse(text!).RootElement.Clone();
    }
}
