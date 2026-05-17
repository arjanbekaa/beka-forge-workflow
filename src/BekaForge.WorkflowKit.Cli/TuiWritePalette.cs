using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Storage;
using Terminal.Gui;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Safe write-mode command palette for the TUI.
///
/// Activated by Ctrl+W from anywhere in the TUI.
///
/// Design invariants:
///   - Every write dispatches through <see cref="OperationDispatcher.Dispatch"/>.
///   - No direct .workflowkit file writes ever occur here.
///   - Phase state transitions are validated server-side; the palette only
///     presents states that are plausibly valid given the current state.
///   - The palette is entirely modal; it never mutates state in the background.
///   - After the palette closes, TuiApp reloads all panels from source.
/// </summary>
internal static class WritePalette
{
    // -- Entry point -----------------------------------------------------------

    public static void Show(
        Phase? selectedPhase,
        WorkflowState? workflow,
        OperationDispatcher? dispatcher,
        string workflowRoot)
    {
        if (dispatcher is null) return;

        var commands = BuildCommands(selectedPhase, workflow);
        int index = PickCommand(commands);
        if (index < 0) return;

        commands[index].Run(selectedPhase, workflow, dispatcher);
    }

    // -- Command picker dialog -------------------------------------------------

    private static int PickCommand(List<PaletteCommand> commands)
    {
        if (commands.Count == 0)
        {
            MessageBox.Query("Write Mode", "No commands available for the current context.", "OK");
            return -1;
        }

        int selected = -1;
        bool confirmed = false;

        var items = commands.Select(c => $"  {c.Icon}  {c.Label}").ToList();
        var listView = new ListView(items)
        {
            X = 1, Y = 3,
            Width = Dim.Fill() - 2,
            Height = Math.Min(commands.Count, 12),
            AllowsMarking = false
        };

        var btnRun    = new Button("Run", true);
        var btnCancel = new Button("Cancel");

        btnRun.Clicked += () =>
        {
            selected = listView.SelectedItem;
            confirmed = true;
            Application.RequestStop();
        };
        btnCancel.Clicked += () => Application.RequestStop();

        // Enter on a list row also confirms
        listView.OpenSelectedItem += _ =>
        {
            selected  = listView.SelectedItem;
            confirmed = true;
            Application.RequestStop();
        };

        var dlg = new Dialog("Write Mode  Command Palette", btnRun, btnCancel)
        {
            Width  = 66,
            Height = Math.Min(commands.Count, 12) + 9
        };
        dlg.Add(
            new Label("[!] Write mode - all changes are dispatched through handlers and logged.")
            {
                X = 1, Y = 0,
                Width = Dim.Fill() - 2
            },
            new Label("Press Escape or Cancel to return to read-only view.")
            {
                X = 1, Y = 1,
                Width = Dim.Fill() - 2
            },
            new Label("Command:") { X = 1, Y = 2 },
            listView);

        Application.Run(dlg);
        return confirmed ? selected : -1;
    }

    // -- Command registry ------------------------------------------------------

    private static List<PaletteCommand> BuildCommands(Phase? phase, WorkflowState? workflow)
    {
        var list = new List<PaletteCommand>();

        if (phase is not null)
        {
            // Phase state transition — only when valid next states exist
            var nextStates = TuiViewHelpers.ValidNextStates(phase.State);
            if (nextStates.Count > 0)
                list.Add(new PaletteCommand(">", "Transition phase state",
                    (p, wf, d) => ExecTransitionPhase(p!, d)));

            // Implementation log — during or just after implementation
            if (phase.State is PhaseState.InImplementation or PhaseState.ImplementationLogged)
                list.Add(new PaletteCommand("*", "Log implementation",
                    (p, wf, d) => ExecLogImplementation(p!, d)));

            // Audit log
            if (phase.State is PhaseState.ImplementationLogged or PhaseState.AuditLogged)
                list.Add(new PaletteCommand("~", "Log audit",
                    (p, wf, d) => ExecLogAuditDetailed(p!, d)));

            // Review log
            if (phase.State is PhaseState.AuditLogged
                or PhaseState.ReadyForReview
                or PhaseState.ReviewInProgress)
                list.Add(new PaletteCommand("^", "Log review",
                    (p, wf, d) => ExecLogReviewDetailed(p!, d)));

            // Sub-phase status
            if (phase.SubPhases.Count > 0)
                list.Add(new PaletteCommand("o", "Update sub-phase status",
                    (p, wf, d) => ExecUpdateSubPhase(p!, d)));

            // Record blocker (available from any active state)
            if (!TuiViewHelpers.IsTerminalState(phase.State))
                list.Add(new PaletteCommand("!", "Record blocker",
                    (p, wf, d) => ExecRecordBlocker(p!, d)));

            // Set next action — planning metadata, always available
            list.Add(new PaletteCommand("@", "Set next action",
                (p, wf, d) => ExecSetNextAction(p!, wf, d)));

            // Assign phase
            list.Add(new PaletteCommand("#", "Assign phase to agent",
                (p, wf, d) => ExecAssignPhase(p!, d)));
        }

        // Always available regardless of selection
        list.Add(new PaletteCommand("+", "Process inbox (run pending operations)",
            (p, wf, d) => ExecProcessInbox(d)));
        list.Add(new PaletteCommand("=", "Sync markdown (regenerate docs)",
            (p, wf, d) => ExecSyncMarkdown(d)));

        return list;
    }

