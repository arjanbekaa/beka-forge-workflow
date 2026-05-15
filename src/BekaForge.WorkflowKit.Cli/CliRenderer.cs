using System.Text.Json;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Core.Tracing;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Server.Handlers;
using BekaForge.WorkflowKit.Storage;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Centralizes CLI presentation so rich, plain, and JSON modes stay isolated.
/// </summary>
public enum CliOutputMode
{
    Rich,
    Plain,
    Json
}

public static class CliRenderer
{
    public static CliOutputMode Resolve(bool jsonFlag, bool plainFlag)
    {
        if (jsonFlag) return CliOutputMode.Json;
        if (plainFlag) return CliOutputMode.Plain;
        return AnsiConsole.Profile.Capabilities.Ansi ? CliOutputMode.Rich : CliOutputMode.Plain;
    }

    public static string PhaseStateMarkup(string? state) => (state ?? "").ToLowerInvariant() switch
    {
        "pass" => $"[bold green]{Markup.Escape(state!)}[/]",
        "passwithwarnings" => $"[bold yellow]{Markup.Escape(state!)}[/]",
        "auditlogged" => $"[bold cyan]{Markup.Escape(state!)}[/]",
        "reviewlogged" => $"[cyan]{Markup.Escape(state!)}[/]",
        "inimplementation" => $"[bold yellow]{Markup.Escape(state!)}[/]",
        "inprogress" => $"[bold yellow]{Markup.Escape(state!)}[/]",
        "implementationlogged" => $"[yellow]{Markup.Escape(state!)}[/]",
        "fixinprogress" => $"[bold yellow]{Markup.Escape(state!)}[/]",
        "planned" => $"[grey]{Markup.Escape(state!)}[/]",
        "blocked" => $"[bold red]{Markup.Escape(state!)}[/]",
        "failed" => $"[bold red]{Markup.Escape(state!)}[/]",
        "failedarchitecture" => $"[bold red]{Markup.Escape(state!)}[/]",
        "failedcompile" => $"[bold red]{Markup.Escape(state!)}[/]",
        "failed_validation" => $"[bold red]{Markup.Escape(state!)}[/]",
        _ => Markup.Escape(state ?? "(none)")
    };

    public static string HealthIcon(bool ok) => ok ? "[green]OK[/]" : "[red]FAIL[/]";
    public static string PlainHealthIcon(bool ok) => ok ? "OK" : "FAIL";

    /// <summary>
    /// Returns a neutral info icon (grey "INFO") rather than a red "FAIL".
    /// Use for optional configuration that has valid defaults — e.g. cache settings.
    /// </summary>
    public static string InfoIcon(bool configured) => configured ? "[green]OK[/]" : "[grey]INFO[/]";
    public static string PlainInfoIcon(bool configured) => configured ? "OK" : "INFO";

