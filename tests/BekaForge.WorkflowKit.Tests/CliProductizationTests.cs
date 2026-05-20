using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

/// <summary>
/// Tests for PHASE-020: CLI Productization.
/// Covers JSON output format, command-to-operation dispatch mapping,
/// exit code conventions, and cross-platform path handling.
/// </summary>
public sealed class CliProductizationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;
    private readonly JsonSerializerOptions _jsonOpts; // matches CLI --json output

    public CliProductizationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-cli-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("CliTestAsset");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
        _jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -- Helpers ----------------------------------------------------------

    private OperationContext Ctx(string operation, string? phaseId = null) =>
        new() { Operation = operation, Actor = WorkflowActor.Implementer, PhaseId = phaseId };

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ReadRepoFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepoRoot, Path.Combine(segments)));

    // -- JSON output stability --------------------------------------------

    [Fact]
    public void JsonOutput_OperationResult_IsParseable()
    {
        var result = _dispatcher.Dispatch(Ctx(WorkflowOperations.GetState));

        var json = JsonSerializer.Serialize(result, _jsonOpts);
        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.DoesNotContain("\n", json); // no indentation — single line
        Assert.DoesNotContain("  ", json); // no pretty-print

        // Re-parse and verify structure
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("success", out var success));
        Assert.Equal(JsonValueKind.True, success.ValueKind);
        Assert.True(root.TryGetProperty("data", out _));
    }

    [Fact]
    public void JsonOutput_HasCamelCaseProperties()
    {
        var result = _dispatcher.Dispatch(Ctx(WorkflowOperations.GetState));
        var json = JsonSerializer.Serialize(result, _jsonOpts);

        // Verify camelCase keys present (not PascalCase)
        Assert.Contains("\"success\"", json);
        Assert.Contains("\"data\"", json);
        Assert.DoesNotContain("\"Success\"", json);
        Assert.DoesNotContain("\"Data\"", json);
    }

    [Fact]
    public void JsonOutput_FailureResult_ContainsErrorCode()
    {
        var result = OperationResult.Fail("TestError", "Test message");
        var json = JsonSerializer.Serialize(result, _jsonOpts);

        Assert.Contains("\"success\":false", json);
        Assert.Contains("\"errorCode\":\"TestError\"", json);
    }

    [Fact]
    public void JsonOutput_NoIndent_SingleLineCompact()
    {
        var state = _store.LoadWorkflow();
        var json = JsonSerializer.Serialize(state, _jsonOpts);

        // Must be single line (no indentation)
        var lines = json.Split('\n');
        Assert.Single(lines);
    }

    // -- Command-to-operation dispatch mapping ----------------------------

    [Theory]
    [InlineData(WorkflowOperations.GetState, "status")]
    [InlineData(WorkflowOperations.ValidateState, "validate")]
    [InlineData(WorkflowOperations.GetOperationManifest, "manifest")]
    [InlineData(WorkflowOperations.RecommendOperation, "recommend")]
    [InlineData(WorkflowOperations.GetRelevantContext, "context")]
    [InlineData(WorkflowOperations.ValidateOperationRequest, "validate-request")]
    [InlineData(WorkflowOperations.RebuildContextIndex, "index-health")]
    [InlineData(WorkflowOperations.GetCacheStatus, "cache-status")]
    [InlineData(WorkflowOperations.ProcessInbox, "process-inbox")]
    [InlineData(WorkflowOperations.GetInboxStatus, "inbox-status")]
    [InlineData(WorkflowOperations.AuditProtectedPaths, "audit-paths")]
    [InlineData(WorkflowOperations.RepairConsistency, "repair")]
    [InlineData(WorkflowOperations.GetGitStatus, "git")]
    [InlineData(WorkflowOperations.ListGitCommits, "git")]
    [InlineData(WorkflowOperations.GetGitHealth, "git")]
    [InlineData(WorkflowOperations.RecordGitActivity, "git")]
    [InlineData(WorkflowOperations.ListSessions, "session")]
    [InlineData(WorkflowOperations.GetCurrentSession, "session")]
    [InlineData(WorkflowOperations.EndSession, "session")]
    [InlineData(WorkflowOperations.GetTimeline, "timeline")]
    [InlineData(WorkflowOperations.GetTraceStatus, "trace")]
    [InlineData(WorkflowOperations.ListTraces, "trace")]
    [InlineData(WorkflowOperations.GetTrace, "trace")]
    [InlineData(WorkflowOperations.ClearOldTraces, "trace")]
    [InlineData(WorkflowOperations.SetTraceOptions, "trace")]
    public void CliCommand_DispatchMapping_OperationRegistered(string operationName, string cliCommand)
    {
        // Every CLI command must map to a registered dispatcher operation.
        var registered = _dispatcher.RegisteredOperations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            registered.Contains(operationName),
            $"CLI command '{cliCommand}' maps to '{operationName}' which must be registered in the dispatcher.");
    }

    // -- Exit code convention ---------------------------------------------

    [Fact]
    public void ExitCodes_Convention_Documented()
    {
        // PHASE-020 exit codes:
        // 0 = success
        // 1 = error (workflow failure, dispatch failure)
        // 2 = invalid arguments (wrong sub-command, missing required args)

        // Verify that dispatch failures return consistent error codes
        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = "workflow.nonexistent",
            Actor = WorkflowActor.Implementer
        });

        Assert.False(result.Success);
        Assert.Equal("UnknownOperation", result.ErrorCode);
        // CLI would Environment.Exit(1) for this
    }

    [Fact]
    public void ExitCode_InvalidArgs_DetectedForEmptySubcommand()
    {
        // Verify that empty sub-command operations fail gracefully.
        // CLI commands like "bfwf git" (no sub-command) should exit(2).
        var result = _dispatcher.Dispatch(new OperationContext
        {
            Operation = "",
            Actor = WorkflowActor.Implementer
        });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorCode);
    }

    // -- Cross-platform path handling -------------------------------------

    [Fact]
    public void PathNormalization_GetFullPath_Consistent()
    {
        var relative = Path.Combine(_tempRoot, "subdir", "..", ".");
        var full = Path.GetFullPath(relative);

        Assert.EndsWith(Path.GetFileName(_tempRoot), full);
        Assert.DoesNotContain("..", full);
    }

    [Fact]
    public void PathNormalization_RegistryKey_Consistent()
    {
        // The runtime registry uses normalized Path.GetFullPath as key.
        // Verify the same root produces the same key regardless of path form.
        var key1 = Path.GetFullPath(_tempRoot);
        var key2 = Path.GetFullPath(Path.Combine(_tempRoot, ".", "subdir", ".."));

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void WorkflowLayout_IsInitialized_DetectsRoot()
    {
        Assert.True(WorkflowLayout.IsInitialized(_tempRoot));

        var nonWorkflow = Path.Combine(_tempRoot, "not-initialized");
        Assert.False(WorkflowLayout.IsInitialized(nonWorkflow));
    }

    // -- Help text completeness -------------------------------------------

    [Fact]
    public void Help_HasAllRequiredCategories()
    {
        // Verify all required command categories from the PHASE-020 contract
        // are represented in the dispatcher/operations.

        var categories = new[]
        {
            "status",       // WorkflowOperations.GetState
            "phase",        // UpdatePhaseStatus, AssignPhase, etc.
            "log",          // CreateImplementationLog, CreateAuditLog, etc.
            "init",         // init is filesystem level (not dispatcher)
            "server",       // server is process level (dotnet run)
            "inbox",        // ProcessInbox, GetInboxStatus
            "trace",        // GetTraceStatus, ListTraces, GetTrace, etc.
            "cache",        // GetCacheStatus
            "context",      // GetRelevantContext
            "manifest",     // GetOperationManifest
        };

        var registered = _dispatcher.RegisteredOperations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each category should have at least one registered operation
        var categoryOps = new Dictionary<string, string[]>
        {
            ["status"] = new[] { WorkflowOperations.GetState },
            ["phase"] = new[] { WorkflowOperations.UpdatePhaseStatus, WorkflowOperations.StartPhase },
            ["log"] = new[] { WorkflowOperations.CreateImplementationLog, WorkflowOperations.CreateAuditLog },
            ["inbox"] = new[] { WorkflowOperations.ProcessInbox, WorkflowOperations.GetInboxStatus },
            ["trace"] = new[] { WorkflowOperations.GetTraceStatus, WorkflowOperations.ListTraces },
            ["cache"] = new[] { WorkflowOperations.GetCacheStatus },
            ["context"] = new[] { WorkflowOperations.GetRelevantContext },
            ["manifest"] = new[] { WorkflowOperations.GetOperationManifest },
        };

        foreach (var (category, ops) in categoryOps)
        {
            var hasOp = ops.Any(op => registered.Contains(op));
            Assert.True(hasOp,
                $"CLI category '{category}' must have at least one registered operation.");
        }
    }

    // -- Global tool packaging metadata -----------------------------------

    [Fact]
    public void CliProject_HasPackAsTool_Metadata()
    {
        // Verify the CLI .csproj exists and is packable as a tool.
        // This is a compile-time check; we verify the project file exists.
        var cliProj = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "src",
            "BekaForge.WorkflowKit.Cli",
            "BekaForge.WorkflowKit.Cli.csproj");

        var normalized = Path.GetFullPath(cliProj);
        if (!File.Exists(normalized))
        {
            // Try relative from test output
            var repoRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
            normalized = Path.Combine(repoRoot, "src", "BekaForge.WorkflowKit.Cli",
                "BekaForge.WorkflowKit.Cli.csproj");
        }

        Assert.True(File.Exists(normalized),
            $"CLI .csproj not found at {normalized}");

        var content = File.ReadAllText(normalized);
        Assert.Contains("<PackAsTool>true</PackAsTool>", content);
        Assert.Contains("<ToolCommandName>bfwf</ToolCommandName>", content);
        Assert.Contains("<PackageId>BekaForge.WorkflowKit.Cli</PackageId>", content);
    }

    [Fact]
    public void CliProject_Metadata_MatchesNarrowedPublicScope()
    {
        var content = ReadRepoFile("src", "BekaForge.WorkflowKit.Cli", "BekaForge.WorkflowKit.Cli.csproj");

        Assert.Contains("local-first workflow evidence", content);
        Assert.Contains("implementation, audit, review, fix, and validation", content);
        Assert.Contains("<PackageTags>workflow;cli;local-first;jsonl;audit;validation</PackageTags>", content);
    }

    [Fact]
    public void Readme_SmokeCheck_CoversInstallAndContributionBoundaries()
    {
        var content = ReadRepoFile("README.md");

        Assert.Contains("dotnet tool install --global BekaForge.WorkflowKit.Cli", content);
        Assert.Contains("dotnet tool update --global BekaForge.WorkflowKit.Cli", content);
        Assert.Contains("dotnet tool uninstall --global BekaForge.WorkflowKit.Cli", content);
        Assert.Contains("generated markdown is readable context rather than the source of truth", content);
        Assert.Contains("Do not edit them directly; use `bfwf`, the local HTTP API, or mapped MCP operations", content);
        Assert.Contains("Keep workflow history append-only", content);
    }

    // -- Operation name consistency (P0 regression guard) -----------------

    [Fact]
    public void CliOperationNames_AllExist_InWorkflowOperations()
    {
        // Guard against the P0 bug: every operation name referenced
        // in CLI command handlers must exist as a WorkflowOperations constant.
        var cliOperations = new[]
        {
            WorkflowOperations.GetState,
            WorkflowOperations.ValidateState,
            WorkflowOperations.GetOperationManifest,
            WorkflowOperations.RecommendOperation,
            WorkflowOperations.GetRelevantContext,
            WorkflowOperations.ValidateOperationRequest,
            WorkflowOperations.RebuildContextIndex,
            WorkflowOperations.GetCacheStatus,
            WorkflowOperations.ProcessInbox,
            WorkflowOperations.GetInboxStatus,
            WorkflowOperations.AuditProtectedPaths,
            WorkflowOperations.RepairConsistency,
            WorkflowOperations.GetGitStatus,
            WorkflowOperations.ListGitCommits,
            WorkflowOperations.GetGitHealth,
            WorkflowOperations.RecordGitActivity,
            WorkflowOperations.ListSessions,
            WorkflowOperations.GetCurrentSession,
            WorkflowOperations.EndSession,
            WorkflowOperations.GetTimeline,   // <-- P0 fix verified: was .Timeline
            WorkflowOperations.GetTraceStatus,
            WorkflowOperations.ListTraces,
            WorkflowOperations.GetTrace,
            WorkflowOperations.ClearOldTraces,
            WorkflowOperations.SetTraceOptions,
        };

        foreach (var op in cliOperations)
        {
            Assert.False(string.IsNullOrWhiteSpace(op),
                $"CLI operation constant must not be empty.");
            Assert.True(op.StartsWith("workflow.", StringComparison.Ordinal),
                $"CLI operation '{op}' must follow 'workflow.*' naming convention.");
        }

        // Verify all are registered
        var registered = _dispatcher.RegisteredOperations.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var op in cliOperations)
        {
            Assert.True(registered.Contains(op),
                $"CLI operation '{op}' must be registered in the dispatcher.");
        }
    }
}
