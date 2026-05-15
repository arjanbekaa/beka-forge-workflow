namespace BekaForge.WorkflowKit.Markdown.Generators;

/// <summary>
/// Generates the canonical workflow-owned instructions for .workflowkit/workflow/Rules.md.
/// </summary>
public sealed class WorkflowRulesMdGenerator
{
    public string Generate() =>
        """
        ## Mandatory First Step

        Before doing any work:

        1. Read this `.workflowkit/workflow/Rules.md` file completely.
        2. Read `.workflowkit/workflow.json`.
        3. Read the current phase JSON file under `.workflowkit/phases/`.
        4. Read `.workflowkit/workflow/workflow.md` and the relevant files under `.workflowkit/workflow/docs/`, `.workflowkit/workflow/phases/`, `.workflowkit/workflow/03_Implementation/`, `.workflowkit/workflow/02_Audits/`, `.workflowkit/workflow/04_Validation/`, and `.workflowkit/workflow/07_Status/`.
        5. Do not write files until you understand the current phase, next action, JSON rules, log rules, and document rules.

        ## Source Of Truth

        Machine-readable state is authoritative:

        - `.workflowkit/workflow.json`
        - `.workflowkit/phases/PHASE-NNN.json`
        - `.workflowkit/logs/*.jsonl`
        - `.workflowkit/blockers/*.jsonl`
        - `.workflowkit/handoffs/*.jsonl`
        - `.workflowkit/metrics/*.jsonl`

        Markdown is generated/readable context. Do not treat Markdown as the source of truth.

        ## Write Rules

        - Update JSON state before updating generated Markdown.
        - Append JSONL records for implementation, fixes, reviews, validation, blockers, handoffs, timing, and events.
        - Never rewrite JSONL history.
        - Never delete unknown files.
        - Never manually edit generated Markdown regions.
        - Preserve human notes outside generated regions.
        - After state changes, sync or update generated Markdown.

        ## Required Documents

        - `.workflowkit/workflow/Rules.md`
        - `.workflowkit/workflow/workflow.md`
        - `.workflowkit/workflow/docs/Architecture.md`
        - `.workflowkit/workflow/docs/ImplementationPlan.md`
        - `.workflowkit/workflow/docs/MigrationNotes.md`
        - `.workflowkit/workflow/docs/ExtractionAudit.md`
        - `.workflowkit/workflow/docs/KnownLimitations.md`
        - `.workflowkit/workflow/docs/ExtensionGuide.md`
        - `.workflowkit/workflow/docs/ConsistencyCheck.md`
        - `.workflowkit/workflow/docs/FinalReview.md`
        - `.workflowkit/workflow/docs/PromptHeader.md`
        - `.workflowkit/workflow/03_Implementation/ImplementationLog.md`
        - `.workflowkit/workflow/03_Implementation/FixLog.md`
        - `.workflowkit/workflow/02_Audits/AuditLog.md`
        - `.workflowkit/workflow/02_Audits/ReviewLog.md`
        - `.workflowkit/workflow/04_Validation/ValidationLog.md`
        - `.workflowkit/workflow/07_Status/CurrentStatus.md`

        Generated regions are owned by Beka Forge Workflow and currently use this marker format:

        ```md
        <!-- BEKAFORGE:BEGIN generated:section-name -->
        ...
        <!-- BEKAFORGE:END generated:section-name -->
        ```

        ## Prompt / Handoff Rule

        Every prompt, handoff, or task description given to another LLM must start with:

        > Read `.workflowkit/workflow/Rules.md` first. Follow the Beka Forge Workflow JSON, log, and document rules before making changes.

        If the receiving agent cannot confirm it has read `.workflowkit/workflow/Rules.md`, the task is not ready.

        ## Implementation Log Rule

        Do not write implementation history only in Markdown.

        For every implementation:

        1. Append a structured record to `.workflowkit/logs/implementation.jsonl`.
        2. Include phase ID, date/time, actor, status, summary, changed files, validation status, and notes.
        3. Regenerate or update `03_Implementation/ImplementationLog.md` from the structured record.

        ## Fix Log Rule

        For every fix:

        1. Append a structured record to `.workflowkit/logs/fix.jsonl`.
        2. Reference the phase and explain the problem, fix, changed files, and verification.
        3. Regenerate or update `03_Implementation/FixLog.md` from the structured record.

        ## Phase Rule

        Every work item belongs to a phase. Do not implement untracked work.

        If no phase exists:

        - Create a phase JSON file.
        - Add it to `workflow.json`.
        - Define its contract.
        - Update `docs/ImplementationPlan.md`.

        ## Review Rule

        - Implementation log is not a review.
        - Audit log is the implementer's self-check.
        - Review log is the independent reviewer's gate decision.
        - Fixes must reference the review or blocker they resolve when that relationship exists.

        ## Validation Rule (MANDATORY — NO FAKE PASSES)

        Validation must be honest. You cannot log a test as "passed" unless it actually ran.

        - **Evidence required**: A Passed or PassedWithWarnings validation MUST include at least one evidence item. Evidence items describe what was tested, how, and by whom.
        - **Manual validation requires human**: BrowserManual, UnityManual, and HumanValidationRequired validation types CANNOT be marked Passed by an LLM. If you cannot run the test, use `workflow.request_user_validation` to ask the human owner.
        - **Skipped requires reason**: If no validation is needed, log a skipped validation with a clear skipReason explaining why.
        - **No fake passes**: Do not mark a phase as Pass unless the latest validation result is Passed/PassedWithWarnings, or validation was skipped with valid reason and approval.
        - **Command evidence**: For AutomatedCommand validation, include the command that was run, its exit code, and the output as evidence.
        - **Static inspection evidence**: For StaticInspection, describe what files were inspected and what was verified.

        To log validation honestly:

        1. Use `workflow.get_validation_plan` to see what must be tested.
        2. If the agent can run the test: run it, capture evidence, and use `workflow.create_validation_log` with evidence.
        3. If the agent cannot run the test: use `workflow.request_user_validation` to ask the user.
        4. If no validation is needed: use `workflow.skip_validation` with a reason.

        ## Dashboard Rule

        The dashboard reads `.workflowkit/` state. If a change should appear in the dashboard, update the structured JSON/JSONL data, not only Markdown.

        ## Feature Tracking Rule

        Features are planning metadata only. The dashboard may create, edit, or remove features through WorkflowMetadataService, which:

        - Appends an event to `.workflowkit/logs/events.jsonl` for every feature CRUD operation.
        - Never touches implementation, audit, review, validation, fix, blocker, handoff, or timing records.
        - Stores features in `.workflowkit/workflow.json` under the `features` array.

        ## Planning Metadata Rule

        The dashboard may edit safe planning metadata fields: current work description, actor, urgency (Low/Medium/High/Critical), due date, and pinned finish-now toggle. All planning metadata writes go through WorkflowMetadataService and append an event to events.jsonl. Historical log records remain append-only.

        ## Generic Workflow Roles

        Beka Forge Workflow uses generic roles to describe gate responsibilities. Any LLM or human can fill any role. The system does not require specific agents.

        | Role | Responsibility |
        |---|---|
        | Planner | Architecture, phase dependency management, implementation plan ownership |
        | Implementer | Code implementation, self-audit, fix execution, implementation logging |
        | Auditor | Self-audit or independent audit of phase deliverables |
        | Reviewer | Independent review gate decision (architecture, safety, completeness) |
        | Validator | Validation execution, evidence collection, validation logging |
        | Fixer | Fix implementation following review or blocker resolution |
        | HumanOwner | Human approval authority, natural-language direction, manual verification |
        | WorkflowSystem | Orchestration state owner, log owner, status owner, handoff owner, markdown sync owner |

        Actor identity (name) is separate from workflow role. Examples:
        - actorName "Codex" with actorRole "Reviewer"
        - actorName "Claude" with actorRole "Implementer"
        - actorName "DeepSeek" with actorRole "Auditor"
        - actorName "User" with actorRole "HumanOwner"

        ## Handler-Only Writes Rule (MANDATORY)

        All writes to `.workflowkit/` must go through CLI commands (`bfwf`) or the HTTP API (`POST /api/workflow/{operation-name}`). Never write `.workflowkit/` JSON or JSONL files directly. The model must never bypass operation handlers for writes.

        ## Root Agent Files

        Root agent files such as `AGENTS.md` are user-owned. Beka Forge Workflow may add a minimal pointer to `.workflowkit/workflow/Rules.md`, but it must not replace broader user instructions.
        """;
}
