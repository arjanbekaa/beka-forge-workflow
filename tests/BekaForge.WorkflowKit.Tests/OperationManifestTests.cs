using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for the Operation Manifest feature (PHASE-007).
/// Covers manifest DTOs, catalog generation, determinism, dispatcher integration,
/// constant-to-manifest coverage, and file export.
/// </summary>
public sealed class OperationManifestTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public OperationManifestTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-manifest-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("ManifestTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -- Helper --------------------------------------------------------------------

    private OperationContext Ctx(string operation) =>
        new() { Operation = operation, Actor = WorkflowActor.Codex };

    // -- Manifest DTOs -------------------------------------------------------------

    [Fact]
    public void ManifestEntry_Construction_RoundTrips()
    {
        var entry = new OperationManifestEntry
        {
            OperationName   = "workflow.test_op",
            AccessLevel     = OperationAccessLevel.Read,
            Category        = "Test",
            Summary         = "A test operation.",
            HandlerTypeName = "TestHandler"
        };

        Assert.Equal("workflow.test_op", entry.OperationName);
        Assert.Equal(OperationAccessLevel.Read, entry.AccessLevel);
        Assert.Equal("Test", entry.Category);
        Assert.Equal("A test operation.", entry.Summary);
        Assert.Equal("TestHandler", entry.HandlerTypeName);
    }

    [Fact]
    public void Manifest_Construction_HasRequiredFields()
    {
        var manifest = new OperationManifest
        {
            SchemaVersion = "1.0",
            GeneratedUtc  = "2026-05-11T00:00:00.0000000+00:00",
            Operations    = []
        };

        Assert.Equal("1.0", manifest.SchemaVersion);
        Assert.NotEmpty(manifest.GeneratedUtc);
        Assert.NotNull(manifest.Operations);
    }

    // -- Catalog generation --------------------------------------------------------

    [Fact]
    public void Catalog_GetAll_ReturnsNonEmptyList()
    {
        var entries = OperationManifestCatalog.GetAll();
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void Catalog_Generate_ReturnsValidManifest()
    {
        var manifest = OperationManifestCatalog.Generate();

        Assert.Equal("1.0", manifest.SchemaVersion);
        Assert.NotEmpty(manifest.GeneratedUtc);
        Assert.NotEmpty(manifest.Operations);

        // Every entry must have a non-empty operation name.
        foreach (var entry in manifest.Operations)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.OperationName),
                $"Entry with summary '{entry.Summary}' has empty operation name.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Category),
                $"Operation '{entry.OperationName}' has empty category.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary),
                $"Operation '{entry.OperationName}' has empty summary.");
        }
    }

    // -- Determinism ---------------------------------------------------------------

    [Fact]
    public void Catalog_Generate_IsDeterministicForOperationList()
    {
        var m1 = OperationManifestCatalog.Generate();
        var m2 = OperationManifestCatalog.Generate();

        Assert.Equal(m1.Operations.Count, m2.Operations.Count);

        for (int i = 0; i < m1.Operations.Count; i++)
        {
            var e1 = m1.Operations[i];
            var e2 = m2.Operations[i];

            Assert.Equal(e1.OperationName, e2.OperationName);
            Assert.Equal(e1.AccessLevel, e2.AccessLevel);
            Assert.Equal(e1.Category, e2.Category);
            Assert.Equal(e1.Summary, e2.Summary);
            Assert.Equal(e1.HandlerTypeName, e2.HandlerTypeName);
        }
    }

    // -- WorkflowOperations constant → manifest coverage ---------------------------

    [Fact]
    public void EveryWorkflowOperationsConstant_HasManifestEntry()
    {
        var manifestOps = OperationManifestCatalog.GetAll()
            .Select(e => e.OperationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Reflect over WorkflowOperations to find all public const string fields.
        var constantFields = typeof(WorkflowOperations)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToList();

        Assert.NotEmpty(constantFields);

        foreach (var field in constantFields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.True(manifestOps.Contains(value),
                $"WorkflowOperations.{field.Name} = \"{value}\" has no manifest entry.");
        }
    }

    // -- Dispatcher registration → manifest coverage -------------------------------

    [Fact]
    public void EveryRegisteredDispatcherOperation_HasManifestEntry()
    {
        var manifestOps = OperationManifestCatalog.GetAll()
            .Where(e => e.HandlerTypeName is not null)
            .Select(e => e.OperationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var op in _dispatcher.RegisteredOperations)
        {
            Assert.True(manifestOps.Contains(op),
                $"Dispatcher operation '{op}' is registered but has no manifest entry with a handler type.");
        }
    }

    [Fact]
    public void EveryManifestEntryWithHandler_IsDispatcherRegistered()
    {
        var registeredOps = _dispatcher.RegisteredOperations
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in OperationManifestCatalog.GetAll())
        {
            if (entry.HandlerTypeName is null)
                continue;

            Assert.True(registeredOps.Contains(entry.OperationName),
                $"Manifest entry '{entry.OperationName}' has handler '{entry.HandlerTypeName}' " +
                "but is not registered in the dispatcher.");
        }
    }

    // -- workflow.get_operation_manifest handler -----------------------------------

    [Fact]
    public void GetOperationManifest_IsRegistered()
    {
        Assert.Contains(WorkflowOperations.GetOperationManifest, _dispatcher.RegisteredOperations);
    }

    [Fact]
    public void GetOperationManifest_ReturnsSuccessWithData()
    {
        var result = _dispatcher.Dispatch(Ctx(WorkflowOperations.GetOperationManifest));

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);

        var manifest = Assert.IsType<OperationManifest>(result.Data);
        Assert.NotEmpty(manifest.Operations);
        Assert.Equal("1.0", manifest.SchemaVersion);
    }

    [Fact]
    public void GetOperationManifest_IsDeterministicAcrossCalls()
    {
        var r1 = _dispatcher.Dispatch(Ctx(WorkflowOperations.GetOperationManifest));
        var r2 = _dispatcher.Dispatch(Ctx(WorkflowOperations.GetOperationManifest));

        Assert.True(r1.Success);
        Assert.True(r2.Success);

        var m1 = Assert.IsType<OperationManifest>(r1.Data);
        var m2 = Assert.IsType<OperationManifest>(r2.Data);

        Assert.Equal(m1.Operations.Count, m2.Operations.Count);

        var opNames1 = m1.Operations.Select(e => e.OperationName).ToList();
        var opNames2 = m2.Operations.Select(e => e.OperationName).ToList();
        Assert.Equal(opNames1, opNames2);
    }

    // -- File export ---------------------------------------------------------------

    [Fact]
    public void ExportToFile_CreatesIndexDirectoryAndManifestFile()
    {
        OperationManifestCatalog.ExportToFile(_tempRoot);

        var indexPath = WorkflowLayout.OperationManifestPath(_tempRoot);
        Assert.True(File.Exists(indexPath), $"Expected manifest file at '{indexPath}'.");

        // Read back and verify it's valid JSON with the expected structure.
        var json = File.ReadAllText(indexPath);
        Assert.NotEmpty(json);

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("schemaVersion", out var sv));
        Assert.Equal("1.0", sv.GetString());

        Assert.True(root.TryGetProperty("operations", out var ops));
        Assert.Equal(JsonValueKind.Array, ops.ValueKind);
        Assert.True(ops.GetArrayLength() > 0);
    }

    [Fact]
    public void ExportToFile_ProducesSameDataAsGenerate()
    {
        OperationManifestCatalog.ExportToFile(_tempRoot);

        var generatedManifest = OperationManifestCatalog.Generate();
        var indexPath = WorkflowLayout.OperationManifestPath(_tempRoot);
        var json = File.ReadAllText(indexPath);
        var fromFile = JsonSerializer.Deserialize<OperationManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(fromFile);
        Assert.Equal(generatedManifest.Operations.Count, fromFile.Operations.Count);

        var genOps = generatedManifest.Operations.Select(e => e.OperationName).OrderBy(x => x).ToList();
        var fileOps = fromFile.Operations.Select(e => e.OperationName).OrderBy(x => x).ToList();
        Assert.Equal(genOps, fileOps);
    }

    // -- Access level classification -----------------------------------------------

    [Fact]
    public void AllReadOperations_HaveHandlerType()
    {
        var readOps = OperationManifestCatalog.GetAll()
            .Where(e => e.AccessLevel == OperationAccessLevel.Read);

        foreach (var entry in readOps)
        {
            Assert.NotNull(entry.HandlerTypeName);
        }
    }

    [Fact]
    public void SyncMarkdown_IsRegenerateLevel()
    {
        var entry = OperationManifestCatalog.GetAll()
            .Single(e => e.OperationName == WorkflowOperations.SyncMarkdown);

        Assert.Equal(OperationAccessLevel.Regenerate, entry.AccessLevel);
    }

    [Fact]
    public void AppendOnlyOperations_AreAppendOnlyWriteTargets()
    {
        var appendOps = OperationManifestCatalog.GetAll()
            .Where(e => e.AccessLevel == OperationAccessLevel.Append)
            .ToList();

        Assert.NotEmpty(appendOps);
        Assert.Contains(appendOps, e => e.OperationName == WorkflowOperations.CreateHandoff);
        Assert.Contains(appendOps, e => e.OperationName == WorkflowOperations.RecordTimeSpent);
        Assert.Contains(appendOps, e => e.OperationName == WorkflowOperations.CreateImplementationLog);

        foreach (var entry in appendOps.Where(e => e.WriteTargets is { Count: > 0 }))
        {
            Assert.All(entry.WriteTargets!, target => Assert.True(target.IsAppendOnly,
                $"Append operation '{entry.OperationName}' has a non-append-only write target."));
        }
    }

    // -- Handler type names are valid ----------------------------------------------

    [Fact]
    public void AllHandlerTypeNames_AreNonNullForRegisteredOps()
    {
        var registered = _dispatcher.RegisteredOperations
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in OperationManifestCatalog.GetAll())
        {
            if (registered.Contains(entry.OperationName))
            {
                Assert.NotNull(entry.HandlerTypeName);
            }
        }
    }

    // -- No duplicate operation names ----------------------------------------------

    [Fact]
    public void Catalog_HasNoDuplicateOperationNames()
    {
        var entries = OperationManifestCatalog.GetAll();
        var names = entries.Select(e => e.OperationName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // -- Count matches expectations ------------------------------------------------

    [Fact]
    public void Catalog_Count_MatchesWorkflowOperationsConstants()
    {
        var constantCount = typeof(WorkflowOperations)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Count(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        var manifestCount = OperationManifestCatalog.GetAll().Count;

        Assert.Equal(constantCount, manifestCount);
    }

    // -- Generated manifest JSON is valid -----------------------------------------

    [Fact]
    public void GeneratedManifest_SerializesToValidJson()
    {
        var manifest = OperationManifestCatalog.Generate();
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        Assert.NotEmpty(json);

        // Must parse without exception.
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // -- Manifest/Dispatcher cross-coverage (PHASE-014) --------------------------

    [Fact]
    public void EveryManifestHandler_HasDispatcherRegistration()
    {
        var registered = _dispatcher.RegisteredOperations
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in OperationManifestCatalog.GetAll())
        {
            if (entry.HandlerTypeName is not null)
            {
                Assert.True(registered.Contains(entry.OperationName),
                    $"Manifest entry '{entry.OperationName}' has handler '{entry.HandlerTypeName}' but is not registered in dispatcher.");
            }
        }
    }

    [Fact]
    public void EveryDispatcherRegistration_HasManifestEntry()
    {
        var manifestOps = OperationManifestCatalog.GetAll()
            .Select(e => e.OperationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var op in _dispatcher.RegisteredOperations)
        {
            Assert.True(manifestOps.Contains(op),
                $"Dispatcher operation '{op}' has no manifest entry.");
        }
    }

    [Fact]
    public void WriteTargets_OnlyOnWriteAppendRegenerateOps()
    {
        foreach (var entry in OperationManifestCatalog.GetAll())
        {
            if (entry.AccessLevel == OperationAccessLevel.Read)
            {
                Assert.True(entry.WriteTargets is null || entry.WriteTargets.Count == 0,
                    $"Read operation '{entry.OperationName}' should not have WriteTargets.");
            }
            else if (entry.AccessLevel is OperationAccessLevel.Write or OperationAccessLevel.Append or OperationAccessLevel.Regenerate)
            {
                Assert.NotNull(entry.WriteTargets);
                Assert.NotEmpty(entry.WriteTargets!);
                Assert.All(entry.WriteTargets!, wt =>
                {
                    Assert.StartsWith("workflow.", wt.OperationName);
                    Assert.False(string.IsNullOrWhiteSpace(wt.TargetDescription));
                });
            }
        }
    }

    // -- Index directory path -----------------------------------------------------

    [Fact]
    public void WorkflowLayout_IndexDir_IsUnderWorkflowKit()
    {
        var indexPath = WorkflowLayout.IndexDir(_tempRoot);
        Assert.Contains(".workflowkit", indexPath);
        Assert.EndsWith("index", indexPath);
    }

    [Fact]
    public void WorkflowLayout_OperationManifestPath_EndsWithJson()
    {
        var path = WorkflowLayout.OperationManifestPath(_tempRoot);
        Assert.EndsWith("operation-manifest.json", path);
    }
}
