namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Explicit bounded retry policy carried by an orchestration session.
/// The runtime reads these values instead of hiding retry limits in code.
/// </summary>
public sealed record OrchestrationAttemptPolicy
{
    public int MaxImplementationAttempts { get; init; } = 3;
    public int MaxAuditAttempts { get; init; } = 2;
    public int MaxReviewAttempts { get; init; } = 2;
    public int MaxValidationAttempts { get; init; } = 2;
    public int MaxFixAttempts { get; init; } = 2;
}
