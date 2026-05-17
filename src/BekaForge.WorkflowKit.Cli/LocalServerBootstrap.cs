using System.Diagnostics;
using System.Net.Http.Json;

namespace BekaForge.WorkflowKit.Cli;

public sealed record ServerHealthSnapshot(string? Status, string? WorkflowRoot);

public sealed record ServerInstanceStatus(
    bool IsRunning,
    bool IsCurrentWorkflow,
    string? ActiveWorkflowRoot,
    int Port);

public static class LocalServerBootstrap
{
    public const int DefaultPort = 51256;

    public static bool EnsureRunning(string workflowRoot, TextWriter output, bool takeOwnershipOfPort = false)
    {
        var normalizedRoot = NormalizeWorkflowRoot(workflowRoot);
        var status = GetStatus(normalizedRoot, DefaultPort);

        if (status.IsCurrentWorkflow)
        {
            output.WriteLine($"Beka Forge Workflow server ready on http://localhost:{DefaultPort}");
            return true;
        }

        if (status.IsRunning)
        {
            output.WriteLine($"Warning: another Beka Forge Workflow server is already running on http://localhost:{DefaultPort}");
            output.WriteLine($"  Active root: {status.ActiveWorkflowRoot ?? "(unknown)"}");
            output.WriteLine($"  Requested root: {normalizedRoot}");

            if (!takeOwnershipOfPort)
            {
                output.WriteLine("Continuing with local TUI state.");
                return false;
            }

            output.WriteLine("Stopping the active server so this workflow can take over the local port.");
            if (!TryShutdownPort(DefaultPort, output))
            {
                output.WriteLine("Warning: the existing server did not stop cleanly. Continuing with local TUI state.");
                return false;
            }
        }

        var (fileName, arguments) = GetServerLaunchCommand(workflowRoot, DefaultPort);
        output.WriteLine($"Starting Beka Forge Workflow server on http://localhost:{DefaultPort}");
        output.WriteLine($"  Root: {workflowRoot}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var proc = Process.Start(psi);
        if (proc is null)
            return false;

        using var wait = ConsoleWaitIndicator.Start(output, "Waiting for local server");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(250);
            if (GetStatus(normalizedRoot, DefaultPort).IsCurrentWorkflow)
            {
                wait.Complete(" connected.");
                return true;
            }
        }

        wait.Complete(" timed out.");
        output.WriteLine("Warning: server did not report healthy in time. Continuing with local TUI state.");
        return false;
    }

    public static bool StopCurrentWorkflowServer(string workflowRoot, TextWriter output)
    {
        var status = GetStatus(workflowRoot, DefaultPort);

        if (!status.IsRunning)
        {
            output.WriteLine("Beka Forge Workflow server is already stopped.");
            return true;
        }

        if (!status.IsCurrentWorkflow)
        {
            output.WriteLine("Another workflow project currently owns the local server port. Leaving it running.");
            output.WriteLine($"  Active root: {status.ActiveWorkflowRoot ?? "(unknown)"}");
            output.WriteLine($"  Requested root: {NormalizeWorkflowRoot(workflowRoot)}");
            return false;
        }

        output.WriteLine($"Stopping Beka Forge Workflow server on http://localhost:{DefaultPort}");
        return TryShutdownPort(DefaultPort, output);
    }

    public static ServerInstanceStatus GetStatus(string workflowRoot, int port = DefaultPort) =>
        ClassifyStatus(workflowRoot, GetHealth(port), port);

    public static ServerInstanceStatus ClassifyStatus(
        string workflowRoot,
        ServerHealthSnapshot? health,
        int port = DefaultPort)
    {
        var normalizedRoot = NormalizeWorkflowRoot(workflowRoot);
        var activeRoot = NormalizeWorkflowRootOrNull(health?.WorkflowRoot);
        var isRunning = string.Equals(health?.Status, "ok", StringComparison.OrdinalIgnoreCase);
        var isCurrentWorkflow = isRunning &&
            string.Equals(activeRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase);

        return new ServerInstanceStatus(isRunning, isCurrentWorkflow, activeRoot, port);
    }

    public static (string FileName, string Arguments) GetServerLaunchCommand(string workflowRootPath, int effectivePort)
    {
        var cliBaseDir = AppContext.BaseDirectory;
        var bundledServerExe = Path.Combine(cliBaseDir, "BekaForge.WorkflowKit.Server.exe");
        if (File.Exists(bundledServerExe))
        {
            return (
                bundledServerExe,
                $"--root \"{workflowRootPath}\" --port {effectivePort}");
        }

        var bundledServerDll = Path.Combine(cliBaseDir, "BekaForge.WorkflowKit.Server.dll");
        if (File.Exists(bundledServerDll))
        {
            return (
                "dotnet",
                $"\"{bundledServerDll}\" --root \"{workflowRootPath}\" --port {effectivePort}");
        }

        var serverProj = FindServerProject();
        return (
            "dotnet",
            $"run --project \"{serverProj}\" --root \"{workflowRootPath}\" --port {effectivePort}");
    }

    public static string FindServerProject()
    {
        var cliDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
            ?? Directory.GetCurrentDirectory();

        var dir = cliDir;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "BekaForge.WorkflowKit.Server",
                "BekaForge.WorkflowKit.Server.csproj");
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(dir, "src", "BekaForge.WorkflowKit.Server",
                "BekaForge.WorkflowKit.Server.csproj");
            if (File.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }

        return Path.GetFullPath(
            Path.Combine(cliDir, "..", "..", "..", "..",
                "BekaForge.WorkflowKit.Server", "BekaForge.WorkflowKit.Server.csproj"));
    }

    private static ServerHealthSnapshot? GetHealth(int port)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(500)
            };

            return client.GetFromJsonAsync<ServerHealthSnapshot>($"http://localhost:{port}/api/health")
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryShutdownPort(int port, TextWriter output)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };

            using var response = client.PostAsync($"http://localhost:{port}/api/shutdown", null)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
                output.WriteLine($"Warning: shutdown request returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            output.WriteLine($"Warning: failed to request server shutdown: {ex.Message}");
            return false;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(250);
            if (!GetStatus(Directory.GetCurrentDirectory(), port).IsRunning)
                return true;
        }

        output.WriteLine("Warning: server did not shut down cleanly in time.");
        return false;
    }

    private static string NormalizeWorkflowRoot(string workflowRoot) =>
        Path.GetFullPath(workflowRoot);

    private static string? NormalizeWorkflowRootOrNull(string? workflowRoot)
    {
        if (string.IsNullOrWhiteSpace(workflowRoot))
            return null;

        try
        {
            return Path.GetFullPath(workflowRoot);
        }
        catch
        {
            return workflowRoot;
        }
    }
}
