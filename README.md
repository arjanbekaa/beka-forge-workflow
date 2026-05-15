# Beka Forge Workflow

A local-first, cross-platform .NET CLI tool that gives AI coding agents a structured, auditable workflow — phases, contracts, implementation logs, reviews, validation, blockers, and append-only JSON/JSONL state.

**Works with:** Codex, Claude Code, Cursor, GitHub Copilot, Windsurf, Cline, and any MCP-compatible client.

## Install

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
bfwf --help
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Quick Start

```bash
# Initialize a new workflow in the current directory
bfwf init "My Project"

# The agent is ready — it reads .workflowkit/workflow/Rules.md on next prompt
```

`bfwf init` creates a single `.workflowkit/` directory containing all state, generated docs, and indexes. It also creates `AGENTS.md` and `CLAUDE.md` at the project root with a pointer to the workflow rules.

## How It Works

Beka Forge Workflow organizes AI-agent development into a **state machine** with phases, roles, and append-only audit trails. Everything lives inside `.workflowkit/` — JSON for current state, JSONL for event history, and generated markdown for human-readable docs.

```
Your Project/
├── .workflowkit/                  ← Everything lives here
│   ├── workflow.json              ← Project state, next action, budget config
│   ├── phases/                    ← One JSON file per phase
│   ├── logs/                      ← Append-only: implementation, audit, review, validation, fix
│   ├── blockers/                  ← Open/resolved blockers
│   ├── handoffs/                  ← Agent-to-agent handoff records
│   ├── metrics/                   ← Timing and session records
│   ├── index/                     ← Rebuildable SQLite search index and context cache
│   ├── evidence/                  ← Validation command output artifacts
│   └── workflow/                  ← Generated human-readable docs (inside .workflowkit/)
│       ├── Rules.md               ← Mandatory first read for every AI agent
│       ├── workflow.md            ← Project overview
│       ├── docs/                  ← Architecture, implementation plan, migration notes
│       ├── phases/                ← Per-phase markdown overviews
│       ├── 02_Audits/             ← Audit and review logs (markdown)
│       ├── 03_Implementation/     ← Implementation and fix logs (markdown)
│       ├── 04_Validation/         ← Validation logs (markdown)
│       └── 07_Status/             ← Current status dashboard
├── AGENTS.md                      ← Entry point for Codex (auto-generated pointer)
└── CLAUDE.md                      ← Entry point for Claude Code (auto-generated pointer)
```

### Key Concepts

| Concept | Description |
|---------|-------------|
| **Phases** | Discrete work units with contracts, state transitions, and assigned agents |
| **Roles** | Planner, Implementer, Auditor, Reviewer, Validator, Fixer, Human Owner — any AI or human can fill any role |
| **Append-only logs** | Every implementation, audit, review, validation, and fix appends a JSONL record — never rewritten |
| **Validation honesty** | Passed validation requires evidence. Manual tests require human confirmation. No fake passes. |
| **Blockers** | Tracked per phase; resolve auto-advances the phase back to ReadyForImplementation |
| **Handoffs** | Structured task handoffs between agents with context and acceptance criteria |
| **Budget profiles** | Low / Medium / High / Full — controls context retrieval depth and token estimates |

---

## Architecture: Context Engine

The context engine is what makes WorkflowKit useful for AI agents navigating large projects. It has four layers:

### 1. Append-Only Source of Truth (JSONL / JSON)

All state is in `.workflowkit/`. The machine-readable JSON/JSONL files are the source of truth — they are never overwritten, only appended to (for logs) or atomically replaced (for current-state files).

```
.workflowkit/
├── workflow.json          ← current project state (atomic replace)
├── phases/PHASE-NNN.json  ← per-phase state (atomic replace)
├── logs/implementation.jsonl
├── logs/audit.jsonl
├── logs/review.jsonl
├── logs/validation.jsonl
├── logs/fix.jsonl
├── blockers/blockers.jsonl
├── handoffs/handoffs.jsonl
└── metrics/timing.jsonl
```

No agent or tool ever writes these files directly — all writes go through the dispatcher (`bfwf` CLI, HTTP API, or MCP).

---

### 2. SQLite Index (Rebuildable Read Model)

Located at `.workflowkit/index/context-index.db` (and `workflowkit.db`), this SQLite database is a **read model** — rebuilt from the authoritative JSONL whenever needed. Agents never write to it directly.

