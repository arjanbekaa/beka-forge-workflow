using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// The result of a dispatcher operation.
/// Protocol-neutral — HTTP, CLI, and future MCP adapters all consume this.
/// </summary>
public sealed record OperationResult
{
    public required bool Success { get; init; }
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static OperationResult Ok(object? data = null) =>
        new() { Success = true, Data = data };

    public static OperationResult Ok(object? data, string message) =>
        new() { Success = true, Data = data, Message = message };

    public static OperationResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    public static OperationResult FromError(WorkflowError error) =>
        Fail(error.Code, error.Message);
}
