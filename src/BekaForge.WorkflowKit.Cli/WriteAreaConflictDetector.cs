using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Detects write-area conflicts between concurrently-active phases.
///
/// A conflict occurs when two phases are both in an active-write state
/// (InImplementation, AssignedToImplementation, or FixInProgress) and their
/// declared RequiredFilesOrAreas overlap.
///
/// Two areas overlap when one is a prefix of the other (case-insensitive,
/// normalised to forward slashes) — e.g. "src/Foo/" conflicts with "src/Foo/Bar".
/// An empty RequiredFilesOrAreas list is treated as "unknown area" and does NOT
/// conflict with anything (we can only detect conflicts on explicit declarations).
///
/// No I/O — all state is provided by the caller.
/// </summary>
public static class WriteAreaConflictDetector
{
    /// <summary>Phase states that indicate active, concurrent write activity.</summary>
    public static readonly IReadOnlySet<PhaseState> ActiveWriteStates = new HashSet<PhaseState>
    {
        PhaseState.ReadyForImplementation,
        PhaseState.AssignedToImplementation,
        PhaseState.InImplementation,
        PhaseState.RequiresFix,
        PhaseState.FixInProgress
    };

    /// <summary>A detected conflict between two phases.</summary>
    public sealed record Conflict(
        string PhaseIdA,
        string PhaseIdB,
        IReadOnlyList<string> OverlappingAreas,
        string Description);

    /// <summary>
    /// Returns all write-area conflicts among the provided phases.
    /// Only checks phases that are in <see cref="ActiveWriteStates"/>.
    /// </summary>
    public static IReadOnlyList<Conflict> Detect(IReadOnlyList<Phase> phases)
    {
        var active = phases
            .Where(p => ActiveWriteStates.Contains(p.State)
                     && p.Contract?.RequiredFilesOrAreas.Count > 0)
            .ToList();

        var conflicts = new List<Conflict>();

        for (int i = 0; i < active.Count; i++)
        for (int j = i + 1; j < active.Count; j++)
        {
            var a = active[i];
            var b = active[j];
            var overlap = FindOverlap(
                a.Contract!.RequiredFilesOrAreas,
                b.Contract!.RequiredFilesOrAreas);

            if (overlap.Count > 0)
                conflicts.Add(new Conflict(
                    PhaseIdA:        a.PhaseId,
                    PhaseIdB:        b.PhaseId,
                    OverlappingAreas: overlap,
                    Description:
                        $"{a.PhaseId} ({a.State}) and {b.PhaseId} ({b.State}) " +
                        $"both declare write access to: {string.Join(", ", overlap)}"));
        }

        return conflicts;
    }

    /// <summary>
    /// Returns the write-area conflicts between one specific phase and all other
    /// concurrently-active phases.
    /// </summary>
    public static IReadOnlyList<Conflict> DetectForPhase(Phase target, IReadOnlyList<Phase> allPhases)
    {
        if (target.Contract?.RequiredFilesOrAreas.Count is null or 0)
            return Array.Empty<Conflict>();

        var others = allPhases
            .Where(p => !string.Equals(p.PhaseId, target.PhaseId, StringComparison.OrdinalIgnoreCase)
                     && ActiveWriteStates.Contains(p.State)
                     && p.Contract?.RequiredFilesOrAreas.Count > 0)
            .ToList();

        var conflicts = new List<Conflict>();
        foreach (var other in others)
        {
            var overlap = FindOverlap(
                target.Contract!.RequiredFilesOrAreas,
                other.Contract!.RequiredFilesOrAreas);

            if (overlap.Count > 0)
                conflicts.Add(new Conflict(
                    PhaseIdA:        target.PhaseId,
                    PhaseIdB:        other.PhaseId,
                    OverlappingAreas: overlap,
                    Description:
                        $"{target.PhaseId} ({target.State}) conflicts with " +
                        $"{other.PhaseId} ({other.State}) " +
                        $"on: {string.Join(", ", overlap)}"));
        }

        return conflicts;
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> FindOverlap(
        IReadOnlyList<string> areasA,
        IReadOnlyList<string> areasB)
    {
        var result = new List<string>();
        foreach (var a in areasA)
        foreach (var b in areasB)
        {
            if (PathsOverlap(a, b))
            {
                // Report the shorter (more specific) of the two conflicting areas.
                var reported = a.Length <= b.Length ? a : b;
                if (!result.Contains(reported, StringComparer.OrdinalIgnoreCase))
                    result.Add(reported);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns true if path A and path B overlap — i.e. one is a prefix of the other.
    /// Both paths are normalised to forward-slash, lower-case, trailing-slash before comparison.
    /// </summary>
    private static bool PathsOverlap(string a, string b)
    {
        var na = NormalizePath(a);
        var nb = NormalizePath(b);
        return na.StartsWith(nb, StringComparison.Ordinal)
            || nb.StartsWith(na, StringComparison.Ordinal);
    }

    private static string NormalizePath(string p) =>
        p.Replace('\\', '/').ToLowerInvariant().TrimEnd('/') + "/";
}
