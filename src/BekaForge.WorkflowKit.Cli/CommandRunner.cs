using System.Diagnostics;
using System.Text;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Runs an external command, captures its output and exit code, and returns a
/// structured result ready to be stored as a validation evidence artifact.
///
/// No workflow knowledge lives here — it is pure process execution.
/// </summary>
public static class CommandRunner
{
    /// <summary>Result of a command execution.</summary>
    public sealed record RunResult(
        string Command,
        int ExitCode,
        string StdOut,
        string StdErr,
        TimeSpan Duration,
        bool TimedOut,
        bool Succeeded);

    /// <summary>
    /// Runs <paramref name="command"/> (parsed as exe + args) with a configurable
    /// timeout. Stdout and stderr are captured and returned in the result.
    ///
    /// Never throws — all errors are surfaced via the returned <see cref="RunResult"/>.
    /// </summary>
    /// <param name="command">Full command line, e.g. "dotnet test --no-build".</param>
    /// <param name="timeoutSeconds">Wall-clock timeout in seconds (default: 120).</param>
    /// <param name="workingDirectory">Working directory (default: current).</param>
    public static async Task<RunResult> RunAsync(
        string command,
        int timeoutSeconds = 120,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new RunResult(command, -1, "", "Command is empty.", TimeSpan.Zero, false, false);

        var (exe, argStr) = ParseCommand(command);
        var sw = Stopwatch.StartNew();
        var stdOut = new StringBuilder();
        var stdErr  = new StringBuilder();
        bool timedOut = false;
        int exitCode  = -1;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = argStr,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                WorkingDirectory       = workingDirectory ?? Directory.GetCurrentDirectory()
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            // Read both streams asynchronously to prevent deadlocks on large output.
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            bool finished = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000));

            if (!finished)
            {
                timedOut = true;
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            }
            else
            {
                exitCode = proc.ExitCode;
            }

            // Always await the stream tasks to ensure buffers are flushed.
            stdOut.Append(await outTask);
            stdErr.Append(await errTask);
        }
        catch (Exception ex)
        {
            stdErr.AppendLine($"[CommandRunner] Exception launching process: {ex.Message}");
            exitCode = -1;
        }

        sw.Stop();
        return new RunResult(
            Command:   command,
            ExitCode:  timedOut ? -1 : exitCode,
            StdOut:    stdOut.ToString(),
            StdErr:    stdErr.ToString(),
            Duration:  sw.Elapsed,
            TimedOut:  timedOut,
            Succeeded: !timedOut && exitCode == 0);
    }

    /// <summary>
    /// Formats a <see cref="RunResult"/> into a human-readable evidence artifact
    /// suitable for writing to a file.
    /// </summary>
    public static string FormatArtifact(RunResult result, string phaseId, DateTimeOffset collectedUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Beka Forge Workflow — Validation Evidence Artifact ===");
        sb.AppendLine($"Phase:     {phaseId}");
        sb.AppendLine($"Command:   {result.Command}");
        sb.AppendLine($"Collected: {collectedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Duration:  {result.Duration.TotalSeconds:F2}s");
        sb.AppendLine($"ExitCode:  {result.ExitCode}");
        sb.AppendLine($"TimedOut:  {result.TimedOut}");
        sb.AppendLine($"Succeeded: {result.Succeeded}");
        sb.AppendLine();
        sb.AppendLine("--- STDOUT ---");
        sb.AppendLine(string.IsNullOrWhiteSpace(result.StdOut) ? "(empty)" : result.StdOut.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("--- STDERR ---");
        sb.AppendLine(string.IsNullOrWhiteSpace(result.StdErr) ? "(empty)" : result.StdErr.TrimEnd());
        return sb.ToString();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>Splits a command line into (executable, arguments).</summary>
    private static (string exe, string args) ParseCommand(string command)
    {
        command = command.Trim();

        // Quoted executable: "C:\Program Files\app.exe" arg1 arg2
        if (command.StartsWith('"'))
        {
            int closeQuote = command.IndexOf('"', 1);
            if (closeQuote > 0)
            {
                string quoted = command[1..closeQuote];
                string rest   = command[(closeQuote + 1)..].TrimStart();
                return (quoted, rest);
            }
        }

        // Unquoted: split at first space
        int space = command.IndexOf(' ');
        return space < 0
            ? (command, string.Empty)
            : (command[..space], command[(space + 1)..]);
    }
}
