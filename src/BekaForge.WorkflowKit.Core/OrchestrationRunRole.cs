namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Delegated role assigned to an orchestration run.
/// </summary>
public enum OrchestrationRunRole
{
    Manager = 0,
    Implementer = 1,
    Auditor = 2,
    Reviewer = 3,
    Tester = 4,
    Fixer = 5
}
