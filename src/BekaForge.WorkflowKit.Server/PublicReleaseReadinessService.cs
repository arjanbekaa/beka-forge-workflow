using System.Text.RegularExpressions;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Core.Records;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server;

internal sealed class PublicReleaseReadinessService(WorkflowStore store)
{
    private readonly DocumentationLedgerService _documentation = new(store);

    public ReleaseCandidateReport BuildReport()
    {
        var workflow = store.LoadWorkflow();
        var integrity = Handlers.GetIntegrityReportHandler.BuildIntegrityReport(store);
        var documentationCoverage = _documentation.CheckCoverage();
        var documentationPolicy = workflow.DocumentationPolicy;
        var supportMatrix = LoadSupportMatrix();
        var packagingChecks = RunPackagingChecks();
        var validations = store.ReadAllValidations();
        var knownLimitationsPresent = HasMeaningfulFile(WorkflowLayout.KnownLimitationsMdPath(store.WorkflowRoot));
        var finalReviewPresent = HasMeaningfulFile(WorkflowLayout.FinalReviewMdPath(store.WorkflowRoot));
        var migrationNotesPresent = HasMeaningfulFile(WorkflowLayout.MigrationNotesMdPath(store.WorkflowRoot));

        var blockingReasons = new List<string>();
        var warningReasons = new List<string>();

        if (integrity.Summary.ReleaseBlockingCount > 0)
            blockingReasons.Add($"Integrity report contains {integrity.Summary.ReleaseBlockingCount} release-blocking issue(s).");

        if (documentationCoverage.Summary.BlockingIssueCount > 0)
        {
            var documentationMessage = $"Documentation coverage report contains {documentationCoverage.Summary.BlockingIssueCount} blocking issue(s).";
            switch (documentationPolicy)
            {
                case DocumentationPolicyMode.Required:
                    blockingReasons.Add(documentationMessage);
                    break;
                case DocumentationPolicyMode.Manual:
                    warningReasons.Add($"{documentationMessage} Documentation policy is 'manual', so release continues with a warning.");
                    break;
            }
        }

        foreach (var check in packagingChecks.Where(check => !check.Passed && check.IsBlocking))
            blockingReasons.Add(check.Message);
        foreach (var check in packagingChecks.Where(check => !check.Passed && !check.IsBlocking))
            warningReasons.Add(check.Message);

        if (supportMatrix.Count == 0)
        {
            blockingReasons.Add("README does not contain a parseable support matrix.");
        }
        else if (supportMatrix.Any(entry => entry.Status == SupportStatus.Unknown))
        {
            blockingReasons.Add("Support matrix contains unknown support states.");
        }

        if (!validations.Any(IsReleaseReadyValidation))
            blockingReasons.Add("No passed validation record with evidence is available for release review.");

        if (!knownLimitationsPresent)
            blockingReasons.Add("KnownLimitations.md does not contain meaningful release guidance.");
        if (!finalReviewPresent)
            blockingReasons.Add("FinalReview.md does not contain meaningful release guidance.");
        if (!migrationNotesPresent)
            warningReasons.Add("MigrationNotes.md is empty or missing meaningful content.");

        return new ReleaseCandidateReport
        {
            WorkflowId = integrity.WorkflowId,
            IntegrityReport = integrity,
            DocumentationCoverage = documentationCoverage,
            DocumentationPolicy = documentationPolicy,
            DocumentationCoverageBlocksRelease = documentationPolicy == DocumentationPolicyMode.Required,
            SupportMatrix = supportMatrix,
            PackagingChecks = packagingChecks,
            KnownLimitationsPresent = knownLimitationsPresent,
            FinalReviewPresent = finalReviewPresent,
            MigrationNotesPresent = migrationNotesPresent,
            BlockingReasons = blockingReasons,
            WarningReasons = warningReasons,
            GeneratedUtc = DateTimeOffset.UtcNow
        };
    }

    public PublicReleaseValidationResult Validate()
    {
        var report = BuildReport();
        var blockingIssueCount = report.BlockingReasons.Count;

        return new PublicReleaseValidationResult
        {
            Report = report,
            Passed = blockingIssueCount == 0,
            BlockingIssueCount = blockingIssueCount
        };
    }

    private IReadOnlyList<SupportMatrixEntry> LoadSupportMatrix()
    {
        var readmePath = Path.Combine(store.WorkflowRoot, "README.md");
        if (!File.Exists(readmePath))
            return [];

        var content = File.ReadAllText(readmePath);
        var section = ExtractMarkdownSection(content, "Support Matrix");
        if (string.IsNullOrWhiteSpace(section))
            return [];

        var entries = new List<SupportMatrixEntry>();
        foreach (var rawLine in section.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
                continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length < 4 || cells[0].Equals("Surface", StringComparison.OrdinalIgnoreCase))
                continue;

            entries.Add(new SupportMatrixEntry
            {
                Surface = cells[0],
                Status = ParseSupportStatus(cells[1]),
                Evidence = cells[2],
                Notes = cells[3]
            });
        }

