using System.Text.Json.Serialization;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Project-level budget configuration persisted as budget-config.json.
/// Controls the default budget mode for context retrieval operations.
///
/// Priority: inline override (CLI --budget flag) &gt; ProjectBudget &gt; built-in Medium.
/// </summary>
public sealed record BudgetConfig
{
    /// <summary>Schema version for forward compatibility.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Default budget mode for this project.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BudgetMode DefaultMode { get; init; } = BudgetMode.Medium;

    /// <summary>Custom per-mode overrides. If a mode is listed here, its profile overrides the built-in default.</summary>
    public Dictionary<string, BudgetProfileOverride>? ModeOverrides { get; init; }

    /// <summary>Returns the effective profile for a mode, considering overrides.</summary>
    public BudgetProfile EffectiveProfile(BudgetMode mode)
    {
        var baseProfile = BudgetProfile.DefaultFor(mode);

        if (ModeOverrides is not null &&
            ModeOverrides.TryGetValue(mode.ToString(), out var ov) &&
            ov is not null)
        {
            return ov.Apply(baseProfile);
        }

        return baseProfile;
    }

    /// <summary>Persistence helper: saves to the given path.</summary>
    public void Save(string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        System.IO.File.WriteAllText(path, json);
    }

    /// <summary>Persistence helper: loads from the given path, or returns a new default.</summary>
    public static BudgetConfig Load(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<BudgetConfig>(json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new BudgetConfig();
            }
        }
        catch
        {
            // Corrupt file — return default
        }

        return new BudgetConfig();
    }

    /// <summary>Path to budget-config.json relative to workflow root.</summary>
    public static string ConfigPath(string workflowRoot) =>
        System.IO.Path.Combine(workflowRoot, ".workflowkit", "budget-config.json");
}

/// <summary>
/// Override values for a specific budget mode profile.
/// Only non-null fields override the built-in default.
/// </summary>
public sealed record BudgetProfileOverride
{
    public int? MaxPointers { get; init; }
    public int? MaxLogRecords { get; init; }
    public int? MaxSummaryLength { get; init; }
    public bool? IncludeMarkdown { get; init; }
    public bool? IncludeTraces { get; init; }
    public bool? IncludeInlineContent { get; init; }
    public int? InlineContentMaxBytes { get; init; }
    public int? MaxEstimatedTokens { get; init; }
    public string? Priority { get; init; }
    public string? Description { get; init; }

    /// <summary>Applies overrides to a base profile, returning a new profile with merged values.</summary>
    public BudgetProfile Apply(BudgetProfile baseProfile) => new()
    {
        Mode = baseProfile.Mode,
        MaxPointers = MaxPointers ?? baseProfile.MaxPointers,
        MaxLogRecords = MaxLogRecords ?? baseProfile.MaxLogRecords,
        MaxSummaryLength = MaxSummaryLength ?? baseProfile.MaxSummaryLength,
        IncludeMarkdown = IncludeMarkdown ?? baseProfile.IncludeMarkdown,
        IncludeTraces = IncludeTraces ?? baseProfile.IncludeTraces,
        IncludeInlineContent = IncludeInlineContent ?? baseProfile.IncludeInlineContent,
        InlineContentMaxBytes = InlineContentMaxBytes ?? baseProfile.InlineContentMaxBytes,
        MaxEstimatedTokens = MaxEstimatedTokens ?? baseProfile.MaxEstimatedTokens,
        Priority = Priority ?? baseProfile.Priority,
        Description = Description ?? baseProfile.Description
    };
}
