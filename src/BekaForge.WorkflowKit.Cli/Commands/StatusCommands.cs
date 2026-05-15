using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdStatus(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain, bool watch = false, int watchIntervalSeconds = 5)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Warn("No Beka Forge Workflow project is initialized here. Run 'bfwf init \"Asset Name\"' first.", mode);
            if (json) Environment.Exit(1);
            return;
        }

        if (watch && json)
        {
            CliRenderer.Error("--watch is only supported for rich/plain status output.", mode);
            Environment.Exit(2);
        }

        if (json)
        {
            WriteJson(new WorkflowStore(wfRoot).LoadWorkflow());
            return;
        }

        if (watch)
        {
            RunWatchLoop(() => RenderStatusView(wfRoot, mode), watchIntervalSeconds);
            return;
        }

        RenderStatusView(wfRoot, mode);
    }

    internal static void RenderStatusView(string wfRoot, CliOutputMode mode)
    {
        var store = new WorkflowStore(wfRoot);
        var state = store.LoadWorkflow();

        var nextActionText = state.NextAction is not null
            ? $"[{state.NextAction.Actor}] {state.NextAction.Description}"
            : "(not set)";

        CliRenderer.RenderStatus(
            state.AssetName, state.WorkflowId, state.CurrentPhaseId,
            state.LastStatus?.ToString(), nextActionText, state.OpenBlockerCount,
            state.PhaseIds.Count, state.UpdatedUtc.UtcDateTime, mode);

        var phases = store.LoadAllPhases();
        var phaseRows = phases
            .OrderBy(p => p.PhaseNumber)
            .Select(p => (
                PhaseId: p.PhaseId,
                Title: p.Title,
                State: p.State.ToString(),
                Progress: PhaseProgress.ForPhase(p),
                Blockers: GetOpenBlockerCount(store, p.PhaseId)))
            .ToList();

        if (phaseRows.Count > 0)
        {
            Console.WriteLine();
            CliRenderer.RenderPhaseList(phaseRows, mode);
        }

        if (!string.IsNullOrWhiteSpace(state.CurrentPhaseId))
        {
            var currentPhase = phases.FirstOrDefault(p => string.Equals(p.PhaseId, state.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
            if (currentPhase is not null)
            {
                Console.WriteLine();
                RenderPhaseCard(store, currentPhase, state, mode);
            }
        }
    }

    internal static void RenderPhaseView(string wfRoot, string phaseId, CliOutputMode mode)
    {
        var store = new WorkflowStore(wfRoot);
        var phase = store.LoadPhase(phaseId);
        if (phase is null)
        {
            CliRenderer.Error($"Phase '{phaseId}' not found.", mode);
            Environment.Exit(1);
        }

        var workflow = store.LoadWorkflow();
        RenderPhaseCard(store, phase, workflow, mode);
    }

    internal static void RenderPhaseCard(WorkflowStore store, Phase phase, WorkflowState workflow, CliOutputMode mode)
    {
        var nextAction = string.Equals(workflow.NextAction?.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase)
            ? workflow.NextAction?.Description
            : null;

        var subPhases = phase.SubPhases
            .Select(sp => (sp.SubPhaseId, sp.Title, sp.Status.ToString()))
            .ToList();

        CliRenderer.RenderPhaseCard(
            phase.PhaseId,
            phase.Title,
            phase.State.ToString(),
            PhaseProgress.ForPhase(phase),
            nextAction,
            phase.Dependencies,
            subPhases,
            GetOpenBlockerCount(store, phase.PhaseId),
            mode);
    }

    internal static int GetOpenBlockerCount(WorkflowStore store, string phaseId) =>
        store.ReadAllBlockers()
            .Where(b => string.Equals(b.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.BlockerId)
            .Select(g => g.Last())
            .Count(b => !b.IsResolved);

    internal static void RunWatchLoop(Action render, int intervalSeconds)
    {
        while (true)
        {
            try
            {
                Console.Clear();
            }
            catch
            {
            }

            render();
            Console.WriteLine();
            Console.WriteLine($"Watching... refresh every {intervalSeconds}s. Press Ctrl+C to stop.");
            Thread.Sleep(TimeSpan.FromSeconds(intervalSeconds));
        }
    }

    internal static void CmdValidate(string? wfRoot, bool json)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ValidateState
        });

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
            return;
        }

        if (result.Success && result.Data is not null)
        {
            // Data is an anonymous object with 'valid' and 'issues'
            Console.WriteLine("VALID");
        }
        else
        {
            Console.Error.WriteLine("Validation failed.");
            if (result.Message is not null)
                Console.Error.WriteLine(result.Message);
            Environment.Exit(1);
        }
    }

    internal static void CmdSyncMarkdown(string? wfRoot)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.SyncMarkdown
        });

        if (result.Success)
        {
            Console.WriteLine(result.Message ?? "Markdown synced.");
        }
        else
        {
            Console.Error.WriteLine($"ERROR: {result.Message ?? "Unknown error"}");
            Environment.Exit(1);
        }
    }
}
