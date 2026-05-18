using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Mcp;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class McpToolMappingTests
{
    [Fact]
    public void GetAllTools_UsesCanonicalOperationNames()
    {
        var tools = McpToolMapping.GetAllTools();

        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.GetState);
        Assert.Contains(tools, tool => tool.Name == WorkflowOperations.UpdatePhaseStatus);
    }

    [Fact]
    public void ToolNameToOperation_ResolvesCanonicalAndLegacyNames()
    {
        Assert.Equal(WorkflowOperations.GetState, McpToolMapping.ToolNameToOperation(WorkflowOperations.GetState));
        Assert.Equal(WorkflowOperations.GetState, McpToolMapping.ToolNameToOperation("workflow_get_state"));
    }

    [Fact]
    public void UpdateSubPhaseStatus_UsesEnumFriendlyValues()
    {
        var tool = McpToolMapping.GetAllTools().Single(t => t.Name == WorkflowOperations.UpdateSubPhaseStatus);

        Assert.NotNull(tool.InputSchema.Required);
        Assert.Contains("phaseId", tool.InputSchema.Required!);
        Assert.Contains("subPhaseId", tool.InputSchema.Required!);
        Assert.Contains("status", tool.InputSchema.Required!);
        Assert.Contains("InProgress", tool.InputSchema.Properties["status"].Enum!);
    }

    [Fact]
    public void SetBudgetConfig_ExposesModeProperty()
    {
        var tool = McpToolMapping.GetAllTools().Single(t => t.Name == WorkflowOperations.SetBudgetConfig);

        Assert.Contains("mode", tool.InputSchema.Properties.Keys);
        Assert.Contains("budgetMode", tool.InputSchema.Properties.Keys);
    }

    [Fact]
    public void OrchestrationAttentionTools_ExposeSessionAndFlagProperties()
    {
        var tool = McpToolMapping.GetAllTools().Single(t => t.Name == WorkflowOperations.SetOrchestrationAttentionFlags);

        Assert.Contains("sessionId", tool.InputSchema.Properties.Keys);
        Assert.Contains("humanValidationRequired", tool.InputSchema.Properties.Keys);
        Assert.Contains("blockedByEnvironment", tool.InputSchema.Properties.Keys);
        Assert.Contains("reasonRecordIds", tool.InputSchema.Properties.Keys);
        Assert.Contains("sessionId", tool.InputSchema.Required!);
    }

    [Fact]
    public void OrchestrationAttentionStatus_RequiresSessionId()
    {
        var tool = McpToolMapping.GetAllTools().Single(t => t.Name == WorkflowOperations.GetOrchestrationAttentionStatus);

        Assert.NotNull(tool.InputSchema.Required);
        Assert.Contains("sessionId", tool.InputSchema.Required!);
    }
}
