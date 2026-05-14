using BekaForge.WorkflowKit.Storage;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class McpProcessIntegrationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _isolatedAppData;
    private readonly string _isolatedLocalAppData;
    private readonly string _projectRoot;

    public McpProcessIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bfwf-mcp-proc-{Guid.NewGuid():N}");
        _isolatedAppData = Path.Combine(_tempRoot, "AppData", "Roaming");
        _isolatedLocalAppData = Path.Combine(_tempRoot, "AppData", "Local");
        Directory.CreateDirectory(_isolatedAppData);
        Directory.CreateDirectory(_isolatedLocalAppData);

        _projectRoot = Path.Combine(_tempRoot, "ProjectA");
        new WorkflowInitializer(_projectRoot).Initialize("Process MCP Project");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void CleanMachineStartup_WithEmptyAppData_HandlesInitialize()
    {
        using var process = StartMcpProcess();

        process.StandardInput.WriteLine("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        process.StandardInput.Flush();

        var line = ReadNextJsonLine(process);
        var response = JsonDocument.Parse(line).RootElement;
        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal("2024-11-05", response.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void MalformedJsonRequest_ReturnsParseError()
    {
        using var process = StartMcpProcess();

        process.StandardInput.WriteLine("{");
        process.StandardInput.Flush();

        var line = ReadNextJsonLine(process);
        var response = JsonDocument.Parse(line).RootElement;
        Assert.Equal(-32700, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("parse", response.GetProperty("error").GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    private Process StartMcpProcess(string? rootOverride = null)
    {
        var cliDllPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "bfwf.dll");
        var args = rootOverride is null
            ? $"\"{cliDllPath}\" mcp"
            : $"\"{cliDllPath}\" mcp --root \"{rootOverride}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["APPDATA"] = _isolatedAppData;
        psi.Environment["LOCALAPPDATA"] = _isolatedLocalAppData;
        psi.Environment["DOTNET_CLI_HOME"] = _tempRoot;
        psi.Environment["NUGET_PACKAGES"] = Path.Combine(_tempRoot, ".nuget", "packages");

        var process = Process.Start(psi);
        Assert.NotNull(process);
        return process!;
    }

    private static string ReadNextJsonLine(Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var line = process.StandardOutput.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        var stderr = process.StandardError.ReadToEnd();
        throw new Xunit.Sdk.XunitException($"Timed out waiting for MCP output. stderr: {stderr}");
    }
}
