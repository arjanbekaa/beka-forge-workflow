namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Urgency levels for phases and current-work planning metadata.
/// </summary>
public enum Urgency
{
    /// <summary>No immediate pressure; can be deferred.</summary>
    Low,

    /// <summary>Standard priority; work through in order.</summary>
    Medium,

    /// <summary>Needs attention soon; should be prioritized.</summary>
    High,

    /// <summary>Blocking other work; must be resolved immediately.</summary>
    Critical
}
