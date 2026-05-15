using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using Microsoft.Data.Sqlite;

namespace BekaForge.WorkflowKit.Storage;

/// <summary>
/// Builds and rebuilds the SQLite context index under .workflowkit/index/workflowkit.db.
/// The index is a rebuildable read model — source JSON/JSONL is always authoritative.
///
/// Rebuild is idempotent: deletes the existing database and recreates all tables
/// and indexes from scratch.
/// </summary>
public sealed class ContextIndexBuilder
{
    private readonly string _workflowRoot;
    private readonly WorkflowStore _store;

    public ContextIndexBuilder(string workflowRoot)
    {
        _workflowRoot = workflowRoot;
        _store = new WorkflowStore(workflowRoot);
    }

    /// <summary>
    /// Full path to the SQLite database file.
    /// </summary>
    public string DatabasePath => WorkflowLayout.WorkflowKitDbPath(_workflowRoot);

    /// <summary>
    /// Rebuilds the entire index from source JSON/JSONL files.
    /// Deletes any existing database first.
    /// Returns a health summary with counts and status.
    /// </summary>
    public IndexHealth Rebuild()
    {
        var dir = Path.GetDirectoryName(DatabasePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Delete existing database for clean rebuild.
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath))
            File.Delete(DatabasePath);

