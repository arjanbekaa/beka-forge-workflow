namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// Typed response envelope returned by WorkflowKit operations.
/// All operations return success=true with a Data payload, or success=false with ErrorCode and Message.
/// </summary>
public sealed record AgentResponse<T>
{
    /// <summary>True if the operation succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>The result payload when Success is true.</summary>
    public T? Data { get; init; }

    /// <summary>Structured error code when Success is false. See WorkflowErrorCodes.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error message when Success is false.</summary>
    public string? Message { get; init; }

    /// <summary>Creates a successful response with a typed payload.</summary>
    public static AgentResponse<T> Ok(T data) =>
        new() { Success = true, Data = data };

    /// <summary>Creates a failed response with a structured error code and message.</summary>
    public static AgentResponse<T> Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    /// <summary>Creates a failed response from a WorkflowError.</summary>
    public static AgentResponse<T> FromError(Core.WorkflowError error) =>
        Fail(error.Code, error.Message);
}

/// <summary>
/// Untyped response envelope for operations that return no payload.
/// </summary>
public sealed record AgentResponse
{
    /// <summary>True if the operation succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Structured error code when Success is false. See WorkflowErrorCodes.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error message when Success is false.</summary>
    public string? Message { get; init; }

    /// <summary>Creates a successful response with no payload.</summary>
    public static AgentResponse Ok() =>
        new() { Success = true };

    /// <summary>Creates a failed response with a structured error code and message.</summary>
    public static AgentResponse Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    /// <summary>Creates a failed response from a WorkflowError.</summary>
    public static AgentResponse FromError(Core.WorkflowError error) =>
        Fail(error.Code, error.Message);
}
