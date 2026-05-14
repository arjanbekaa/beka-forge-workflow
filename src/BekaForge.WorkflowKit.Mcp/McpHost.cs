using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Mcp;

/// <summary>
/// Model Context Protocol host for WorkflowKit.
/// Reads newline-delimited JSON-RPC 2.0 requests from stdin and writes responses to stdout.
/// </summary>
public sealed class McpHost
{
    private readonly string? _defaultRoot;
    private readonly ProjectRegistry? _globalRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, OperationDispatcher> _dispatcherCache = new(StringComparer.OrdinalIgnoreCase);

    public McpHost(string? defaultRoot = null, ProjectRegistry? globalRegistry = null)
    {
        _defaultRoot = string.IsNullOrWhiteSpace(defaultRoot) ? null : Path.GetFullPath(defaultRoot);
        _globalRegistry = globalRegistry;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Converters = { new JsonElementConverter() }
        };
    }

    public void Run()
    {
        Console.Error.WriteLine(_defaultRoot is null
            ? "[WorkflowKit MCP] Global multi-project mode"
            : $"[WorkflowKit MCP] Single-project mode: {_defaultRoot}");

        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonRpcResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOptions);
                if (request is null)
                {
                    response = JsonRpcResponse.Fail(null, JsonRpcErrorCodes.ParseError, "Failed to parse JSON-RPC request.");
                }
                else
                {
                    response = ProcessRequest(request) ?? JsonRpcResponse.Success(request.Id, new { }, _jsonOptions);
                }
            }
            catch (JsonException ex)
            {
                response = JsonRpcResponse.Fail(null, JsonRpcErrorCodes.ParseError, $"JSON parse error: {ex.Message}");
            }
            catch (Exception ex)
            {
                response = JsonRpcResponse.Fail(null, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
            }

            WriteResponse(response);
        }
    }

    public JsonRpcResponse? ProcessRequest(JsonRpcRequest request)
    {
        return request.Method switch
        {
            "initialize" => JsonRpcResponse.Success(request.Id, new McpServerInfo
            {
                ProtocolVersion = "2024-11-05",
                Server = new McpServerIdentity
                {
                    Name = "BekaForge WorkflowKit MCP",
                    Version = "1.0.0"
                },
                Capabilities = new McpCapabilities
                {
                    Tools = new McpToolsCapability { ListChanged = false }
                }
            }, _jsonOptions),
            "initialized" => null,
            "ping" => JsonRpcResponse.Success(request.Id, new { }, _jsonOptions),
            "tools/list" => JsonRpcResponse.Success(request.Id, new { tools = McpToolMapping.GetAllTools() }, _jsonOptions),
            "tools/call" => HandleToolsCall(request),
            _ => JsonRpcResponse.Fail(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method: {request.Method}")
        };
    }

    private JsonRpcResponse HandleToolsCall(JsonRpcRequest request)
    {
        var callParams = DeserializeCallParams(request.Params);
        if (callParams is null || string.IsNullOrWhiteSpace(callParams.Name))
            return JsonRpcResponse.Fail(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing tool call parameters.");

        var operationName = McpToolMapping.ToolNameToOperation(callParams.Name);
        if (operationName is null)
            return JsonRpcResponse.Fail(request.Id, JsonRpcErrorCodes.InvalidParams, $"Unknown tool: {callParams.Name}");

        var resolvedRoot = ResolveRoot(callParams.Arguments);
        if (!resolvedRoot.Success)
            return JsonRpcResponse.Fail(request.Id, JsonRpcErrorCodes.InvalidParams, resolvedRoot.ErrorMessage!);

        if (!ProjectRegistry.IsValidWorkflowRoot(resolvedRoot.Root!))
        {
            return JsonRpcResponse.Fail(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                $"Path is not a valid WorkflowKit project (no .workflowkit/workflow.json): {resolvedRoot.Root}");
        }

        var dispatcher = GetDispatcher(resolvedRoot.Root);
        if (dispatcher is null)
        {
            return JsonRpcResponse.Fail(
                request.Id,
                JsonRpcErrorCodes.InternalError,
                $"Failed to initialize workflow store for: {resolvedRoot.Root}");
        }

        try
        {
            var context = BuildOperationContext(operationName, callParams.Arguments);
            var result = dispatcher.Dispatch(context);

            var payload = new
            {
                success = result.Success,
                operation = operationName,
                root = resolvedRoot.Root,
                data = result.Data,
                errorCode = result.ErrorCode,
                message = result.Message
            };

            var toolResult = result.Success
                ? McpToolCallResult.Ok(payload, _jsonOptions)
                : McpToolCallResult.Fail(payload, _jsonOptions);

            return JsonRpcResponse.Success(request.Id, toolResult, _jsonOptions);
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Success(
                request.Id,
                McpToolCallResult.Fail(new
                {
                    success = false,
                    operation = operationName,
                    root = resolvedRoot.Root,
                    errorCode = "DispatchError",
                    message = ex.Message
                }, _jsonOptions),
                _jsonOptions);
        }
    }

    private McpToolCallRequest? DeserializeCallParams(JsonElement? paramsElement)
    {
        if (paramsElement is not { } element)
            return null;

        try
        {
            return JsonSerializer.Deserialize<McpToolCallRequest>(element.GetRawText(), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private RootResolution ResolveRoot(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return _defaultRoot is null
                ? RootResolution.Fail("Missing 'root' or 'projectId' argument. Provide an absolute root path or a registered project ID.")
                : RootResolution.Ok(_defaultRoot);
        }

        if (arguments.TryGetValue("root", out var rootElement) &&
            rootElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(rootElement.GetString()))
        {
            return RootResolution.Ok(Path.GetFullPath(rootElement.GetString()!));
        }

        if (arguments.TryGetValue("projectId", out var projectIdElement) &&
            projectIdElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(projectIdElement.GetString()))
        {
            var projectId = projectIdElement.GetString()!;
            var resolved = _globalRegistry?.Resolve(projectId);
            if (!string.IsNullOrWhiteSpace(resolved))
                return RootResolution.Ok(resolved!);

            if (_defaultRoot is not null)
            {
                var localRegistry = new ProjectRegistry(_defaultRoot);
                resolved = localRegistry.Resolve(projectId);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return RootResolution.Ok(resolved!);
            }

            return _defaultRoot is null
                ? RootResolution.Fail($"Unknown projectId '{projectId}'.")
                : RootResolution.Ok(_defaultRoot);
        }

        return _defaultRoot is null
            ? RootResolution.Fail("Missing 'root' or 'projectId' argument. Provide an absolute root path or a registered project ID.")
            : RootResolution.Ok(_defaultRoot);
    }

    private OperationDispatcher? GetDispatcher(string root)
    {
        if (_dispatcherCache.TryGetValue(root, out var dispatcher))
            return dispatcher;

        try
        {
            dispatcher = new OperationDispatcher(new WorkflowStore(root));
            _dispatcherCache[root] = dispatcher;
            return dispatcher;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WorkflowKit MCP] Failed to create dispatcher for {root}: {ex.Message}");
            return null;
        }
    }

    private OperationContext BuildOperationContext(string operationName, Dictionary<string, JsonElement>? arguments)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        string? phaseId = null;

        if (arguments is not null)
        {
            foreach (var (key, value) in arguments)
            {
                if (key is "root" or "projectId")
                    continue;

                var normalizedKey = NormalizeArgumentKey(operationName, key);
                var normalizedValue = NormalizeArgumentValue(operationName, normalizedKey, value);

                if (string.Equals(normalizedKey, "phaseId", StringComparison.OrdinalIgnoreCase) &&
                    normalizedValue is string phaseIdValue &&
                    !string.IsNullOrWhiteSpace(phaseIdValue))
                {
                    phaseId = phaseIdValue;
                }

                parameters[normalizedKey] = normalizedValue;
            }
        }

        var actor = WorkflowActor.Implementer;
        if (parameters.TryGetValue("actor", out var actorValue) &&
            actorValue is string actorName &&
            Enum.TryParse<WorkflowActor>(actorName, ignoreCase: true, out var parsedActor))
        {
            actor = parsedActor;
        }

        return new OperationContext
        {
            Operation = operationName,
            Actor = actor,
            PhaseId = phaseId,
            Parameters = parameters
        };
    }

    private static string NormalizeArgumentKey(string operationName, string key)
    {
        return operationName switch
        {
            var op when op == WorkflowOperations.CreateHandoff && key == "task" => "summary",
            var op when op == WorkflowOperations.SetBudgetConfig && key == "budgetMode" => "mode",
            _ => key
        };
    }

    private static object? NormalizeArgumentValue(string operationName, string key, JsonElement value)
    {
        if (operationName == WorkflowOperations.SetTraceOptions &&
            key == "enabled" &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean() ? "Basic" : "Off";
        }

        if (operationName == WorkflowOperations.UpdateSubPhaseStatus &&
            key == "status" &&
            value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            return raw?.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase) switch
            {
                "inprogress" => nameof(SubPhaseStatus.InProgress),
                "completed" => nameof(SubPhaseStatus.Completed),
                "complete" => nameof(SubPhaseStatus.Completed),
                "planned" => nameof(SubPhaseStatus.Planned),
                "blocked" => nameof(SubPhaseStatus.Blocked),
                "deferred" => nameof(SubPhaseStatus.Deferred),
                _ => raw
            };
        }

        return JsonElementToObject(value);
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.GetRawText()
        };
    }

    private void WriteResponse(JsonRpcResponse response)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(response, _jsonOptions));
        Console.Out.Flush();
    }

    private sealed class JsonElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonDocument.ParseValue(ref reader).RootElement.Clone();

        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
            => value.WriteTo(writer);
    }

    private readonly record struct RootResolution(bool Success, string? Root, string? ErrorMessage)
    {
        public static RootResolution Ok(string root) => new(true, root, null);
        public static RootResolution Fail(string errorMessage) => new(false, null, errorMessage);
    }
}
