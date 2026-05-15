using BekaForge.WorkflowKit.Cli;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// PHASE-023-D: Tests for TuiCapabilityProbe — pre-flight terminal capability check.
///
/// Only <see cref="TuiCapabilityProbe.IsKnownCiEnvironment"/> is unit-testable
/// here because it reads environment variables.  The full <see cref="TuiCapabilityProbe.Check"/>
/// pipeline inspects <see cref="Console.IsOutputRedirected"/>, <see cref="Console.IsInputRedirected"/>,
/// and OS-level terminal variables — those require integration tests that run inside
/// known terminal configurations.
/// </summary>
public sealed class TuiCapabilityProbeTests
{
    // -- IsKnownCiEnvironment --------------------------------------------------

    [Fact]
    public void IsKnownCiEnvironment_NoCiVariables_ReturnsFalse()
    {
        // Save all known CI-related environment variables so we can restore them.
        // GitHub Actions sets CI=true by default; other CI systems set their own.
        // Without this save/clear/restore the test would fail inside real CI.
        var knownCiVars = new[] { "CI", "TF_BUILD", "GITLAB_CI", "JENKINS_HOME", "TEAMCITY_VERSION" };
        var saved = new Dictionary<string, string?>();

        try
        {
            foreach (var name in knownCiVars)
            {
                saved[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }

            Assert.False(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            foreach (var (name, value) in saved)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_GitHubActions_ReturnsTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("CI", "true");
            Assert.True(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_CiEqualsOne_ReturnsTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("CI", "1");
            Assert.True(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_AzurePipelines_ReturnsTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("TF_BUILD", "True");
            Assert.True(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TF_BUILD", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_GitLabCI_ReturnsTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("GITLAB_CI", "true");
            Assert.True(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITLAB_CI", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_Jenkins_ReturnsTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("JENKINS_HOME", "/var/jenkins");
            Assert.True(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JENKINS_HOME", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_TeamCity_ReturnsTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("TEAMCITY_VERSION", "2024.1");
            Assert.True(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEAMCITY_VERSION", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_CiSetToFalse_ReturnsFalse()
    {
        try
        {
            Environment.SetEnvironmentVariable("CI", "false");
            Assert.False(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", null);
        }
    }

    [Fact]
    public void IsKnownCiEnvironment_EmptyCi_ReturnsFalse()
    {
        try
        {
            Environment.SetEnvironmentVariable("CI", "");
            Assert.False(TuiCapabilityProbe.IsKnownCiEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", null);
        }
    }

    // -- CheckResult struct ----------------------------------------------------

    [Fact]
    public void CheckResult_CanRunTrue_HasEmptyReason()
    {
        var result = new TuiCapabilityProbe.CheckResult(true, "");
        Assert.True(result.CanRun);
        Assert.Empty(result.Reason);
    }

    [Fact]
    public void CheckResult_CanRunFalse_HasNonEmptyReason()
    {
        var result = new TuiCapabilityProbe.CheckResult(false, "Test reason");
        Assert.False(result.CanRun);
        Assert.Equal("Test reason", result.Reason);
    }

    // -- Integration note ------------------------------------------------------

    /// <summary>
    /// Full <see cref="TuiCapabilityProbe.Check"/> pipeline integration tests
    /// require specific console environments (piped stdout, missing TERM, etc.)
    /// and should be run manually or via a CI matrix that provisions those
    /// configurations explicitly.
    /// </summary>
    [Fact]
    public void Check_DoesNotThrow()
    {
        // At minimum, Check() must never throw — it must always return a result,
        // even when the environment is unknown.
        var result = TuiCapabilityProbe.Check();
        Assert.NotNull(result.Reason); // Reason is never null
    }
}
