namespace BekaForge.WorkflowKit.Markdown;

/// <summary>
/// Merges WorkflowKit-generated regions into an existing markdown document
/// without disturbing any human-written content outside those regions.
///
/// Algorithm:
///   1. Parse the document into segments: human text or named generated region.
///   2. For each section that WorkflowKit wants to write, replace the region
///      content if a marker pair exists, or append a new block at the end if
///      the file has never had that section before.
///   3. Reassemble and return the final document string.
///
/// Marker format:
///   <!-- BEKAFORGE:BEGIN generated:section-name -->
///   ...anything...
///   <!-- BEKAFORGE:END generated:section-name -->
/// </summary>
public sealed class HumanSectionPreserver
{
    // -- Public API -------------------------------------------------------------

    /// <summary>
    /// Merges <paramref name="sections"/> into <paramref name="existingContent"/>.
    /// Returns the merged document.
    /// </summary>
    /// <param name="existingContent">
    ///   Current file content (may be empty or null if the file is new).
    /// </param>
    /// <param name="sections">
    ///   Map of section-name → generated content to embed.
    ///   Only sections present in this map are touched.
    /// </param>
    public string Merge(string? existingContent,
                        IReadOnlyDictionary<string, string> sections)
    {
        if (sections.Count == 0)
            return existingContent ?? string.Empty;

        var segments = ParseSegments(existingContent ?? string.Empty);

        // Replace existing generated segments with new content.
        var touched = new HashSet<string>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] is GeneratedSegment gs && sections.TryGetValue(gs.SectionName, out var newContent))
            {
                segments[i] = new GeneratedSegment(gs.SectionName, newContent);
                touched.Add(gs.SectionName);
            }
        }

        // Append any sections that had no marker in the existing file.
        foreach (var (name, content) in sections)
        {
            if (touched.Contains(name))
                continue;

            // Ensure there is a blank line before appending.
            segments.Add(new HumanSegment("\n"));
            segments.Add(new GeneratedSegment(name, content));
        }

        return Reassemble(segments);
    }

    // -- Segment model ---------------------------------------------------------

    private abstract record Segment;

    /// <summary>Verbatim human-written text — never modified.</summary>
    private sealed record HumanSegment(string Text) : Segment;

    /// <summary>A BEKAFORGE-generated region identified by section name.</summary>
    private sealed record GeneratedSegment(string SectionName, string Content) : Segment;

    // -- Parser ----------------------------------------------------------------

    private static List<Segment> ParseSegments(string content)
    {
        var segments = new List<Segment>();
        int pos = 0;

        while (pos < content.Length)
        {
            // Look for the next BEGIN marker.
            int beginIdx = FindBeginMarker(content, pos, out string? sectionName);

            if (beginIdx < 0 || sectionName is null)
            {
                // No more generated regions — rest is human text.
                if (pos < content.Length)
                    segments.Add(new HumanSegment(content[pos..]));
                break;
            }

            // Everything before the BEGIN marker is human text.
            if (beginIdx > pos)
                segments.Add(new HumanSegment(content[pos..beginIdx]));

            // Find matching END marker.
            string endMarker = MarkdownRegion.End(sectionName);
            int endIdx = content.IndexOf(endMarker, beginIdx, StringComparison.Ordinal);

            if (endIdx < 0)
            {
                // Malformed: BEGIN without END — treat rest as human text.
                segments.Add(new HumanSegment(content[beginIdx..]));
                pos = content.Length;
                break;
            }

            int afterEnd = endIdx + endMarker.Length;

            // Extract the body between BEGIN\n and END.
            string beginMarker = MarkdownRegion.Begin(sectionName);
            int bodyStart = beginIdx + beginMarker.Length;
            // Skip the newline right after the BEGIN marker.
            if (bodyStart < content.Length && content[bodyStart] == '\n')
                bodyStart++;
            else if (bodyStart < content.Length && content[bodyStart] == '\r'
                     && bodyStart + 1 < content.Length && content[bodyStart + 1] == '\n')
                bodyStart += 2;

            string body = bodyStart < endIdx ? content[bodyStart..endIdx] : string.Empty;
            segments.Add(new GeneratedSegment(sectionName, body));

            pos = afterEnd;
        }

        return segments;
    }

    /// <summary>
    /// Searches for the next <c>&lt;!-- BEKAFORGE:BEGIN generated:... --&gt;</c>
    /// starting at <paramref name="startPos"/>.
    /// Returns the index of the marker or -1 if not found.
    /// Sets <paramref name="sectionName"/> to the parsed name when found.
    /// </summary>
    private static int FindBeginMarker(string content, int startPos, out string? sectionName)
    {
        const string prefix = "<!-- BEKAFORGE:BEGIN generated:";
        const string suffix = " -->";

        int idx = startPos;
        while (true)
        {
            int found = content.IndexOf(prefix, idx, StringComparison.Ordinal);
            if (found < 0)
            {
                sectionName = null;
                return -1;
            }

            int nameStart = found + prefix.Length;
            int nameEnd   = content.IndexOf(suffix, nameStart, StringComparison.Ordinal);
            if (nameEnd < 0)
            {
                // Malformed marker; skip past it.
                idx = found + prefix.Length;
                continue;
            }

            sectionName = content[nameStart..nameEnd];
            return found;
        }
    }

    // -- Reassembler -----------------------------------------------------------

    private static string Reassemble(List<Segment> segments)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var seg in segments)
        {
            switch (seg)
            {
                case HumanSegment hs:
                    sb.Append(hs.Text);
                    break;

                case GeneratedSegment gs:
                    sb.Append(MarkdownRegion.Wrap(gs.SectionName, gs.Content));
                    break;
            }
        }

        return sb.ToString();
    }
}
