using System.Security.Cryptography;
using System.Text;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Appends JSON lines to an append-only .jsonl file.
///
/// Rules:
/// - The file is created if it does not exist.
/// - Each call appends exactly one line; existing content is never modified.
/// - A newline is always written after the JSON object.
/// - The file is opened with FileShare.Read so other processes can tail the log.
/// - Temporary writer collisions are retried so parallel CLI calls do not fail
///   when tracing or workflow events append at the same time.
/// </summary>
public static class JsonlAppender
{
    private const int MaxAppendAttempts = 200;
    private const int AppendRetryDelayMilliseconds = 25;
    private static readonly TimeSpan MutexRetryDelay = TimeSpan.FromMilliseconds(25);
    private const int MutexRetryLimit = 200;

    /// <summary>
    /// Serializes <paramref name="record"/> and appends it as a single JSON line.
    /// </summary>
    public static void Append<T>(string filePath, T record)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Cannot determine directory for: {filePath}"));

        var line = WorkflowSerializer.SerializeJsonl(record);
        using var mutex = AcquireLock(filePath);

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        filePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read);
                    using var writer = new StreamWriter(stream);

                    writer.WriteLine(line);
                    return;
                }
                catch (Exception ex) when (IsRetryableAppendException(ex) && attempt < MaxAppendAttempts)
                {
                    Thread.Sleep(AppendRetryDelayMilliseconds);
                }
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Reads all records from a .jsonl file.
    /// Skips blank lines. Returns an empty list if the file does not exist.
    /// </summary>
    public static IReadOnlyList<T> ReadAll<T>(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var results = new List<T>();
        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var item = WorkflowSerializer.Deserialize<T>(line);
                if (item is not null)
                    results.Add(item);
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed append-only diagnostic lines should not prevent
                // reading later valid records from the same JSONL file.
            }
        }
        return results;
    }

    private static Mutex AcquireLock(string filePath)
    {
        var mutex = new Mutex(false, BuildMutexName(filePath));

        for (var attempt = 0; attempt < MutexRetryLimit; attempt++)
        {
            try
            {
                if (mutex.WaitOne(MutexRetryDelay))
                    return mutex;
            }
            catch (AbandonedMutexException)
            {
                return mutex;
            }
        }

        mutex.Dispose();
        throw new IOException($"Could not acquire JSONL append mutex for '{filePath}'.");
    }

    private static bool IsRetryableAppendException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private static string BuildMutexName(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        return $"BekaForge.WorkflowKit.JsonlAppender.{Convert.ToHexString(bytes)}";
    }
}
