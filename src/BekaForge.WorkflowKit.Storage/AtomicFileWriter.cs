namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Writes files atomically using a write-to-temp-then-rename strategy.
///
/// This prevents partial writes from corrupting state files.
/// The target file is either fully replaced or left unchanged — never partially written.
///
/// Rules:
/// - Temp file is written in the same directory as the target (ensures same-volume rename).
/// - On success: temp file is moved over the target using overwrite.
/// - On failure: temp file is cleaned up; target is untouched.
/// - Unknown files adjacent to the target are never deleted.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="targetPath"/> atomically.
    /// </summary>
    public static void Write(string targetPath, string content)
    {
        var dir = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for path: {targetPath}");

        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".tmp_{Path.GetFileName(targetPath)}_{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup of the temp file; do not disturb the target.
            try { File.Delete(tempPath); } catch { /* ignored */ }
            throw;
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="targetPath"/> atomically (async).
    /// </summary>
    public static async Task WriteAsync(string targetPath, string content, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for path: {targetPath}");

        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".tmp_{Path.GetFileName(targetPath)}_{Guid.NewGuid():N}");

        try
        {
            await File.WriteAllTextAsync(tempPath, content, ct);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* ignored */ }
            throw;
        }
    }
}
