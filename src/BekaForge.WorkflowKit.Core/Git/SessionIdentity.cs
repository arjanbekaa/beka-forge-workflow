namespace BekaForge.WorkflowKit.Core.Git;

/// <summary>
/// Lightweight identity for the current user/machine/session.
/// Derived from git config and environment — no passwords, tokens, or private keys.
/// </summary>
public sealed record SessionIdentity
{
    /// <summary>User name from git config (user.name).</summary>
    public string UserName { get; init; } = "unknown";

    /// <summary>User email from git config (user.email).</summary>
    public string UserEmail { get; init; } = "unknown";

    /// <summary>Machine name (Environment.MachineName).</summary>
    public string MachineName { get; init; } = Environment.MachineName;

    /// <summary>OS platform description.</summary>
    public string Platform { get; init; } =
        $"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}";

    /// <summary>Current session ID, if one is active.</summary>
    public string? SessionId { get; init; }

    /// <summary>Whether git config was readable for user identity.</summary>
    public bool GitConfigAvailable { get; init; } = true;

    /// <summary>Creates a SessionIdentity from the local git configuration.</summary>
    /// <param name="workflowRoot">Path to the git repository root.</param>
    public static SessionIdentity FromGitConfig(string workflowRoot)
    {
        var identity = new SessionIdentity();

        try
        {
            var userName = RunGitConfig(workflowRoot, "user.name");
            if (!string.IsNullOrWhiteSpace(userName))
                identity = identity with { UserName = userName.Trim() };

            var userEmail = RunGitConfig(workflowRoot, "user.email");
            if (!string.IsNullOrWhiteSpace(userEmail))
                identity = identity with { UserEmail = userEmail.Trim() };
        }
        catch
        {
            identity = identity with { GitConfigAvailable = false };
        }

        return identity;
    }

    private static string? RunGitConfig(string workDir, string key)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"config {key}",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);

        return process.ExitCode == 0 ? output.Trim() : null;
    }
}
