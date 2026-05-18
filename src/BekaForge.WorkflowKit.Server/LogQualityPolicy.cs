using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

internal enum WorkflowLogKind
{
    Implementation,
    Audit,
    Review,
    Validation,
    Fix
}

internal static class LogQualityPolicy
{
    private static readonly HashSet<string> VagueSummaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "done",
        "fixed",
        "ok",
        "okay",
        "looks good",
        "implemented",
        "implementation complete",
        "audit passed",
        "review passed",
        "validation passed",
        "tests passed"
    };

    public static OperationResult? Validate(
        WorkflowStore store,
        WorkflowLogKind kind,
        string summary,
        string? notes,
        IReadOnlyList<string>? issues = null,
        IReadOnlyList<string>? recommendations = null,
        bool? passed = null,
        bool? requiresFix = null)
    {
        var budgetMode = BudgetConfig.Load(BudgetConfig.ConfigPath(store.WorkflowRoot)).DefaultMode;
        var normalizedSummary = Normalize(summary);
        var noteLength = (notes ?? string.Empty).Trim().Length;
        var issueCount = issues?.Count ?? 0;
        var recommendationCount = recommendations?.Count ?? 0;

        if (budgetMode is BudgetMode.High or BudgetMode.Full)
        {
            if (normalizedSummary.Length < 16 || VagueSummaries.Contains(normalizedSummary))
            {
                return OperationResult.Fail("LogQualityFailed",
                    $"{kind} summary is too vague for {budgetMode} detail mode. " +
                    "Summaries must describe the concrete work, risk, or outcome.");
            }
        }

        if (budgetMode == BudgetMode.Full && normalizedSummary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4)
        {
            return OperationResult.Fail("LogQualityFailed",
                $"{kind} summary is too short for Full detail mode.");
        }

        switch (kind)
        {
            case WorkflowLogKind.Implementation:
            case WorkflowLogKind.Fix:
                if (budgetMode is BudgetMode.High or BudgetMode.Full && noteLength < 24)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        $"{kind} logs require notes explaining what changed in {budgetMode} detail mode.");
                }
                break;

            case WorkflowLogKind.Audit:
                if (budgetMode is BudgetMode.High or BudgetMode.Full && noteLength < 32)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        $"Audit logs require notes covering critical parts checked and risks in {budgetMode} detail mode.");
                }

                if (budgetMode == BudgetMode.Full && issueCount == 0 && recommendationCount == 0)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        "Full detail audit logs must include at least one concrete issue or recommendation.");
                }
                break;

            case WorkflowLogKind.Review:
                if (budgetMode is BudgetMode.High or BudgetMode.Full && noteLength < 32)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        $"Review logs require notes covering critical parts checked and risks in {budgetMode} detail mode.");
                }

                if (budgetMode == BudgetMode.Full && passed == true && recommendationCount == 0 && issueCount == 0)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        "Full detail review logs must include at least one concrete issue or recommendation.");
                }

                if (requiresFix == true && issueCount == 0)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        "Review logs that require fixes must include at least one concrete issue.");
                }
                break;

            case WorkflowLogKind.Validation:
                if (budgetMode is BudgetMode.High or BudgetMode.Full && noteLength < 20)
                {
                    return OperationResult.Fail("LogQualityFailed",
                        $"Validation logs require notes describing what was verified in {budgetMode} detail mode.");
                }
                break;
        }

        return null;
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var chars = trimmed.Where(ch => !char.IsPunctuation(ch)).ToArray();
        return new string(chars).Trim();
    }
}
