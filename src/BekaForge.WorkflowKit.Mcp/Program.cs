using BekaForge.WorkflowKit.Mcp;

var root = args.Length > 0 ? args[0] : null;
if (!string.IsNullOrWhiteSpace(root))
{
    root = Path.GetFullPath(root);
    if (!ProjectRegistry.IsValidWorkflowRoot(root))
    {
        Console.Error.WriteLine($"[WorkflowKit MCP] ERROR: Not a valid WorkflowKit project: {root}");
        Console.Error.WriteLine("  Must contain .workflowkit/workflow.json");
        Environment.Exit(1);
    }
}

ProjectRegistry? globalRegistry = null;
try
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    if (!string.IsNullOrWhiteSpace(appData))
    {
        var globalRegistryPath = Path.Combine(appData, "BekaForge", "mcp-registry.json");
        globalRegistry = new ProjectRegistry(globalRegistryPath);
    }
}
catch
{
    globalRegistry = null;
}

new McpHost(root, globalRegistry).Run();
