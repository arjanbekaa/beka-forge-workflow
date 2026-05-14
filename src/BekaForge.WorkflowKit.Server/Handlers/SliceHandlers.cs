using BekaForge.WorkflowKit.AgentContracts;
using BekaForge.WorkflowKit.Storage;

namespace BekaForge.WorkflowKit.Server.Handlers;

public sealed class GetFileSliceHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetFileSlice;
    public OperationResult Execute(OperationContext context)
    {
        var path = context.GetString("path");
        if (string.IsNullOrWhiteSpace(path)) return OperationResult.Fail("ValidationFailed", "Parameter 'path' is required.");
        var svc = new SliceService(store.WorkflowRoot);
        var start = context.GetString("startLine") is string s && int.TryParse(s, out var sl) ? sl : (int?)null;
        var end = context.GetString("endLine") is string e && int.TryParse(e, out var el) ? el : (int?)null;
        return OperationResult.Ok(svc.GetFileSlice(path, start, end));
    }
}

public sealed class GetRecordSliceHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetRecordSlice;
    public OperationResult Execute(OperationContext context)
    {
        var id = context.GetString("recordId");
        if (string.IsNullOrWhiteSpace(id)) return OperationResult.Fail("ValidationFailed", "Parameter 'recordId' is required.");
        var svc = new SliceService(store.WorkflowRoot);
        return OperationResult.Ok(svc.GetRecordSlice(id));
    }
}

public sealed class GetJsonPointerValueHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetJsonPointerValue;
    public OperationResult Execute(OperationContext context)
    {
        var source = context.GetString("source");
        var pointer = context.GetString("pointer");
        if (string.IsNullOrWhiteSpace(source)) return OperationResult.Fail("ValidationFailed", "Parameter 'source' is required.");
        if (string.IsNullOrWhiteSpace(pointer)) return OperationResult.Fail("ValidationFailed", "Parameter 'pointer' is required.");
        var svc = new SliceService(store.WorkflowRoot);
        return OperationResult.Ok(svc.GetJsonPointerValue(source, pointer));
    }
}

public sealed class GetMarkdownRegionHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetMarkdownRegion;
    public OperationResult Execute(OperationContext context)
    {
        var path = context.GetString("path");
        var section = context.GetString("section");
        if (string.IsNullOrWhiteSpace(path)) return OperationResult.Fail("ValidationFailed", "Parameter 'path' is required.");
        if (string.IsNullOrWhiteSpace(section)) return OperationResult.Fail("ValidationFailed", "Parameter 'section' is required.");
        var svc = new SliceService(store.WorkflowRoot);
        return OperationResult.Ok(svc.GetMarkdownRegion(path, section));
    }
}

public sealed class GetFileHistoryHandler(WorkflowStore store) : IOperationHandler
{
    public string OperationName => WorkflowOperations.GetFileHistory;
    public OperationResult Execute(OperationContext context)
    {
        var path = context.GetString("path");
        if (string.IsNullOrWhiteSpace(path)) return OperationResult.Fail("ValidationFailed", "Parameter 'path' is required.");
        var svc = new SliceService(store.WorkflowRoot);
        return OperationResult.Ok(svc.GetFileHistory(path));
    }
}
