namespace BekaForge.WorkflowKit.Server;

/// <summary>
/// A single protocol-neutral operation handler.
/// Each workflow.* operation is implemented as its own handler class.
/// </summary>
public interface IOperationHandler
{
    /// <summary>The operation name this handler serves (e.g. "workflow.get_state").</summary>
    string OperationName { get; }

    /// <summary>Executes the operation and returns a result.</summary>
    OperationResult Execute(OperationContext context);
}
