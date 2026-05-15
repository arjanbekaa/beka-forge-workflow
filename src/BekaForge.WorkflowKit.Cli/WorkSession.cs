namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// A work session lock stored as JSON in .workflowkit/work/{phaseId}.json.
/// Tracks which agent is actively working on a phase and in what role.
///
/// A session is considered "stale" (abandoned) if its LastUpdatedUtc is older
/// than <see cref="StaleThreshold"/> (default 2 hours). Stale sessions can be
/// silently replaced by a new `bfwf work begin`.
/// </summary>
public sealed record WorkSession(
    string PhaseId,
    string Role,
    string Actor,
    string? AgentName,
    string? MachineName,
    DateTimeOffset StartedUtc,
    DateTimeOffset LastUpdatedUtc)
{
    /// <summary>Age beyond which a session is considered abandoned/stale.</summary>
    public static TimeSpan StaleThreshold { get; } = TimeSpan.FromHours(2);

    /// <summary>True if this session has not been updated within <see cref="StaleThreshold"/>.</summary>
    public bool IsStale =>
        DateTimeOffset.UtcNow - LastUpdatedUtc > StaleThreshold;

    /// <summary>
    /// Attempts to load a session from the given path.
    /// Returns null if the file does not exist or cannot be deserialized.
    /// </summary>
    public static WorkSession? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<WorkSession>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes this session to <paramref name="path"/>, creating directories as needed.
    /// Overwrites any existing file.
    /// </summary>
    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented        = true
                }),
            System.Text.Encoding.UTF8);
    }
}
