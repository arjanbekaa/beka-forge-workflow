using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Storage;

public sealed class DocumentationLedgerStore(string workflowRoot)
{
    public IReadOnlyList<DocumentationLedgerRecord> Load()
    {
        var path = WorkflowLayout.DocumentationLedgerPath(workflowRoot);
        if (!File.Exists(path))
            return [];

        var records = WorkflowSerializer.Deserialize<List<DocumentationLedgerRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    public void Save(IReadOnlyList<DocumentationLedgerRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var path = WorkflowLayout.DocumentationLedgerPath(workflowRoot);
        AtomicFileWriter.Write(path, WorkflowSerializer.SerializeState(records));
    }
}
