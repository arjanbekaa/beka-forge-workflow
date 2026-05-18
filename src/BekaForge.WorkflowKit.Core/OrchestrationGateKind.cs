namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Gate kinds used by orchestration gate decisions.
/// </summary>
public enum OrchestrationGateKind
{
    Implementation = 0,
    Audit = 1,
    Review = 2,
    Validation = 3,
    HumanAttention = 4,
    Stop = 5
}
