namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Represents the outcome of a WorkflowKit operation.
/// Operations return WorkflowResult rather than throwing exceptions so callers
/// can handle errors without catch blocks and state is never partially mutated.
/// </summary>
public abstract record WorkflowResult<T>
{
    public bool IsSuccess => this is WorkflowSuccess<T>;
    public bool IsFailure => this is WorkflowFailure<T>;

    /// <summary>Gets the value if successful; throws if this is a failure result.</summary>
    public T Value => this is WorkflowSuccess<T> s
        ? s.Data
        : throw new InvalidOperationException($"Cannot access Value on a failed result. Error: {((WorkflowFailure<T>)this).Error.Message}");

    /// <summary>Gets the error if failure; throws if this is a success result.</summary>
    public WorkflowError Error => this is WorkflowFailure<T> f
        ? f.WorkflowError
        : throw new InvalidOperationException("Cannot access Error on a successful result.");
}

/// <summary>Successful operation result carrying a typed payload.</summary>
public sealed record WorkflowSuccess<T>(T Data) : WorkflowResult<T>;

/// <summary>Failed operation result carrying a structured error.</summary>
public sealed record WorkflowFailure<T>(WorkflowError WorkflowError) : WorkflowResult<T>;

/// <summary>
/// Static factory for creating WorkflowResult instances.
/// </summary>
public static class WorkflowResult
{
    public static WorkflowResult<T> Ok<T>(T value) => new WorkflowSuccess<T>(value);

    public static WorkflowResult<T> Fail<T>(WorkflowError error) => new WorkflowFailure<T>(error);

    public static WorkflowResult<Unit> Ok() => new WorkflowSuccess<Unit>(Unit.Value);

    public static WorkflowResult<Unit> Fail(WorkflowError error) => new WorkflowFailure<Unit>(error);
}

/// <summary>
/// Represents a successful operation with no meaningful return value.
/// </summary>
public sealed record Unit
{
    public static readonly Unit Value = new();
    private Unit() { }
}
