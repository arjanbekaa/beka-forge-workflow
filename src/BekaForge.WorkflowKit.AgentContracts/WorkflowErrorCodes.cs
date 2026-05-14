namespace BekaForge.WorkflowKit.AgentContracts;

/// <summary>
/// String constants for all structured error codes returned by WorkflowKit operations.
/// These are the Code values in WorkflowError and the ErrorCode in AgentResponse.
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>The requested phase state transition is not allowed by the state machine.</summary>
    public const string InvalidTransition = "InvalidTransition";

    /// <summary>The phase is in a terminal state and cannot transition further.</summary>
    public const string TerminalState = "TerminalState";

    /// <summary>A BLOCKED transition was requested without a blocker reason or blocker ID.</summary>
    public const string BlockerRequired = "BlockerRequired";

    /// <summary>PASS was requested but the Unity test requirement has not been satisfied.</summary>
    public const string UnityTestRequired = "UnityTestRequired";

    /// <summary>General input or state validation failure.</summary>
    public const string ValidationFailed = "ValidationFailed";

    /// <summary>A requested resource (phase, record, workflow) was not found.</summary>
    public const string NotFound = "NotFound";

    /// <summary>A resource with the given ID or key already exists.</summary>
    public const string AlreadyExists = "AlreadyExists";

    /// <summary>A storage read or write operation failed.</summary>
    public const string StorageError = "StorageError";

    /// <summary>The workflow has not been initialized at the given root path.</summary>
    public const string NotInitialized = "NotInitialized";
}
