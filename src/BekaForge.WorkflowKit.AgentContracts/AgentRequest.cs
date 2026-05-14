namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Base request envelope for all agent-facing WorkflowKit operations.
/// Agents post this to the operation dispatcher.
/// </summary>
public sealed record AgentRequest
{
    /// <summary>The operation to invoke. Must be one of the constants in WorkflowOperations.</summary>
    public required string Operation { get; init; }

    /// <summary>Phase ID targeted by this operation, if applicable (PHASE-NNN).</summary>
    public string? PhaseId { get; init; }

    /// <summary>
    /// Arbitrary key-value parameters specific to the operation.
    /// The dispatcher will validate and extract these per-operation.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; init; } = [];
}