        return entries;
    }

    private IReadOnlyList<PackagingCheck> RunPackagingChecks()
    {
        var readmePath = Path.Combine(store.WorkflowRoot, "README.md");
        var cliProjectPath = Path.Combine(store.WorkflowRoot, "src", "BekaForge.WorkflowKit.Cli", "BekaForge.WorkflowKit.Cli.csproj");
        var checks = new List<PackagingCheck>();

        if (!File.Exists(readmePath))
        {
            checks.Add(new PackagingCheck
            {
                Name = "README",
                Passed = false,
                Message = "README.md is missing from the workflow root.",
                IsBlocking = true
            });
            return checks;
        }

        var readme = File.ReadAllText(readmePath);
        var requiredReadmeSnippets = new Dictionary<string, string>
        {
            ["install"] = "dotnet tool install --global BekaForge.WorkflowKit.Cli",
            ["update"] = "dotnet tool update --global BekaForge.WorkflowKit.Cli",
            ["uninstall"] = "dotnet tool uninstall --global BekaForge.WorkflowKit.Cli",
            ["pack"] = "dotnet pack src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj",
            ["startup"] = "bfwf --help"
        };

        foreach (var (name, snippet) in requiredReadmeSnippets)
        {
            checks.Add(new PackagingCheck
            {
                Name = $"readme-{name}",
                Passed = readme.Contains(snippet, StringComparison.Ordinal),
                Message = readme.Contains(snippet, StringComparison.Ordinal)
                    ? $"README includes the {name} smoke path."
                    : $"README is missing the {name} smoke path: '{snippet}'.",
                IsBlocking = true
            });
        }

        if (!File.Exists(cliProjectPath))
        {
            checks.Add(new PackagingCheck
            {
                Name = "cli-project",
                Passed = false,
                Message = "CLI project file is missing at src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj.",
                IsBlocking = true
            });
            return checks;
        }

        var cliProject = File.ReadAllText(cliProjectPath);
        checks.Add(new PackagingCheck
        {
            Name = "pack-as-tool",
            Passed = cliProject.Contains("<PackAsTool>true</PackAsTool>", StringComparison.Ordinal),
            Message = cliProject.Contains("<PackAsTool>true</PackAsTool>", StringComparison.Ordinal)
                ? "CLI project is packable as a global tool."
                : "CLI project is not marked as PackAsTool.",
            IsBlocking = true
        });
        checks.Add(new PackagingCheck
        {
            Name = "tool-command-name",
            Passed = cliProject.Contains("<ToolCommandName>bfwf</ToolCommandName>", StringComparison.Ordinal),
            Message = cliProject.Contains("<ToolCommandName>bfwf</ToolCommandName>", StringComparison.Ordinal)
                ? "CLI project exposes the bfwf tool command."
                : "CLI project is missing the bfwf tool command metadata.",
            IsBlocking = true
        });
        checks.Add(new PackagingCheck
        {
            Name = "package-readme",
            Passed = cliProject.Contains("<PackageReadmeFile>README.md</PackageReadmeFile>", StringComparison.Ordinal),
            Message = cliProject.Contains("<PackageReadmeFile>README.md</PackageReadmeFile>", StringComparison.Ordinal)
                ? "CLI package embeds the README."
                : "CLI package is missing PackageReadmeFile metadata.",
            IsBlocking = true
        });

        return checks;
    }

    private static bool IsReleaseReadyValidation(ValidationRecord record)
    {
        return (record.ValidationResult == ValidationResult.Passed
                || record.ValidationResult == ValidationResult.PassedWithWarnings)
               && record.EvidenceItems.Count > 0;
    }

    private static bool HasMeaningfulFile(string path)
    {
        if (!File.Exists(path))
            return false;

        var content = File.ReadAllText(path);
        var stripped = Regex.Replace(content, "<!--.*?-->", string.Empty, RegexOptions.Singleline)
            .Replace("#", string.Empty)
            .Trim();
        return stripped.Length > 20;
    }

    private static SupportStatus ParseSupportStatus(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "verified" => SupportStatus.Verified,
        "limited" => SupportStatus.Limited,
        "unsupported" => SupportStatus.Unsupported,
        _ => SupportStatus.Unknown
    };

    private static string ExtractMarkdownSection(string content, string heading)
    {
        var marker = $"## {heading}";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += marker.Length;
        var end = content.IndexOf("\n## ", start, StringComparison.Ordinal);
        return end >= 0 ? content[start..end].Trim() : content[start..].Trim();
    }
}
