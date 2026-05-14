namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Describes the access level (safety class) of a WorkflowKit operation.
/// Used by the operation manifest to help agents and tools decide
/// whether an operation is safe to call in a given context.
/// </summary>
public enum OperationAccessLevel
{
    /// <summary>Pure read — no mutation of any kind.</summary>
    Read,

    /// <summary>Appends new records to JSONL logs without overwriting state.</summary>
    Append,

    /// <summary>Modifies authoritative JSON state files (workflow.json, phase JSON).</summary>
    Write,

    /// <summary>Rebuilds generated read models (markdown, manifest JSON, index).</summary>
    Regenerate
}

/// <summary>
/// A safe write target for an operation. Write targets expose safe operation names
/// and descriptions, never raw file paths or editable line ranges.
/// </summary>
public sealed record WriteTargetEntry
{
    /// <summary>The safe operation name to call for this write purpose (e.g. "workflow.create_implementation_log").</summary>
    public required string OperationName { get; init; }

    /// <summary>Human-readable description of what this target writes to or mutates.</summary>
    public required string TargetDescription { get; init; }

    /// <summary>The access level of this write target.</summary>
    public required OperationAccessLevel AccessLevel { get; init; }

    /// <summary>True if this target only appends new records (historical logs).</summary>
    public required bool IsAppendOnly { get; init; }

    /// <summary>True if this target is tracked as an event (EVT-).</summary>
    public bool IsEventTracked { get; init; }

    /// <summary>
    /// Key parameters required for this write target operation.
    /// Used by validation to report missing required parameters.
    /// </summary>
    public string[]? RequiredParameters { get; init; }

    /// <summary>
    /// The actor roles that are considered suitable to call this operation.
    /// Empty or null means any actor is permitted.
    /// </summary>
    public string[]? SuitableActors { get; init; }
}

/// <summary>
/// A single entry in the operation manifest describing one WorkflowKit operation.
/// This is code-owned metadata — the catalog in Server is authoritative.
/// </summary>
public sealed record OperationManifestEntry
{
    /// <summary>The canonical operation name (e.g. "workflow.get_state").</summary>
    public required string OperationName { get; init; }

    /// <summary>Safety classification for this operation.</summary>
    public required OperationAccessLevel AccessLevel { get; init; }

    /// <summary>Logical grouping (e.g. "Workflow state reads", "Phase management").</summary>
    public required string Category { get; init; }

    /// <summary>One-line human-readable description of what this operation does.</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Fully-qualified handler type name, if this operation is backed by a handler class.
    /// Null for operations that are read-model-only (no dispatcher handler).
    /// </summary>
    public string? HandlerTypeName { get; init; }

    /// <summary>
    /// Write targets for this operation. Non-null only for Write/Append/Regenerate operations.
    /// Each entry names a safe operation call, never a raw file path or line range.
    /// </summary>
    public IReadOnlyList<WriteTargetEntry>? WriteTargets { get; init; }
}

/// <summary>
/// The full operation manifest — a generated read model describing every
/// WorkflowKit operation known to the system. This is rebuildable from code
/// and must not be treated as source of truth.
/// </summary>
public sealed record OperationManifest
{
    /// <summary>Schema version for the manifest format.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>ISO 8601 UTC timestamp of generation.</summary>
    public required string GeneratedUtc { get; init; }

    /// <summary>All operation entries in this manifest.</summary>
    public required IReadOnlyList<OperationManifestEntry> Operations { get; init; }
}
