using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Cache;
using BekaForge.WorkflowKit.Core;
using BekaForge.WorkflowKit.Server;
using BekaForge.WorkflowKit.Server.Handlers;
using BekaForge.WorkflowKit.Storage;
using Terminal.Gui;

namespace BekaForge.WorkflowKit.Cli;

/// <summary>
/// Interactive terminal dashboard entry point.
///
/// Displays workflow status, phase details, recent activity, diagnostics,
/// and local server controls in a keyboard-driven terminal layout.
/// </summary>
public static class TuiCommand
{
    /// <summary>Entry point called from Program.cs for the <c>bfwf tui</c> command.</summary>
    public static void Run(string startDir, int refreshIntervalSeconds, bool allowAncestorDiscovery = true)
    {
        var discoveredRoot = TuiBootstrap.DiscoverWorkflowRoot(startDir, allowAncestorDiscovery);

        var probe = TuiCapabilityProbe.Check();
        if (!probe.CanRun)
        {
            var bootstrap = discoveredRoot is null
                ? TuiBootstrap.EnsureWorkflowReady(
                    startDir,
                    Console.In,
                    Console.Out,
                    allowAncestorDiscovery)
                : new TuiBootstrapResult(discoveredRoot, false, false);

            if (bootstrap.Cancelled || string.IsNullOrWhiteSpace(bootstrap.WorkflowRoot))
                return;

            var workflowRoot = bootstrap.WorkflowRoot;
            LocalServerBootstrap.EnsureRunning(workflowRoot, Console.Out);
            Console.Error.WriteLine($"WARN: {probe.Reason} Falling back to one-shot status display.");
            FallbackWatchLoop(workflowRoot, refreshIntervalSeconds);
            return;
        }

        if (!string.IsNullOrWhiteSpace(discoveredRoot))
            LocalServerBootstrap.EnsureRunning(discoveredRoot, Console.Out);

        try
        {
            TuiApp.Run(startDir, discoveredRoot, refreshIntervalSeconds, allowAncestorDiscovery);
        }
        catch (Exception ex) when (IsTerminalCapabilityFailure(ex))
        {
            var bootstrap = discoveredRoot is null
                ? TuiBootstrap.EnsureWorkflowReady(
                    startDir,
                    Console.In,
                    Console.Out,
                    allowAncestorDiscovery)
                : new TuiBootstrapResult(discoveredRoot, false, false);

            if (bootstrap.Cancelled || string.IsNullOrWhiteSpace(bootstrap.WorkflowRoot))
                return;

            var workflowRoot = bootstrap.WorkflowRoot;
            LocalServerBootstrap.EnsureRunning(workflowRoot, Console.Out);
            Console.Error.WriteLine(
                $"WARN: TUI not supported in this terminal ({ex.Message}). " +
                "Falling back to one-shot status display.");
            FallbackWatchLoop(workflowRoot, refreshIntervalSeconds);
        }
        catch (Exception ex)
        {
            var logDir = !string.IsNullOrWhiteSpace(discoveredRoot) && WorkflowLayout.IsInitialized(discoveredRoot)
                ? Path.Combine(discoveredRoot, ".workflowkit")
                : Path.GetFullPath(startDir);
            var logPath = Path.Combine(logDir, "tui-crash.log");
            try { File.WriteAllText(logPath, $"{DateTime.UtcNow:u}\n{ex}"); } catch { }
            Console.Error.WriteLine($"TUI crashed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"Full error written to: {logPath}");
        }
    }

    private static bool IsTerminalCapabilityFailure(Exception ex) =>
        ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException;

    private static void FallbackWatchLoop(string workflowRoot, int intervalSeconds)
    {
        var store = new WorkflowStore(workflowRoot);
        var state = store.LoadWorkflow();
        var next = state.NextAction is not null
            ? $"[{state.NextAction.Actor}] {state.NextAction.Description}"
            : "(not set)";
        Console.WriteLine($"Beka Forge Workflow - {state.AssetName}");
        Console.WriteLine($"Phase:   {state.CurrentPhaseId}");
        Console.WriteLine($"Status:  {state.LastStatus}");
        Console.WriteLine($"Next:    {next}");
        Console.WriteLine($"Blockers:{state.OpenBlockerCount}");
        Console.WriteLine($"Updated: {state.UpdatedUtc:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine();
        Console.WriteLine("[TUI unavailable - use 'bfwf status --watch' for continuous monitoring.]");
    }
}

// -- TuiApp --------------------------------------------------------------------

/// <summary>
/// Terminal.Gui application host.
/// Catppuccin-inspired dark theme with server controls.
/// </summary>
internal static class TuiApp
{
    // -- Configuration ----------------------------------------------------------
    private static string _startDir = "";
    private static bool   _allowAncestorDiscovery = true;
    private static string _workflowRoot = "";
    private static bool   _workflowInitialized;
    private static int    _refreshInterval = 5;

    // -- Services ---------------------------------------------------------------
    private static WorkflowStore?        _store;
    private static OperationDispatcher?  _dispatcher;

    // -- Cached data ------------------------------------------------------------
    private static WorkflowState? _workflow;
    private static List<Phase>    _phases = [];
    private static int            _selectedPhaseIndex;
    private static bool           _layoutReady;
    private static bool           _serverRunning;
    private static ServerInstanceStatus _serverStatus =
        new(false, false, null, LocalServerBootstrap.DefaultPort);

    // -- Views ------------------------------------------------------------------
    private static Label     _headerBadge   = null!;
    private static Label     _headerLine1   = null!;
    private static Label     _headerLine2   = null!;
    private static Label     _headerLine3   = null!;
    private static ListView  _phaseListView = null!;
    private static FrameView _detailFrame   = null!;
    private static TextView  _detailTextView= null!;
    private static ListView  _activityListView = null!;
    private static Label     _diagLine1     = null!;
    private static Label     _diagLine2     = null!;

    // -- Color schemes (catppuccin-inspired, set up after Application.Init) -----
    private static ColorScheme _schemeAccent  = null!;
    private static ColorScheme _schemeBase    = null!;
    private static ColorScheme _schemeHeader  = null!;
    private static ColorScheme _schemeList    = null!;
    private static ColorScheme _schemeDetail  = null!;
    private static ColorScheme _schemeDiag    = null!;
    private static ColorScheme _schemeStatus  = null!;

    // --------------------------------------------------------------------------

    public static void Run(
        string startDir,
        string? workflowRoot,
        int refreshIntervalSeconds,
        bool allowAncestorDiscovery = true)
    {
        _startDir = Path.GetFullPath(startDir);
        _allowAncestorDiscovery = allowAncestorDiscovery;
        _refreshInterval = Math.Max(1, refreshIntervalSeconds);
        _selectedPhaseIndex = 0;

        if (!string.IsNullOrWhiteSpace(workflowRoot) && WorkflowLayout.IsInitialized(workflowRoot))
            AttachWorkflowRoot(workflowRoot);
        else
            ClearWorkflowBinding();

        using var wait = ConsoleWaitIndicator.Start(Console.Out, "Launching TUI");
        Application.Init();
        try
        {
            BuildColorSchemes();
            BuildLayout(Application.Top);
            _layoutReady = true;
            LoadAndRefresh();
            SetupRefreshTimer();
            wait.Complete(" ready.");
            Application.Run();
        }
        finally
        {
            Application.Shutdown();
        }
    }

    private static void AttachWorkflowRoot(string workflowRoot)
    {
        _workflowRoot = Path.GetFullPath(workflowRoot);
        _store = new WorkflowStore(_workflowRoot);
        _dispatcher = new OperationDispatcher(_store);
        _workflowInitialized = true;
    }

    private static void ClearWorkflowBinding()
    {
        _workflowRoot = Path.GetFullPath(_startDir);
        _store = null;
        _dispatcher = null;
        _workflow = null;
        _phases = [];
        _workflowInitialized = false;
        _serverRunning = false;
        _serverStatus = new ServerInstanceStatus(false, false, null, LocalServerBootstrap.DefaultPort);
    }

    private static bool DiscoverAndAttachWorkflow()
    {
        var discoveredRoot = TuiBootstrap.DiscoverWorkflowRoot(_startDir, _allowAncestorDiscovery);
        if (string.IsNullOrWhiteSpace(discoveredRoot))
        {
            if (_workflowInitialized)
                ClearWorkflowBinding();

            return false;
        }

        if (!_workflowInitialized ||
            !string.Equals(Path.GetFullPath(discoveredRoot), _workflowRoot, StringComparison.OrdinalIgnoreCase))
        {
            AttachWorkflowRoot(discoveredRoot);
        }

        return true;
    }

    // -- Color scheme setup (Beka Forge brand — obsidian base, forge amber accent) --
    //
    // Brand palette (~ terminal ANSI approximation):
    //   Forge orange  #E8541A  → Color.Brown
    //   Obsidian      #0E1117  → Color.Black
    //   Steel         #1C2333  → Color.Black
    //   Slate         #8892A4  → Color.Gray
    //   Ash           #F0F2F5  → Color.White
    //
    // Keep it calm — no saturated fills. The brand shows through subtle
    // forge-amber accents on hot/focused elements.

    private static void BuildColorSchemes()
    {
        // Accent: forge amber on black — used for the BF badge
        _schemeAccent = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus     = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
            HotFocus  = Application.Driver.MakeAttribute(Color.White, Color.Black),
        };

        // Base: gray text on black, forge amber hot-focus
        _schemeBase = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.Gray,  Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus     = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotFocus  = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
        };

        // Header: white text, forge amber on hot elements
        _schemeHeader = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
            Focus     = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotFocus  = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
        };

