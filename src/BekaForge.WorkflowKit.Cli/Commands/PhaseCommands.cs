using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdPhase(string subCmd, string? wfRoot, string? phaseId, string? title, string? targetState, string? blockerReason, string? agentName,
        string? phaseSummary, string? dependencies, string? objective, string? scope, string? outOfScope,
        string? implementationNotes, string? auditRequirements, string? validationRequirements, string? parallelizationNotes,
        string? architectureConstraints, string? requiredFilesOrAreas, string? acceptanceCriteria, string? dependsOnPhaseIds,
        string? executionLanesJson, string? continueWithPhaseId,
        string? requiresValidation, string? subPhaseId, string? subPhaseSummary, string? subPhaseDependencies, string? subPhasesJson,
        bool json, CliOutputMode mode, bool watch, int watchIntervalSeconds,
        string? needsFilter = null, bool stuckFilter = false)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            CliRenderer.Error("No Beka Forge Workflow project is initialized.", mode);
            Environment.Exit(1);
        }

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        switch (subCmd.ToLowerInvariant())
        {
            case "create":
                if (string.IsNullOrWhiteSpace(title))
                {
                    CliRenderer.Error("--title is required.", mode);
                    Environment.Exit(1);
                }

                var createParameters = new Dictionary<string, object?> { ["title"] = title };
                AddIfPresent(createParameters, "summary", phaseSummary);
                AddIfPresent(createParameters, "assignedAgent", agentName);
                AddIfPresent(createParameters, "dependencies", dependencies);
                AddIfPresent(createParameters, "contractObjective", objective);
                AddIfPresent(createParameters, "contractScope", scope);
                AddIfPresent(createParameters, "contractOutOfScope", outOfScope);
                AddIfPresent(createParameters, "contractImplementationNotes", implementationNotes);
                AddIfPresent(createParameters, "contractAuditRequirements", auditRequirements);
                AddIfPresent(createParameters, "contractValidationRequirements", validationRequirements);
                AddIfPresent(createParameters, "contractParallelizationNotes", parallelizationNotes);
                AddIfPresent(createParameters, "contractArchitectureConstraints", architectureConstraints);
                AddIfPresent(createParameters, "contractRequiredFilesOrAreas", requiredFilesOrAreas);
                AddIfPresent(createParameters, "contractAcceptanceCriteria", acceptanceCriteria);
                AddIfPresent(createParameters, "contractDependsOnPhaseIds", dependsOnPhaseIds);
                AddIfPresent(createParameters, "contractExecutionLanesJson", executionLanesJson);
                AddIfPresent(createParameters, "requiresValidation", requiresValidation);
                AddIfPresent(createParameters, "subPhasesJson", subPhasesJson);

                var createResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.CreatePhase,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = createParameters
                });

                if (json)
                {
                    WriteJson(createResult);
                    return;
                }

                if (createResult.Success)
                    CliRenderer.Ok($"Phase created.", mode);
                else
                {
                    CliRenderer.Error($"{createResult.ErrorCode}: {createResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            case "show":
                var effectivePhaseId = phaseId ?? store.LoadWorkflow().CurrentPhaseId;
                if (string.IsNullOrWhiteSpace(effectivePhaseId))
                {
                    CliRenderer.Error("No phase selected. Use --phase PHASE-NNN or set a current phase first.", mode);
                    Environment.Exit(1);
                }

                if (watch && json)
                {
                    CliRenderer.Error("--watch is only supported for rich/plain phase output.", mode);
                    Environment.Exit(2);
                }

                if (watch)
                {
                    RunWatchLoop(() => RenderPhaseView(wfRoot, effectivePhaseId, mode), watchIntervalSeconds);
                    return;
                }

                if (json)
                {
                    var phaseJson = store.LoadPhase(effectivePhaseId);
                    if (phaseJson is null)
                    {
                        CliRenderer.Error($"Phase '{effectivePhaseId}' not found.", mode);
                        Environment.Exit(1);
                    }

                    WriteJson(phaseJson);
                    return;
                }

                RenderPhaseView(wfRoot, effectivePhaseId, mode);
                break;

            case "list":
                var allPhases = store.LoadAllPhases()
                    .OrderBy(p => p.PhaseNumber)
                    .ToList();

                // PHASE-003: --needs filter
                if (!string.IsNullOrWhiteSpace(needsFilter))
                {
                    allPhases = needsFilter.ToLowerInvariant() switch
                    {
                        "audit" => allPhases.Where(p => p.State == PhaseState.ImplementationLogged).ToList(),
                        "review" => allPhases.Where(p => p.State is PhaseState.AuditLogged or PhaseState.ReadyForReview).ToList(),
                        "validation" or "test" => allPhases.Where(p => p.State is PhaseState.ReviewLogged or PhaseState.ReadyForTest).ToList(),
                        "fix" => allPhases.Where(p => p.State is PhaseState.RequiresFix or PhaseState.FixLogged).ToList(),
                        _ => allPhases
                    };
                }

                // PHASE-003: --stuck filter (active non-terminal phase not updated in >4 hours)
                if (stuckFilter)
                {
                    var stuckThreshold = TimeSpan.FromHours(4);
                    allPhases = allPhases.Where(p =>
                        p.State is not (PhaseState.Planned or PhaseState.Pass or PhaseState.PassWithWarnings
                            or PhaseState.FailedArchitecture or PhaseState.FailedCompile or PhaseState.FailedValidation
                            or PhaseState.Blocked)
                        && DateTimeOffset.UtcNow - p.UpdatedUtc > stuckThreshold).ToList();
                }

                if (json)
                {
                    WriteJson(allPhases);
                    return;
                }

                CliRenderer.RenderPhaseList(
                    allPhases.Select(p => (
                        PhaseId: p.PhaseId,
                        Title: p.Title,
                        State: p.State.ToString(),
                        Progress: PhaseProgress.ForPhase(p),
                        Blockers: GetOpenBlockerCount(store, p.PhaseId))).ToList(),
                    mode);
                break;

            case "status":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(targetState))
                {
                    CliRenderer.Error("--state is required.", mode);
                    Environment.Exit(1);
                }

                var parameters = new Dictionary<string, object?> { ["state"] = targetState };
                if (!string.IsNullOrWhiteSpace(blockerReason))
                    parameters["blockerReason"] = blockerReason;

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.UpdatePhaseStatus,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = parameters
                });

                if (result.Success)
                    CliRenderer.Ok("Phase state updated.", mode);
                else
                {
                    CliRenderer.Error($"{result.ErrorCode}: {result.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            case "assign":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(agentName))
                {
                    CliRenderer.Error("--agent is required.", mode);
                    Environment.Exit(1);
                }

                var assignResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.AssignPhase,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.Codex,
                    Parameters = new Dictionary<string, object?> { ["agent"] = agentName }
                });

                if (assignResult.Success)
                    CliRenderer.Ok("Phase assigned.", mode);
                else
                {
                    CliRenderer.Error($"{assignResult.ErrorCode}: {assignResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            case "update":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }

                var updateParameters = new Dictionary<string, object?>();
                AddIfPresent(updateParameters, "summary", phaseSummary);
                AddIfPresent(updateParameters, "dependencies", dependencies);
                AddIfPresent(updateParameters, "contractObjective", objective);
                AddIfPresent(updateParameters, "contractScope", scope);
                AddIfPresent(updateParameters, "contractOutOfScope", outOfScope);
                AddIfPresent(updateParameters, "contractImplementationNotes", implementationNotes);
                AddIfPresent(updateParameters, "contractAuditRequirements", auditRequirements);
                AddIfPresent(updateParameters, "contractValidationRequirements", validationRequirements);
                AddIfPresent(updateParameters, "contractParallelizationNotes", parallelizationNotes);
                AddIfPresent(updateParameters, "contractArchitectureConstraints", architectureConstraints);
                AddIfPresent(updateParameters, "contractRequiredFilesOrAreas", requiredFilesOrAreas);
                AddIfPresent(updateParameters, "contractAcceptanceCriteria", acceptanceCriteria);
                AddIfPresent(updateParameters, "contractDependsOnPhaseIds", dependsOnPhaseIds);
                AddIfPresent(updateParameters, "contractExecutionLanesJson", executionLanesJson);
                AddIfPresent(updateParameters, "requiresValidation", requiresValidation);
                AddIfPresent(updateParameters, "subPhasesJson", subPhasesJson);
                AddIfPresent(updateParameters, "subPhaseId", subPhaseId);
                AddIfPresent(updateParameters, "subPhaseSummary", subPhaseSummary);
                AddIfPresent(updateParameters, "subPhaseDependencies", subPhaseDependencies);

                var updateResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.UpdatePhase,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = updateParameters
                });

                if (updateResult.Success)
                    CliRenderer.Ok($"Phase {phaseId} updated.", mode);
                else
                {
                    CliRenderer.Error($"{updateResult.ErrorCode}: {updateResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            case "defer":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(blockerReason))
                {
                    CliRenderer.Error("--reason is required.", mode);
                    Environment.Exit(1);
                }

                var deferParameters = new Dictionary<string, object?> { ["reason"] = blockerReason };
                AddIfPresent(deferParameters, "continueWithPhaseId", continueWithPhaseId);

                var deferResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.DeferPhase,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.Planner,
                    Parameters = deferParameters
                });

                if (deferResult.Success)
                    CliRenderer.Ok($"Phase {phaseId} deferred.", mode);
                else
                {
                    CliRenderer.Error($"{deferResult.ErrorCode}: {deferResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            case "focus":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(blockerReason))
                {
                    CliRenderer.Error("--reason is required.", mode);
                    Environment.Exit(1);
                }

                var focusResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.FocusPhase,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.Planner,
                    Parameters = new Dictionary<string, object?> { ["reason"] = blockerReason }
                });

                if (focusResult.Success)
                    CliRenderer.Ok($"Phase {phaseId} focused.", mode);
                else
                {
                    CliRenderer.Error($"{focusResult.ErrorCode}: {focusResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            case "remove":
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }

                var removeResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.RemovePhase,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User
                });

                if (removeResult.Success)
                    CliRenderer.Ok($"Phase {phaseId} removed.", mode);
                else
                {
                    CliRenderer.Error($"{removeResult.ErrorCode}: {removeResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;

            // PHASE-008: recovery / reopen
            case "reopen":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(blockerReason))
                {
                    CliRenderer.Error("--reason is required. Explain why this phase is being reopened.", mode);
                    Environment.Exit(1);
                }

                var reopenResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation  = WorkflowOperations.ReopenPhase,
                    PhaseId    = phaseId,
                    Actor      = WorkflowActor.User,
                    Parameters = new Dictionary<string, object?> { ["reason"] = blockerReason }
                });

                if (reopenResult.Success)
                    CliRenderer.Ok($"Phase {phaseId} reopened -> ReadyForImplementation.", mode);
                else
                {
                    CliRenderer.Error($"{reopenResult.ErrorCode}: {reopenResult.Message}", mode);
                    Environment.Exit(1);
                }
                break;
            }

            // PHASE-007: drift detection
            case "drift-check":
            {
                int threshHours = ParseIntFlag(CommandLineArgs, "--threshold-hours") ?? 24;
                var threshold = TimeSpan.FromHours(threshHours);
                var driftPhases = store.LoadAllPhases();

                IReadOnlyList<DriftDetector.DriftResult> driftResults;
                if (!string.IsNullOrWhiteSpace(phaseId))
                {
                    var target = driftPhases.FirstOrDefault(p =>
                        string.Equals(p.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));
                    if (target is null) { CliRenderer.Error($"Phase '{phaseId}' not found.", mode); Environment.Exit(1); }
                    string? lockPath = WorkflowLayout.WorkLockPath(wfRoot, target.PhaseId);
                    driftResults = new[] { DriftDetector.Check(target, threshold, wfRoot, lockPath) }
                        .Where(r => r.HasDrift).ToList();
                }
                else
                {
                    driftResults = DriftDetector.CheckAll(driftPhases, threshold, wfRoot);
                }

                if (json)
                {
                    WriteJson(driftResults.Select(r => new
                    {
                        r.PhaseId, r.Title, r.State, r.HasDrift,
                        Findings = r.Findings.Select(f => new { f.Category, f.Message })
                    }));
                    if (driftResults.Any(r => r.HasDrift)) Environment.Exit(1);
                    return;
                }

                if (!driftResults.Any(r => r.HasDrift))
                {
                    CliRenderer.Ok($"No drift detected (threshold: {threshHours}h).", mode);
                    return;
                }

                Console.WriteLine($"Drift detected ({driftResults.Sum(r => r.Findings.Count)} findings, threshold: {threshHours}h):");
                Console.WriteLine();
                foreach (var r in driftResults.Where(x => x.HasDrift))
                {
                    Console.WriteLine($"  {r.PhaseId} [{r.State}] {r.Title}");
                    foreach (var f in r.Findings)
                        Console.WriteLine($"    [{f.Category}] {f.Message}");
                }
                Environment.Exit(1);
                break;
            }

            // PHASE-007: file manifest check
            case "manifest":
            {
                var manifestPhases = store.LoadAllPhases();
                IEnumerable<Phase> toCheck = string.IsNullOrWhiteSpace(phaseId)
                    ? manifestPhases
                    : manifestPhases.Where(p => string.Equals(p.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));

                var results = toCheck.Select(p => new
                {
                    PhaseId = p.PhaseId,
                    Title   = p.Title,
                    State   = p.State.ToString(),
                    Areas   = (p.Contract?.RequiredFilesOrAreas ?? []).Select(area =>
                    {
                        var full = Path.GetFullPath(Path.Combine(wfRoot, area.Replace('\\', '/').TrimEnd('/')));
                        bool exists = Directory.Exists(full) || File.Exists(full);
                        return new { Area = area, Exists = exists };
                    }).ToList()
                }).ToList();

                if (json) { WriteJson(results); return; }

                foreach (var r in results)
                {
                    if (r.Areas.Count == 0)
                    {
                        if (!string.IsNullOrWhiteSpace(phaseId))
                            Console.WriteLine($"  {r.PhaseId}: no RequiredFilesOrAreas declared.");
                        continue;
                    }
                    Console.WriteLine($"  {r.PhaseId} [{r.State}]:");
                    foreach (var a in r.Areas)
                        Console.WriteLine($"    {(a.Exists ? "[OK]  " : "[MISS]")} {a.Area}");
                }
                break;
            }

            // PHASE-006: write-area conflict detection
            case "check-conflicts":
            {
                var conflictPhases = store.LoadAllPhases();

                IReadOnlyList<WriteAreaConflictDetector.Conflict> conflicts;
                if (!string.IsNullOrWhiteSpace(phaseId))
                {
                    var targetPhase = conflictPhases.FirstOrDefault(p =>
                        string.Equals(p.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));
                    if (targetPhase is null)
                    {
                        CliRenderer.Error($"Phase '{phaseId}' not found.", mode);
                        Environment.Exit(1);
                    }
                    conflicts = WriteAreaConflictDetector.DetectForPhase(targetPhase, conflictPhases);
                }
                else
                {
                    conflicts = WriteAreaConflictDetector.Detect(conflictPhases);
                }

                if (json)
                {
                    WriteJson(conflicts.Select(c => new
                    {
                        c.PhaseIdA, c.PhaseIdB, c.OverlappingAreas, c.Description
                    }));
                    if (conflicts.Count > 0) Environment.Exit(1);
                    return;
                }

                if (conflicts.Count == 0)
                {
                    CliRenderer.Ok("No write-area conflicts detected.", mode);
                    return;
                }

                if (mode == CliOutputMode.Plain)
                {
                    Console.WriteLine($"Write-area conflicts detected ({conflicts.Count}):");
                    foreach (var c in conflicts)
                    {
                        Console.WriteLine($"  [CONFLICT] {c.PhaseIdA} <-> {c.PhaseIdB}");
                        foreach (var area in c.OverlappingAreas)
                            Console.WriteLine($"    Area: {area}");
                    }
                }
                else
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[bold red]Write-area conflicts detected: {conflicts.Count}[/]");
                    foreach (var c in conflicts)
                        Spectre.Console.AnsiConsole.MarkupLine(
                            $"  [red]✗[/] {Spectre.Console.Markup.Escape(c.PhaseIdA)} [dim]<->[/] {Spectre.Console.Markup.Escape(c.PhaseIdB)}: " +
                            $"[yellow]{Spectre.Console.Markup.Escape(string.Join(", ", c.OverlappingAreas))}[/]");
                }

                Environment.Exit(1);
                break;
            }

            // PHASE-012: bfwf phase contract show|save
            case "contract":
            {
                // Use CommandLineArgs for the contract sub-commands that need full arg list
                var cmdArgs = CommandLineArgs;
                var contractSubCmd = cmdArgs.Length > 2 ? cmdArgs[2].ToLowerInvariant() : "show";
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    CliRenderer.Error("--phase is required.", mode);
                    Environment.Exit(1);
                }

                switch (contractSubCmd)
                {
                    case "show":
                    case "get":
                    {
                        var contractResult = dispatcher.Dispatch(new OperationContext
                        {
                            Operation = WorkflowOperations.GetPhaseContract,
                            PhaseId   = phaseId,
                            Actor     = WorkflowActor.Implementer,
                        });
                        if (!contractResult.Success)
                        {
                            CliRenderer.Error($"[{contractResult.ErrorCode}]: {contractResult.Message}", mode);
                            Environment.Exit(1);
                        }
                        if (json) { WriteJson(contractResult.Data); return; }
                        var cp = store.LoadPhase(phaseId);
                        if (cp?.Contract is null)
                        {
                            CliRenderer.Warn($"Phase '{phaseId}' has no contract defined.", mode);
                            return;
                        }
                        var c = cp.Contract;
                        if (mode == CliOutputMode.Plain)
                        {
                            Console.WriteLine($"Contract: {phaseId}");
                            Console.WriteLine($"  Objective: {c.Objective}");
                            Console.WriteLine($"  Scope: {c.Scope}");
                            if (!string.IsNullOrWhiteSpace(c.OutOfScope))
                                Console.WriteLine($"  Out of scope: {c.OutOfScope}");
                            if (c.AcceptanceCriteria.Count > 0)
                            {
                                Console.WriteLine("  Acceptance criteria:");
                                foreach (var ac in c.AcceptanceCriteria) Console.WriteLine($"    - {ac}");
                            }
                            if (c.ArchitectureConstraints.Count > 0)
                            {
                                Console.WriteLine("  Architecture constraints:");
                                foreach (var ac in c.ArchitectureConstraints) Console.WriteLine($"    - {ac}");
                            }
                            if (c.RequiredFilesOrAreas.Count > 0)
                            {
                                Console.WriteLine("  Required files/areas:");
                                foreach (var f in c.RequiredFilesOrAreas) Console.WriteLine($"    - {f}");
                            }
                            if (!string.IsNullOrWhiteSpace(c.ImplementationNotes))
                                Console.WriteLine($"  Implementation notes: {c.ImplementationNotes}");
                            if (!string.IsNullOrWhiteSpace(c.AuditRequirements))
                                Console.WriteLine($"  Audit requirements: {c.AuditRequirements}");
                            if (!string.IsNullOrWhiteSpace(c.ValidationRequirements))
                                Console.WriteLine($"  Validation requirements: {c.ValidationRequirements}");
                            Console.WriteLine($"  Requires validation: {c.RequiresValidation}");
                        }
                        else
                        {
                            Spectre.Console.AnsiConsole.MarkupLine($"[bold cyan]Contract: {Spectre.Console.Markup.Escape(phaseId)}[/]");
                            Spectre.Console.AnsiConsole.MarkupLine($"[bold]Objective:[/] {Spectre.Console.Markup.Escape(c.Objective)}");
                            Spectre.Console.AnsiConsole.MarkupLine($"[bold]Scope:[/] {Spectre.Console.Markup.Escape(c.Scope)}");
                            if (!string.IsNullOrWhiteSpace(c.OutOfScope))
                                Spectre.Console.AnsiConsole.MarkupLine($"[bold]Out of scope:[/] {Spectre.Console.Markup.Escape(c.OutOfScope)}");
                            foreach (var ac in c.AcceptanceCriteria)
                                Spectre.Console.AnsiConsole.MarkupLine($"  [green]✓[/] {Spectre.Console.Markup.Escape(ac)}");
                        }
                        break;
                    }

                    case "save":
                    case "set":
                    case "edit":
                    {
                        var cObjective   = ParseFlag(cmdArgs, "--objective");
                        var cScope       = ParseFlag(cmdArgs, "--scope");
                        var cOutOfScope  = ParseFlag(cmdArgs, "--out-of-scope");
                        var cImplNotes   = ParseFlag(cmdArgs, "--notes");
                        var cAuditReqs   = ParseFlag(cmdArgs, "--audit-requirements");
                        var cValReqs     = ParseFlag(cmdArgs, "--validation-requirements");
                        var cReqFiles    = ParseFlag(cmdArgs, "--required-files");   // comma-separated
                        var cCriteria    = ParseFlag(cmdArgs, "--criteria");         // comma-separated
                        var cConstraints = ParseFlag(cmdArgs, "--constraints");      // comma-separated
                        var cExecutionLanes = ParseFlag(cmdArgs, "--execution-lanes-json");

                        if (string.IsNullOrWhiteSpace(cObjective) && string.IsNullOrWhiteSpace(cScope))
                        {
                            CliRenderer.Error("At least --objective and --scope are required to save a contract.", mode);
                            Console.Error.WriteLine("  bfwf phase contract save --phase PHASE-NNN --objective \"...\" --scope \"...\"");
                            Environment.Exit(2);
                        }

                        var contractParams = new Dictionary<string, object?>();
                        if (!string.IsNullOrWhiteSpace(cObjective))   contractParams["objective"]              = cObjective;
                        if (!string.IsNullOrWhiteSpace(cScope))       contractParams["scope"]                  = cScope;
                        if (!string.IsNullOrWhiteSpace(cOutOfScope))  contractParams["outOfScope"]             = cOutOfScope;
                        if (!string.IsNullOrWhiteSpace(cImplNotes))   contractParams["implementationNotes"]    = cImplNotes;
                        if (!string.IsNullOrWhiteSpace(cAuditReqs))   contractParams["auditRequirements"]      = cAuditReqs;
                        if (!string.IsNullOrWhiteSpace(cValReqs))     contractParams["validationRequirements"] = cValReqs;
                        if (!string.IsNullOrWhiteSpace(cReqFiles))
                            contractParams["requiredFilesOrAreas"] = cReqFiles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        if (!string.IsNullOrWhiteSpace(cCriteria))
                            contractParams["acceptanceCriteria"] = cCriteria.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        if (!string.IsNullOrWhiteSpace(cConstraints))
                            contractParams["architectureConstraints"] = cConstraints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        if (!string.IsNullOrWhiteSpace(cExecutionLanes))
                            contractParams["executionLanesJson"] = cExecutionLanes;

                        var saveResult = dispatcher.Dispatch(new OperationContext
                        {
                            Operation  = WorkflowOperations.SavePhaseContract,
                            PhaseId    = phaseId,
                            Actor      = WorkflowActor.Implementer,
                            Parameters = contractParams
                        });

                        if (saveResult.Success)
                            CliRenderer.Ok($"Contract saved for {phaseId}.", mode);
                        else
                        {
                            CliRenderer.Error($"[{saveResult.ErrorCode}]: {saveResult.Message}", mode);
                            Environment.Exit(1);
                        }
                        break;
                    }

                    default:
                        CliRenderer.Error($"Unknown contract sub-command: '{contractSubCmd}'. Use: show | save", mode);
                        Environment.Exit(2);
                        break;
                }
                break;
            }

            default:
                CliRenderer.Error($"Unknown phase sub-command: {subCmd}", mode);
                Console.Error.WriteLine("Usage: bfwf phase <show|list|contract|check-conflicts|status|assign|update|remove> [...]");
                Environment.Exit(1);
                break;
        }
    }

    internal static void AddIfPresent(Dictionary<string, object?> parameters, string key, string? value)
    {
        if (value is not null)
            parameters[key] = value;
    }
}
