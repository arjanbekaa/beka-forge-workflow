using BekaForge.WorkflowKit.Cli;
using Xunit;

namespace BekaForge.WorkflowKit.Tests;

public sealed class LocalServerBootstrapTests
{
    [Fact]
    public void ClassifyStatus_CurrentWorkflowRoot_IsMarkedAsOwned()
    {
        var workflowRoot = Path.Combine("E:\\", "Work", "ProjectA");
        var health = new ServerHealthSnapshot("ok", workflowRoot);

        var status = LocalServerBootstrap.ClassifyStatus(workflowRoot, health);

        Assert.True(status.IsRunning);
        Assert.True(status.IsCurrentWorkflow);
        Assert.Equal(Path.GetFullPath(workflowRoot), status.ActiveWorkflowRoot);
    }

    [Fact]
    public void ClassifyStatus_ForeignWorkflowRoot_IsMarkedAsBusy()
    {
        var requestedRoot = Path.Combine("E:\\", "Work", "ProjectA");
        var activeRoot = Path.Combine("E:\\", "Work", "ProjectB");
        var health = new ServerHealthSnapshot("ok", activeRoot);

        var status = LocalServerBootstrap.ClassifyStatus(requestedRoot, health);

        Assert.True(status.IsRunning);
        Assert.False(status.IsCurrentWorkflow);
        Assert.Equal(Path.GetFullPath(activeRoot), status.ActiveWorkflowRoot);
    }

    [Fact]
    public void ClassifyStatus_MissingHealth_IsStopped()
    {
        var workflowRoot = Path.Combine("E:\\", "Work", "ProjectA");

        var status = LocalServerBootstrap.ClassifyStatus(workflowRoot, health: null);

        Assert.False(status.IsRunning);
        Assert.False(status.IsCurrentWorkflow);
        Assert.Null(status.ActiveWorkflowRoot);
    }
}
