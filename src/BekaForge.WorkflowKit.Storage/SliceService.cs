using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BekaForge.WorkflowKit.AgentContracts;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Read-only service for slice operations: file slices, record lookups,
/// JSON Pointer reads, markdown region extraction, and file history.
/// All operations are read-only — they never mutate source files.
/// </summary>
public sealed class SliceService
{
    private readonly string _workflowRoot;
    private readonly WorkflowStore _store;

    public SliceService(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        _store = new WorkflowStore(workflowRoot);
    }

    // ── File slice ───────────────────────────────────────────────────────────────

    public FileSliceResult GetFileSlice(string relativePath, int? startLine = null, int? endLine = null)
    {
        var warnings = new List<string>();
        var fullPath = ResolvePath(relativePath);
        if (fullPath is null)
            return new FileSliceResult { FilePath = relativePath, Content = "", Warnings = ["Path not found or outside workflow root."] };

        if (!File.Exists(fullPath))
            return new FileSliceResult { FilePath = relativePath, Content = "", Warnings = ["File not found."] };

        var allLines = File.ReadAllLines(fullPath);
        var total = allLines.Length;
        var sl = Math.Max(1, startLine ?? 1);
        var el = Math.Min(total, endLine ?? total);

        if (sl > total)
            return new FileSliceResult { FilePath = relativePath, Content = "", StartLine = sl, EndLine = el, TotalLines = total, Warnings = ["Start line exceeds file length."] };

        var content = string.Join(Environment.NewLine, allLines[(sl - 1)..el]);
        var hash = ComputeHash(content);
        var stale = IsFileStale(fullPath, hash);

        if (stale) warnings.Add("Content may be stale — file has been modified since indexing.");

        return new FileSliceResult
        {
            FilePath = relativePath, Content = content,
            StartLine = sl, EndLine = el, TotalLines = total,
            ContentHash = hash, IsStale = stale, Warnings = warnings
        };
    }

    // ── Record slice ─────────────────────────────────────────────────────────────