    public static void RenderStatus(
        string assetName,
        string workflowId,
        string? currentPhase,
        string? lastStatus,
        string? nextActionText,
        int openBlockers,
        int phaseCount,
        DateTime updatedUtc,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[bold]Asset[/]", Markup.Escape(assetName));
            grid.AddRow("[bold]Workflow ID[/]", Markup.Escape(workflowId));
            grid.AddRow("[bold]Current Phase[/]", Markup.Escape(currentPhase ?? "(none)"));
            grid.AddRow("[bold]Last Status[/]", PhaseStateMarkup(lastStatus));
            grid.AddRow("[bold]Open Blockers[/]", openBlockers > 0 ? $"[red]{openBlockers}[/]" : "[green]0[/]");
            grid.AddRow("[bold]Phases[/]", $"{phaseCount}");
            grid.AddRow("[bold]Updated[/]", $"{updatedUtc:yyyy-MM-dd HH:mm} UTC");

            if (!string.IsNullOrWhiteSpace(nextActionText))
                grid.AddRow("[bold]Next Action[/]", Markup.Escape(nextActionText));

            AnsiConsole.Write(new Panel(grid)
            {
                Header = new PanelHeader("[bold]BekaForge AI FlowKit[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine($"Asset:           {assetName}");
        Console.WriteLine($"Workflow ID:     {workflowId}");
        Console.WriteLine($"Current Phase:   {currentPhase ?? "(none)"}");
        Console.WriteLine($"Last Status:     {lastStatus}");
        Console.WriteLine($"Next Action:     {nextActionText ?? "(not set)"}");
        Console.WriteLine($"Open Blockers:   {openBlockers}");
        Console.WriteLine($"Phases:          {phaseCount}");
        Console.WriteLine($"Updated:         {updatedUtc:yyyy-MM-dd HH:mm} UTC");
    }

    public static void RenderDoctor(
        bool sdkOk,
        string? sdkVersion,
        bool gitOk,
        string? gitVersion,
        bool workflowInitialized,
        string? currentPhase,
        int openBlockers,
        bool indexExists,
        int indexFileCount,
        long indexBytes,
        bool cacheSettingsExist,
        int? maxPackages,
        bool dashboardAvailable,
        int driftCount,
        IReadOnlyList<string> issues,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            AnsiConsole.Write(new Rule("[bold]BekaForge AI FlowKit - Doctor[/]").RuleStyle("grey"));

            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("").Width(12))
                .AddColumn(new TableColumn(""));

            table.AddRow(HealthIcon(sdkOk),
                sdkOk ? $"[bold]SDK[/] {Markup.Escape(sdkVersion ?? "unknown")}" : "[red bold]SDK missing or broken[/]");
            table.AddRow(HealthIcon(gitOk),
                gitOk ? $"[bold]Git[/] {Markup.Escape(gitVersion ?? "unknown")}" : "[red bold]Git not found[/]");
            table.AddRow(HealthIcon(workflowInitialized),
                workflowInitialized
                    ? $"[bold]Workflow[/] phase: [cyan]{Markup.Escape(currentPhase ?? "(none)")}[/], blockers: {(openBlockers > 0 ? $"[red]{openBlockers}[/]" : "[green]0[/]")}"
                    : "[red bold]Workflow not initialized[/]");
            table.AddRow(HealthIcon(indexExists),
                indexExists
                    ? $"[bold]Index[/] {indexFileCount} files, {FormatBytes(indexBytes)}"
                    : "[yellow]Index missing (run index-health to rebuild)[/]");
            table.AddRow(InfoIcon(cacheSettingsExist),
                cacheSettingsExist
                    ? $"[bold]Cache[/] max {maxPackages ?? 0} packages"
                    : "[grey]Cache using default settings[/]");
            table.AddRow(InfoIcon(dashboardAvailable),
                dashboardAvailable
                    ? "[bold]Dashboard[/] Windows WPF available"
                    : "[grey]Dashboard CLI only (cross-platform)[/]");
            table.AddRow(InfoIcon(driftCount == 0),
                driftCount == 0
                    ? "[bold]Drift[/] No stale phases"
                    : $"[yellow bold]Drift[/] {driftCount} phase(s) stale — run [grey]bfwf phase drift-check[/] for details");

            AnsiConsole.Write(table);
            RenderIssuesBlock(issues, mode);
            return;
        }

        Console.WriteLine("BekaForge AI FlowKit Doctor");
        Console.WriteLine("===========================");
        Console.WriteLine();
        Console.WriteLine($"  SDK:       {(sdkOk ? $"OK ({sdkVersion})" : "MISSING")}");
        Console.WriteLine($"  Git:       {(gitOk ? $"OK ({gitVersion})" : "MISSING")}");
        Console.WriteLine($"  Workflow:  {(workflowInitialized ? $"Initialized (phase: {currentPhase}, blockers: {openBlockers})" : "Not initialized")}");
        Console.WriteLine($"  Index:     {(indexExists ? $"{indexFileCount} files, {indexBytes} bytes" : "Missing")}");
        Console.WriteLine($"  Cache:     {PlainInfoIcon(cacheSettingsExist)}  {(cacheSettingsExist ? $"max {maxPackages} packages" : "Default settings (no cache-settings.json)")}");
        Console.WriteLine($"  Dashboard: {PlainInfoIcon(dashboardAvailable)}  {(dashboardAvailable ? "Windows WPF available" : "CLI only (cross-platform)")}");
        Console.WriteLine($"  Drift:     {PlainInfoIcon(driftCount == 0)}  {(driftCount == 0 ? "No stale phases" : $"{driftCount} phase(s) stale — run `bfwf phase drift-check` for details")}");
        Console.WriteLine();

        if (issues.Count > 0)
        {
            Console.WriteLine("Issues:");
            foreach (var issue in issues)
                Console.WriteLine($"  - {issue}");
        }
        else
        {
            Console.WriteLine("All checks passed.");
        }
    }

    public static void RenderManifestTable(
        IEnumerable<(string Name, string Access, string Category)> operations,
        int totalCount,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var table = new Table()
                .Title($"[bold]Operation Manifest[/] - {totalCount} operations")
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("[bold]Operation[/]").Width(45))
                .AddColumn(new TableColumn("[bold]Access[/]").Width(12))
                .AddColumn(new TableColumn("[bold]Category[/]"));

            foreach (var (name, access, category) in operations)
            {
                var accessMarkup = access.ToLowerInvariant() switch
                {
                    "read" => $"[grey]{Markup.Escape(access)}[/]",
                    "append" => $"[cyan]{Markup.Escape(access)}[/]",
                    "write" => $"[yellow]{Markup.Escape(access)}[/]",
                    "regenerate" => $"[blue]{Markup.Escape(access)}[/]",
                    _ => Markup.Escape(access)
                };
                table.AddRow(Markup.Escape(name), accessMarkup, Markup.Escape(category));
            }

            AnsiConsole.Write(table);
            return;
        }

