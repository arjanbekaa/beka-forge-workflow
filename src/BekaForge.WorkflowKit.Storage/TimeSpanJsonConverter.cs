using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Serializes TimeSpan as total seconds (double) for portability.
/// Example: TimeSpan.FromMinutes(45) → 2700.0
/// </summary>
public sealed class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var seconds = reader.GetDouble();
        return TimeSpan.FromSeconds(seconds);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.TotalSeconds);
    }
}
