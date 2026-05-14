using BekaForge.WorkflowKit.Core;

namespace BekaForge.WorkflowKit.Markdown.Generators;

public sealed class ArchitectureMdGenerator
{
    public string Generate(WorkflowState workflow, IReadOnlyList<Phase> phases)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"**Project:** {workflow.AssetName}  ");
        sb.AppendLine($"**Workflow ID:** {workflow.WorkflowId}  ");
        sb.AppendLine();

        sb.AppendLine("## Architecture Constraints");
        sb.AppendLine();
        if (workflow.ArchitectureConstraints.Count == 0)
        {
            sb.AppendLine("_No global architecture constraints recorded yet._");
        }
        else
        {
            foreach (var constraint in workflow.ArchitectureConstraints)
                sb.AppendLine($"- {constraint}");
        }

        sb.AppendLine();
        sb.AppendLine("## Phase Architecture Notes");
        sb.AppendLine();

        if (phases.Count == 0)
        {
            sb.AppendLine("_No phases created yet._");
            return sb.ToString().TrimEnd();
        }

        foreach (var phase in phases.OrderBy(p => p.PhaseNumber))
        {
            sb.AppendLine($"### {phase.PhaseId} - {phase.Title}");
            sb.AppendLine();

            if (phase.Contract is null || phase.Contract.ArchitectureConstraints.Count == 0)
            {
                sb.AppendLine("_No phase architecture constraints recorded._");
            }
            else
            {
                foreach (var constraint in phase.Contract.ArchitectureConstraints)
                    sb.AppendLine($"- {constraint}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
