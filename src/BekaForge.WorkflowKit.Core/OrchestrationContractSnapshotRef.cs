namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Reference to the contract snapshot an orchestration session or run used.
/// </summary>
public sealed record OrchestrationContractSnapshotRef
{
    public string? PhaseId { get; init; }
    public string? SessionId { get; init; }
    public string? SnapshotVersion { get; init; }
    public string? SnapshotHash { get; init; }
    public IReadOnlyList<string> SourceDocumentPaths { get; init; } = [];
}