        // Phase list: gray text, forge amber selection highlight
        _schemeList = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.Gray,  Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus     = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotFocus  = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
        };

        // Detail: gray text, no bright background fills
        _schemeDetail = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.Gray,  Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus     = Application.Driver.MakeAttribute(Color.Gray,  Color.Black),
            HotFocus  = Application.Driver.MakeAttribute(Color.White, Color.Black),
        };

        // Diagnostics: gray text, forge amber accent on focus
        _schemeDiag = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.Gray,  Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus     = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotFocus  = Application.Driver.MakeAttribute(Color.Brown, Color.Black),
        };

        // Status bar: muted gray on dark-gray footer
        _schemeStatus = new ColorScheme
        {
            Normal    = Application.Driver.MakeAttribute(Color.Gray,  Color.DarkGray),
            HotNormal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
            Focus     = Application.Driver.MakeAttribute(Color.Gray,  Color.DarkGray),
            HotFocus  = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
        };
    }

    // -- Layout construction ----------------------------------------------------

    private static void BuildLayout(Toplevel top)
    {
        top.ColorScheme = _schemeBase;

        // -- Status bar ---------------------------------------------------------
        var statusBar = new StatusBar(new[]
        {
            new StatusItem(Key.Q,                        "~Q~ Quit",           () => Application.RequestStop()),
            new StatusItem(Key.R,                        "~R~ Refresh",        LoadAndRefresh),
            new StatusItem(Key.I,                        "~I~ Init",           InitializeWorkflowFromTui),
            new StatusItem(Key.S,                        "~S~ Server",         ToggleServer),
            new StatusItem(Key.B,                        "~B~ Budget",         CycleBudgetMode),
            new StatusItem(Key.T,                        "~T~ Trace",          CycleTraceMode),
            new StatusItem(Key.Null,                     "Up/Down Navigate",   null),
            new StatusItem(Key.CtrlMask | Key.W,         "~Ctrl+W~ Write",     OpenWritePalette),
        });
        statusBar.ColorScheme = _schemeStatus;

        // -- Header (4 rows: info / progress bar / next action / blank padding) -
        var headerFrame = new FrameView("  Beka Forge  ")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = 5
        };
        headerFrame.ColorScheme = _schemeHeader;

        _headerBadge = new Label("BF")
        {
            X = 1, Y = 0, Width = 4, Height = 1
        };
        _headerBadge.ColorScheme = _schemeAccent;

        _headerLine1 = new Label("Loading…")
        {
            X = 7, Y = 0, Width = Dim.Fill() - 8, Height = 1
        };
        _headerLine2 = new Label("")
        {
            X = 1, Y = 1, Width = Dim.Fill() - 2, Height = 1
        };
        _headerLine3 = new Label("")
        {
            X = 1, Y = 2, Width = Dim.Fill() - 2, Height = 1
        };
        headerFrame.Add(_headerBadge, _headerLine1, _headerLine2, _headerLine3);

        // -- Phase list (left, 38 chars wide) ----------------------------------
        int phaseListWidth = 38;
        var phaseListFrame = new FrameView(" Phases ")
        {
            X = 0, Y = 5,
            Width = phaseListWidth, Height = Dim.Fill() - 11
        };
        phaseListFrame.ColorScheme = _schemeList;

        _phaseListView = new ListView
        {
            X = 0, Y = 0,
            Width  = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false
        };
        _phaseListView.ColorScheme = _schemeList;
        _phaseListView.SelectedItemChanged += OnPhaseSelectionChanged;
        phaseListFrame.Add(_phaseListView);

        // -- Phase detail (right panel) -----------------------------------------
        _detailFrame = new FrameView(" Phase Detail ")
        {
            X = phaseListWidth, Y = 5,
            Width = Dim.Fill(), Height = Dim.Fill() - 11
        };
        _detailFrame.ColorScheme = _schemeBase;

        _detailTextView = new TextView
        {
            X = 0, Y = 0,
            Width    = Dim.Fill(),
            Height   = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false
        };
        _detailTextView.ColorScheme = _schemeDetail;
        _detailFrame.Add(_detailTextView);

        // -- Recent activity (5 rows) -------------------------------------------
        var activityFrame = new FrameView(" Recent Activity ")
        {
            X = 0, Y = Pos.Bottom(phaseListFrame),
            Width = Dim.Fill(), Height = 5
        };
        activityFrame.ColorScheme = _schemeBase;

        _activityListView = new ListView
        {
            X = 0, Y = 0,
            Width  = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false
        };
        _activityListView.ColorScheme = _schemeBase;
        activityFrame.Add(_activityListView);

        // -- Diagnostics (4 rows: 2 content lines) -----------------------------
        var diagFrame = new FrameView(" System Status ")
        {
            X = 0, Y = Pos.Bottom(activityFrame),
            Width = Dim.Fill(), Height = 4
        };
        diagFrame.ColorScheme = _schemeDiag;

        _diagLine1 = new Label("Loading…")
        {
            X = 1, Y = 0, Width = Dim.Fill() - 2, Height = 1
        };
        _diagLine2 = new Label("")
        {
            X = 1, Y = 1, Width = Dim.Fill() - 2, Height = 1
        };
        diagFrame.Add(_diagLine1, _diagLine2);

        // -- Global key handler -------------------------------------------------
        top.KeyDown += OnKeyDown;

        top.Add(headerFrame, phaseListFrame, _detailFrame, activityFrame, diagFrame, statusBar);
    }

    // -- Keyboard handling ------------------------------------------------------

    private static void OnKeyDown(View.KeyEventEventArgs args)
    {
        var key = args.KeyEvent.Key;

        if (MatchesPlainLetter(key, Key.Q, Key.q))
        {
            Application.RequestStop();
            args.Handled = true;
        }
        else if (MatchesPlainLetter(key, Key.R, Key.r))
        {
            LoadAndRefresh();
            args.Handled = true;
        }
        else if (MatchesPlainLetter(key, Key.I, Key.i))
        {
            InitializeWorkflowFromTui();
            args.Handled = true;
        }
        else if (MatchesPlainLetter(key, Key.S, Key.s))
        {
            ToggleServer();
            args.Handled = true;
        }
        else if (MatchesPlainLetter(key, Key.B, Key.b))
        {
            CycleBudgetMode();
            args.Handled = true;
        }
        else if (MatchesPlainLetter(key, Key.T, Key.t))
        {
            CycleTraceMode();
            args.Handled = true;
        }
        else if (MatchesCtrlLetter(key, Key.W, Key.w))
        {
            OpenWritePalette();
            args.Handled = true;
        }
    }

    // -- Server controls --------------------------------------------------------

    private static bool EnsureInitializedForAction(string actionName)
    {
        if (_workflowInitialized)
            return true;

        MessageBox.Query(actionName, "Initialize the workflow in this folder first with I.", "OK");
        return false;
    }

    private static void InitializeWorkflowFromTui()
    {
        if (_workflowInitialized)
        {
            MessageBox.Query(
                "Initialize Workflow",
                $"Beka Forge Workflow is already initialized for {DisplayRootName(_workflowRoot)}.",
                "OK");
            return;
        }

        try
        {
            var workflowRoot = TuiBootstrap.InitializeWorkflowInFolder(_startDir);
            AttachWorkflowRoot(workflowRoot);
            LocalServerBootstrap.EnsureRunning(_workflowRoot, TextWriter.Null);
            LoadAndRefresh();

            MessageBox.Query(
                "Initialize Workflow",
                $"Initialized Beka Forge Workflow for '{TuiBootstrap.DeriveAssetName(_startDir)}'.",
                "OK");
        }
        catch (Exception ex)
        {
            MessageBox.Query("Initialize Workflow", $"Initialization failed: {ex.Message}", "OK");
        }
    }

    private static void ToggleServer()
    {
        if (!EnsureInitializedForAction("Server"))
            return;

        Task.Run(() =>
        {
            var status = LocalServerBootstrap.GetStatus(_workflowRoot);
            if (status.IsCurrentWorkflow)
            {
                LocalServerBootstrap.StopCurrentWorkflowServer(_workflowRoot, TextWriter.Null);
            }
            else
            {
                LocalServerBootstrap.EnsureRunning(_workflowRoot, TextWriter.Null, takeOwnershipOfPort: true);
            }

            Application.MainLoop.Invoke(LoadAndRefresh);
        });
    }

    private static void CycleBudgetMode()
    {
        if (!EnsureInitializedForAction("Budget"))
            return;

        Task.Run(() =>
        {
            if (_dispatcher is null)
                return;

            var currentMode = "Medium";
            var getResult = _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.GetBudgetConfig,
                Actor = WorkflowActor.WorkflowSystem
            });

            if (getResult.Success && getResult.Data is BudgetConfigResult budget &&
                !string.IsNullOrWhiteSpace(budget.Mode))
            {
                currentMode = budget.Mode;
            }

            var nextMode = TuiViewHelpers.NextBudgetMode(currentMode);
            _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.SetBudgetConfig,
                Actor = WorkflowActor.WorkflowSystem,
                Parameters = new Dictionary<string, object?> { ["mode"] = nextMode.ToString() }
            });

            Application.MainLoop.Invoke(LoadAndRefresh);
        });
    }

    private static void CycleTraceMode()
    {
        if (!EnsureInitializedForAction("Trace"))
            return;

        Task.Run(() =>
        {
            if (_dispatcher is null)
                return;

            var statusResult = _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.GetTraceStatus,
                Actor = WorkflowActor.WorkflowSystem
            });

            var currentMode = "Off";
            var isEnabled = false;

            if (statusResult.Success)
            {
                var root = JsonSerializer.SerializeToElement(
                    statusResult.Data,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                if (root.TryGetProperty("mode", out var modeElement))
                    currentMode = modeElement.GetString() ?? currentMode;
                if (root.TryGetProperty("isEnabled", out var enabledElement))
                    isEnabled = enabledElement.GetBoolean();
            }

            var nextMode = TuiViewHelpers.NextTraceMode(currentMode, isEnabled);
            _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.SetTraceOptions,
                Actor = WorkflowActor.WorkflowSystem,
                Parameters = new Dictionary<string, object?> { ["mode"] = nextMode.ToString() }
            });

            Application.MainLoop.Invoke(LoadAndRefresh);
        });
    }

    // -- Write palette ----------------------------------------------------------

    private static void OpenWritePalette()
    {
        if (!EnsureInitializedForAction("Write Mode"))
            return;

        var phase = _selectedPhaseIndex >= 0 && _selectedPhaseIndex < _phases.Count
            ? _phases[_selectedPhaseIndex]
            : null;
        WritePalette.Show(phase, _workflow, _dispatcher, _workflowRoot);
        LoadAndRefresh();
    }

    private static void OnPhaseSelectionChanged(ListViewItemEventArgs args)
    {
        _selectedPhaseIndex = args.Item;
        if (_layoutReady)
            RefreshPhaseDetail();
    }

    // -- Data loading -----------------------------------------------------------

    private static void LoadAndRefresh()
    {
        if (!DiscoverAndAttachWorkflow())
        {
            _workflow = null;
            _phases = [];
            _serverStatus = new ServerInstanceStatus(false, false, null, LocalServerBootstrap.DefaultPort);
            _serverRunning = false;
        }

        try
        {
            if (_store is not null)
            {
                _workflow = _store.LoadWorkflow();
                _phases = _store.LoadAllPhases().OrderBy(p => p.PhaseNumber).ToList();
                _serverStatus = LocalServerBootstrap.GetStatus(_workflowRoot);
                _serverRunning = _serverStatus.IsCurrentWorkflow;
            }
        }
        catch
        {
            // Leave stale data in place on transient read errors
        }

        if (!_layoutReady) return;

        Application.MainLoop.Invoke(() =>
        {
            RefreshHeader();
            RefreshPhaseList();
            RefreshPhaseDetail();
            RefreshActivity();
            RefreshDiagnostics();
        });
    }

    private static void SetupRefreshTimer()
    {
        Application.MainLoop.AddTimeout(
            TimeSpan.FromSeconds(_refreshInterval),
            _ => { LoadAndRefresh(); return true; });
    }

    // -- Panel refresh ----------------------------------------------------------

    private static void RefreshHeader()
    {
        if (_workflow is null || !_workflowInitialized)
        {
            _headerLine1.Text = $"Folder: {TuiBootstrap.DeriveAssetName(_startDir)}    Status: NOT INITIALIZED";
            _headerLine2.Text = "Press I to initialize Beka Forge Workflow in this folder.";
            _headerLine3.Text = $"Path: {_startDir.Truncate(120)}";
            return;
        }

        var serverIndicator = _serverStatus.IsCurrentWorkflow
            ? "Server: Running"
            : _serverStatus.IsRunning
                ? $"Server: Busy ({DisplayRootName(_serverStatus.ActiveWorkflowRoot)})"
                : "Server: Stopped";
        _headerLine1.Text =
            $"Asset: {_workflow.AssetName}    " +
            $"Phase: {_workflow.CurrentPhaseId ?? "(none)"}    " +
            $"Status: {_workflow.LastStatus?.ToString().ToUpperInvariant()}    " +
            serverIndicator;

        var completedCount = _phases.Count(p => PhaseProgress.IsSuccessfulTerminal(p.State));
        var blockerCount = _workflow.OpenBlockerCount;
        var overallPct = _phases.Count > 0
            ? (int)Math.Round(_phases.Average(p => PhaseProgress.ForPhase(p)))
            : 0;
        var bar = TuiViewHelpers.ProgressBar(overallPct, 32);
        var blockerSuffix = blockerCount > 0
            ? $"   {blockerCount} blocker{(blockerCount == 1 ? "" : "s")}"
            : string.Empty;
        _headerLine2.Text =
            $"{bar}  {overallPct}%    " +
            $"{completedCount}/{_phases.Count} phases complete{blockerSuffix}";

        _headerLine3.Text = _workflow.NextAction is not null
            ? $"Next: [{_workflow.NextAction.Actor}] {_workflow.NextAction.Description.Truncate(120)}"
            : "Next: (not set)";
    }

    private static void RefreshPhaseList()
    {
        if (!_workflowInitialized)
        {
            _phaseListView.SetSource(new List<string> { "  Press I to initialize this folder." });
            _phaseListView.SelectedItem = 0;
            _phaseListView.TopItem = 0;
            return;
        }

        if (_phases.Count == 0)
        {
            _phaseListView.SetSource(new List<string> { "  No phases yet." });
            _phaseListView.SelectedItem = 0;
            _phaseListView.TopItem = 0;
            return;
        }

        var currentId = _workflow?.CurrentPhaseId;

        var rows = _phases.Select(p =>
        {
            var marker = string.Equals(p.PhaseId, currentId, StringComparison.OrdinalIgnoreCase)
                ? "►" : " ";
            var icon   = StateIcon(p.State);
            var tag    = TuiViewHelpers.StateTag(p.State);
            var pct    = PhaseProgress.ForPhase(p);
            var bar    = TuiViewHelpers.ProgressBar(pct, 6);
            // Format: "► PHASE-028  ✓PASS  ██████ 100%"
            return $"{marker} {p.PhaseId,-10}  {icon}{tag,-5}  {bar} {pct,3}%";
        }).ToList();

        var savedIndex = _selectedPhaseIndex;
        var savedTop = _phaseListView.TopItem;
        _phaseListView.SetSource(rows);
        if (savedIndex >= 0 && savedIndex < rows.Count)
            _phaseListView.SelectedItem = savedIndex;
        if (savedTop >= 0 && savedTop < rows.Count)
            _phaseListView.TopItem = savedTop;
    }

    private static void RefreshPhaseDetail()
    {
        if (!_workflowInitialized)
        {
            _detailFrame.Title = " Workflow Setup ";
            _detailTextView.Text = TuiBootstrap.BuildUninitializedDetailText(_startDir);
            return;
        }

        var phase = _selectedPhaseIndex >= 0 && _selectedPhaseIndex < _phases.Count
            ? _phases[_selectedPhaseIndex]
            : null;

        if (phase is null)
        {
            _detailFrame.Title = " Phase Detail ";
            _detailTextView.Text = "\n  (no phase selected - use Up/Down to navigate)";
            return;
        }

        var savedTopRow = _detailTextView.TopRow;
        _detailFrame.Title = $" Phase Detail - {phase.PhaseId} ";
        _detailTextView.Text = BuildDetailText(phase);
        _detailTextView.TopRow = savedTopRow;
    }

    private static void RefreshActivity()
    {
        if (!_workflowInitialized || _dispatcher is null)
        {
            _activityListView.SetSource(new List<string> { "  Initialize the workflow to start tracking activity." });
            _activityListView.SelectedItem = 0;
            _activityListView.TopItem = 0;
            return;
        }

        try
        {
            var result = _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.GetTimeline,
                Actor     = WorkflowActor.WorkflowSystem
            });

            if (!result.Success) return;

            var root = JsonSerializer.SerializeToElement(result.Data,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var entries = root.TryGetProperty("entries", out var el)
                ? JsonSerializer.Deserialize<List<TimelineEntry>>(el.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? []
                : [];

            var rows = entries
                .Where(e => e.Timestamp != default && !string.IsNullOrWhiteSpace(e.Summary))
                .Take(20)
                .Select(e =>
                {
                    var phaseLabel = string.IsNullOrWhiteSpace(e.PhaseId) ? "-" : e.PhaseId;
                    var summary = e.Summary.ReplaceLineEndings(" ").Trim();
                    return $"  {e.Timestamp:MM-dd HH:mm}  {phaseLabel,-12}  {summary.Truncate(72)}";
                })
                .ToList();

            if (rows.Count == 0)
                rows = ["  No recent workflow activity yet."];

            var savedActivityTop = _activityListView.TopItem;
            _activityListView.SetSource(rows);
            if (savedActivityTop >= 0 && savedActivityTop < rows.Count)
                _activityListView.TopItem = savedActivityTop;
        }
        catch { /* Non-fatal - leave previous data */ }
    }

    private static void RefreshDiagnostics()
    {
        if (!_workflowInitialized || _dispatcher is null || _store is null)
        {
            _diagLine1.Text = "Server: Not started   press I to initialize this folder";
            _diagLine2.Text = $"Folder: {TuiBootstrap.DeriveAssetName(_startDir)}     Write mode is unavailable until initialization";
            return;
        }

        var serverStr = _serverStatus.IsCurrentWorkflow
            ? $"Server: Running   http://localhost:{LocalServerBootstrap.DefaultPort}   press S to stop"
            : _serverStatus.IsRunning
                ? $"Server: Busy with {DisplayRootName(_serverStatus.ActiveWorkflowRoot)}   press S to take over"
                : $"Server: Stopped   press S to start";

        var inboxStr  = "Inbox: -";
        var cacheStr  = "Cache: -";
        var traceStr  = "Trace: -";
        var budgetStr = "Budget: -";

        try
        {
            var r = _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.GetInboxStatus,
                Actor     = WorkflowActor.WorkflowSystem
            });
            if (r.Success && r.Data is InboxStatus inbox)
                inboxStr = $"Inbox: {inbox.PendingCount} pending / {inbox.FailedCount} failed";
        }
        catch { }

        try
        {
            if (_serverStatus.IsRunning)
            {
                // Query the server's cache via HTTP
                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = http.PostAsync(
                    $"http://localhost:{LocalServerBootstrap.DefaultPort}/api/workflow/workflow.get_cache_status",
                    content)
                    .GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(json);
                    var data = doc.RootElement.GetProperty("data");
                    var pkgCount = data.GetProperty("packageCount").GetInt32();
                    var hitRate = data.GetProperty("hitRate").GetDouble();
                    cacheStr = $"Cache: {pkgCount} pkgs  hit {hitRate:P0}";
                }
            }
            else
            {
                // Fallback: local dispatcher when the server isn't running
                var settingsPath = Path.Combine(_workflowRoot, ".workflowkit", "cache-settings.json");
                var settings     = CacheSettings.Load(settingsPath);
                var cache        = new ContextPackageCache(settings);
                var cacheDisp    = new OperationDispatcher(_store, cache);
                var r = cacheDisp.Dispatch(new OperationContext
                {
                    Operation = WorkflowOperations.GetCacheStatus,
                    Actor     = WorkflowActor.WorkflowSystem
                });
                if (r.Success && r.Data is CacheDiagnostics diag)
                    cacheStr = $"Cache: {diag.PackageCount} pkgs  hit {diag.HitRate:P0}";
            }
        }
        catch { }

        try
        {
            var r = _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.GetTraceStatus,
                Actor     = WorkflowActor.WorkflowSystem,
                Parameters = new Dictionary<string, object?> { ["includeCounts"] = false }
            });
            if (r.Success)
            {
                var el      = JsonSerializer.SerializeToElement(r.Data,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var enabled = el.TryGetProperty("isEnabled", out var ie) && ie.GetBoolean();
                var mode    = el.TryGetProperty("mode", out var m) ? m.GetString() ?? "?" : "?";
                traceStr    = $"Trace: {(enabled ? mode : "Off")}";
            }
        }
        catch { }

        try
        {
            var r = _dispatcher.Dispatch(new OperationContext
            {
                Operation = WorkflowOperations.GetBudgetConfig,
                Actor     = WorkflowActor.WorkflowSystem
            });
            if (r.Success && r.Data is BudgetConfigResult budget)
                budgetStr = TuiViewHelpers.BudgetStatus(budget);
        }
        catch { }

        _diagLine1.Text = $"{serverStr}";
        _diagLine2.Text = $"{inboxStr}     {cacheStr}     {traceStr}     {budgetStr}";
    }

    // -- Detail text builder ----------------------------------------------------

    private static string BuildDetailText(Phase phase)
    {
        var sb       = new StringBuilder();
        var progress = PhaseProgress.ForPhase(phase);
        var isPass   = PhaseProgress.IsSuccessfulTerminal(phase.State);

        // -- Header block -------------------------------------------------------
        sb.AppendLine($"  {StateIcon(phase.State)} {phase.PhaseId} — {phase.Title}");
        sb.AppendLine();
        sb.AppendLine($"  State:    {phase.State,-22}  Agent: {phase.AssignedAgent?.ToString() ?? "(unassigned)"}");

        // Progress bar
        var bar = TuiViewHelpers.ProgressBar(progress, 36);
        sb.AppendLine($"  Progress: {bar}  {progress}%");

        // Dates
        if (phase.StartedUtc is { } started)
            sb.AppendLine($"  Started:  {started:yyyy-MM-dd HH:mm} UTC");
        if (phase.CompletedUtc is { } completed)
            sb.AppendLine($"  Completed:{completed:yyyy-MM-dd HH:mm} UTC");
        if (phase.Dependencies.Count > 0)
            sb.AppendLine($"  Depends:  {string.Join(", ", phase.Dependencies)}");
        if (AttentionFlagRules.HasAny(phase.AttentionFlags))
        {
            sb.AppendLine($"  Attention:{AttentionFlagRules.DeriveOutcome(phase.AttentionFlags),-22}");
            sb.AppendLine($"  Flags:    {string.Join(", ", DescribeAttentionFlags(phase.AttentionFlags))}");
        }

        // -- Next action for current phase --------------------------------------
        if (_workflow?.NextAction?.PhaseId is { } naId &&
            string.Equals(naId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("  ▶ Next Action:");
            sb.AppendLine($"    [{_workflow.NextAction.Actor}] {_workflow.NextAction.Description.Truncate(100)}");
            sb.AppendLine($"    Urgency: {_workflow.NextAction.Urgency}");
        }

        // -- Sub-phases ---------------------------------------------------------
        if (phase.SubPhases.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  Sub-phases ({phase.SubPhases.Count}):");
            foreach (var sp in phase.SubPhases)
            {
                var icon = sp.Status switch
                {
                    SubPhaseStatus.Completed  => "✓",
                    SubPhaseStatus.InProgress => "▶",
                    SubPhaseStatus.Blocked    => "✗",
                    SubPhaseStatus.Deferred   => "~",
                    _                         => "·"
                };
                sb.AppendLine($"    {icon} {sp.SubPhaseId,-18}  [{sp.Status,-10}]  {sp.Title}");
            }
        }

        // -- Log counts ---------------------------------------------------------
        sb.AppendLine();
        sb.AppendLine("  Gate Logs:");
        sb.AppendLine(
            $"    Impl: {phase.ImplementationLogIds.Count}   " +
            $"Audits: {phase.AuditLogIds.Count}   " +
            $"Reviews: {phase.ReviewLogIds.Count}   " +
            $"Tests: {(phase.ValidationLogIds.Count + phase.TestLogIds.Count)}   " +
            $"Fixes: {phase.FixLogIds.Count}   " +
            $"Blockers: {phase.BlockerIds.Count}");

        if (phase.ImplementationLogIds.Count > 0)
            sb.AppendLine($"    IDs: {string.Join(", ", phase.ImplementationLogIds.TakeLast(3))}");
        if (phase.ReviewLogIds.Count > 0)
            sb.AppendLine($"         {string.Join(", ", phase.ReviewLogIds.TakeLast(3))}");

        // -- Acceptance criteria ------------------------------------------------
        if (phase.Contract?.AcceptanceCriteria.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  Acceptance Criteria ({phase.Contract.AcceptanceCriteria.Count}):");
            foreach (var ac in phase.Contract.AcceptanceCriteria)
            {
                var checkmark = isPass ? "✓" : "·";
                foreach (var line in TuiViewHelpers.WordWrap(ac, 70))
                    sb.AppendLine($"    {checkmark} {line}");
            }
        }

        // -- Objective ---------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(phase.Contract?.Objective))
        {
            sb.AppendLine();
            sb.AppendLine("  Objective:");
            foreach (var line in TuiViewHelpers.WordWrap(phase.Contract.Objective, 74))
                sb.AppendLine($"    {line}");
        }

        return sb.ToString();
    }

    // -- Formatting helpers -----------------------------------------------------

    private static string StateIcon(PhaseState state) => state switch
    {
        PhaseState.Pass                => "✓",
        PhaseState.PassWithWarnings    => "✓",
        PhaseState.Blocked             => "✗",
        PhaseState.FailedArchitecture
        or PhaseState.FailedCompile
        or PhaseState.FailedValidation      => "✗",
        PhaseState.InImplementation    => "▶",
        PhaseState.AuditLogged         => "◐",
        PhaseState.ReviewLogged
        or PhaseState.ReviewInProgress => "◑",
        PhaseState.RequiresFix
        or PhaseState.FixInProgress    => "⚑",
        _                              => "·"
    };

    private static string StateTag(PhaseState state) => TuiViewHelpers.StateTag(state);

    private static IEnumerable<string> DescribeAttentionFlags(AttentionFlagsSnapshot flags)
    {
        if (flags.HumanValidationRequired) yield return "HumanValidationRequired";
        if (flags.TestsNotRunnable) yield return "TestsNotRunnable";
        if (flags.ManualReviewRequired) yield return "ManualReviewRequired";
        if (flags.ExternalToolRequired) yield return "ExternalToolRequired";
        if (flags.MaxAgentAttemptsReached) yield return "MaxAgentAttemptsReached";
        if (flags.UnresolvedRisk) yield return "UnresolvedRisk";
        if (flags.BlockedByUser) yield return "BlockedByUser";
        if (flags.BlockedByEnvironment) yield return "BlockedByEnvironment";
    }

    private static bool MatchesPlainLetter(Key key, Key upper, Key lower)
    {
        var normalized = NormalizeKey(key);
        return !HasModifier(key, Key.CtrlMask) &&
               !HasModifier(key, Key.AltMask) &&
               (normalized == upper || normalized == lower);
    }

    private static bool MatchesCtrlLetter(Key key, Key upper, Key lower)
    {
        var normalized = NormalizeKey(key);
        return HasModifier(key, Key.CtrlMask) &&
               (normalized == upper || normalized == lower);
    }

    private static bool HasModifier(Key key, Key modifier) => (key & modifier) == modifier;

    private static Key NormalizeKey(Key key) =>
        key & ~(Key.CtrlMask | Key.AltMask | Key.ShiftMask);

    private static string DisplayRootName(string? workflowRoot)
    {
        if (string.IsNullOrWhiteSpace(workflowRoot))
            return "another project";

        var trimmed = workflowRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? workflowRoot : leaf;
    }
}

// -- StringExtensions ----------------------------------------------------------

internal static class StringExtensions
{
    public static string Truncate(this string s, int maxLength) =>
        s.Length <= maxLength ? s : string.Concat(s.AsSpan(0, maxLength - 1), "…");
}
