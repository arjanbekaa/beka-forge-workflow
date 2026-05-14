using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.get_budget_config</c>.
/// Returns the current budget configuration including effective profile,
/// project config, and built-in defaults.
/// </summary>
public sealed class GetBudgetConfigHandler : IOperationHandler
{
    private readonly WorkflowStore _store;

    public string OperationName => WorkflowOperations.GetBudgetConfig;

    public GetBudgetConfigHandler(WorkflowStore store)
    {
        _store = store;
    }

    public OperationResult Execute(OperationContext context)
    {
        // Resolve the mode: check for override in context, then project config, then default
        var modeStr = context.GetString("mode");
        BudgetMode mode = BudgetMode.Medium;
        string source = "default";

        var configPath = BudgetConfig.ConfigPath(_store.WorkflowRoot);
        var hasProjectConfig = File.Exists(configPath);
        var projectConfig = BudgetConfig.Load(configPath);

        if (!string.IsNullOrWhiteSpace(modeStr))
        {
            if (!Enum.TryParse<BudgetMode>(modeStr, ignoreCase: true, out var parsedMode))
            {
                return OperationResult.Fail("InvalidBudgetMode",
                    $"Invalid budget mode '{modeStr}'. Valid values: Low, Medium, High, Full.");
            }

            mode = parsedMode;
            source = "override";
        }
        else if (hasProjectConfig)
        {
            mode = projectConfig.DefaultMode;
            source = "project";
        }
        else
        {
            mode = BudgetMode.Medium;
            source = "default";
        }

        var effectiveProfile = hasProjectConfig
            ? projectConfig.EffectiveProfile(mode)
            : BudgetProfile.DefaultFor(mode);
        var defaultProfile = BudgetProfile.DefaultFor(mode);

        var result = new BudgetConfigResult
        {
            Mode = mode.ToString(),
            Source = source,
            Profile = ToData(effectiveProfile),
            ProjectConfig = hasProjectConfig ? ToData(projectConfig.EffectiveProfile(projectConfig.DefaultMode)) : null,
            DefaultProfile = ToData(defaultProfile),
            Warnings = new[]
            {
                "Budget priority: inline override > project config > built-in default (Medium).",
                "Use workflow.set_budget_config to change the project-level default mode.",
                "Pass mode=Low|Medium|High|Full to override per-request."
            }
        };

        return OperationResult.Ok(result);
    }

    private static BudgetProfileData ToData(BudgetProfile p) => new()
    {
        MaxPointers = p.MaxPointers,
        MaxLogRecords = p.MaxLogRecords,
        MaxSummaryLength = p.MaxSummaryLength,
        IncludeMarkdown = p.IncludeMarkdown,
        IncludeTraces = p.IncludeTraces,
        IncludeInlineContent = p.IncludeInlineContent,
        InlineContentMaxBytes = p.InlineContentMaxBytes,
        MaxEstimatedTokens = p.MaxEstimatedTokens,
        Priority = p.Priority,
        Description = p.Description
    };
}
