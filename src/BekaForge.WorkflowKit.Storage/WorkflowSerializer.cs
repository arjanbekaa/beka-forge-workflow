using System.Text.Json;
using System.Text.Json.Serialization;
using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Shared JSON serializer options for BekaForge.WorkflowKit storage.
///
/// Two option sets:
/// - StateOptions: indented JSON for human-readable state files (workflow.json, PHASE-NNN.json)
/// - JsonlOptions: compact single-line JSON for append-only JSONL log files
///
/// Both use camelCase property names and string enum values for readability.
/// </summary>
public static class WorkflowSerializer
{
    /// <summary>
    /// Options for human-readable state files written atomically.
    /// Indented, camelCase, string enums, null values omitted.
    /// </summary>
    public static readonly JsonSerializerOptions StateOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new WorkflowActorJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new TimeSpanJsonConverter()
        }
    };

    /// <summary>
    /// Options for append-only JSONL log files.
    /// Compact (no indentation), camelCase, string enums, null values omitted.
    /// Each record is one line.
    /// </summary>
    public static readonly JsonSerializerOptions JsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new WorkflowActorJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new TimeSpanJsonConverter()
        }
    };

    /// <summary>Serializes a value to a JSON string using StateOptions.</summary>
    public static string SerializeState<T>(T value) =>
        JsonSerializer.Serialize(value, StateOptions);

    /// <summary>Serializes a value to a compact JSON string using JsonlOptions.</summary>
    public static string SerializeJsonl<T>(T value) =>
        JsonSerializer.Serialize(value, JsonlOptions);

    /// <summary>Deserializes a JSON string using StateOptions.</summary>
    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, StateOptions);
}
