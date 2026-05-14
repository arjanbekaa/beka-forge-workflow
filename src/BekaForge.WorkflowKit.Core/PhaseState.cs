namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// All valid states in the phase lifecycle state machine.
/// Exact names are part of the contract — do not rename without a migration plan.
/// </summary>
public enum PhaseState
{
    /// <summary>Phase is defined but not yet ready for implementation.</summary>
    Planned,

    /// <summary>Phase is ready to be picked up for implementation.</summary>
    ReadyForImplementation,

    /// <summary>Phase has been assigned to an implementation agent.</summary>
    AssignedToImplementation,

    /// <summary>Implementation is actively in progress.</summary>
    InImplementation,

    /// <summary>Implementation is complete and logged.</summary>
    ImplementationLogged,

    /// <summary>Self-audit by the implementing agent is complete and logged.</summary>
    AuditLogged,

    /// <summary>Phase is ready for Codex architecture review.</summary>
    ReadyForCodexReview,

    /// <summary>Codex review is actively in progress.</summary>
    CodexReviewInProgress,

    /// <summary>Codex review is complete and logged.</summary>
    CodexReviewLogged,

    /// <summary>Codex review determined that fixes are required.</summary>
    RequiresFix,

    /// <summary>Fix implementation is actively in progress.</summary>
    FixInProgress,

    /// <summary>Fix is complete and logged; ready to re-enter Codex review.</summary>
    FixLogged,

    /// <summary>Phase is ready for Unity Editor testing.</summary>
    ReadyForUnityTest,

    /// <summary>Unity Editor testing is actively in progress.</summary>
    UnityTestInProgress,

    /// <summary>Unity testing is complete and logged.</summary>
    UnityTestLogged,

    /// <summary>Phase passed all gates. Terminal success state.</summary>
    Pass,

    /// <summary>Phase passed all gates with non-blocking warnings. Terminal success state.</summary>
    PassWithWarnings,

    /// <summary>Phase is blocked by an unresolved blocker. Progress is suspended.</summary>
    Blocked,

    /// <summary>Phase failed the architecture review gate. Terminal failure state.</summary>
    FailedArchitecture,

    /// <summary>Phase failed due to compile errors. Terminal failure state.</summary>
    FailedCompile,

    /// <summary>Phase failed Unity or regression tests. Terminal failure state.</summary>
    FailedTests
}