The index stores:
- Full-text search over phase contracts, implementation summaries, and log records
- Structural metadata: phase state, dependencies, assigned actors, timestamps
- Token-cost estimates per pointer type (for budget-aware retrieval)

If the index gets out of sync with JSONL, it can be rebuilt with `bfwf cache rebuild`. The JSONL always wins.

---

### 3. In-Memory Context Cache

The `BekaForge.WorkflowKit.Cache` layer keeps a **hot read cache** of the most recently accessed context packages in memory during a session. This avoids repeated disk reads when an agent calls `workflow.get_relevant_context` for the same phase multiple times.

Cache behavior:
- Keyed by `(phaseId, budgetMode)` — each combination is a separate cache entry
- Evicted when the underlying phase JSON changes (write-through invalidation)
- Estimated token cost is pre-computed on fill so budget enforcement is instant

---

### 4. Pointer and Slice APIs

Rather than handing agents raw file contents, the context engine returns **pointers** — lightweight references that describe where information lives and how much it costs to read. Agents call `workflow.get_relevant_context` and get back a list of pointers ranked by relevance score. They then call `workflow.get_file_slice`, `workflow.get_record_slice`, or `workflow.get_markdown_region` to resolve individual pointers.

```json
{
  "pointerType": "phase_contract",
  "target": "PHASE-026.json#contract",
  "estimatedTokens": 420,
  "score": 1.0
}
```

Pointer types:
| Type | Description |
|------|-------------|
| `phase_contract` | The contract section of a phase JSON |
| `phase_state` | Current state, actor, and next action |
| `implementation_log` | Recent implementation record |
| `audit_log` | Audit record |
| `review_log` | Review record |
| `validation_log` | Validation record with evidence |
| `blocker` | Open or resolved blocker |
| `markdown_region` | A named region in a generated markdown file |
| `file_slice` | A line range in any project file |
| `json_pointer` | A JSON Pointer path into a structured file |

This approach keeps AI context lean — agents load only what they need, budget permitting.

---

### 5. Budget-Aware Retrieval

`workflow.get_relevant_context` accepts a `budgetMode` (Low / Medium / High / Full). Each mode has a token cap and a maximum number of pointers. The retrieval engine:

1. Scores pointers by relevance to the current phase and task description using hybrid retrieval (keyword + structural match).
2. Ranks by score descending.
3. Cuts off at the budget's token limit.
4. Returns the ranked pointer list — never raw content.

The agent decides which pointers to resolve. It can call `workflow.get_budget_report` to see current budget usage.

---

## Agent Setup

`bfwf init` creates `AGENTS.md` and `CLAUDE.md` at the project root. Each contains one pointer:

> Read `.workflowkit/workflow/Rules.md` first before making workflow-related changes.

For other tools, add that same line to the instructions file your tool reads:

| Tool | Instructions file |
|------|-------------------|
| Codex (OpenAI) | `AGENTS.md` — created automatically |
| Claude Code | `CLAUDE.md` — created automatically |
| Cursor | `.cursor/rules/` |
| GitHub Copilot | `.github/copilot-instructions.md` |
| Windsurf | `.windsurfrules` |
| Cline | `.clinerules` |

### Copy-Ready Agent Rules Text

To manually add Beka Forge Workflow instructions to any agent, copy this text into your agent's rules/instructions file:

```markdown
## Beka Forge Workflow

Read `.workflowkit/workflow/Rules.md` first before making workflow-related changes.

### Validation Honesty Rule

Do not log a test as passed unless it actually ran. If you cannot run validation,
ask the user. If no validation is needed, log a skipped validation with a reason.
Do not mark a phase Pass until validation is passed or explicitly skipped.
```

`AGENTS.md` and `CLAUDE.md` are user-owned — `bfwf sync-markdown` only touches the `<!-- BEKAFORGE:BEGIN ... -->` generated region inside them.

## Validation

Beka Forge Workflow enforces honest validation logging. You cannot mark a test as passed without evidence.

