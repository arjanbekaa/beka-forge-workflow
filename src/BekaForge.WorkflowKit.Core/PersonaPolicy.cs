namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// A small persona profile used for workflow guidance and task-fit checks.
/// Personas do not bypass workflow rules; they only describe safe operating lanes.
/// </summary>
public sealed record PersonaProfile
{
    public required string PersonaId { get; init; }
    public required string DisplayName { get; init; }
    public string Summary { get; init; } = string.Empty;
    public WorkflowActor PrimaryActor { get; init; } = WorkflowActor.Implementer;
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<string> SupportedTaskTypes { get; init; } = [];
    public IReadOnlyList<string> PreferredTaskKeywords { get; init; } = [];
    public IReadOnlyList<string> ProhibitedTaskKeywords { get; init; } = [];
}

/// <summary>
/// Defines the safe task lane for a persona-facing workflow activity.
/// </summary>
public sealed record TaskExecutionPolicy
{
    public required string TaskType { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyList<WorkflowActor> AllowedActors { get; init; } = [];
    public IReadOnlyList<string> AllowedOperations { get; init; } = [];
    public IReadOnlyList<string> ForbiddenOperations { get; init; } = [];
    public IReadOnlyList<string> RequiredContext { get; init; } = [];
    public IReadOnlyList<string> DisallowedPhrases { get; init; } = [];
    public bool RequiresPhaseContext { get; init; } = true;
    public bool RequiresEvidence { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public bool ForbidSelfApproval { get; init; }
}

/// <summary>
/// The resolved persona catalog.
/// </summary>
public sealed record PersonaCatalog
{
    public required IReadOnlyList<PersonaProfile> Personas { get; init; }
    public required IReadOnlyList<TaskExecutionPolicy> TaskPolicies { get; init; }
}

/// <summary>
/// Result of loading the persona catalog from persisted files or safe defaults.
/// </summary>
public sealed record PersonaCatalogLoadResult
{
    public required PersonaCatalog Catalog { get; init; }
    public required string Source { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// A scored persona recommendation for a task.
/// </summary>
public sealed record PersonaRecommendationEntry
{
    public required string PersonaId { get; init; }
    public required string DisplayName { get; init; }
    public required WorkflowActor PrimaryActor { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<string> MatchedTaskTypes { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
}

/// <summary>
/// Recommendation result for a free-form task.
/// </summary>
public sealed record PersonaRecommendationResult
{
    public required string Task { get; init; }
    public string? RequestedOperation { get; init; }
    public string? RequestedActor { get; init; }
    public required string CatalogSource { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public required IReadOnlyList<PersonaRecommendationEntry> Recommendations { get; init; }
}

/// <summary>
/// Validation result for a persona-task pairing.
/// </summary>
public sealed record PersonaTaskValidationResult
{
    public required bool IsValid { get; init; }
    public required string PersonaId { get; init; }
    public required string DisplayName { get; init; }
    public required string CatalogSource { get; init; }
    public string? RequestedOperation { get; init; }
    public string? RequestedActor { get; init; }
    public string? RequestedPhaseId { get; init; }
    public string? MatchedTaskType { get; init; }
    public IReadOnlyList<string> RequiredContext { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
