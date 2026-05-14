using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// JSON converter for <see cref="WorkflowActor"/> that uses case-insensitive
/// enum name matching and writes camelCase strings.
/// This replaces reliance on <see cref="JsonStringEnumConverter"/> which may
/// fail when JSON values use alternate casing conventions from different agents.
/// </summary>
public sealed class WorkflowActorJsonConverter : JsonConverter<WorkflowActor>
{
    public override WorkflowActor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return WorkflowActor.WorkflowKit;

        // Case-insensitive lookup first
        if (Enum.TryParse<WorkflowActor>(value, ignoreCase: true, out var actor))
            return actor;

        // Fallback: manual lowercase mapping for legacy values not matching any enum name
        return value.ToLowerInvariant() switch
        {
            "workflowkit" => WorkflowActor.WorkflowKit,
            "deepseek" => WorkflowActor.DeepSeek,
            "claude" => WorkflowActor.Claude,
            "codex" => WorkflowActor.Codex,
            "unityassistant" => WorkflowActor.UnityAssistant,
            "unitybridge" => WorkflowActor.UnityBridge,
            "user" => WorkflowActor.User,
            "planner" => WorkflowActor.Planner,
            "implementer" => WorkflowActor.Implementer,
            "auditor" => WorkflowActor.Auditor,
            "reviewer" => WorkflowActor.Reviewer,
            "validator" => WorkflowActor.Validator,
            "fixer" => WorkflowActor.Fixer,
            "humanowner" => WorkflowActor.HumanOwner,
            "workflowsystem" => WorkflowActor.WorkflowSystem,
            _ => throw new JsonException($"Unknown WorkflowActor value: '{value}'")
        };
    }

    public override void Write(Utf8JsonWriter writer, WorkflowActor value, JsonSerializerOptions options)
    {
        // Write camelCase string matching existing JSON convention
        var name = value.ToString();
        var camel = char.ToLowerInvariant(name[0]) + name[1..];
        writer.WriteStringValue(camel);
    }
}
