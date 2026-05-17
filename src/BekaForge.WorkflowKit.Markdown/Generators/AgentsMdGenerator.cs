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

        **STOP. Read `.workflowkit/workflow/Rules.md` before you answer, edit files, run `bfwf`, or call any workflow tool.**
        This is mandatory. Do not continue until you have read and understood it.
        If you cannot read the Rules file, or you cannot use the required
        workflow tool calls (`bfwf`, the workflow HTTP API, or mapped MCP
        workflow tools), stop and tell the user exactly what is blocked and why.
        Do not continue with untracked workflow work.

        ### Validation Honesty Rule

        Do not log a test as passed unless it actually ran. If you cannot run
        validation, use `bfwf validation request-user` to ask the human owner.
        If no validation is needed, use `bfwf validation skip` with a reason.
        Do not mark a phase Pass until validation is passed or explicitly
        skipped with a recorded reason and any required human approval.

        ### Audit And Review Rule

        Audit and review logs must contain real findings, not acknowledgements.
        Record the critical parts checked, potential risks, concrete issues, and
        recommendations. Review must also make an explicit gate decision:
        pass, or requires fix.

        ### Quick Reference

        - State lives in `.workflowkit/`; never write it directly
        - Use `bfwf` CLI or HTTP API for all `.workflowkit/` writes
        - Every implementation, audit, review, validation, blocker, handoff, and
          fix appends a record; history is append-only
        - Phases follow a strict state machine; check `.workflowkit/workflow/Rules.md`
          for valid transitions
        """;
}
