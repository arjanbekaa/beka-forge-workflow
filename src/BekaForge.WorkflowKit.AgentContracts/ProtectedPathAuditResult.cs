namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Result of auditing protected workflow paths for unauthorized direct writes.
/// Returned by workflow.audit_protected_paths.
/// </summary>
public sealed record ProtectedPathAuditResult
{
    /// <summary>True if all protected paths are intact with no unauthorized writes.</summary>
    public required bool AllProtected { get; init; }

    /// <summary>List of individual path audit results.</summary>
    public required IReadOnlyList<ProtectedPathEntry> Paths { get; init; }

    /// <summary>Summary message suitable for display.</summary>
    public required string Summary { get; init; }
}

/// <summary>
/// Audit result for a single protected path or directory.
/// </summary>
public sealed record ProtectedPathEntry
{
    /// <summary>The relative path within .workflowkit (or "AGENTS.md" for root-level).</summary>
    public required string Path { get; init; }

    /// <summary>Whether this path is protected (agents must never write here directly).</summary>
    public required bool IsProtected { get; init; }

    /// <summary>Whether the file exists on disk.</summary>
    public required bool Exists { get; init; }

    /// <summary>
    /// "ok" — file exists and is protected, "missing" — file does not exist (may be ok),
    /// "unprotected" — file exists but is not in the protected list (investigate),
    /// "integrity_ok" — JSONL/JSON integrity verified.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Last write time if file exists, null otherwise.</summary>
    public DateTimeOffset? LastWriteUtc { get; init; }

    /// <summary>Human-readable note about this path.</summary>
    public string? Note { get; init; }
}