        Console.WriteLine($"Operation Manifest - {totalCount} operations");
        Console.WriteLine();
        Console.WriteLine($"{"Operation",-45} {"Access",-12} Category");
        Console.WriteLine(new string('-', 80));
        foreach (var (name, access, category) in operations)
            Console.WriteLine($"{name,-45} {access,-12} {category}");
    }

    public static void RenderPhaseCard(
        string phaseId,
        string title,
        string state,
        int progressPercent,
        string? nextAction,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<(string Id, string Title, string Status)> subPhases,
        int blockerCount,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var summaryGrid = new Grid().AddColumn().AddColumn();
            summaryGrid.AddRow("[bold]Phase[/]", Markup.Escape(phaseId));
            summaryGrid.AddRow("[bold]Title[/]", Markup.Escape(title));
            summaryGrid.AddRow("[bold]State[/]", PhaseStateMarkup(state));
            summaryGrid.AddRow("[bold]Progress[/]", $"{progressPercent}%");
            summaryGrid.AddRow("[bold]Blockers[/]", blockerCount > 0 ? $"[red]{blockerCount}[/]" : "[green]0[/]");

            if (dependencies.Count > 0)
                summaryGrid.AddRow("[bold]Depends On[/]", Markup.Escape(string.Join(", ", dependencies)));

            if (!string.IsNullOrWhiteSpace(nextAction))
                summaryGrid.AddRow("[bold]Next Action[/]", Markup.Escape(nextAction));

            var parts = new List<IRenderable>
            {
                summaryGrid,
                new BarChart()
                    .Width(40)
                    .AddItem("Progress", progressPercent, progressPercent >= 100 ? Color.Green : Color.Yellow)
            };

            if (subPhases.Count > 0)
            {
                var subPhaseTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("[bold]Sub-phase[/]")
                    .AddColumn("[bold]Title[/]")
                    .AddColumn("[bold]Status[/]");

                foreach (var subPhase in subPhases)
                {
                    subPhaseTable.AddRow(
                        Markup.Escape(subPhase.Id),
                        Markup.Escape(subPhase.Title),
                        PhaseStateMarkup(subPhase.Status));
                }

                parts.Add(new Rule("[grey]Sub-phases[/]").LeftJustified());
                parts.Add(subPhaseTable);
            }

            AnsiConsole.Write(new Panel(new Rows(parts))
            {
                Header = new PanelHeader($"[bold cyan]{Markup.Escape(phaseId)}[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine($"Phase:      {phaseId}");
        Console.WriteLine($"Title:      {title}");
        Console.WriteLine($"State:      {state}");
        Console.WriteLine($"Progress:   {progressPercent}%");
        Console.WriteLine($"Blockers:   {blockerCount}");
        if (dependencies.Count > 0)
            Console.WriteLine($"Depends On: {string.Join(", ", dependencies)}");
        if (!string.IsNullOrWhiteSpace(nextAction))
            Console.WriteLine($"Next:       {nextAction}");
        if (subPhases.Count > 0)
        {
            Console.WriteLine("Sub-phases:");
            foreach (var subPhase in subPhases)
                Console.WriteLine($"  - {subPhase.Id} | {subPhase.Status} | {subPhase.Title}");
        }
    }

    public static void RenderPhaseList(
        IReadOnlyList<(string PhaseId, string Title, string State, int Progress, int Blockers)> phases,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var table = new Table()
                .Title("[bold]Phases[/]")
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]Phase[/]")
                .AddColumn("[bold]Title[/]")
                .AddColumn("[bold]State[/]")
                .AddColumn("[bold]Progress[/]")
                .AddColumn("[bold]Blockers[/]");

            foreach (var phase in phases)
            {
                table.AddRow(
                    Markup.Escape(phase.PhaseId),
                    Markup.Escape(phase.Title),
                    PhaseStateMarkup(phase.State),
                    $"{phase.Progress}%",
                    phase.Blockers > 0 ? $"[red]{phase.Blockers}[/]" : "[green]0[/]");
            }

            AnsiConsole.Write(table);
            return;
        }

        Console.WriteLine("Phases");
        Console.WriteLine("======");
        foreach (var phase in phases)
            Console.WriteLine($"{phase.PhaseId} | {phase.State} | {phase.Progress,3}% | blockers {phase.Blockers} | {phase.Title}");
    }

    public static void RenderInboxStatus(
        int pending,
        int processed,
        int failed,
        bool available,
        DateTime? oldestPending,
        IReadOnlyList<string> pendingFiles,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[bold]Available[/]", available ? "[green]Yes[/]" : "[red]No[/]");
            grid.AddRow("[bold]Pending[/]", pending > 0 ? $"[yellow]{pending}[/]" : "[green]0[/]");
            grid.AddRow("[bold]Processed[/]", $"{processed}");
            grid.AddRow("[bold]Failed[/]", failed > 0 ? $"[red]{failed}[/]" : "[green]0[/]");
            if (oldestPending.HasValue)
                grid.AddRow("[bold]Oldest Pending[/]", $"{oldestPending.Value:yyyy-MM-dd HH:mm} UTC");

            var renderables = new List<IRenderable> { grid };
            if (pendingFiles.Count > 0)
            {
                var pendingTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("[bold]Pending Files[/]");

                foreach (var file in pendingFiles)
                    pendingTable.AddRow(Markup.Escape(file));

                renderables.Add(new Rule("[grey]Pending Files[/]").LeftJustified());
                renderables.Add(pendingTable);
            }

            AnsiConsole.Write(new Panel(new Rows(renderables))
            {
                Header = new PanelHeader("[bold]Inbox Status[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine("Inbox status:");
        Console.WriteLine($"  Available: {available}");
        Console.WriteLine($"  Pending:   {pending}");
        Console.WriteLine($"  Processed: {processed}");
        Console.WriteLine($"  Failed:    {failed}");
        if (oldestPending.HasValue)
            Console.WriteLine($"  Oldest:    {oldestPending.Value:yyyy-MM-dd HH:mm} UTC");
        if (pendingFiles.Count > 0)
        {
            Console.WriteLine("  Pending files:");
            foreach (var file in pendingFiles)
                Console.WriteLine($"    {file}");
        }
    }

    public static void RenderProcessInbox(ProcessInboxResult result, CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[bold]Processed[/]", result.Processed ? "[green]Yes[/]" : "[yellow]No[/]");
            grid.AddRow("[bold]Total Pending[/]", $"{result.TotalPending}");
            grid.AddRow("[bold]Succeeded[/]", result.Succeeded > 0 ? $"[green]{result.Succeeded}[/]" : "0");
            grid.AddRow("[bold]Failed[/]", result.Failed > 0 ? $"[red]{result.Failed}[/]" : "[green]0[/]");
            grid.AddRow("[bold]Skipped[/]", result.Skipped > 0 ? $"[yellow]{result.Skipped}[/]" : "0");

            var renderables = new List<IRenderable> { grid };
            if (result.Errors.Count > 0)
            {
                var errorTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("[bold red]Errors[/]");
                foreach (var error in result.Errors)
                    errorTable.AddRow(Markup.Escape(error));
                renderables.Add(new Rule("[red]Failures[/]").LeftJustified());
                renderables.Add(errorTable);
            }

            AnsiConsole.Write(new Panel(new Rows(renderables))
            {
                Header = new PanelHeader("[bold]Inbox Processing[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine($"Inbox processed: {result.Succeeded} succeeded, {result.Failed} failed, {result.Skipped} skipped (of {result.TotalPending} total)");
        if (result.Errors.Count > 0)
        {
            Console.WriteLine("Errors:");
            foreach (var error in result.Errors)
                Console.WriteLine($"  - {error}");
        }
    }

    public static void RenderContext(
        RelevantContextResult context,
        CliOutputMode mode)
    {
        var title = string.IsNullOrWhiteSpace(context.PhaseId)
            ? "Context Pointers (workflow-level)"
            : $"Context Pointers for {context.PhaseId}";

        if (mode == CliOutputMode.Rich)
        {
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(title)}[/]").RuleStyle("grey"));

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]Type[/]")
                .AddColumn("[bold]Target[/]")
                .AddColumn("[bold]Score[/]")
                .AddColumn("[bold]Tokens[/]")
                .AddColumn("[bold]Reason[/]");

            foreach (var pointer in context.Pointers)
            {
                table.AddRow(
                    Markup.Escape(pointer.PointerType),
                    Markup.Escape(pointer.Target),
                    $"{pointer.RelevanceScore:0.00}",
                    pointer.EstimatedTokens > 0 ? pointer.EstimatedTokens.ToString() : "-",
                    Markup.Escape(pointer.Reason));
            }

            AnsiConsole.Write(table);
            RenderContextFooter(context, mode);
            return;
        }

        Console.WriteLine(title);
        Console.WriteLine(new string('=', title.Length));
        foreach (var pointer in context.Pointers)
        {
            Console.WriteLine($"- {pointer.PointerType} | {pointer.Target}");
            Console.WriteLine($"  score: {pointer.RelevanceScore:0.00} | tokens: {(pointer.EstimatedTokens > 0 ? pointer.EstimatedTokens : -1)}");
            Console.WriteLine($"  reason: {pointer.Reason}");
        }
        RenderContextFooter(context, mode);
    }

    public static void RenderIndexHealth(
        IndexHealth health,
        IReadOnlyList<(string Name, long Bytes, DateTime Modified)> files,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[bold]Database[/]", health.DatabaseExists ? "[green]Present[/]" : "[red]Missing[/]");
            grid.AddRow("[bold]Healthy[/]", health.IsHealthy ? "[green]Yes[/]" : "[red]No[/]");
            grid.AddRow("[bold]Phases[/]", $"{health.PhaseCount}");
            grid.AddRow("[bold]Implementations[/]", $"{health.ImplementationCount}");
            grid.AddRow("[bold]Audits[/]", $"{health.AuditCount}");
            grid.AddRow("[bold]Reviews[/]", $"{health.ReviewCount}");
            grid.AddRow("[bold]Validations[/]", $"{health.ValidationCount}");
            grid.AddRow("[bold]Fixes[/]", $"{health.FixCount}");
            grid.AddRow("[bold]Blockers[/]", $"{health.BlockerCount}");
            grid.AddRow("[bold]Events[/]", $"{health.EventCount}");

            var renderables = new List<IRenderable> { grid };

            if (files.Count > 0)
            {
                var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("[bold]File[/]")
                    .AddColumn("[bold]Size[/]")
                    .AddColumn("[bold]Modified[/]");

                foreach (var file in files)
                    table.AddRow(Markup.Escape(file.Name), FormatBytes(file.Bytes), $"{file.Modified:yyyy-MM-dd HH:mm}");

                renderables.Add(new Rule("[grey]Index Files[/]").LeftJustified());
                renderables.Add(table);
            }

            if (health.Errors.Count > 0)
            {
                var errors = new Table().Border(TableBorder.Simple).AddColumn("[bold red]Errors[/]");
                foreach (var error in health.Errors)
                    errors.AddRow(Markup.Escape(error));
                renderables.Add(errors);
            }

            renderables.Add(new Markup("[grey]Note: .workflowkit/index/* is rebuildable and not source of truth.[/]"));

            AnsiConsole.Write(new Panel(new Rows(renderables))
            {
                Header = new PanelHeader("[bold]Context Index Health[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine("Context Index Health");
        Console.WriteLine("--------------------");
        Console.WriteLine($"Database:        {(health.DatabaseExists ? "Present" : "Missing")}");
        Console.WriteLine($"Healthy:         {health.IsHealthy}");
        Console.WriteLine($"Phases:          {health.PhaseCount}");
        Console.WriteLine($"Implementations: {health.ImplementationCount}");
        Console.WriteLine($"Audits:          {health.AuditCount}");
        Console.WriteLine($"Reviews:         {health.ReviewCount}");
        Console.WriteLine($"Tests:           {health.ValidationCount}");
        Console.WriteLine($"Fixes:           {health.FixCount}");
        Console.WriteLine($"Blockers:        {health.BlockerCount}");
        Console.WriteLine($"Events:          {health.EventCount}");
        if (files.Count > 0)
        {
            Console.WriteLine("Index files:");
            foreach (var file in files)
                Console.WriteLine($"  - {file.Name} ({file.Bytes} bytes, {file.Modified:yyyy-MM-dd HH:mm})");
        }
        if (health.Errors.Count > 0)
        {
            Console.WriteLine("Errors:");
            foreach (var error in health.Errors)
                Console.WriteLine($"  - {error}");
        }
        Console.WriteLine("Note: .workflowkit/index/* is rebuildable and not source of truth.");
    }

    public static void RenderCacheStatus(
        CacheDiagnostics diagnostics,
        CacheSettings settings,
        bool settingsFileExists,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[bold]Settings[/]", settingsFileExists ? "[green]Configured[/]" : "[yellow]Default[/]");
            grid.AddRow("[bold]Packages[/]", $"{diagnostics.PackageCount}");
            grid.AddRow("[bold]Pinned[/]", $"{diagnostics.PinnedCount}");
            grid.AddRow("[bold]Memory[/]", $"{FormatBytes(diagnostics.EstimatedTotalMemoryBytes)} / {FormatBytes(diagnostics.MaxMemoryEstimateBytes)}");
            grid.AddRow("[bold]Hit Rate[/]", $"{diagnostics.HitRate:P1}");
            grid.AddRow("[bold]Hits[/]", $"{diagnostics.HitCount}");
            grid.AddRow("[bold]Misses[/]", $"{diagnostics.MissCount}");
            grid.AddRow("[bold]Stale[/]", $"{diagnostics.StaleCount}");
            grid.AddRow("[bold]Evictions[/]", $"{diagnostics.EvictionCount}");
            grid.AddRow("[bold]Max Packages[/]", $"{settings.MaxPackageCount}");
            grid.AddRow("[bold]Max Age[/]", settings.MaxPackageAgeMinutes > 0 ? $"{settings.MaxPackageAgeMinutes} min" : "disabled");
            grid.AddRow("[bold]Predictive Warm[/]", settings.PredictiveWarmingEnabled ? "[green]On[/]" : "[yellow]Off[/]");
            grid.AddRow("[bold]Content Slice Cache[/]", settings.ContentSliceCacheEnabled ? "[green]On[/]" : "[grey]Off[/]");

            var renderables = new List<IRenderable> { grid };
            if (diagnostics.LruOrder.Count > 0)
            {
                var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("[bold]LRU Order (most recent first)[/]");

                foreach (var item in diagnostics.LruOrder.Take(10))
                    table.AddRow(Markup.Escape(item));

                renderables.Add(new Rule("[grey]Cache Keys[/]").LeftJustified());
                renderables.Add(table);
            }

            AnsiConsole.Write(new Panel(new Rows(renderables))
            {
                Header = new PanelHeader("[bold]Cache Diagnostics[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine("Cache Diagnostics");
        Console.WriteLine("-----------------");
        Console.WriteLine($"Settings:            {(settingsFileExists ? "Configured" : "Default")}");
        Console.WriteLine($"Packages:            {diagnostics.PackageCount}");
        Console.WriteLine($"Pinned:              {diagnostics.PinnedCount}");
        Console.WriteLine($"Memory:              {diagnostics.EstimatedTotalMemoryBytes} / {diagnostics.MaxMemoryEstimateBytes} bytes");
        Console.WriteLine($"Hit Rate:            {diagnostics.HitRate:P1}");
        Console.WriteLine($"Hits/Misses:         {diagnostics.HitCount}/{diagnostics.MissCount}");
        Console.WriteLine($"Stale/Evictions:     {diagnostics.StaleCount}/{diagnostics.EvictionCount}");
        Console.WriteLine($"Predictive Warm:     {settings.PredictiveWarmingEnabled}");
        Console.WriteLine($"Content Slice Cache: {settings.ContentSliceCacheEnabled}");
        if (diagnostics.LruOrder.Count > 0)
        {
            Console.WriteLine("LRU order:");
            foreach (var item in diagnostics.LruOrder.Take(10))
                Console.WriteLine($"  - {item}");
        }
    }

    public static void RenderTraceStatus(
        string modeName,
        int retentionDays,
        long maxDirectorySizeBytes,
        int fileCount,
        int recordCount,
        long directorySizeBytes,
        bool isEnabled,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[bold]Enabled[/]", isEnabled ? "[green]Yes[/]" : "[yellow]No[/]");
            grid.AddRow("[bold]Mode[/]", Markup.Escape(modeName));
            grid.AddRow("[bold]Retention[/]", $"{retentionDays} day(s)");
            grid.AddRow("[bold]Max Size[/]", FormatBytes(maxDirectorySizeBytes));
            grid.AddRow("[bold]Trace Files[/]", $"{fileCount}");
            grid.AddRow("[bold]Trace Records[/]", $"{recordCount}");
            grid.AddRow("[bold]Directory Size[/]", FormatBytes(directorySizeBytes));

            AnsiConsole.Write(new Panel(grid)
            {
                Header = new PanelHeader("[bold]Trace Status[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            return;
        }

        Console.WriteLine("Trace Status");
        Console.WriteLine("------------");
        Console.WriteLine($"Enabled:        {isEnabled}");
        Console.WriteLine($"Mode:           {modeName}");
        Console.WriteLine($"Retention:      {retentionDays} day(s)");
        Console.WriteLine($"Max Size:       {maxDirectorySizeBytes} bytes");
        Console.WriteLine($"Trace Files:    {fileCount}");
        Console.WriteLine($"Trace Records:  {recordCount}");
        Console.WriteLine($"Directory Size: {directorySizeBytes} bytes");
    }

    public static void RenderTraceList(
        IReadOnlyList<TraceRecord> traces,
        int count,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var table = new Table()
                .Title($"[bold]Trace List[/] - {count} record(s)")
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]Trace[/]")
                .AddColumn("[bold]When[/]")
                .AddColumn("[bold]Phase[/]")
                .AddColumn("[bold]Operation[/]")
                .AddColumn("[bold]Status[/]")
                .AddColumn("[bold]Duration[/]")
                .AddColumn("[bold]Cache[/]");

            foreach (var trace in traces)
            {
                table.AddRow(
                    Markup.Escape(trace.TraceId),
                    $"{trace.TimestampUtc:yyyy-MM-dd HH:mm:ss}",
                    Markup.Escape(trace.PhaseId ?? "-"),
                    Markup.Escape(trace.OperationName),
                    TraceStatusMarkup(trace.Status),
                    $"{trace.DurationMs} ms",
                    trace.CacheHit ? "[green]hit[/]" : "[grey]miss[/]");
            }

            AnsiConsole.Write(table);
            return;
        }

        Console.WriteLine($"Trace List ({count} record(s))");
        Console.WriteLine("------------------------------");
        foreach (var trace in traces)
        {
            Console.WriteLine($"{trace.TraceId} | {trace.TimestampUtc:yyyy-MM-dd HH:mm:ss} | {trace.Status} | {trace.DurationMs} ms | {trace.OperationName} | phase {trace.PhaseId ?? "-"}");
        }
    }

    public static void RenderTimeline(
        IReadOnlyList<TimelineEntry> entries,
        int count,
        CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
        {
            var table = new Table()
                .Title($"[bold]Timeline[/] - {count} event(s)")
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]When[/]")
                .AddColumn("[bold]Source[/]")
                .AddColumn("[bold]Type[/]")
                .AddColumn("[bold]Phase[/]")
                .AddColumn("[bold]Actor[/]")
                .AddColumn("[bold]Summary[/]");

            foreach (var entry in entries)
            {
                table.AddRow(
                    $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}",
                    Markup.Escape(entry.Source),
                    Markup.Escape(entry.EventType),
                    Markup.Escape(entry.PhaseId ?? "-"),
                    Markup.Escape(entry.Actor ?? "-"),
                    Markup.Escape(entry.Summary));
            }

            AnsiConsole.Write(table);
            return;
        }

        Console.WriteLine($"Timeline ({count} event(s))");
        Console.WriteLine("---------------------------");
        foreach (var entry in entries)
            Console.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Source} | {entry.EventType} | {entry.PhaseId ?? "-"} | {entry.Actor ?? "-"} | {entry.Summary}");
    }

    public static void Ok(string message, CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
            AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(message)}");
        else
            Console.WriteLine(message);
    }

    public static void Error(string message, CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
            AnsiConsole.MarkupLine($"[red bold]ERROR:[/] {Markup.Escape(message)}");
        else
            Console.Error.WriteLine($"ERROR: {message}");
    }

    public static void Warn(string message, CliOutputMode mode)
    {
        if (mode == CliOutputMode.Rich)
            AnsiConsole.MarkupLine($"[yellow]WARN[/] {Markup.Escape(message)}");
        else
            Console.Error.WriteLine($"WARN: {message}");
    }

    private static void RenderIssuesBlock(IReadOnlyList<string> issues, CliOutputMode mode)
    {
        if (issues.Count == 0)
        {
            if (mode == CliOutputMode.Rich)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green bold]All checks passed.[/]");
            }

            return;
        }

        if (mode == CliOutputMode.Rich)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red bold]Issues:[/]");
            foreach (var issue in issues)
                AnsiConsole.MarkupLine($"  [red]*[/] {Markup.Escape(issue)}");
        }
    }

    private static void RenderContextFooter(RelevantContextResult context, CliOutputMode mode)
    {
        var cacheText = context.IsFromCache ? "yes" : "no";
        var tokenText = context.EstimatedTotalTokens > 0 ? context.EstimatedTotalTokens.ToString() : "unknown";

        if (mode == CliOutputMode.Rich)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]From Cache:[/] {Markup.Escape(cacheText)}");
            AnsiConsole.MarkupLine($"[bold]Estimated Tokens:[/] {Markup.Escape(tokenText)}");
            AnsiConsole.MarkupLine($"[bold]Omitted Candidates:[/] {context.OmittedCandidates}");

            if (context.Warnings.Count > 0)
            {
                var warningTable = new Table().Border(TableBorder.Simple).AddColumn("[bold yellow]Warnings[/]");
                foreach (var warning in context.Warnings)
                    warningTable.AddRow(Markup.Escape(warning));
                AnsiConsole.Write(warningTable);
            }

            return;
        }

        Console.WriteLine();
        Console.WriteLine($"From cache:         {cacheText}");
        Console.WriteLine($"Estimated tokens:   {tokenText}");
        Console.WriteLine($"Omitted candidates: {context.OmittedCandidates}");
        if (context.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in context.Warnings)
                Console.WriteLine($"  - {warning}");
        }
    }

    private static string TraceStatusMarkup(TraceStatus status) => status switch
    {
        TraceStatus.Success => "[green]Success[/]",
        TraceStatus.Warning => "[yellow]Warning[/]",
        TraceStatus.Failed => "[red]Failed[/]",
        _ => Markup.Escape(status.ToString())
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.0} {units[unitIndex]}";
    }
}
