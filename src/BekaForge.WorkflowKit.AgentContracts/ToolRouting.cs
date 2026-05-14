namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// A single routing rule that maps an intent keyword to a WorkflowKit operation
/// with a confidence score. Used by the ToolRoutingCatalog to recommend
/// operations for agent task descriptions.
/// </summary>
public sealed record ToolRoutingRule
{
    /// <summary>Lowercase keyword or short phrase that triggers this rule (e.g. "get state", "blocker").</summary>
    public required string IntentKeyword { get; init; }

    /// <summary>Canonical operation name (e.g. "workflow.get_state").</summary>
    public required string OperationName { get; init; }

    /// <summary>Confidence score 0.0–1.0. Primary matches use 1.0; alternatives use lower scores.</summary>
    public required double Confidence { get; init; }

    /// <summary>True if this is the primary/best match for the intent keyword.</summary>
    public required bool IsPrimary { get; init; }
}

/// <summary>
/// A single ranked entry in an operation recommendation result.
/// Includes the operation name, confidence, access level, summary,
/// and an optional access-level warning string.
/// </summary>
public sealed record OperationRecommendationEntry
{
    /// <summary>Canonical operation name.</summary>
    public required string OperationName { get; init; }

    /// <summary>Match confidence 0.0–1.0.</summary>
    public required double Confidence { get; init; }

    /// <summary>Access level of the recommended operation.</summary>
    public required OperationAccessLevel AccessLevel { get; init; }

    /// <summary>One-line summary from the manifest.</summary>
    public required string Summary { get; init; }

    /// <summary>Safety warning for write/append/regenerate operations, null if read.</summary>
    public string? AccessWarning { get; init; }
}

/// <summary>
/// The full recommendation result for a given task description.
/// Contains ranked operation matches, aggregated warnings,
/// and an optional safer alternative when the intent appears unsafe.
/// </summary>
public sealed record OperationRecommendation
{
    /// <summary>The task description that was analyzed.</summary>
    public required string TaskDescription { get; init; }

    /// <summary>Ranked list of recommended operations (best match first).</summary>
    public required IReadOnlyList<OperationRecommendationEntry> Recommendations { get; init; }

    /// <summary>Aggregated safety warnings for this recommendation.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// If the intent appears dangerous (raw file edit, log rewrite),
    /// this field contains the safer WorkflowKit operation to use instead.
    /// </summary>
    public string? SaferAlternative { get; init; }
}
