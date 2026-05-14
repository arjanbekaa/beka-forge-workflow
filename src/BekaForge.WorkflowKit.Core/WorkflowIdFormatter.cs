namespace BekaForge.WorkflowKit.Core;

/// <summary>
/// Produces deterministic, uppercase, zero-padded IDs for all WorkflowKit entity types.
/// IDs are used as stable references in JSON state files, JSONL logs, and markdown.
/// </summary>
public static class WorkflowIdFormatter
{
    /// <summary>Formats a phase ID. Example: PHASE-001</summary>
    public static string Phase(int number) => Format("PHASE", number);

    /// <summary>Formats an implementation log ID. Example: IMP-001</summary>
    public static string Implementation(int number) => Format("IMP", number);

    /// <summary>Formats a self-audit log ID. Example: AUD-001</summary>
    public static string Audit(int number) => Format("AUD", number);

    /// <summary>Formats a Codex review log ID. Example: REV-001</summary>
    public static string Review(int number) => Format("REV", number);

    /// <summary>Formats a Unity test log ID. Example: TEST-001</summary>
    public static string Test(int number) => Format("TEST", number);

    /// <summary>Formats a fix log ID. Example: FIX-001</summary>
    public static string Fix(int number) => Format("FIX", number);

    /// <summary>Formats a blocker record ID. Example: BLK-001</summary>
    public static string Blocker(int number) => Format("BLK", number);

    /// <summary>Formats a handoff record ID. Example: HANDOFF-001</summary>
    public static string Handoff(int number) => Format("HANDOFF", number);

    /// <summary>Formats a timing record ID. Example: TIME-001</summary>
    public static string Timing(int number) => Format("TIME", number);

    /// <summary>Formats an event ID. Example: EVT-001</summary>
    public static string Event(int number) => Format("EVT", number);

    /// <summary>
    /// Core formatting logic. Validates the number and produces PREFIX-NNN.
    /// Always uppercase. Zero-padded to at least 3 digits.
    /// </summary>
    private static string Format(string prefix, int number)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                $"ID number must be a positive integer greater than zero. Received: {number}");

        return $"{prefix}-{number:D3}";
    }
}
