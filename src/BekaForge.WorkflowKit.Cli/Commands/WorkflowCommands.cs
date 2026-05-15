using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdMetrics(string subCmd, string? wfRoot, string? phaseId, bool json, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var phases = store.LoadAllPhases();
        var events = store.ReadAllEvents();

        bool showBottleneck = string.Equals(subCmd, "bottleneck", StringComparison.OrdinalIgnoreCase);
        bool showSummary    = string.Equals(subCmd, "summary", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(subCmd);

        // Single-phase detail
        if (!string.IsNullOrWhiteSpace(phaseId) && !showBottleneck)
        {
            var p = phases.FirstOrDefault(x =>
                string.Equals(x.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));
            if (p is null) { CliRenderer.Error($"Phase '{phaseId}' not found.", mode); Environment.Exit(1); }

            var m = PhaseMetricsCalculator.ComputePhase(p, events);
            if (json) { WriteJson(m); return; }

            Console.WriteLine($"Metrics: {m.PhaseId} — {m.Title}");
            Console.WriteLine($"  State:        {m.CurrentState}");
            Console.WriteLine($"  Queue time:   {PhaseMetricsCalculator.FormatDuration(m.QueueTime)}");
            Console.WriteLine($"  Cycle time:   {PhaseMetricsCalculator.FormatDuration(m.CycleTime)}");
            Console.WriteLine($"  Total age:    {PhaseMetricsCalculator.FormatDuration(m.TotalAge)}");
            Console.WriteLine($"  Reopens:      {m.ReopenCount}");
            Console.WriteLine($"  Blockers:     {m.BlockerCount}");
            Console.WriteLine($"  Fix cycles:   {m.FixCycleCount}");
            return;
        }

        var agg = PhaseMetricsCalculator.Compute(phases, events);

        if (json) { WriteJson(agg); return; }

        if (showBottleneck)
        {
            Console.WriteLine("Bottleneck States (most phases currently waiting):");
            if (agg.BottleneckStates.Count == 0)
                Console.WriteLine("  None — all phases are planned or terminal.");
            else
                foreach (var s in agg.BottleneckStates)
                    Console.WriteLine($"  • {s}");
            Console.WriteLine();
            Console.WriteLine("Active phases:");
            foreach (var m in agg.PhaseDetails.Where(m => !m.IsComplete && !m.IsFailed && m.CurrentState != "Planned"))
                Console.WriteLine($"  {m.PhaseId,-14} {m.CurrentState,-26} cycle: {PhaseMetricsCalculator.FormatDuration(m.CycleTime)}");
            return;
        }

        // Summary
        Console.WriteLine($"Phase Metrics Summary");
        Console.WriteLine($"=====================");
        Console.WriteLine($"  Total:    {agg.TotalPhases}");
        Console.WriteLine($"  Passed:   {agg.PassedPhases}");
        Console.WriteLine($"  Failed:   {agg.FailedPhases}");
        Console.WriteLine($"  Active:   {agg.ActivePhases}");
        Console.WriteLine($"  Planned:  {agg.PlannedPhases}");
        Console.WriteLine($"  Avg cycle time:    {PhaseMetricsCalculator.FormatDuration(agg.AverageCycleTime)}");
        Console.WriteLine($"  Median cycle time: {PhaseMetricsCalculator.FormatDuration(agg.MedianCycleTime)}");
        Console.WriteLine();
        if (agg.BottleneckStates.Count > 0)
        {
            Console.WriteLine("Active bottleneck states:");
            foreach (var s in agg.BottleneckStates)
                Console.WriteLine($"  • {s}");
            Console.WriteLine();
        }
        Console.WriteLine("Per-phase summary:");
        foreach (var m in agg.PhaseDetails)
        {
            var status = m.IsComplete ? "✓" : m.IsFailed ? "✗" : "~";
            Console.WriteLine($"  {status} {m.PhaseId,-14} {m.CurrentState,-26} age: {PhaseMetricsCalculator.FormatDuration(m.TotalAge)}");
        }
    }

    internal static void CmdWork(string subCmd, string? wfRoot, string? phaseId, string? roleArg, bool json, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        bool forceFlag = HasFlag(CommandLineArgs, "--force");
        bool activeOnly = HasFlag(CommandLineArgs, "--active-only");

        switch (subCmd.ToLowerInvariant())
        {
            case "begin":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(roleArg))
                {
                    CliRenderer.Error("--role is required. Valid: Implementer, Auditor, Reviewer, Validator.", mode);
                    Environment.Exit(1);
                }

                var lockPath = WorkflowLayout.WorkLockPath(wfRoot, phaseId);
                var existing = WorkSession.TryLoad(lockPath);

                if (existing is not null && !existing.IsStale)
                {
                    if (!forceFlag)
                    {
                        CliRenderer.Error(
                            $"Phase {phaseId} is already locked by {existing.Actor} as {existing.Role} " +
                            $"(started {existing.StartedUtc:yyyy-MM-dd HH:mm} UTC). Use --force to override.",
                            mode);
                        Environment.Exit(1);
                    }
                    else
                    {
                        CliRenderer.Ok($"Overriding existing lock held by {existing.Actor} as {existing.Role}.", mode);
                    }
                }
                else if (existing is not null && existing.IsStale)
                {
                    CliRenderer.Ok($"Replacing stale lock (last seen {existing.LastUpdatedUtc:yyyy-MM-dd HH:mm} UTC).", mode);
                }

                var session = new WorkSession(
                    PhaseId:       phaseId,
                    Role:          roleArg,
                    Actor:         Environment.UserName,
                    AgentName:     ParseFlag(CommandLineArgs, "--agent") ?? "bfwf-cli",
                    MachineName:   Environment.MachineName,
                    StartedUtc:    DateTimeOffset.UtcNow,
                    LastUpdatedUtc: DateTimeOffset.UtcNow);

                session.Save(lockPath);

                if (json)
                    WriteJson(new { session.PhaseId, session.Role, session.Actor, session.StartedUtc });
                else
                    CliRenderer.Ok($"Work session begun: {phaseId} as {roleArg}.", mode);
                break;
            }

            case "end":
            {
                // If no phase specified, end all sessions for current user
                var workDir = WorkflowLayout.WorkDirPath(wfRoot);
                IEnumerable<string> lockFiles;

                if (!string.IsNullOrWhiteSpace(phaseId))
                {
                    lockFiles = new[] { WorkflowLayout.WorkLockPath(wfRoot, phaseId) };
                }
                else
                {
                    lockFiles = Directory.Exists(workDir)
                        ? Directory.GetFiles(workDir, "*.json")
                        : Array.Empty<string>();
                }

                int ended = 0;
                foreach (var lf in lockFiles)
                {
                    if (!File.Exists(lf)) continue;
                    var s = WorkSession.TryLoad(lf);
                    if (s is null) continue;

                    if (!forceFlag && !string.Equals(s.Actor, Environment.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        CliRenderer.Error(
                            $"Lock for {s.PhaseId} is held by {s.Actor}, not {Environment.UserName}. Use --force to release.",
                            mode);
                        continue;
                    }

                    File.Delete(lf);
                    ended++;
                    if (!json) CliRenderer.Ok($"Work session ended: {s.PhaseId}.", mode);
                }

                if (json)
                    WriteJson(new { ended });
                else if (ended == 0)
                    CliRenderer.Ok("No active work sessions found.", mode);
                break;
            }

            case "list":
            {
                var workDir = WorkflowLayout.WorkDirPath(wfRoot);
                if (!Directory.Exists(workDir))
                {
                    if (json) { WriteJson(Array.Empty<object>()); return; }
                    CliRenderer.Ok("No active work sessions.", mode);
                    return;
                }

                var sessions = Directory.GetFiles(workDir, "*.json")
                    .Select(WorkSession.TryLoad)
                    .Where(s => s is not null)
                    .Where(s => !activeOnly || !s!.IsStale)
                    .OrderBy(s => s!.StartedUtc)
                    .ToList();

                if (json)
                {
                    WriteJson(sessions);
                    return;
                }

                if (sessions.Count == 0)
                {
                    CliRenderer.Ok("No active work sessions.", mode);
                    return;
                }

                Console.WriteLine($"Active work sessions ({sessions.Count}):");
                Console.WriteLine();
                foreach (var s in sessions)
                {
                    var age = DateTimeOffset.UtcNow - s!.StartedUtc;
                    var staleMarker = s.IsStale ? " [STALE]" : "";
                    Console.WriteLine($"  {s.PhaseId,-14} {s.Role,-14} {s.Actor} on {s.MachineName} ({age.TotalMinutes:F0}m ago){staleMarker}");
                }
                break;
            }

            default:
                CliRenderer.Error("Usage: bfwf work begin|end|list [--phase PHASE-NNN] [--role <role>] [--active-only] [--force]", mode);
                Environment.Exit(1);
                break;
        }
    }

    internal static void CmdNext(string? wfRoot, string? phaseId, bool json, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);

        // If no --phase specified, advise on all active (non-terminal, non-Planned) phases,
        // or show next action for the explicit phase.
        IReadOnlyList<Phase> phases;
        if (!string.IsNullOrWhiteSpace(phaseId))
        {
            var p = store.LoadPhase(phaseId);
            if (p is null)
            {
                CliRenderer.Error($"Phase '{phaseId}' not found.", mode);
                Environment.Exit(1);
            }
            phases = [p];
        }
        else
        {
            phases = store.LoadAllPhases().OrderBy(p => p.PhaseNumber).ToList();
        }

        var adviceList = phases.Select(p =>
            NextActionAdvisor.Advise(p, GetOpenBlockerDescriptions(store, p.PhaseId))).ToList();

        if (json)
        {
            WriteJson(adviceList.Select(a => new
            {
                a.PhaseId, a.Title, a.State, a.Command, a.Explanation, a.Blockers, a.IsTerminal
            }));
            return;
        }

        if (mode == CliOutputMode.Plain)
        {
            foreach (var a in adviceList)
            {
                Console.WriteLine($"[{a.PhaseId}] {a.Title}");
                Console.WriteLine($"  State:       {a.State}");
                Console.WriteLine($"  Next action: {a.Command}");
                Console.WriteLine($"  Why:         {a.Explanation}");
                if (a.Blockers.Count > 0)
                {
                    Console.WriteLine("  Blockers:");
                    foreach (var b in a.Blockers)
                        Console.WriteLine($"    - {b}");
                }
                Console.WriteLine();
            }
            return;
        }

        // Rich mode
        foreach (var a in adviceList)
        {
            var panel = new Spectre.Console.Panel(
                new Spectre.Console.Markup(
                    $"[bold]State:[/] {Spectre.Console.Markup.Escape(a.State)}\n" +
                    $"[bold]Command:[/] [cyan]{Spectre.Console.Markup.Escape(a.Command)}[/]\n" +
                    $"[dim]{Spectre.Console.Markup.Escape(a.Explanation)}[/]" +
                    (a.Blockers.Count > 0
                        ? "\n[red]Blockers:[/]\n" + string.Join("\n", a.Blockers.Select(b => $"  • {Spectre.Console.Markup.Escape(b)}"))
                        : "")))
            {
                Header = new Spectre.Console.PanelHeader($"{Spectre.Console.Markup.Escape(a.PhaseId)} — {Spectre.Console.Markup.Escape(a.Title)}")
            };
            Spectre.Console.AnsiConsole.Write(panel);
        }
    }

    internal static IReadOnlyList<string> GetOpenBlockerDescriptions(WorkflowStore store, string phaseId) =>
        store.ReadAllBlockers()
            .Where(b => string.Equals(b.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.BlockerId)
            .Select(g => g.Last())
            .Where(b => !b.IsResolved)
            .Select(b => $"{b.BlockerId}: {b.Reason}")
            .ToList();

    internal static void CmdPreflight(string? wfRoot, string? phaseId, string? role, bool json, CliOutputMode mode)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        if (string.IsNullOrWhiteSpace(phaseId))
        {
            CliRenderer.Error("--phase is required.", mode);
            Environment.Exit(1);
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            CliRenderer.Error("--role is required. Valid roles: Implementer, Auditor, Reviewer, Validator.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var phase = store.LoadPhase(phaseId);
        if (phase is null)
        {
            CliRenderer.Error($"Phase '{phaseId}' not found.", mode);
            Environment.Exit(1);
        }

        var allPhases = store.LoadAllPhases();
        var openBlockers = GetOpenBlockerCount(store, phaseId);
        var hasContract = phase.Contract is not null;

        var result = PreflightChecker.Check(phase, role, allPhases, openBlockers, hasContract);

        if (json)
        {
            WriteJson(new
            {
                result.PhaseId, result.Role, result.Clear,
                result.Issues, result.Warnings
            });
            if (!result.Clear) Environment.Exit(1);
            return;
        }

        if (mode == CliOutputMode.Plain)
        {
            Console.WriteLine($"Preflight: {result.PhaseId} as {result.Role}");
            Console.WriteLine($"Result: {(result.Clear ? "CLEAR" : "BLOCKED")}");
            if (result.Issues.Count > 0)
            {
                Console.WriteLine("Issues:");
                foreach (var i in result.Issues)
                    Console.WriteLine($"  [FAIL] {i}");
            }
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var w in result.Warnings)
                    Console.WriteLine($"  [WARN] {w}");
            }
            if (result.Clear)
                Console.WriteLine("Clear to proceed.");
            if (!result.Clear) Environment.Exit(1);
            return;
        }

        // Rich mode
        var statusMarkup = result.Clear
            ? "[bold green]CLEAR TO PROCEED[/]"
            : "[bold red]BLOCKED[/]";

        var content = $"[bold]Phase:[/] {Spectre.Console.Markup.Escape(result.PhaseId)}\n" +
                      $"[bold]Role:[/]  {Spectre.Console.Markup.Escape(result.Role)}\n" +
                      $"[bold]Status:[/] {statusMarkup}";

        if (result.Issues.Count > 0)
            content += "\n\n[bold red]Issues:[/]\n" +
                       string.Join("\n", result.Issues.Select(i => $"  [red]✗[/] {Spectre.Console.Markup.Escape(i)}"));

        if (result.Warnings.Count > 0)
            content += "\n\n[bold yellow]Warnings:[/]\n" +
                       string.Join("\n", result.Warnings.Select(w => $"  [yellow]![/] {Spectre.Console.Markup.Escape(w)}"));

        Spectre.Console.AnsiConsole.Write(
            new Spectre.Console.Panel(new Spectre.Console.Markup(content))
            {
                Header = new Spectre.Console.PanelHeader("Preflight Check")
            });

        if (!result.Clear) Environment.Exit(1);
    }

    internal static void CmdDoctor(string? wfRoot, bool json, CliOutputMode mode = CliOutputMode.Plain, bool strict = false)
    {
        var diag = new Dictionary<string, object?>();
        var issues = new List<string>();

        // 1. SDK check
        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            var sdkVersion = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            diag["sdkVersion"] = sdkVersion ?? "unknown";
            diag["sdkOk"] = proc?.ExitCode == 0;
        }
        catch
        {
            diag["sdkVersion"] = null;
            diag["sdkOk"] = false;
            issues.Add("dotnet SDK not found or broken");
        }

        // 2. Git check
        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            var gitVersion = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            diag["gitVersion"] = gitVersion ?? "unknown";
            diag["gitOk"] = proc?.ExitCode == 0;
        }
        catch
        {
            diag["gitVersion"] = null;
            diag["gitOk"] = false;
            issues.Add("git not found");
        }

        // 3. Workflow state
        if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
        {
            var store = new WorkflowStore(wfRoot);
            var state = store.LoadWorkflow();
            diag["workflowRoot"] = wfRoot;
            diag["workflowInitialized"] = true;
            diag["currentPhase"] = state.CurrentPhaseId;
            diag["openBlockers"] = state.OpenBlockerCount;
            diag["phaseCount"] = state.PhaseIds.Count;
        }
        else
        {
            diag["workflowInitialized"] = false;
            if (wfRoot is not null)
                issues.Add($"No .workflowkit found at {wfRoot}");
        }

        // 4. Inbox status
        if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
        {
            try
            {
                var store = new WorkflowStore(wfRoot);
                var dispatcher = new OperationDispatcher(store);
                var inboxResult = dispatcher.Dispatch(new OperationContext { Operation = WorkflowOperations.GetInboxStatus, Actor = WorkflowActor.Implementer });
                if (inboxResult.Success && inboxResult.Data is InboxStatus istatus)
                {
                    diag["inboxPending"] = istatus.PendingCount;
                    diag["inboxFailed"] = istatus.FailedCount;
                    diag["inboxAvailable"] = istatus.InboxAvailable;
                }
            }
            catch { issues.Add("inbox status check failed"); }
        }

        // 5. Index health
        if (wfRoot is not null)
        {
            var indexDir = Path.Combine(wfRoot, ".workflowkit", "index");
            diag["indexExists"] = Directory.Exists(indexDir);
            if (Directory.Exists(indexDir))
            {
                var indexFiles = Directory.GetFiles(indexDir);
                diag["indexFileCount"] = indexFiles.Length;
                diag["indexTotalBytes"] = indexFiles.Sum(f => new FileInfo(f).Length);
            }
        }

        // 6. Trace status
        if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
        {
            try
            {
                var store = new WorkflowStore(wfRoot);
                var dispatcher = new OperationDispatcher(store);
                var traceResult = dispatcher.Dispatch(new OperationContext { Operation = WorkflowOperations.GetTraceStatus, Actor = WorkflowActor.Implementer });
                diag["traceAvailable"] = traceResult.Success;
            }
            catch { diag["traceAvailable"] = false; }
        }

        // 7. Cache settings
        if (wfRoot is not null)
        {
            var cachePath = Path.Combine(wfRoot, ".workflowkit", "cache-settings.json");
            diag["cacheSettingsExist"] = File.Exists(cachePath);
            if (File.Exists(cachePath))
            {
                try
                {
                    var settings = CacheSettings.Load(cachePath);
                    diag["cacheMaxPackages"] = settings.MaxPackageCount;
                    diag["cacheMaxMemory"] = settings.MaxMemoryEstimateBytes;
                }
                catch { issues.Add("cache settings load failed"); }
            }
        }

        // 8. Dashboard (WPF - Windows only)
        diag["dashboardAvailable"] = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        if (!(diag["dashboardAvailable"]?.Equals(true) == true))
            diag["dashboardNote"] = "WPF dashboard is Windows-only; CLI is cross-platform";

        // 9. Drift detection — always run; failures only block exit in strict mode.
        int driftCount = 0;
        if (wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
        {
            try
            {
                var driftStore = new WorkflowStore(wfRoot);
                var driftPhases = driftStore.LoadAllPhases();
                var driftResults = DriftDetector.CheckAll(driftPhases, DriftDetector.DefaultThreshold, wfRoot);
                driftCount = driftResults.Count;
                diag["driftCount"] = driftCount;
                if (strict)
                {
                    foreach (var dr in driftResults)
                        foreach (var f in dr.Findings)
                            issues.Add($"[drift] {dr.PhaseId}: {f.Category} — {f.Message}");
                }
            }
            catch { diag["driftCount"] = 0; }
        }
        else
        {
            diag["driftCount"] = 0;
        }

        // 10. PHASE-003: Strict lifecycle gap detection
        if (strict && wfRoot is not null && WorkflowLayout.IsInitialized(wfRoot))
        {
            try
            {
                var strictStore = new WorkflowStore(wfRoot);
                var phases = strictStore.LoadAllPhases();
                var stuckPhases = phases
                    .Where(p =>
                        p.State is not (PhaseState.Planned or PhaseState.Pass or PhaseState.PassWithWarnings
                            or PhaseState.FailedArchitecture or PhaseState.FailedCompile or PhaseState.FailedValidation)
                        && DateTimeOffset.UtcNow - p.UpdatedUtc > DriftDetector.DefaultThreshold)
                    .ToList();

                if (stuckPhases.Count > 0)
                {
                    foreach (var sp in stuckPhases)
                        issues.Add($"[strict] Phase {sp.PhaseId} stuck in {sp.State} for >{(int)(DateTimeOffset.UtcNow - sp.UpdatedUtc).TotalHours}h");
                }

                // Missing contracts on active phases
                var missingContractPhases = phases
                    .Where(p =>
                        p.State is PhaseState.ReadyForImplementation or PhaseState.AssignedToImplementation
                            or PhaseState.InImplementation
                        && p.Contract is null)
                    .ToList();

                if (missingContractPhases.Count > 0)
                {
                    foreach (var mp in missingContractPhases)
                        issues.Add($"[strict] Phase {mp.PhaseId} ({mp.State}) has no contract defined.");
                }

                // Unstarted dependencies (phase is Pass but depends on non-Pass phases)
                foreach (var p in phases)
                {
                    foreach (var depId in p.Dependencies)
                    {
                        var dep = phases.FirstOrDefault(d =>
                            string.Equals(d.PhaseId, depId, StringComparison.OrdinalIgnoreCase));
                        if (dep is not null && dep.State is not (PhaseState.Pass or PhaseState.PassWithWarnings)
                            && p.State is not PhaseState.Planned)
                            issues.Add($"[strict] Phase {p.PhaseId} depends on {depId} which is not yet complete (state: {dep.State}).");
                    }
                }

                diag["strictChecksRun"] = true;
                diag["stuckPhaseCount"] = stuckPhases.Count;
                diag["missingContractCount"] = missingContractPhases.Count;
            }
            catch (Exception ex)
            {
                issues.Add($"[strict] Lifecycle gap check failed: {ex.Message}");
            }
        }

        diag["issues"] = issues;
        diag["healthy"] = issues.Count == 0;

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(diag,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = false }));
            if (!(diag["healthy"]?.Equals(true) == true)) Environment.Exit(1);
            return;
        }

        CliRenderer.RenderDoctor(
            sdkOk:                diag["sdkOk"]?.Equals(true) == true,
            sdkVersion:           diag["sdkVersion"]?.ToString(),
            gitOk:                diag["gitOk"]?.Equals(true) == true,
            gitVersion:           diag["gitVersion"]?.ToString(),
            workflowInitialized:  diag["workflowInitialized"]?.Equals(true) == true,
            currentPhase:         diag.ContainsKey("currentPhase") ? diag["currentPhase"]?.ToString() : null,
            openBlockers:         diag.ContainsKey("openBlockers") ? Convert.ToInt32(diag["openBlockers"]) : 0,
            indexExists:          diag["indexExists"]?.Equals(true) == true,
            indexFileCount:       diag.ContainsKey("indexFileCount") ? Convert.ToInt32(diag["indexFileCount"]) : 0,
            indexBytes:           diag.ContainsKey("indexTotalBytes") ? Convert.ToInt64(diag["indexTotalBytes"]) : 0,
            cacheSettingsExist:   diag["cacheSettingsExist"]?.Equals(true) == true,
            maxPackages:          diag.ContainsKey("cacheMaxPackages") ? (int?)Convert.ToInt32(diag["cacheMaxPackages"]) : null,
            dashboardAvailable:   diag["dashboardAvailable"]?.Equals(true) == true,
            driftCount:           driftCount,
            issues:               issues,
            mode:                 mode);

        if (!(diag["healthy"]?.Equals(true) == true))
            Environment.Exit(1);
    }

    internal static void CmdInstallAgentRules(string? wfRoot)
    {
        if (wfRoot is null) { Console.Error.WriteLine("ERROR: Not in a Beka Forge Workflow project."); Environment.Exit(1); }

        var sourceDir = wfRoot;
        var targetDir = Path.Combine(wfRoot, ".deepseek");
        var instructionsDir = Path.Combine(targetDir, "instructions");

        var filesToCopy = new (string Source, string DestRelative)[]
        {
            (Path.Combine(sourceDir, "AGENTS.md"), "AGENTS.md"),
            (WorkflowLayout.RulesMdPath(sourceDir), Path.Combine("instructions", "Rules.md")),
            (Path.Combine(sourceDir, "BekaWorkflowSystemPrompt.md"), "BekaWorkflowSystemPrompt.md"),
        };

        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(instructionsDir);

        var copied = 0;
        foreach (var (src, destRel) in filesToCopy)
        {
            if (!File.Exists(src))
            {
                Console.WriteLine($"  SKIP: {Path.GetFileName(src)} not found at project root.");
                continue;
            }

            var dest = Path.Combine(targetDir, destRel);
            File.Copy(src, dest, overwrite: true);
            Console.WriteLine($"  COPY: {Path.GetFileName(src)} -> {dest}");
            copied++;
        }

        // Ensure .deepseek directory exists in target
        var deepseekDir = Path.Combine(targetDir);
        if (!Directory.Exists(deepseekDir))
            Directory.CreateDirectory(deepseekDir);

        Console.WriteLine();
        Console.WriteLine(copied > 0
            ? $"Installed {copied} agent rule file(s) to {targetDir}"
            : "No agent rules found to install. Ensure AGENTS.md exists at the project root.");

        if (copied == 0)
            Environment.Exit(1);
    }
}
