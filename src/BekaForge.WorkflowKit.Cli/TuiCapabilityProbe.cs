using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// PHASE-023-D: Pre-flight capability check for the Terminal.Gui TUI.
///
/// Called before <see cref="TuiApp.Run"/> so that capability failures are
/// caught cleanly — no partial <c>Application.Init()</c> side-effects,
/// no torn screen state, and a clear fallback message.
///
/// Design notes:
///   - No Terminal.Gui types are referenced here; this runs before Init().
///   - Checks are ordered from cheapest (property read) to most expensive.
///   - The probe is intentionally conservative: a false-positive "cannot run"
///     forces the plain fallback, which is always safe.
///   - WPF is not referenced and is not checked; TUI operates on any OS.
/// </summary>
public static class TuiCapabilityProbe
{
    /// <summary>Result of a capability check.</summary>
    /// <param name="CanRun">True if Terminal.Gui is likely to initialise successfully.</param>
    /// <param name="Reason">Human-readable explanation when <paramref name="CanRun"/> is false.</param>
    public readonly record struct CheckResult(bool CanRun, string Reason);

    /// <summary>
    /// Runs all capability checks and returns the first failure found,
    /// or a success result if no issues are detected.
    /// </summary>
    public static CheckResult Check()
    {
        // ── 1. Redirected stdout ──────────────────────────────────────────
        // Terminal.Gui draws directly to the console buffer. If stdout is a
        // pipe or file, ANSI escape sequences cannot produce a readable TUI.
        if (Console.IsOutputRedirected)
            return new(false,
                "stdout is redirected (pipe or file). " +
                "Use 'bfwf status' for non-interactive output.");

        // ── 2. Headless Unix terminal ─────────────────────────────────────
        // On Linux/macOS a missing or 'dumb' TERM variable means the terminal
        // does not support cursor addressing, so Terminal.Gui would fail.
        if (!OperatingSystem.IsWindows())
        {
            var term = Environment.GetEnvironmentVariable("TERM");
            if (string.IsNullOrEmpty(term))
                return new(false,
                    "TERM environment variable is not set. " +
                    "Run inside a capable terminal emulator or use 'bfwf status --watch'.");

            if (term.Equals("dumb", StringComparison.OrdinalIgnoreCase))
                return new(false,
                    "TERM=dumb — terminal does not support cursor addressing. " +
                    "Use 'bfwf status --watch' for headless monitoring.");
        }

        // ── 3. Known headless CI environments ────────────────────────────
        // GitHub Actions sets CI=true; Azure Pipelines sets TF_BUILD=True.
        // Both redirect I/O and have no interactive console.
        if (IsKnownCiEnvironment())
            return new(false,
                "Running inside a CI environment. " +
                "Use 'bfwf status --watch' for headless monitoring.");

        // ── 4. stdin redirected (non-interactive shell) ───────────────────
        // If stdin is not attached to a console the user cannot interact with
        // the TUI anyway (no keyboard events).
        if (Console.IsInputRedirected)
            return new(false,
                "stdin is redirected — no keyboard input available. " +
                "Use 'bfwf status --watch' for non-interactive output.");

        return new(true, string.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when common CI/CD environment variables indicate a
    /// headless build agent where interactive TUI is not meaningful.
    /// </summary>
    public static bool IsKnownCiEnvironment() =>
        // GitHub Actions
        Environment.GetEnvironmentVariable("CI") is "true" or "1" ||
        // Azure Pipelines
        Environment.GetEnvironmentVariable("TF_BUILD") is "True" ||
        // GitLab CI
        Environment.GetEnvironmentVariable("GITLAB_CI") is "true" ||
        // Jenkins
        Environment.GetEnvironmentVariable("JENKINS_HOME") is not null ||
        // TeamCity
        Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null;
}
