using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

/// <summary>
/// Handles <c>workflow.get_budget_report</c>.
/// Returns the resolved budget profile and the same priority explanation used by
/// budget-aware context retrieval.
/// </summary>
public sealed class GetBudgetReportHandler : IOperationHandler
{
    private readonly WorkflowStore _store;

    public string OperationName => WorkflowOperations.GetBudgetReport;

    public GetBudgetReportHandler(WorkflowStore store)
    {
        _store = store;
    }

    public OperationResult Execute(OperationContext context)
    {
        var configPath = BudgetConfig.ConfigPath(_store.WorkflowRoot);
        var projectConfig = BudgetConfig.Load(configPath);
        var modeStr = context.GetString("mode") ?? context.GetString("budgetMode");

        var source = File.Exists(configPath) ? "project" : "default";
        var mode = projectConfig.DefaultMode;

        if (!string.IsNullOrWhiteSpace(modeStr))
        {
            if (!Enum.TryParse<BudgetMode>(modeStr, ignoreCase: true, out mode))
            {
                return OperationResult.Fail("InvalidBudgetMode",
                    $"Invalid budget mode '{modeStr}'. Valid values: Low, Medium, High, Full.");
            }

            source = "override";
        }

        var effectiveProfile = source == "default"
            ? BudgetProfile.DefaultFor(mode)
            : projectConfig.EffectiveProfile(mode);

        var result = new BudgetConfigResult
        {
            Mode = mode.ToString(),
            Source = source,
            Profile = ToData(effectiveProfile),
            ProjectConfig = File.Exists(configPath)
                ? ToData(projectConfig.EffectiveProfile(projectConfig.DefaultMode))
                : null,
            DefaultProfile = ToData(BudgetProfile.DefaultFor(mode)),
            Warnings =
            [
                "Budget priority: inline override > project config > built-in default (Medium).",
                "workflow.get_relevant_context includes per-response Budget metadata."
            ]
        };

        return OperationResult.Ok(result);
    }

    private static BudgetProfileData ToData(BudgetProfile profile) => new()
    {
        MaxPointers = profile.MaxPointers,
        MaxLogRecords = profile.MaxLogRecords,
        MaxSummaryLength = profile.MaxSummaryLength,
        IncludeMarkdown = profile.IncludeMarkdown,
        IncludeTraces = profile.IncludeTraces,
        IncludeInlineContent = profile.IncludeInlineContent,
        InlineContentMaxBytes = profile.InlineContentMaxBytes,
        MaxEstimatedTokens = profile.MaxEstimatedTokens,
        Priority = profile.Priority,
        Description = profile.Description
    };
}
