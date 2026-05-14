using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.set_budget_config</c>.
/// Updates the project-level budget configuration (budget-config.json).
///
/// Accepts: mode (Low|Medium|High|Full) and optional per-mode overrides.
/// All writes go through the dispatcher-safe path.
/// </summary>
public sealed class SetBudgetConfigHandler : IOperationHandler
{
    private readonly WorkflowStore _store;

    public string OperationName => WorkflowOperations.SetBudgetConfig;

    public SetBudgetConfigHandler(WorkflowStore store)
    {
        _store = store;
    }

    public OperationResult Execute(OperationContext context)
    {
        var configPath = BudgetConfig.ConfigPath(_store.WorkflowRoot);
        var config = File.Exists(configPath)
            ? BudgetConfig.Load(configPath)
            : new BudgetConfig();

        // Update default mode
        var modeStr = context.GetString("mode");
        if (!string.IsNullOrWhiteSpace(modeStr))
        {
            if (!Enum.TryParse<BudgetMode>(modeStr, ignoreCase: true, out var parsedMode))
            {
                return OperationResult.Fail("InvalidBudgetMode",
                    $"Invalid budget mode '{modeStr}'. Valid values: Low, Medium, High, Full.");
            }
            config = config with { DefaultMode = parsedMode };
        }

        // Update per-mode overrides
        var overridesStr = context.GetString("modeOverrides");
        if (!string.IsNullOrWhiteSpace(overridesStr))
        {
            try
            {
                var overrides = System.Text.Json.JsonSerializer.Deserialize<
                    Dictionary<string, BudgetProfileOverride>>(overridesStr,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (overrides is not null)
                {
                    var existing = config.ModeOverrides ?? new Dictionary<string, BudgetProfileOverride>();
                    foreach (var (key, value) in overrides)
                    {
                        existing[key] = value;
                    }
                    config = config with { ModeOverrides = existing };
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("InvalidOverrides",
                    $"Failed to parse modeOverrides JSON: {ex.Message}");
            }
        }

        // Persist
        try
        {
            var dir = Path.GetDirectoryName(configPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            config.Save(configPath);

            var effectiveProfile = config.EffectiveProfile(config.DefaultMode);
            var result = new BudgetConfigResult
            {
                Mode = config.DefaultMode.ToString(),
                Source = "project",
                Profile = new BudgetProfileData
                {
                    MaxPointers = effectiveProfile.MaxPointers,
                    MaxLogRecords = effectiveProfile.MaxLogRecords,
                    MaxSummaryLength = effectiveProfile.MaxSummaryLength,
                    IncludeMarkdown = effectiveProfile.IncludeMarkdown,
                    IncludeTraces = effectiveProfile.IncludeTraces,
                    IncludeInlineContent = effectiveProfile.IncludeInlineContent,
                    InlineContentMaxBytes = effectiveProfile.InlineContentMaxBytes,
                    MaxEstimatedTokens = effectiveProfile.MaxEstimatedTokens,
                    Priority = effectiveProfile.Priority,
                    Description = effectiveProfile.Description
                },
                Warnings = new[] { "Budget config saved. Use workflow.get_budget_config to verify." }
            };

            return OperationResult.Ok(result);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("SaveFailed",
                $"Failed to save budget config: {ex.Message}");
        }
    }
}
