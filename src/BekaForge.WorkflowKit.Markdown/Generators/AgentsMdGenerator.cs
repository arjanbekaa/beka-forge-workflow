namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates a minimal Beka Forge Workflow pointer block for AGENTS.md.
/// AGENTS.md is user-owned; the canonical workflow instructions live in workflow/Rules.md.
/// </summary>
public sealed class AgentsMdGenerator
{
    public string Generate() =>
        """
        ## Beka Forge Workflow

        Read `workflow/Rules.md` first before making workflow-related changes.
        """;
}
