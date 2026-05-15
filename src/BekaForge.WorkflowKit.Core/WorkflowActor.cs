namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// All known agent and actor roles in the BekaForge workflow.
/// Agents are identified by role, not by external service identity.
///
/// Generic role names (Planner, Implementer, etc.) are preferred for new code.
/// Legacy names (Codex, DeepSeek, etc.) are preserved for backward compatibility
/// with existing JSONL records.
/// </summary>
public enum WorkflowActor
{
    // -- Legacy actor names (preserved for backward compatibility) ------------

    /// <summary>Legacy: Architect, planner, review gate owner.</summary>
    Codex,

    /// <summary>Legacy: Implementation manager, self-auditor, fix executor.</summary>
    DeepSeek,

    /// <summary>Legacy: Implementer used in older workflow records.</summary>
    Claude,

    /// <summary>Legacy: Unity Editor tester, test logger.</summary>
    UnityAssistant,

    /// <summary>Legacy: Future Unity automation bridge (reserved).</summary>
    UnityBridge,

    /// <summary>Legacy: Human supervisor providing direction and approvals.</summary>
    User,

    /// <summary>Legacy: Orchestration state owner, log owner, markdown sync owner.</summary>
    WorkflowKit,

    // -- Generic workflow role names (preferred) ------------------------------

    /// <summary>Architecture, phase dependency management, implementation plan ownership.</summary>
    Planner = 10,

    /// <summary>Code implementation, self-audit, fix execution, implementation logging.</summary>
    Implementer = 11,

    /// <summary>Self-audit or independent audit of phase deliverables.</summary>
    Auditor = 12,

    /// <summary>Independent review gate decision (architecture, safety, completeness).</summary>
    Reviewer = 13,

    /// <summary>Test execution, regression verification, test logging.</summary>
    Validator = 14,

    /// <summary>Fix implementation following review or blocker resolution.</summary>
    Fixer = 15,

    /// <summary>Human approval authority, natural-language direction, manual verification.</summary>
    HumanOwner = 16,

    /// <summary>Orchestration state owner, log owner, status owner, handoff owner, markdown sync owner.</summary>
    WorkflowSystem = 17
}
