namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates a compatibility pointer for older tools that still look for BekaWorkflowSystemPrompt.md.
/// </summary>
public sealed class BekaWorkflowSystemPromptGenerator
{
    public string Generate() =>
        """
        ## Compatibility Pointer

        The canonical Beka Forge Workflow instructions now live in `workflow/Rules.md`.

        Read `workflow/Rules.md` first. Follow the Beka Forge Workflow JSON, log, and document rules before making changes.

        This file remains only as a compatibility pointer for older prompts and agent integrations.
        """;
}
