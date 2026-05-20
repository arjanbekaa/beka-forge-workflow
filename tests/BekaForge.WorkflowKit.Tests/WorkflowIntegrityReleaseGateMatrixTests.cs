using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Markdown;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class WorkflowIntegrityReleaseGateMatrixTests
{
    [Fact]
    public void ValidateReleaseGate_CorruptFixtureMatrix_MatchesExpectedOutcomes()
    {
        var scenarios = new[]
        {
            new ReleaseGateScenario(
                Name: "malformed authoritative jsonl",
                Mutate: static fixture =>
                {
                    File.AppendAllText(
                        WorkflowLayout.ReviewLog(fixture.Root),
                        "{\"reviewId\":\"REV-001\"" + Environment.NewLine);
                },
                ExpectedPassed: false,
                ExpectedBlockingIssueCount: 1,
                ExpectedIssueCodes: ["MalformedJsonlLine"]),
            new ReleaseGateScenario(
                Name: "orphan phase drift",
                Mutate: static fixture =>
                {
                    fixture.Store.SavePhase(new Phase
                    {
                        PhaseId = "PHASE-002",
                        PhaseNumber = 2,
                        Title = "Orphan phase"
                    });
                },
                ExpectedPassed: false,
                ExpectedBlockingIssueCount: 1,
                ExpectedIssueCodes: ["OrphanPhaseFile"]),
            new ReleaseGateScenario(
                Name: "markdown and read-model drift only",
                Mutate: static fixture =>
                {
                    new MarkdownSyncService(fixture.Store).SyncAll();
                    new ContextIndexBuilder(fixture.Root).Rebuild();

                    var staleWriteUtc = DateTime.UtcNow.AddMinutes(-10);
                    var freshWriteUtc = DateTime.UtcNow;

                    File.SetLastWriteTimeUtc(WorkflowLayout.CurrentStatusMdPath(fixture.Root), staleWriteUtc);
                    File.SetLastWriteTimeUtc(WorkflowLayout.WorkflowKitDbPath(fixture.Root), staleWriteUtc);
                    File.SetLastWriteTimeUtc(WorkflowLayout.WorkflowFile(fixture.Root), freshWriteUtc);
                },
                ExpectedPassed: true,
                ExpectedBlockingIssueCount: 0,
                ExpectedIssueCodes: ["MarkdownMirrorStale", "ReadModelStale"])
        };

        foreach (var scenario in scenarios)
        {
            using var fixture = TempWorkflowFixture.Create();
            scenario.Mutate(fixture);

            var gate = fixture.RunReleaseGate();

            Assert.Equal(scenario.ExpectedPassed, gate.Passed);
            Assert.Equal(scenario.ExpectedBlockingIssueCount, gate.BlockingIssueCount);
            foreach (var expectedIssueCode in scenario.ExpectedIssueCodes)
            {
                Assert.Contains(
                    gate.Report.Issues,
                    issue => string.Equals(issue.Code, expectedIssueCode, StringComparison.Ordinal));
            }

            if (scenario.ExpectedPassed)
            {
                Assert.All(gate.Report.Issues, issue => Assert.False(issue.IsReleaseBlocking));
            }
            else
            {
                Assert.Contains(gate.Report.Issues, issue => issue.IsReleaseBlocking);
            }
        }
    }

    private sealed record ReleaseGateScenario(
        string Name,
        Action<TempWorkflowFixture> Mutate,
        bool ExpectedPassed,
        int ExpectedBlockingIssueCount,
        IReadOnlyList<string> ExpectedIssueCodes);

    private sealed class TempWorkflowFixture : IDisposable
    {
        private TempWorkflowFixture(string root, WorkflowStore store)
        {
            Root = root;
            Store = store;
        }

        public string Root { get; }

        public WorkflowStore Store { get; }

        public static TempWorkflowFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"bfwf-integrity-matrix-{Guid.NewGuid():N}");
            new WorkflowInitializer(root).Initialize("Integrity Release Gate Matrix Tests");
            var store = new WorkflowStore(root);
            store.SavePhase(new Phase
            {
                PhaseId = "PHASE-001",
                PhaseNumber = 1,
                Title = "Matrix phase"
            });

            var workflow = store.LoadWorkflow();
            store.SaveWorkflow(workflow with
            {
                PhaseIds = ["PHASE-001"],
                CurrentPhaseId = "PHASE-001"
            });

            return new TempWorkflowFixture(root, store);
        }

        public WorkflowReleaseGateResult RunReleaseGate()
        {
            var dispatcher = new OperationDispatcher(Store);
            var result = dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.ValidateReleaseGate,
                Actor = WorkflowActor.Implementer
            });

            Assert.True(result.Success, result.Message);
            return Assert.IsType<WorkflowReleaseGateResult>(result.Data);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