        try
        {
            using var connection = new SqliteConnection(CreateConnectionString());
            connection.Open();

            CreateSchema(connection);
            var health = PopulateData(connection);

            connection.Close();
            return health;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Returns the current health of the index without rebuilding.
    /// Returns null if the database does not exist.
    /// </summary>
    public IndexHealth? GetHealth()
    {
        if (!File.Exists(DatabasePath))
            return null;

        try
        {
            using var connection = new SqliteConnection(CreateConnectionString());
            connection.Open();

            var health = ReadHealth(connection);
            connection.Close();
            return health;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    // -- Schema -------------------------------------------------------------------

    private static void CreateSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            -- Phases
            CREATE TABLE phases (
                phase_id       TEXT PRIMARY KEY,
                phase_number   INTEGER NOT NULL,
                title          TEXT NOT NULL,
                state          TEXT NOT NULL,
                assigned_agent TEXT,
                summary        TEXT,
                created_utc    TEXT,
                updated_utc    TEXT,
                completed_utc  TEXT
            );

            -- Implementation records
            CREATE TABLE implementation_records (
                implementation_id TEXT PRIMARY KEY,
                phase_id         TEXT NOT NULL,
                actor            TEXT,
                title            TEXT,
                summary          TEXT,
                status           TEXT,
                created_utc      TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Audit records
            CREATE TABLE audit_records (
                audit_id    TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                actor       TEXT,
                summary     TEXT,
                passed      INTEGER,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Review records
            CREATE TABLE review_records (
                review_id   TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                actor       TEXT,
                summary     TEXT,
                passed      INTEGER,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Test records
            CREATE TABLE test_records (
                test_id     TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                actor       TEXT,
                summary     TEXT,
                passed      INTEGER,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Fix records
            CREATE TABLE fix_records (
                fix_id      TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                actor       TEXT,
                summary     TEXT,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Blocker records
            CREATE TABLE blocker_records (
                blocker_id  TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                reason      TEXT,
                reported_by TEXT,
                is_resolved INTEGER,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Handoff records
            CREATE TABLE handoff_records (
                handoff_id  TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                from_actor  TEXT,
                to_actor    TEXT,
                summary     TEXT,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Timing records
            CREATE TABLE timing_records (
                timing_id   TEXT PRIMARY KEY,
                phase_id    TEXT NOT NULL,
                actor       TEXT,
                activity    TEXT,
                duration_seconds REAL,
                created_utc TEXT,
                FOREIGN KEY (phase_id) REFERENCES phases(phase_id)
            );

            -- Events
            CREATE TABLE events (
                event_id    TEXT PRIMARY KEY,
                event_type  TEXT NOT NULL,
                actor       TEXT,
                phase_id    TEXT,
                summary     TEXT,
                timestamp   TEXT
            );

            -- Source file tracking
            CREATE TABLE source_files (
                file_path    TEXT PRIMARY KEY,
                file_type    TEXT NOT NULL,
                record_count INTEGER,
                last_checked TEXT
            );

            -- FTS table for full-text search across records and events
            CREATE VIRTUAL TABLE fts_index USING fts5(
                content_type,
                content_id,
                text_content,
                phase_id
            );

            -- Indexes
            CREATE INDEX idx_impl_phase ON implementation_records(phase_id);
            CREATE INDEX idx_audit_phase ON audit_records(phase_id);
            CREATE INDEX idx_review_phase ON review_records(phase_id);
            CREATE INDEX idx_test_phase ON test_records(phase_id);
            CREATE INDEX idx_fix_phase ON fix_records(phase_id);
            CREATE INDEX idx_blocker_phase ON blocker_records(phase_id);
            CREATE INDEX idx_handoff_phase ON handoff_records(phase_id);
            CREATE INDEX idx_timing_phase ON timing_records(phase_id);
            CREATE INDEX idx_events_phase ON events(phase_id);
            CREATE INDEX idx_events_type ON events(event_type);
        ";
        cmd.ExecuteNonQuery();
    }

    // -- Population ---------------------------------------------------------------

    private IndexHealth PopulateData(SqliteConnection connection)
    {
        var health = new IndexHealth();
        var errors = new List<string>();

        // Phases
        try
        {
            var phases = _store.LoadAllPhases();
            foreach (var p in phases)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"INSERT INTO phases VALUES (@id, @num, @title, @state, @agent, @summary, @created, @updated, @completed)";
                cmd.Parameters.AddWithValue("@id", p.PhaseId);
                cmd.Parameters.AddWithValue("@num", p.PhaseNumber);
                cmd.Parameters.AddWithValue("@title", p.Title);
                cmd.Parameters.AddWithValue("@state", p.State.ToString());
                cmd.Parameters.AddWithValue("@agent", (object?)p.AssignedAgent?.ToString() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@summary", (object?)p.Summary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@created", p.CreatedUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@updated", p.UpdatedUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@completed", (object?)p.CompletedUtc?.ToString("O") ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            health.PhaseCount = phases.Count;
        }
        catch (Exception ex) { errors.Add($"phases: {ex.Message}"); }

        // Implementation records
        try
        {
            var records = _store.ReadAllImplementations();
            foreach (var r in records)
                InsertRecord(connection, "implementation_records", r.ImplementationId, r.PhaseId,
                    r.Actor.ToString(), r.Title, r.Summary, r.Status.ToString(), r.CreatedUtc.ToString("O"));
            health.ImplementationCount = records.Count;
            IndexFts(connection, "implementation", records.Select(r => (r.ImplementationId, r.PhaseId, $"{r.Title} {r.Summary}")));
        }
        catch (Exception ex) { errors.Add($"implementations: {ex.Message}"); }

        // Audit records
        try
        {
            var records = _store.ReadAllAudits();
            foreach (var r in records)
                InsertAuditRecord(connection, r);
            health.AuditCount = records.Count;
            IndexFts(connection, "audit", records.Select(r => (r.AuditId, r.PhaseId, r.Summary ?? "")));
        }
        catch (Exception ex) { errors.Add($"audits: {ex.Message}"); }

        // Review records
        try
        {
            var records = _store.ReadAllReviews();
            foreach (var r in records)
                InsertReviewRecord(connection, r);
            health.ReviewCount = records.Count;
            IndexFts(connection, "review", records.Select(r => (r.ReviewId, r.PhaseId, r.Summary ?? "")));
        }
        catch (Exception ex) { errors.Add($"reviews: {ex.Message}"); }

        // Test records
        try
        {
            var records = _store.ReadAllTests();
            foreach (var r in records)
                InsertTestRecord(connection, r);
            health.ValidationCount = records.Count;
            IndexFts(connection, "test", records.Select(r => (r.TestId, r.PhaseId, r.Summary ?? "")));
        }
        catch (Exception ex) { errors.Add($"tests: {ex.Message}"); }

        // Fix records
        try
        {
            var records = _store.ReadAllFixes();
            foreach (var r in records)
                InsertFixRecord(connection, r);
            health.FixCount = records.Count;
            IndexFts(connection, "fix", records.Select(r => (r.FixId, r.PhaseId, r.Summary ?? "")));
        }
        catch (Exception ex) { errors.Add($"fixes: {ex.Message}"); }

        // Blocker records
        try
        {
            var records = _store.ReadAllBlockers();
            foreach (var r in records)
                InsertBlockerRecord(connection, r);
            health.BlockerCount = records.Count;
        }
        catch (Exception ex) { errors.Add($"blockers: {ex.Message}"); }

        // Handoff records
        try
        {
            var records = _store.ReadAllHandoffs();
            foreach (var r in records)
                InsertHandoffRecord(connection, r);
            health.HandoffCount = records.Count;
        }
        catch (Exception ex) { errors.Add($"handoffs: {ex.Message}"); }

        // Timing records
        try
        {
            var records = _store.ReadAllTimings();
            foreach (var r in records)
                InsertTimingRecord(connection, r);
            health.TimingCount = records.Count;
        }
        catch (Exception ex) { errors.Add($"timings: {ex.Message}"); }

        // Events
        try
        {
            var events = _store.ReadAllEvents();
            foreach (var e in events)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"INSERT INTO events VALUES (@id, @type, @actor, @phase, @summary, @ts)";
                cmd.Parameters.AddWithValue("@id", e.EventId);
                cmd.Parameters.AddWithValue("@type", e.EventType);
                cmd.Parameters.AddWithValue("@actor", e.Actor.ToString());
                cmd.Parameters.AddWithValue("@phase", (object?)e.PhaseId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@summary", (object?)e.Summary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ts", e.Timestamp.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            health.EventCount = events.Count;
        }
        catch (Exception ex) { errors.Add($"events: {ex.Message}"); }

        // Source files
        try
        {
            TrackSourceFiles(connection);
        }
        catch (Exception ex) { errors.Add($"source files: {ex.Message}"); }

        health.Errors = errors;
        health.IsHealthy = errors.Count == 0;
        health.DatabaseExists = true;
        return health;
    }

    // -- Record insert helpers ----------------------------------------------------

    private static void InsertRecord(SqliteConnection conn, string table,
        string id, string phaseId, string? actor, string? title, string? summary,
        string? status, string? createdUtc)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} VALUES (@id, @phase, @actor, @title, @summary, @status, @created)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@phase", phaseId);
        cmd.Parameters.AddWithValue("@actor", (object?)actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@summary", (object?)summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", (object?)createdUtc ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertAuditRecord(SqliteConnection conn, AuditRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO audit_records VALUES (@id, @phase, @actor, @summary, @passed, @created)";
        cmd.Parameters.AddWithValue("@id", r.AuditId);
        cmd.Parameters.AddWithValue("@phase", r.PhaseId);
        cmd.Parameters.AddWithValue("@actor", r.Actor.ToString());
        cmd.Parameters.AddWithValue("@summary", (object?)r.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@passed", r.Passed ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", r.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void InsertReviewRecord(SqliteConnection conn, ReviewRecord r) { /* similar */ InsertAuditReview(conn, "review_records", r.ReviewId, r.PhaseId, r.Actor.ToString(), r.Summary, r.Passed, r.CreatedUtc.ToString("O")); }
    private static void InsertTestRecord(SqliteConnection conn, TestRecord r) { InsertAuditReview(conn, "test_records", r.TestId, r.PhaseId, r.Actor.ToString(), r.Summary, r.Passed, r.CreatedUtc.ToString("O")); }

    private static void InsertAuditReview(SqliteConnection conn, string table,
        string id, string phaseId, string? actor, string? summary, bool passed, string? createdUtc)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} VALUES (@id, @phase, @actor, @summary, @passed, @created)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@phase", phaseId);
        cmd.Parameters.AddWithValue("@actor", (object?)actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@summary", (object?)summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@passed", passed ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", (object?)createdUtc ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertFixRecord(SqliteConnection conn, FixRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO fix_records VALUES (@id, @phase, @actor, @summary, @created)";
        cmd.Parameters.AddWithValue("@id", r.FixId);
        cmd.Parameters.AddWithValue("@phase", r.PhaseId);
        cmd.Parameters.AddWithValue("@actor", r.Actor.ToString());
        cmd.Parameters.AddWithValue("@summary", (object?)r.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", r.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void InsertBlockerRecord(SqliteConnection conn, BlockerRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO blocker_records VALUES (@id, @phase, @reason, @by, @resolved, @created)";
        cmd.Parameters.AddWithValue("@id", r.BlockerId);
        cmd.Parameters.AddWithValue("@phase", r.PhaseId);
        cmd.Parameters.AddWithValue("@reason", (object?)r.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", r.ReportedBy.ToString());
        cmd.Parameters.AddWithValue("@resolved", r.IsResolved ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", r.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void InsertHandoffRecord(SqliteConnection conn, HandoffRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO handoff_records VALUES (@id, @phase, @from, @to, @summary, @created)";
        cmd.Parameters.AddWithValue("@id", r.HandoffId);
        cmd.Parameters.AddWithValue("@phase", r.PhaseId);
        cmd.Parameters.AddWithValue("@from", r.FromActor.ToString());
        cmd.Parameters.AddWithValue("@to", r.ToActor.ToString());
        cmd.Parameters.AddWithValue("@summary", (object?)r.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", r.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void InsertTimingRecord(SqliteConnection conn, TimingRecord r)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO timing_records VALUES (@id, @phase, @actor, @activity, @dur, @created)";
        cmd.Parameters.AddWithValue("@id", r.TimingId);
        cmd.Parameters.AddWithValue("@phase", r.PhaseId);
        cmd.Parameters.AddWithValue("@actor", r.Actor.ToString());
        cmd.Parameters.AddWithValue("@activity", (object?)r.Activity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dur", r.Duration.TotalSeconds);
        cmd.Parameters.AddWithValue("@created", r.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    // -- FTS ----------------------------------------------------------------------

    private static void IndexFts(SqliteConnection conn, string contentType,
        IEnumerable<(string Id, string PhaseId, string Text)> items)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO fts_index VALUES (@type, @id, @text, @phase)";
        var typeParam = cmd.Parameters.AddWithValue("@type", contentType);
        var idParam = cmd.Parameters.Add("@id", SqliteType.Text);
        var textParam = cmd.Parameters.Add("@text", SqliteType.Text);
        var phaseParam = cmd.Parameters.Add("@phase", SqliteType.Text);

        foreach (var (id, phaseId, text) in items)
        {
            idParam.Value = id;
            textParam.Value = text;
            phaseParam.Value = phaseId;
            cmd.ExecuteNonQuery();
        }
    }

    // -- Source file tracking -----------------------------------------------------

    private void TrackSourceFiles(SqliteConnection connection)
    {
        var files = new (string Path, string Type)[]
        {
            (WorkflowLayout.WorkflowFile(_workflowRoot), "workflow"),
            (WorkflowLayout.SequencesFile(_workflowRoot), "sequences"),
            (WorkflowLayout.EventsLog(_workflowRoot), "events"),
            (WorkflowLayout.ImplementationLog(_workflowRoot), "implementation"),
            (WorkflowLayout.AuditLog(_workflowRoot), "audit"),
            (WorkflowLayout.ReviewLog(_workflowRoot), "review"),
            (WorkflowLayout.TestLog(_workflowRoot), "test"),
            (WorkflowLayout.FixLog(_workflowRoot), "fix"),
            (WorkflowLayout.BlockersLog(_workflowRoot), "blockers"),
            (WorkflowLayout.HandoffsLog(_workflowRoot), "handoffs"),
            (WorkflowLayout.TimingLog(_workflowRoot), "timing"),
        };

        var now = DateTime.UtcNow.ToString("O");
        foreach (var (path, type) in files)
        {
            var exists = File.Exists(path);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO source_files VALUES (@path, @type, @count, @checked)";
            cmd.Parameters.AddWithValue("@path", path);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@count", exists ? (object)CountLines(path) : DBNull.Value);
            cmd.Parameters.AddWithValue("@checked", now);
            cmd.ExecuteNonQuery();
        }
    }

    private static int CountLines(string path)
    {
        var count = 0;
        foreach (var _ in File.ReadLines(path))
            count++;
        return count;
    }

    // -- Health -------------------------------------------------------------------

    private static IndexHealth ReadHealth(SqliteConnection connection)
    {
        var health = new IndexHealth { DatabaseExists = true };

        health.PhaseCount          = QueryCount(connection, "phases");
        health.ImplementationCount = QueryCount(connection, "implementation_records");
        health.AuditCount          = QueryCount(connection, "audit_records");
        health.ReviewCount         = QueryCount(connection, "review_records");
        health.ValidationCount           = QueryCount(connection, "test_records");
        health.FixCount            = QueryCount(connection, "fix_records");
        health.BlockerCount        = QueryCount(connection, "blocker_records");
        health.HandoffCount        = QueryCount(connection, "handoff_records");
        health.TimingCount         = QueryCount(connection, "timing_records");
        health.EventCount          = QueryCount(connection, "events");

        health.IsHealthy = true;
        return health;
    }

    private static int QueryCount(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private string CreateConnectionString() => $"Data Source={DatabasePath};Pooling=False";
}

/// <summary>
/// Health summary returned after a context index rebuild or health check.
/// </summary>
public sealed class IndexHealth
{
    public bool DatabaseExists { get; set; }
    public bool IsHealthy { get; set; }
    public int PhaseCount { get; set; }
    public int ImplementationCount { get; set; }
    public int AuditCount { get; set; }
    public int ReviewCount { get; set; }
    public int ValidationCount { get; set; }
    public int FixCount { get; set; }
    public int BlockerCount { get; set; }
    public int HandoffCount { get; set; }
    public int TimingCount { get; set; }
    public int EventCount { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}
