namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the Beka Forge Workflow pointer block for AGENTS.md and CLAUDE.md.
/// These files are user-owned; the canonical workflow instructions live in .workflowkit/workflow/Rules.md.
/// </summary>
public sealed class AgentsMdGenerator
{
    public string Generate() =>
        """
        ## Beka Forge Workflow

        **STOP. Read `.workflowkit/workflow/Rules.md` NOW before doing anything else.**
        This is not optional. The Rules file contains mandatory workflow rules
        you must follow. Return here only after you have read and understood it.

        ### Validation Honesty Rule

        Do not log a test as passed unless it actually ran. If you cannot run
        validation, use `bfwf validation request-user` to ask the human owner.
        If no validation is needed, use `bfwf validation skip` with a reason.
        Do not mark a phase Pass until validation is passed or explicitly skipped.

        ### Quick Reference

        - State lives in `.workflowkit/` — never write it directly
        - Use `bfwf` CLI or HTTP API for all writes
        - Every implementation, audit, review, validation, and fix appends a
          JSONL record — never rewritten
        - Phases follow a strict state machine — check `.workflowkit/workflow/Rules.md`
          for valid transitions
        """;
}
