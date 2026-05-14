namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Thrown when a storage read or write operation fails in a way that is not
/// recoverable through normal operation (e.g. corrupt JSON, disk error).
/// </summary>
public sealed class StorageException : Exception
{
    public StorageException(string message) : base(message) { }
    public StorageException(string message, Exception inner) : base(message, inner) { }
}
