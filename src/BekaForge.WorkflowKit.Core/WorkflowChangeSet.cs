using System.Text.Json;

namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// File-backed batch of allowlisted workflow mutations.
/// </summary>
public sealed record WorkflowChangeSet
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Mode { get; init; } = "apply";
    public IReadOnlyList<WorkflowChangeSetOperation> Operations { get; init; } = [];
}

public sealed record WorkflowChangeSetOperation
{
    public string? Type { get; init; }
    public string? RefId { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; } =
        new Dictionary<string, JsonElement>();
}

public sealed record WorkflowChangeSetIssue
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public int? OperationIndex { get; init; }
    public string? OperationType { get; init; }
    public string? RefId { get; init; }
}

public sealed record WorkflowChangeSetOperationPreview
{
    public required int OperationIndex { get; init; }
    public required string Type { get; init; }
    public string? RefId { get; init; }
    public required string OperationName { get; init; }
    public required string Summary { get; init; }
    public bool WouldWrite { get; init; }
    public IReadOnlyDictionary<string, object?> ResolvedParameters { get; init; } =
        new Dictionary<string, object?>();
}

public sealed record WorkflowChangeSetValidationReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string? FilePath { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsValid => Issues.Count == 0;
    public IReadOnlyList<WorkflowChangeSetIssue> Issues { get; init; } = [];
    public IReadOnlyList<WorkflowChangeSetIssue> Warnings { get; init; } = [];
    public IReadOnlyList<WorkflowChangeSetOperationPreview> OperationPreviews { get; init; } = [];
}

public sealed record WorkflowChangeSetAppliedOperation
{
    public required int OperationIndex { get; init; }
    public required string Type { get; init; }
    public string? RefId { get; init; }
    public required string OperationName { get; init; }
    public string? CreatedId { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
}

public sealed record WorkflowChangeSetApplyReport
{
    public bool DryRun { get; init; }
    public bool Applied { get; init; }
    public WorkflowChangeSetValidationReport Validation { get; init; } = new();
    public IReadOnlyList<WorkflowChangeSetAppliedOperation> AppliedOperations { get; init; } = [];
    public IReadOnlyDictionary<string, string> CreatedIds { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<WorkflowChangeSetIssue> Warnings { get; init; } = [];
}