    // -- Executors -------------------------------------------------------------

    private static void ExecTransitionPhase(Phase phase, OperationDispatcher dispatcher)
    {
        var nextStates = TuiViewHelpers.ValidNextStates(phase.State).Select(s => s.ToString()).ToList();
        if (nextStates.Count == 0) return;

        int choice = PickFromList(
            $"Transition {phase.PhaseId}",
            $"Current state: {phase.State}\nChoose next state:",
            nextStates);
        if (choice < 0) return;

        var targetState = nextStates[choice];
        string? blockerReason = null;

        if (targetState is "Blocked" || targetState.StartsWith("Failed"))
        {
            blockerReason = PromptText(
                $"Transition {phase.PhaseId}",
                "Reason / blocker detail:",
                "");
            if (blockerReason is null) return; // cancelled
        }

        var parameters = new Dictionary<string, object?> { ["state"] = targetState };
        if (!string.IsNullOrWhiteSpace(blockerReason))
            parameters["blockerReason"] = blockerReason;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.UpdatePhaseStatus,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = parameters
        });

        ShowResult("Transition Phase", result);
    }

    private static void ExecLogImplementation(Phase phase, OperationDispatcher dispatcher)
    {
        var summary = PromptText($"Log Implementation — {phase.PhaseId}", "Summary:", "");
        if (summary is null) return;

        var notes = PromptText($"Log Implementation — {phase.PhaseId}", "Notes (optional — press OK to skip):", "");
        if (notes is null) return; // cancelled

        var parameters = new Dictionary<string, object?> { ["summary"] = summary };
        if (!string.IsNullOrWhiteSpace(notes))
            parameters["notes"] = notes;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.CreateImplementationLog,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = parameters
        });

        ShowResult("Log Implementation", result);
    }

    private static void ExecLogAudit(Phase phase, OperationDispatcher dispatcher)
    {
        var summary = PromptText($"Log Audit — {phase.PhaseId}", "Summary:", "");
        if (summary is null) return;

        int passedChoice = PickFromList($"Log Audit — {phase.PhaseId}", "Passed?", ["Yes", "No"]);
        if (passedChoice < 0) return;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.CreateAuditLog,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?>
            {
                ["summary"] = summary,
                ["passed"]  = passedChoice == 0
            }
        });

        ShowResult("Log Audit", result);
    }

    private static void ExecLogReview(Phase phase, OperationDispatcher dispatcher)
    {
        var summary = PromptText($"Log Review — {phase.PhaseId}", "Summary:", "");
        if (summary is null) return;

        int passedChoice = PickFromList($"Log Review — {phase.PhaseId}", "Passed?", ["Yes", "No"]);
        if (passedChoice < 0) return;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.CreateReviewLog,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?>
            {
                ["summary"] = summary,
                ["passed"]  = passedChoice == 0
            }
        });

        ShowResult("Log Review", result);
    }

    private static void ExecLogAuditDetailed(Phase phase, OperationDispatcher dispatcher)
    {
        var summary = PromptText($"Log Audit - {phase.PhaseId}", "Summary:", "");
        if (summary is null) return;

        var criticalParts = PromptText($"Log Audit - {phase.PhaseId}",
            "Critical parts checked (optional):", "");
        if (criticalParts is null) return;

        var risks = PromptText($"Log Audit - {phase.PhaseId}",
            "Potential risks (optional):", "");
        if (risks is null) return;

        var issues = PromptText($"Log Audit - {phase.PhaseId}",
            "Issues found (use ';' between items, optional unless failing):", "");
        if (issues is null) return;

        var recommendations = PromptText($"Log Audit - {phase.PhaseId}",
            "Recommendations (use ';' between items, optional):", "");
        if (recommendations is null) return;

        int passedChoice = PickFromList($"Log Audit - {phase.PhaseId}", "Passed?", ["Yes", "No"]);
        if (passedChoice < 0) return;

        var notes = BuildAssessmentNotes(criticalParts, risks);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.CreateAuditLog,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?>
            {
                ["summary"] = summary,
                ["passed"] = passedChoice == 0,
                ["notes"] = notes,
                ["issues"] = issues,
                ["recommendations"] = recommendations
            }
        });

        ShowResult("Log Audit", result);
    }

    private static void ExecLogReviewDetailed(Phase phase, OperationDispatcher dispatcher)
    {
        var summary = PromptText($"Log Review - {phase.PhaseId}", "Summary:", "");
        if (summary is null) return;

        var criticalParts = PromptText($"Log Review - {phase.PhaseId}",
            "Critical parts checked (optional):", "");
        if (criticalParts is null) return;

        var risks = PromptText($"Log Review - {phase.PhaseId}",
            "Potential risks (optional):", "");
        if (risks is null) return;

        var issues = PromptText($"Log Review - {phase.PhaseId}",
            "Issues found (use ';' between items, required when fixes are needed):", "");
        if (issues is null) return;

        var recommendations = PromptText($"Log Review - {phase.PhaseId}",
            "Recommendations (use ';' between items, optional):", "");
        if (recommendations is null) return;

        int decisionChoice = PickFromList(
            $"Log Review - {phase.PhaseId}",
            "Decision:",
            ["Pass", "Requires fix"]);
        if (decisionChoice < 0) return;

        var passed = decisionChoice == 0;
        var requiresFix = !passed;
        var notes = BuildAssessmentNotes(criticalParts, risks);

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.CreateReviewLog,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?>
            {
                ["summary"] = summary,
                ["passed"] = passed,
                ["requiresFix"] = requiresFix,
                ["notes"] = notes,
                ["issues"] = issues,
                ["recommendations"] = recommendations
            }
        });

        ShowResult("Log Review", result);
    }

    private static void ExecUpdateSubPhase(Phase phase, OperationDispatcher dispatcher)
    {
        var subOptions = phase.SubPhases
            .Select(sp => $"{sp.SubPhaseId,-16}  [{sp.Status,-10}]  {sp.Title.Truncate(28)}")
            .ToList();

        int subChoice = PickFromList(
            $"Update Sub-phase — {phase.PhaseId}",
            "Select sub-phase:",
            subOptions);
        if (subChoice < 0) return;

        var sp = phase.SubPhases[subChoice];
        var statusOptions = new List<string> { "InProgress", "Completed", "Blocked", "Deferred" };

        int statusChoice = PickFromList(
            $"Sub-phase: {sp.SubPhaseId}",
            $"Current: {sp.Status}\nNew status:",
            statusOptions);
        if (statusChoice < 0) return;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.UpdateSubPhaseStatus,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?>
            {
                ["subPhaseId"] = sp.SubPhaseId,
                ["status"]     = statusOptions[statusChoice]
            }
        });

        ShowResult("Update Sub-phase", result);
    }

    private static void ExecRecordBlocker(Phase phase, OperationDispatcher dispatcher)
    {
        var reason = PromptText($"Record Blocker — {phase.PhaseId}", "Blocker reason:", "");
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.RecordBlocker,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?> { ["reason"] = reason }
        });

        ShowResult("Record Blocker", result);
    }

    private static void ExecSetNextAction(Phase phase, WorkflowState? workflow, OperationDispatcher dispatcher)
    {
        var actorNames = Enum.GetNames<WorkflowActor>().ToList();
        int actorChoice = PickFromList(
            $"Set Next Action — {phase.PhaseId}",
            "Assign to actor:",
            actorNames);
        if (actorChoice < 0) return;

        // Pre-fill with existing description if this is already the current next-action phase
        var existing = string.Equals(workflow?.NextAction?.PhaseId, phase.PhaseId,
            StringComparison.OrdinalIgnoreCase)
            ? workflow!.NextAction!.Description
            : "";

        var description = PromptText(
            $"Set Next Action — {phase.PhaseId}",
            "Description:",
            existing);
        if (description is null) return;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.SetNextAction,
            PhaseId   = phase.PhaseId,
            Actor     = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?>
            {
                ["actor"]       = actorNames[actorChoice],
                ["description"] = description,
                ["phaseId"]     = phase.PhaseId
            }
        });

        ShowResult("Set Next Action", result);
    }

    private static void ExecAssignPhase(Phase phase, OperationDispatcher dispatcher)
    {
        var actorNames = Enum.GetNames<WorkflowActor>().ToList();
        int choice = PickFromList(
            $"Assign {phase.PhaseId}",
            "Assign to agent:",
            actorNames);
        if (choice < 0) return;

        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation  = WorkflowOperations.AssignPhase,
            PhaseId    = phase.PhaseId,
            Actor      = WorkflowActor.HumanOwner,
            Parameters = new Dictionary<string, object?> { ["agent"] = actorNames[choice] }
        });

        ShowResult("Assign Phase", result);
    }

    private static void ExecProcessInbox(OperationDispatcher dispatcher)
    {
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.ProcessInbox,
            Actor     = WorkflowActor.HumanOwner
        });
        ShowResult("Process Inbox", result);
    }

    private static void ExecSyncMarkdown(OperationDispatcher dispatcher)
    {
        var result = dispatcher.Dispatch(new OperationContext
        {
            Operation = WorkflowOperations.SyncMarkdown,
            Actor     = WorkflowActor.HumanOwner
        });
        ShowResult("Sync Markdown", result);
    }

    // -- Dialog helpers --------------------------------------------------------

    /// <summary>Shows a single-line text input dialog. Returns null if cancelled.</summary>
    private static string? PromptText(string title, string label, string defaultValue)
    {
        string? result = null;
        bool confirmed = false;

        var tf = new TextField(defaultValue)
        {
            X = 1, Y = 2,
            Width = Dim.Fill() - 2
        };

        var btnOk     = new Button("OK", true);
        var btnCancel = new Button("Cancel");

        btnOk.Clicked += () =>
        {
            result    = tf.Text?.ToString() ?? "";
            confirmed = true;
            Application.RequestStop();
        };
        btnCancel.Clicked += () => Application.RequestStop();

        var dlg = new Dialog(title, btnOk, btnCancel) { Width = 66, Height = 9 };
        dlg.Add(new Label(label) { X = 1, Y = 1 }, tf);
        Application.Run(dlg);

        return confirmed ? result : null;
    }

    /// <summary>Shows a list-picker dialog. Returns selected index, or -1 if cancelled.</summary>
    private static int PickFromList(string title, string prompt, IReadOnlyList<string> options)
    {
        if (options.Count == 0) return -1;

        int selected = -1;
        bool confirmed = false;
        int promptLines = prompt.Count(c => c == '\n') + 1;

        var listView = new ListView(options.ToList())
        {
            X = 1, Y = 1 + promptLines,
            Width  = Dim.Fill() - 2,
            Height = Math.Min(options.Count, 10),
            AllowsMarking = false
        };

        var btnOk     = new Button("OK", true);
        var btnCancel = new Button("Cancel");

        btnOk.Clicked += () =>
        {
            selected  = listView.SelectedItem;
            confirmed = true;
            Application.RequestStop();
        };
        btnCancel.Clicked += () => Application.RequestStop();

        listView.OpenSelectedItem += _ =>
        {
            selected  = listView.SelectedItem;
            confirmed = true;
            Application.RequestStop();
        };

        var dlg = new Dialog(title, btnOk, btnCancel)
        {
            Width  = 66,
            Height = Math.Min(options.Count, 10) + promptLines + 6
        };

        // Add each prompt line as a separate label
        int y = 1;
        foreach (var line in prompt.Split('\n'))
            dlg.Add(new Label(line) { X = 1, Y = y++ });

        dlg.Add(listView);
        Application.Run(dlg);

        return confirmed ? selected : -1;
    }

    private static string BuildAssessmentNotes(string? criticalParts, string? risks)
    {
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(criticalParts))
            notes.Add($"Critical parts checked:\n{criticalParts}");

        if (!string.IsNullOrWhiteSpace(risks))
            notes.Add($"Potential risks:\n{risks}");

        return string.Join("\n\n", notes);
    }

    /// <summary>Shows a result dialog (success or error).</summary>
    private static void ShowResult(string title, OperationResult result)
    {
        var msg = result.Success
            ? $"OK — {result.Message ?? "Operation completed successfully."}"
            : $"ERROR [{result.ErrorCode}]:\n{result.Message ?? "Unknown error."}";

        MessageBox.Query(title, msg, "OK");
    }

    // -- Internal record -------------------------------------------------------

    private sealed record PaletteCommand(
        string Icon,
        string Label,
        Action<Phase?, WorkflowState?, OperationDispatcher> Run);
}
