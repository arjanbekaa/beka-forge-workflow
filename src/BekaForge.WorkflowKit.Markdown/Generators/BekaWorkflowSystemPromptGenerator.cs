namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates a compatibility pointer for older tools that still look for BekaWorkflowSystemPrompt.md.
/// </summary>
public sealed class BekaWorkflowSystemPromptGenerator
{
    public string Generate() =>
        """
        ## Compatibility Pointer

        The canonical Beka Forge Workflow instructions now live in `.workflowkit/workflow/Rules.md`.

        Read `.workflowkit/workflow/Rules.md` first. Follow the Beka Forge Workflow JSON, log, and document rules before making changes.

        ### Validation Honesty Rule

        Do not log a test as passed unless it actually ran. If you cannot run validation,
        ask the user. If no validation is needed, log a skipped validation with a reason.
        Do not mark a phase Pass until validation is passed or explicitly skipped.

        This file remains only as a compatibility pointer for older prompts and agent integrations.
        """;
}