| Command | Description |
|---------|-------------|
| `bfwf validation plan --phase PHASE-NNN` | Show what must be tested, what the agent can test, and what requires the user |
| `bfwf validation request-user --phase PHASE-NNN` | Ask the human owner to run manual validation steps |
| `bfwf validation log --phase PHASE-NNN --type AutomatedCommand --result Passed --evidence-description "All tests passed" --evidence-source Command --summary "Tests passed."` | Log a validation result with evidence |
| `bfwf validation skip --phase PHASE-NNN --reason "No validation needed" --approved-by HumanOwner` | Skip validation with reason and approval |

### Validation Types

- `AutomatedCommand` — run a command and capture output (agent can do this)
- `StaticInspection` — inspect code/logs/build output (agent can do this)
- `BrowserManual` — requires human to test in a browser (LLM cannot mark Passed)
- `UnityManual` — requires human to test in Unity Editor (LLM cannot mark Passed)
- `UnityAutomated` — automated Unity test runner
- `HumanValidationRequired` — requires human to perform steps (LLM cannot mark Passed)
- `SkippedNotNeeded` — no validation needed
- `SkippedByUserOverride` — user explicitly approved skipping

### Validation Results

- `Passed` — all checks passed (requires evidence)
- `PassedWithWarnings` — passed with non-blocking warnings (requires evidence)
- `Failed` — validation failed
- `Skipped` — intentionally skipped (requires skip reason)
- `PendingUser` — waiting for human to complete validation

## CLI Reference

| Command | Description |
|---------|-------------|
| `bfwf init "Name"` | Initialize a new workflow project |
| `bfwf status` | Print workflow state and phase list |
| `bfwf status --watch` | Live-updating status dashboard |
| `bfwf tui` | Interactive terminal dashboard (read-only) |
| `bfwf validate` | Validate workflow consistency (includes doctor checks) |
| `bfwf sync-markdown` | Regenerate all markdown docs |
| `bfwf manifest` | List all registered operations with access levels |
| `bfwf recommend --task "..."` | Recommend operations for a task description |
| `bfwf context --phase PHASE-001` | Get context pointers for a phase |
| `bfwf budget --budget Medium` | Show or set project budget profile |
| `bfwf log implementation\|audit\|review\|test\|fix` | Create append-only log records |
| `bfwf validation plan\|log\|skip\|request-user` | Validation commands (preferred over `bfwf log test`) |
| `bfwf phase status\|assign` | Manage phase state and assignment |
| `bfwf blocker add\|resolve` | Record or resolve blockers |
| `bfwf server start\|stop` | Manage loopback HTTP server |
| `bfwf cache rebuild` | Rebuild the SQLite context index from JSONL |

All commands support `--json` for machine-readable output and `--plain` for dumb terminals.

## MCP Support

Beka Forge Workflow exposes the full dispatcher through an MCP stdio host:

```bash
# Single project
bfwf mcp --root /path/to/project

# Global multi-project mode (routes by projectId)
bfwf mcp
```

Claude Desktop config:

```json
{
  "mcpServers": {
    "workflowkit": {
      "command": "bfwf",
      "args": ["mcp", "--root", "/path/to/project"]
    }
  }
}
```

Tool names use the canonical `workflow.*` namespace. The MCP layer resolves the target project first, then routes every call through `OperationDispatcher` — it never writes `.workflowkit/` files directly.

## HTTP API

```bash
bfwf server start
```

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/health` | Health, manifest coverage, index status |
| `GET` | `/api/workflow/operations` | Registered operation names |
| `POST` | `/api/workflow/{operation-name}` | Dispatch any operation |
| `POST` | `/api/shutdown` | Graceful shutdown |

## Project Structure

```
src/
├── BekaForge.WorkflowKit.Core/           Domain models, state machine, roles
├── BekaForge.WorkflowKit.AgentContracts/ Operation DTOs, error codes
├── BekaForge.WorkflowKit.Storage/        .workflowkit/ persistence, hybrid retrieval
├── BekaForge.WorkflowKit.Cache/          Context package caching (in-memory hot cache)
├── BekaForge.WorkflowKit.Markdown/       Markdown sync with human-preserving regions
├── BekaForge.WorkflowKit.Server/         Loopback HTTP JSON API
├── BekaForge.WorkflowKit.Cli/            bfwf CLI (cross-platform .NET global tool)
└── BekaForge.WorkflowKit.Mcp/            MCP stdio adapter with project registry
tests/
└── BekaForge.WorkflowKit.Tests/          xUnit tests
```

## Build & Test

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## License

MIT — see [LICENSE](LICENSE).
