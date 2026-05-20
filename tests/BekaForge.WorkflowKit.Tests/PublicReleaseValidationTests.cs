using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class PublicReleaseValidationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkflowStore _store;
    private readonly OperationDispatcher _dispatcher;

    public PublicReleaseValidationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-public-release-{Guid.NewGuid():N}");
        new WorkflowInitializer(_tempRoot).Initialize("Public Release Validation Tests");
        _store = new WorkflowStore(_tempRoot);
        _dispatcher = new OperationDispatcher(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void PublicReleaseOperations_AppearInManifestRoutingAndMcpMapping()
    {
        Assert.Contains(OperationManifestCatalog.GetAll(), entry => entry.OperationName == WorkflowOperations.GetReleaseCandidateReport);
        Assert.Contains(OperationManifestCatalog.GetAll(), entry => entry.OperationName == WorkflowOperations.ValidatePublicRelease);
        Assert.Contains(ToolRoutingCatalog.GetRules(), rule => rule.OperationName == WorkflowOperations.GetReleaseCandidateReport);
        Assert.Contains(ToolRoutingCatalog.GetRules(), rule => rule.OperationName == WorkflowOperations.ValidatePublicRelease);

        var tools = BekaForge.WorkflowKit.Mcp.McpToolMapping.GetAllTools();
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.GetReleaseCandidateReport);
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.ValidatePublicRelease);
    }

    [Fact]
    public void ValidatePublicRelease_FailsWhenCoreReleaseEvidenceIsMissing()
    {
        var result = Dispatch(WorkflowOperations.ValidatePublicRelease);
        Assert.True(result.Success, result.Message);

        var validation = Assert.IsType<PublicReleaseValidationResult>(result.Data);
        Assert.False(validation.Passed);
        Assert.True(validation.BlockingIssueCount > 0);
        Assert.Contains(validation.Report.BlockingReasons, reason => reason.Contains("No passed validation record", StringComparison.Ordinal));
        Assert.Contains(validation.Report.WarningReasons, reason => reason.Contains("Documentation coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatePublicRelease_DefaultPolicy_DoesNotBlockOnMissingDocumentation()
    {
        SeedCleanReleaseFixture();

        var result = Dispatch(WorkflowOperations.ValidatePublicRelease);
        Assert.True(result.Success, result.Message);

        var validation = Assert.IsType<PublicReleaseValidationResult>(result.Data);
        Assert.True(validation.Passed, string.Join(" | ", validation.Report.BlockingReasons));
        Assert.Equal(0, validation.BlockingIssueCount);
        Assert.Equal(DocumentationPolicyMode.Manual, validation.Report.DocumentationPolicy);
        Assert.Contains(validation.Report.WarningReasons, reason => reason.Contains("Documentation coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatePublicRelease_RequiredDocumentationPolicy_BlocksOnMissingDocumentation()
    {
        SeedCleanReleaseFixture();

        var setPolicy = Dispatch(WorkflowOperations.SetProjectGuidance, new()
        {
            ["section"] = "documentation-policy",
            ["content"] = "required"
        });
        Assert.True(setPolicy.Success, setPolicy.Message);

        var result = Dispatch(WorkflowOperations.ValidatePublicRelease);
        Assert.True(result.Success, result.Message);

        var validation = Assert.IsType<PublicReleaseValidationResult>(result.Data);
        Assert.False(validation.Passed);
        Assert.True(validation.BlockingIssueCount > 0);
        Assert.Equal(DocumentationPolicyMode.Required, validation.Report.DocumentationPolicy);
        Assert.Contains(validation.Report.BlockingReasons, reason => reason.Contains("Documentation coverage", StringComparison.Ordinal));
    }

    private void SeedCleanReleaseFixture()
    {
        var phaseCreate = Dispatch(WorkflowOperations.CreatePhase, new() { ["title"] = "Release fixture phase" });
        Assert.True(phaseCreate.Success, phaseCreate.Message);
        _store.SavePhase(_store.LoadPhase("PHASE-001")! with { State = PhaseState.TestInProgress });

        File.WriteAllText(Path.Combine(_tempRoot, "README.md"), """
# Test README

## Support Matrix

| Surface | Status | Evidence | Notes |
| --- | --- | --- | --- |
| Local CLI and global tool on Windows with .NET 8 SDK | Verified | build, test, pack, install/update/uninstall | Primary release surface |
| Local HTTP adapter | Limited | local adapter only | Not separate public product |

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
dotnet tool update --global BekaForge.WorkflowKit.Cli
dotnet tool uninstall --global BekaForge.WorkflowKit.Cli
dotnet pack src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj
bfwf --help
```
""");

        var cliProjectDir = Path.Combine(_tempRoot, "src", "BekaForge.WorkflowKit.Cli");
        Directory.CreateDirectory(cliProjectDir);
        File.WriteAllText(Path.Combine(cliProjectDir, "BekaForge.WorkflowKit.Cli.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>bfwf</ToolCommandName>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(WorkflowLayout.KnownLimitationsMdPath(_tempRoot), "# Known Limitations\n\nWindows is the currently verified release surface.\n");
        File.WriteAllText(WorkflowLayout.FinalReviewMdPath(_tempRoot), "# Final Review\n\nThe release checklist was reviewed against current evidence.\n");
        File.WriteAllText(WorkflowLayout.MigrationNotesMdPath(_tempRoot), "# Migration Notes\n\nNo migration blockers remain for this fixture.\n");

        var validationCreate = Dispatch(WorkflowOperations.CreateValidationLog, new()
        {
            ["phaseId"] = "PHASE-001",
            ["summary"] = "Release fixture validation passed.",
            ["validationType"] = "AutomatedCommand",
            ["validationResult"] = "Passed",
            ["evidenceItems"] = "[{\"description\":\"dotnet test passed for release fixture\",\"source\":2,\"reference\":\"dotnet test\"}]",
            ["command"] = "dotnet test",
            ["exitCode"] = 0
        });
        Assert.True(validationCreate.Success, validationCreate.Message);
    }

    private OperationResult Dispatch(string operation, Dictionary<string, object?>? parameters = null)
    {
        parameters ??= new Dictionary<string, object?>();
        parameters.TryGetValue("phaseId", out var phaseIdValue);

        return _dispatcher.Dispatch(new OperationContext
        {
            Operation = operation,
            Actor = WorkflowActor.Implementer,
            PhaseId = phaseIdValue as string,
            Parameters = parameters
        });
    }
}