    public RecordSliceResult GetRecordSlice(string recordId)
    {
        var warnings = new List<string>();
        var upper = recordId.ToUpperInvariant();

        try
        {
            if (upper.StartsWith("IMP-"))
            {
                var records = _store.ReadAllImplementations().Where(r => r.ImplementationId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "implementation");
                var r = records.Last();
                return new RecordSliceResult { RecordId = recordId, RecordType = "implementation", JsonContent = JsonSerializer.Serialize(r), PhaseId = r.PhaseId };
            }
            if (upper.StartsWith("AUD-"))
            {
                var records = _store.ReadAllAudits().Where(r => r.AuditId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "audit");
                return new RecordSliceResult { RecordId = recordId, RecordType = "audit", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("REV-"))
            {
                var records = _store.ReadAllReviews().Where(r => r.ReviewId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "review");
                return new RecordSliceResult { RecordId = recordId, RecordType = "review", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("TEST-"))
            {
                var records = _store.ReadAllTests().Where(r => r.TestId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "test");
                return new RecordSliceResult { RecordId = recordId, RecordType = "test", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("FIX-"))
            {
                var records = _store.ReadAllFixes().Where(r => r.FixId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "fix");
                return new RecordSliceResult { RecordId = recordId, RecordType = "fix", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("BLK-"))
            {
                var records = _store.ReadAllBlockers().Where(r => r.BlockerId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "blocker");
                return new RecordSliceResult { RecordId = recordId, RecordType = "blocker", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("HANDOFF-"))
            {
                var records = _store.ReadAllHandoffs().Where(r => r.HandoffId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "handoff");
                return new RecordSliceResult { RecordId = recordId, RecordType = "handoff", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("TIME-"))
            {
                var records = _store.ReadAllTimings().Where(r => r.TimingId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "timing");
                return new RecordSliceResult { RecordId = recordId, RecordType = "timing", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }
            if (upper.StartsWith("EVT-"))
            {
                var records = _store.ReadAllEvents().Where(r => r.EventId == recordId).ToList();
                if (records.Count == 0) return NotFound(recordId, "event");
                return new RecordSliceResult { RecordId = recordId, RecordType = "event", JsonContent = JsonSerializer.Serialize(records.Last()), PhaseId = records.Last().PhaseId };
            }

            return new RecordSliceResult { RecordId = recordId, RecordType = "unknown", JsonContent = "{}", Warnings = [$"Unknown record prefix. Expected: IMP-, AUD-, REV-, TEST-, FIX-, BLK-, HANDOFF-, TIME-, or EVT-."] };
        }
        catch (Exception ex)
        {
            return new RecordSliceResult { RecordId = recordId, RecordType = "error", JsonContent = "{}", Warnings = [$"Error reading record: {ex.Message}"] };
        }
    }

    private static RecordSliceResult NotFound(string id, string type) =>
        new() { RecordId = id, RecordType = type, JsonContent = "{}", Warnings = [$"Record '{id}' not found."] };

    // ── JSON Pointer ─────────────────────────────────────────────────────────────

    public JsonPointerResult GetJsonPointerValue(string sourceFile, string pointer)
    {
        var warnings = new List<string>();
        var fullPath = ResolvePath(sourceFile);
        if (fullPath is null || !File.Exists(fullPath))
            return new JsonPointerResult { SourceFile = sourceFile, Pointer = pointer, Found = false, Warnings = ["Source file not found."] };

        try
        {
            var json = File.ReadAllText(fullPath);
            using var doc = JsonDocument.Parse(json);
            var element = ResolvePointer(doc.RootElement, pointer);

            if (element is null)
                return new JsonPointerResult { SourceFile = sourceFile, Pointer = pointer, Found = false, Warnings = [$"Pointer '{pointer}' not found in {sourceFile}."] };

            return new JsonPointerResult { SourceFile = sourceFile, Pointer = pointer, Value = element.Value.ToString(), Found = true };
        }
        catch (Exception ex)
        {
            return new JsonPointerResult { SourceFile = sourceFile, Pointer = pointer, Found = false, Warnings = [$"Error: {ex.Message}"] };
        }
    }

    private static JsonElement? ResolvePointer(JsonElement root, string pointer)
    {
        var tokens = pointer.TrimStart('/').Split('/');
        var current = root;
        foreach (var token in tokens)
        {
            if (token.Length == 0) continue;
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out var prop))
                    return null;
                current = prop;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(token, out var idx) && idx < current.GetArrayLength())
            {
                current = current[idx];
            }
            else return null;
        }
        return current;
    }

    // ── Markdown region ──────────────────────────────────────────────────────────

    public MarkdownRegionResult GetMarkdownRegion(string relativePath, string sectionName)
    {
        var fullPath = ResolvePath(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
            return new MarkdownRegionResult { FilePath = relativePath, SectionName = sectionName, Content = "", Found = false, Warnings = ["File not found."] };

        try
        {
            var lines = File.ReadAllLines(fullPath);
            var beginMarker = $"<!-- BEKAFORGE:BEGIN generated:{sectionName} -->";
            var endMarker = $"<!-- BEKAFORGE:END generated:{sectionName} -->";

            int? begin = null, end = null;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == beginMarker) begin = i;
                if (lines[i].Trim() == endMarker) end = i;
            }

            if (begin is null || end is null)
                return new MarkdownRegionResult { FilePath = relativePath, SectionName = sectionName, Content = "", Found = false, Warnings = [$"Region '{sectionName}' not found in {relativePath}."] };

            var content = string.Join(Environment.NewLine, lines[(begin.Value + 1)..end.Value]);
            return new MarkdownRegionResult { FilePath = relativePath, SectionName = sectionName, Content = content, Found = true };
        }
        catch (Exception ex)
        {
            return new MarkdownRegionResult { FilePath = relativePath, SectionName = sectionName, Content = "", Found = false, Warnings = [$"Error: {ex.Message}"] };
        }
    }

    // ── File history ─────────────────────────────────────────────────────────────

    public FileHistoryResult GetFileHistory(string relativePath)
    {
        var warnings = new List<string>();
        var entries = new List<FileHistoryEntry>();

        try
        {
            foreach (var r in _store.ReadAllImplementations())
                if (r.FilesModified?.Contains(relativePath) == true)
                    entries.Add(new FileHistoryEntry { PhaseId = r.PhaseId, RecordType = "implementation", RecordId = r.ImplementationId, Summary = r.Summary });

            foreach (var r in _store.ReadAllFixes())
                if (r.FilesModified?.Contains(relativePath) == true)
                    entries.Add(new FileHistoryEntry { PhaseId = r.PhaseId, RecordType = "fix", RecordId = r.FixId, Summary = r.Summary });

        }
        catch (Exception ex)
        {
            warnings.Add($"Error scanning records: {ex.Message}");
        }

        entries = entries.OrderByDescending(e => e.RecordId).ToList();

        if (entries.Count == 0)
            warnings.Add($"No records found referencing '{relativePath}'.");

        return new FileHistoryResult { FilePath = relativePath, Entries = entries, Warnings = warnings };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private string? ResolvePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_workflowRoot, relativePath));
        var normalizedWorkflow = Path.GetFullPath(_workflowRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedFull = Path.GetFullPath(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Path must be within the workflow root.
        if (!normalizedFull.StartsWith(normalizedWorkflow + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            normalizedFull != normalizedWorkflow)
            return null;

        return full;
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private bool IsFileStale(string path, string currentHash)
    {
        // Simple staleness check: if the file exists but was modified since last known state.
        // For now, always report as not-stale (full staleness tracking requires index).
        return false;
    }
}
