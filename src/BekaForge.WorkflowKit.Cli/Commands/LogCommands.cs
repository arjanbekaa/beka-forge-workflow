using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cli;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using System.Text.Json;

partial class Program
{
    internal static void CmdLog(string logType, string? wfRoot, string? phaseId, string? logSummary, string? logNotes, string? passedStr, string? issues = null, string? recommendations = null, string? requiresFixStr = null)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
            Environment.Exit(1);
        }

        if (string.IsNullOrWhiteSpace(phaseId))
        {
            Console.Error.WriteLine("ERROR: --phase is required.");
            Environment.Exit(1);
        }

        if (string.IsNullOrWhiteSpace(logSummary))
        {
            Console.Error.WriteLine("ERROR: --summary is required.");
            Environment.Exit(1);
        }

        string operationName = logType.ToLowerInvariant() switch
        {
            "implementation" => WorkflowOperations.CreateImplementationLog,
            "audit"          => WorkflowOperations.CreateAuditLog,
            "review"         => WorkflowOperations.CreateReviewLog,
            "test"           => WorkflowOperations.CreateTestLog,
            "fix"            => WorkflowOperations.CreateFixLog,
            _ => throw new ArgumentException($"Unknown log type: {logType}. Use: implementation, audit, review, test, or fix.")
        };

        var parameters = new Dictionary<string, object?>
        {
            ["summary"] = logSummary
        };

        if (!string.IsNullOrWhiteSpace(logNotes))
            parameters["notes"] = logNotes;

        if (!string.IsNullOrWhiteSpace(passedStr) && bool.TryParse(passedStr, out var p))
            parameters["passed"] = p;

        if (!string.IsNullOrWhiteSpace(issues) &&
            logType.ToLowerInvariant() is "audit" or "review")
            parameters["issues"] = issues;

        if (!string.IsNullOrWhiteSpace(recommendations) &&
            logType.ToLowerInvariant() is "audit" or "review")
            parameters["recommendations"] = recommendations;

        if (!string.IsNullOrWhiteSpace(requiresFixStr) &&
            bool.TryParse(requiresFixStr, out var requiresFix) &&
            logType.Equals("review", StringComparison.OrdinalIgnoreCase))
            parameters["requiresFix"] = requiresFix;

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = operationName,
            PhaseId = phaseId,
            Actor = WorkflowActor.User,
            Parameters = parameters
        });

        if (result.Success)
            Console.WriteLine("OK");
        else
        {
            Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
            Environment.Exit(1);
        }
    }

    internal static void CmdValidation(string[] args, string? wfRoot)
    {
        if (wfRoot is null || !WorkflowLayout.IsInitialized(wfRoot))
        {
            Console.Error.WriteLine("ERROR: No Beka Forge Workflow project is initialized.");
            Environment.Exit(1);
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: bfwf validation <plan|log|skip|request-user|complete-user> [options]");
            Environment.Exit(1);
        }

        var subCommand = args[1].ToLowerInvariant();
        var phaseId = ParseFlag(args, "--phase");
        var summary = ParseFlag(args, "--summary");
        var validationType = ParseFlag(args, "--type");
        var validationResult = ParseFlag(args, "--result");
        var evidenceJson        = ParseFlag(args, "--evidence");
        var evidenceDescription = ParseFlag(args, "--evidence-description");
        var evidenceSource      = ParseFlag(args, "--evidence-source");
        var evidenceReference   = ParseFlag(args, "--evidence-reference");
        var skipReason = ParseFlag(args, "--reason") ?? ParseFlag(args, "--skip-reason");
        var riskNote = ParseFlag(args, "--risk-note");
        var approvedBy = ParseFlag(args, "--approved-by");
        var command = ParseFlag(args, "--command");
        var exitCodeStr = ParseFlag(args, "--exit-code");
        var manualSteps = ParseFlag(args, "--manual-steps");
        var notes = ParseFlag(args, "--notes");
        var jsonOutput = HasFlag(args, "--json");

        var store = new WorkflowStore(wfRoot);
        var dispatcher = new OperationDispatcher(store);

        switch (subCommand)
        {
            case "plan":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.GetValidationPlan,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User
                });

                if (result.Success)
                {
                    if (jsonOutput)
                        Console.WriteLine(JsonSerializer.Serialize(result.Data));
                    else if (result.Data is JsonElement data)
                    {
                        Console.WriteLine($"Validation Plan for {phaseId}");
                        Console.WriteLine(new string('-', 40));
                        Console.WriteLine($"Validation Required: {data.GetProperty("validationRequired")}");

                        if (data.TryGetProperty("message", out var msg))
                        {
                            Console.WriteLine(msg.GetString());
                        }
                        else
                        {
                            Console.WriteLine($"Requirements: {data.GetProperty("validationRequirements").GetString()}");
                            Console.WriteLine();
                            Console.WriteLine("Agent can test:");
                            foreach (var item in data.GetProperty("agentCanTest").EnumerateArray())
                                Console.WriteLine($"  - {item.GetString()}");
                            Console.WriteLine();
                            Console.WriteLine("Requires user:");
                            foreach (var item in data.GetProperty("requiresUser").EnumerateArray())
                                Console.WriteLine($"  - {item.GetString()}");
                            Console.WriteLine();
                            Console.WriteLine("Manual test steps:");
                            foreach (var item in data.GetProperty("manualTestSteps").EnumerateArray())
                                Console.WriteLine($"  {item.GetString()}");
                            Console.WriteLine();
                            Console.WriteLine($"Next steps: {data.GetProperty("nextSteps").GetString()}");
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("ERROR: Validation plan returned no structured data.");
                        Environment.Exit(1);
                    }
                }
                else
                {
                    Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
                    Environment.Exit(1);
                }
                break;
            }

            case "log":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(summary))
                {
                    Console.Error.WriteLine("ERROR: --summary is required.");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(validationType))
                {
                    Console.Error.WriteLine("ERROR: --type is required (AutomatedCommand, StaticInspection, BrowserManual, UnityManual, UnityAutomated, HumanValidationRequired, SkippedNotNeeded, SkippedNotPossible, SkippedByUserOverride, LegacyTest).");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(validationResult))
                {
                    Console.Error.WriteLine("ERROR: --result is required (Passed, PassedWithWarnings, Failed, Skipped, PendingHumanValidation).");
                    Environment.Exit(1);
                }

                var parameters = new Dictionary<string, object?>
                {
                    ["summary"] = summary,
                    ["validationType"] = validationType,
                    ["validationResult"] = validationResult
                };

                var resolvedEvidenceJson = evidenceJson
                    ?? (evidenceDescription is not null
                        ? BuildEvidenceJson(evidenceDescription, evidenceSource, evidenceReference)
                        : null);
                if (resolvedEvidenceJson is not null)
                    parameters["evidenceItems"] = resolvedEvidenceJson;

                if (!string.IsNullOrWhiteSpace(command))
                    parameters["command"] = command;

                if (!string.IsNullOrWhiteSpace(exitCodeStr) && int.TryParse(exitCodeStr, out var ec))
                    parameters["exitCode"] = ec;

                if (!string.IsNullOrWhiteSpace(manualSteps))
                    parameters["manualSteps"] = manualSteps;

                if (!string.IsNullOrWhiteSpace(skipReason))
                    parameters["skipReason"] = skipReason;

                if (!string.IsNullOrWhiteSpace(riskNote))
                    parameters["riskNote"] = riskNote;

                if (!string.IsNullOrWhiteSpace(approvedBy))
                    parameters["approvedBy"] = approvedBy;

                if (!string.IsNullOrWhiteSpace(notes))
                    parameters["notes"] = notes;

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.CreateValidationLog,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = parameters
                });

                if (result.Success)
                {
                    if (jsonOutput)
                        Console.WriteLine(JsonSerializer.Serialize(result.Data));
                    else
                        Console.WriteLine("Validation logged successfully.");
                }
                else
                {
                    Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
                    Environment.Exit(1);
                }
                break;
            }

            case "skip":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(skipReason))
                {
                    Console.Error.WriteLine("ERROR: --reason is required for skip.");
                    Environment.Exit(1);
                }

                var parameters = new Dictionary<string, object?>
                {
                    ["skipReason"] = skipReason,
                    ["summary"] = summary ?? $"Validation skipped for {phaseId}",
                    ["validationType"] = string.IsNullOrWhiteSpace(approvedBy) ? "SkippedNotNeeded" : "SkippedByUserOverride"
                };

                if (!string.IsNullOrWhiteSpace(approvedBy))
                    parameters["approvedBy"] = approvedBy;

                if (!string.IsNullOrWhiteSpace(notes))
                    parameters["notes"] = notes;

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.SkipValidation,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = parameters
                });

                if (result.Success)
                {
                    if (jsonOutput)
                        Console.WriteLine(JsonSerializer.Serialize(result.Data));
                    else
                        Console.WriteLine("Validation skipped successfully.");
                }
                else
                {
                    Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
                    Environment.Exit(1);
                }
                break;
            }

            case "request-user":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }

                var parameters = new Dictionary<string, object?>
                {
                    ["summary"] = summary ?? $"Manual validation requested for {phaseId}"
                };

                if (!string.IsNullOrWhiteSpace(manualSteps))
                    parameters["manualSteps"] = manualSteps;

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.RequestUserValidation,
                    PhaseId = phaseId,
                    Actor = WorkflowActor.User,
                    Parameters = parameters
                });

                if (result.Success)
                {
                    if (jsonOutput)
                        Console.WriteLine(JsonSerializer.Serialize(result.Data));
                    else if (result.Data is JsonElement data)
                    {
                        if (data.TryGetProperty("userMessage", out var userMsg))
                            Console.WriteLine(userMsg.GetString());
                        else
                            Console.WriteLine("User validation requested.");
                    }
                    else
                    {
                        Console.WriteLine("User validation requested.");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
                    Environment.Exit(1);
                }
                break;
            }

            case "complete-user":
            {
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(validationResult))
                {
                    Console.Error.WriteLine("ERROR: --result is required (Passed, PassedWithWarnings, Failed).");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(summary))
                {
                    Console.Error.WriteLine("ERROR: --summary is required.");
                    Environment.Exit(1);
                }

                var parameters = new Dictionary<string, object?>
                {
                    ["validationResult"] = validationResult,
                    ["summary"]          = summary
                };

                var resolvedEvidenceJsonUser = evidenceJson
                    ?? (evidenceDescription is not null
                        ? BuildEvidenceJson(evidenceDescription, evidenceSource ?? "HumanOwner", evidenceReference)
                        : null);
                if (resolvedEvidenceJsonUser is not null)
                    parameters["evidenceItems"] = resolvedEvidenceJsonUser;

                if (!string.IsNullOrWhiteSpace(notes))
                    parameters["notes"] = notes;

                // Optional reference to the pending validation record being completed.
                var pendingId = ParseFlag(args, "--pending-id");
                if (!string.IsNullOrWhiteSpace(pendingId))
                    parameters["pendingValidationId"] = pendingId;

                var result = dispatcher.Dispatch(new OperationContext
                {
                    Operation  = WorkflowOperations.CompleteUserValidation,
                    PhaseId    = phaseId,
                    Actor      = WorkflowActor.HumanOwner,
                    Parameters = parameters
                });

                if (result.Success)
                {
                    if (jsonOutput)
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Data));
                    else
                        Console.WriteLine("Human validation completed and logged.");
                }
                else
                {
                    Console.Error.WriteLine($"ERROR [{result.ErrorCode}]: {result.Message}");
                    Environment.Exit(1);
                }
                break;
            }

            case "run":
            {
                // PHASE-004: Command runner with evidence capture.
                if (string.IsNullOrWhiteSpace(phaseId))
                {
                    Console.Error.WriteLine("ERROR: --phase is required.");
                    Environment.Exit(1);
                }
                if (string.IsNullOrWhiteSpace(command))
                {
                    Console.Error.WriteLine("ERROR: --command is required. Example: --command \"dotnet test\"");
                    Environment.Exit(1);
                }

                // Resolve timeout
                int timeoutSec = ParseIntFlag(args, "--timeout") ?? 120;
                bool autoAdvance = HasFlag(args, "--advance");
                string runType = validationType ?? "AutomatedCommand";

                // Optionally auto-advance to TestInProgress
                if (autoAdvance)
                {
                    var phase = store.LoadPhase(phaseId);
                    if (phase is not null)
                    {
                        if (phase.State == PhaseState.ReadyForTest)
                        {
                            var adv = dispatcher.Dispatch(new OperationContext
                            {
                                Operation = WorkflowOperations.UpdatePhaseStatus,
                                PhaseId   = phaseId,
                                Actor     = WorkflowActor.Implementer,
                                Parameters = new Dictionary<string, object?> { ["state"] = "TestInProgress" }
                            });
                            if (!adv.Success)
                            {
                                Console.Error.WriteLine($"ERROR advancing to TestInProgress: {adv.ErrorCode}: {adv.Message}");
                                Environment.Exit(1);
                            }
                        }
                    }
                }

                Console.WriteLine($"Running: {command}");
                Console.WriteLine($"Timeout: {timeoutSec}s");
                Console.WriteLine();

                var runResult = CommandRunner.RunAsync(command, timeoutSec, wfRoot).GetAwaiter().GetResult();

                // Print live output summary
                Console.WriteLine($"Exit code: {runResult.ExitCode} ({(runResult.Succeeded ? "SUCCESS" : runResult.TimedOut ? "TIMED OUT" : "FAILED")})");
                Console.WriteLine($"Duration:  {runResult.Duration.TotalSeconds:F2}s");
                Console.WriteLine();

                if (!string.IsNullOrWhiteSpace(runResult.StdOut))
                {
                    Console.WriteLine("--- STDOUT ---");
                    Console.WriteLine(runResult.StdOut.TrimEnd());
                    Console.WriteLine();
                }
                if (!string.IsNullOrWhiteSpace(runResult.StdErr))
                {
                    Console.WriteLine("--- STDERR ---");
                    Console.WriteLine(runResult.StdErr.TrimEnd());
                    Console.WriteLine();
                }

                // Save evidence artifact
                var collectedUtc = DateTimeOffset.UtcNow;
                var artifactTimestamp = collectedUtc.ToString("yyyyMMdd-HHmmss");
                var artifactId = Guid.NewGuid().ToString("N")[..8];
                var artifactPath = WorkflowLayout.EvidenceArtifactPath(wfRoot, phaseId, artifactTimestamp, artifactId);
                var artifactDir = Path.GetDirectoryName(artifactPath)!;

                if (!Directory.Exists(artifactDir))
                    Directory.CreateDirectory(artifactDir);

                var artifactContent = CommandRunner.FormatArtifact(runResult, phaseId, collectedUtc);
                File.WriteAllText(artifactPath, artifactContent, System.Text.Encoding.UTF8);
                Console.WriteLine($"Evidence artifact saved: {artifactPath}");

                // Build evidence JSON and log the validation result via dispatcher
                string resultStr = runResult.Succeeded ? "Passed" : "Failed";
                string evidenceSummary = runResult.TimedOut
                    ? $"Command timed out after {timeoutSec}s"
                    : $"Exit code {runResult.ExitCode} after {runResult.Duration.TotalSeconds:F2}s";

                var evidenceItemsJson = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new { description = evidenceSummary, source = 2 /* Command */, reference = artifactPath }
                });

                string runSummary = summary ?? (runResult.Succeeded
                    ? $"Command passed: {command}"
                    : $"Command failed (exit {runResult.ExitCode}): {command}");

                var logParams = new Dictionary<string, object?>
                {
                    ["summary"]          = runSummary,
                    ["validationType"]   = runType,
                    ["validationResult"] = resultStr,
                    ["evidenceItems"]    = evidenceItemsJson,
                    ["command"]          = command,
                    ["exitCode"]         = runResult.ExitCode
                };

                var logResult = dispatcher.Dispatch(new OperationContext
                {
                    Operation  = WorkflowOperations.CreateValidationLog,
                    PhaseId    = phaseId,
                    Actor      = WorkflowActor.Implementer,
                    Parameters = logParams
                });

                if (logResult.Success)
                    Console.WriteLine($"Validation logged: {resultStr}");
                else
                {
                    Console.Error.WriteLine($"WARNING: Could not log validation: {logResult.ErrorCode}: {logResult.Message}");
                    // Exit with the command's exit code even if logging failed
                }

                Environment.Exit(runResult.Succeeded ? 0 : 1);
                break;
            }

            default:
                Console.Error.WriteLine($"Unknown validation sub-command: {subCommand}");
                Console.Error.WriteLine("Usage: bfwf validation <plan|log|run|skip|request-user|complete-user> [options]");
                Environment.Exit(1);
                break;
        }
    }

    // Builds a single-item evidence JSON array from named flags.
    // Source string maps to EvidenceSource enum names: Agent(0), HumanOwner(1), Command(2), Tool(3), CI(4).
    internal static string BuildEvidenceJson(string description, string? sourceName = null, string? reference = null)
    {
        int sourceInt = (sourceName?.ToLowerInvariant()) switch
        {
            "humanowner" or "human" => 1,
            "command"               => 2,
            "tool"                  => 3,
            "ci"                    => 4,
            _                       => 0  // Agent (default)
        };

        var item = new { description, source = sourceInt, reference = reference ?? "" };
        return JsonSerializer.Serialize(new[] { item });
    }
}
