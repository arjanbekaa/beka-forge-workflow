using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Mcp;

/// <summary>
/// MCP protocol types: tool definitions, call requests, call results.
/// Follows the Model Context Protocol specification for tools.
/// </summary>

/// <summary>An MCP tool definition exposed via tools/list.</summary>
public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public McpInputSchema InputSchema { get; set; } = new();
}

/// <summary>JSON Schema for tool input parameters.</summary>
public sealed class McpInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, McpProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }
}

/// <summary>A single property in a tool's input schema.</summary>
public sealed class McpProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }
}

/// <summary>Request payload for tools/call.</summary>
public sealed class McpToolCallRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Arguments { get; set; }
}

/// <summary>Result returned by a tool call.</summary>
public sealed class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpContent> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; set; }

    public static McpToolCallResult Ok(object data, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(data, options ?? DefaultOptions);
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = json }
            }
        };
    }

    public static McpToolCallResult Fail(string message)
    {
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = message }
            },
            IsError = true
        };
    }

    public static McpToolCallResult Fail(object data, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(data, options ?? DefaultOptions);
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = json }
            },
            IsError = true
        };
    }

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

/// <summary>Content block within a tool call result.</summary>
public sealed class McpContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>Server info returned in the initialize response.</summary>
public sealed class McpServerInfo
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("serverInfo")]
    public McpServerIdentity Server { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public McpCapabilities Capabilities { get; set; } = new();
}

public sealed class McpServerIdentity
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "BekaForge WorkflowKit MCP";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
}

public sealed class McpCapabilities
{
    [JsonPropertyName("tools")]
    public McpToolsCapability Tools { get; set; } = new();
}

public sealed class McpToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; } = false;
}
