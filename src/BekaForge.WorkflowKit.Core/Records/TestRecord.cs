namespace BekaForge.WorkflowKit.Core.Records;

/// <summary>
/// Records a Unity Editor test run performed by Unity Assistant or UnityBridge.
/// Appended to logs/test.jsonl.
/// </summary>
public sealed record TestRecord
{
    /// <summary>Unique identifier in the format TEST-NNN.</summary>
    public required string TestId { get; init; }

    /// <summary>The phase this test log belongs to.</summary>
    public required string PhaseId { get; init; }

    /// <summary>The agent who performed the test run (UnityAssistant or UnityBridge).</summary>
    public required WorkflowActor Actor { get; init; }

    /// <summary>Summary of the test run and results.</summary>
    public required string Summary { get; init; }

    /// <summary>Whether all tests passed (no failures).</summary>
    public required bool Passed { get; init; }

    /// <summary>Whether there were non-blocking warnings in the test run.</summary>
    public bool HasWarnings { get; init; }

    /// <summary>Names of test cases that failed, if any.</summary>
    public IReadOnlyList<string> FailedTests { get; init; } = [];

    /// <summary>Additional notes about the test run.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
