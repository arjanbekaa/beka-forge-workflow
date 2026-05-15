using System.Text.Json;
using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// JSON converter for <see cref="PhaseState"/> that reads both legacy and
/// current state names and writes camelCase current names.
///
/// Legacy → Current mappings (case-insensitive on read):
///   readyForCodexReview    → ReadyForReview
///   codexReviewInProgress  → ReviewInProgress
///   codexReviewLogged      → ReviewLogged
///   readyForUnityTest      → ReadyForTest
///   unityTestInProgress    → TestInProgress
///   unityTestLogged        → TestLogged
///   failedTests            → FailedValidation
///
/// Writes always use the current enum member names (e.g. "readyForReview").
/// </summary>
public sealed class PhaseStateJsonConverter : JsonConverter<PhaseState>
{
    public override PhaseState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return PhaseState.Planned;

        // Try current names first
        if (Enum.TryParse<PhaseState>(value, ignoreCase: true, out var state))
            return state;

        // Legacy name mapping (case-insensitive)
        return value.ToLowerInvariant() switch
        {
            "readyforcodexreview" => PhaseState.ReadyForReview,
            "codexreviewinprogress" => PhaseState.ReviewInProgress,
            "codexreviewlogged" => PhaseState.ReviewLogged,
            "readyforunitytest" => PhaseState.ReadyForTest,
            "unitytestinprogress" => PhaseState.TestInProgress,
            "unitytestlogged" => PhaseState.TestLogged,
            "failedtests" => PhaseState.FailedValidation,
            _ => throw new JsonException($"Unknown PhaseState value: '{value}'")
        };
    }

    public override void Write(Utf8JsonWriter writer, PhaseState value, JsonSerializerOptions options)
    {
        var name = value.ToString();
        var camel = char.ToLowerInvariant(name[0]) + name[1..];
        writer.WriteStringValue(camel);
    }
}
