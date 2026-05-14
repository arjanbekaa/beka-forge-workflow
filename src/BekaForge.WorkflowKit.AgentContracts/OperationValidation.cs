namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// A single validation issue found while validating an operation request.
/// Issues can be errors (blocking) or warnings (advisory).
/// </summary>
public sealed record ValidationIssue
{
    /// <summary>The severity of this issue: "error" or "warning".</summary>
    public required string Severity { get; init; }

    /// <summary>A stable identifier for this validation check (e.g. "MissingRequiredParameter").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>The name of the parameter or field causing the issue, if applicable.</summary>
    public string? Field { get; init; }

    /// <summary>
    /// Safer alternative operations, if this operation is deemed unsafe.
    /// Each entry is a canonical operation name that would be safer to call.
    /// </summary>
    public string[]? SaferAlternatives { get; init; }

    public static ValidationIssue Error(string code, string message, string? field = null, string[]? alternatives = null) =>
        new() { Severity = "error", Code = code, Message = message, Field = field, SaferAlternatives = alternatives };

    public static ValidationIssue Warning(string code, string message, string? field = null, string[]? alternatives = null) =>
        new() { Severity = "warning", Code = code, Message = message, Field = field, SaferAlternatives = alternatives };
}

/// <summary>
/// The result of validating an operation request.
/// Returned by workflow.validate_operation_request — side-effect free.
/// </summary>
public sealed record OperationValidationResult
{
    /// <summary>True if no errors were found (warnings are still acceptable).</summary>
    public required bool IsValid { get; init; }

    /// <summary>The operation name that was validated.</summary>
    public required string OperationName { get; init; }

    /// <summary>The access level of the operation being validated.</summary>
    public OperationAccessLevel AccessLevel { get; init; }

    /// <summary>All validation issues (errors and warnings) in priority order.</summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Required parameter names for this operation, extracted from the manifest.
    /// Empty if the operation has no required parameters or couldn't be found.
    /// </summary>
    public string[]? RequiredParameters { get; init; }

    /// <summary>
    /// Write targets for this operation, if it is a Write/Append/Regenerate operation.
    /// Each target names a safe operation, never a raw file path.
    /// </summary>
    public IReadOnlyList<WriteTargetEntry>? WriteTargets { get; init; }

    /// <summary>
    /// The actor role proposed for this request, if known.
    /// </summary>
    public string? ProposedActor { get; init; }

    /// <summary>
    /// The phase ID proposed for this request, if any.
    /// </summary>
    public string? ProposedPhaseId { get; init; }
}
