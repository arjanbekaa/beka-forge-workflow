# Beka Forge Workflow

A local-first, cross-platform .NET CLI tool that gives AI coding agents a structured, auditable workflow — phases, contracts, implementation logs, reviews, tests, blockers, and append-only JSON/JSONL state.

**Works with:** Codex, Claude Code, Cursor, GitHub Copilot, Windsurf, Cline, and any MCP-compatible client.

## Install

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
bfwf --help
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Quick Start

```bash
# Create a new workflow project in the current directory
bfwf init "My Project"

# The agent is ready — it reads workflow/Rules.md on next prompt
```

`bfwf init` sets up `.workflowkit/` (structured state) and `workflow/` (human-readable docs). It also creates `AGENTS.md` and `CLAUDE.md` at the project root with a pointer to the workflow rules.

## How It Works

Beka Forge Workflow organizes AI-agent development into a **state machine** with phases, roles, and append-only audit trails. All state lives in `.workflowkit/` — JSON for current state, JSONL for event history.

```
Your Project/
├── .workflowkit/          ← Source of truth (machine-readable)
│   ├── workflow.json       ← Project state, next action, budget config
│   ├── phases/             ← One JSON file per phase
│   ├── logs/               ← Append-only: implementation, audit, review, test, fix
│   ├── blockers/           ← Open/resolved blockers
│   ├── handoffs/           ← Agent-to-agent handoff records
│   └── index/              ← Rebuildable SQLite search index
├── workflow/               ← Generated docs (human-readable)
│   ├── Rules.md             ← Mandatory first read for every AI agent
│   ├── docs/                ← Architecture, implementation plan, migration notes
│   ├── phases/              ← Per-phase markdown overviews
│   └── 07_Status/           ← Current status dashboard
├── AGENTS.md               ← Entry point for Codex (auto-generated)
└── CLAUDE.md               ← Entry point for Claude Code (auto-generated)
```

### Key concepts

| Concept | Description |
|---------|-------------|
| **Phases** | Discrete work units with contracts, state transitions, and assigned agents |
| **Roles** | Planner, Implementer, Auditor, Reviewer, Tester, Fixer, Human Owner — any AI or human can fill any role |
| **Append-only logs** | Every implementation, audit, review, test, and fix appends a JSONL record — never rewritten |
| **Blockers** | Tracked per phase, resolve with fix references |
| **Handoffs** | Structured task handoffs between agents with context and acceptance criteria |
| **Budget profiles** | Low / Medium / High / Full — controls context retrieval depth and token estimates |

### Architecture

The system is layered into four read-model layers that help AI agents navigate the project safely:

- **Operation Manifest** — code-owned metadata describing every `workflow.*` operation, its access level (Read / Append / Write / Regenerate), and write-target safety metadata.
- **Tool Routing** — intent-to-operation guidance: given a task description, recommends which operation to call.
- **Context Index** — rebuildable SQLite/search layer derived from authoritative JSON/JSONL sources under `.workflowkit/index/`.
- **Pointer and Slice APIs** — read-only access to exact files, records, JSON Pointer values, and generated markdown regions. Never exposes raw editable line ranges.
- **Budget-Aware Retrieval** — `workflow.get_relevant_context` is budget-aware and pointer-first, using local hybrid retrieval/reranking, budget reports, and token estimates.

Source of truth is always `.workflowkit/` JSON/JSONL. Generated markdown and indexes are rebuildable — if they disagree with source JSON/JSONL, source wins. All writes route through dispatcher-backed `workflow.*` operations; the model never writes `.workflowkit/` files directly.

## Agent Setup

`bfwf init` creates `AGENTS.md` and `CLAUDE.md` at the project root. Each contains one pointer:

> Read `workflow/Rules.md` first before making workflow-related changes.

For other tools, add that same line to the instructions file your tool reads:

| Tool | Instructions file |
|------|-------------------|
| Codex (OpenAI) | `AGENTS.md` — created automatically |
| Claude Code | `CLAUDE.md` — created automatically |
| Cursor | `.cursor/rules/` |
| GitHub Copilot | `.github/copilot-instructions.md` |
| Windsurf | `.windsurfrules` |
| Cline | `.clinerules` |

`AGENTS.md` and `CLAUDE.md` are user-owned — `bfwf sync-markdown` only touches the `<!-- BEKAFORGE:BEGIN ... -->` generated region inside them.

## CLI Reference

| Command | Description |
|---------|-------------|
| `bfwf init "Name"` | Initialize a new workflow project |
| `bfwf status` | Print workflow state and phase list |
| `bfwf status --watch` | Live-updating status dashboard |
| `bfwf tui` | Interactive terminal dashboard (read-only) |
| `bfwf validate` | Validate workflow consistency |
| `bfwf sync-markdown` | Regenerate all markdown docs |
| `bfwf manifest` | List all registered operations with access levels |
| `bfwf recommend --task "..."` | Recommend operations for a task description |
| `bfwf context --phase PHASE-001` | Get context pointers for a phase |
| `bfwf budget --budget Medium` | Show or set project budget profile |
| `bfwf log implementation\|audit\|review\|test\|fix` | Create append-only log records |
| `bfwf phase status\|assign` | Manage phase state and assignment |
| `bfwf blocker add\|resolve` | Record or resolve blockers |
| `bfwf server start\|stop` | Manage loopback HTTP server |

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
├── BekaForge.WorkflowKit.AgentContracts/  Operation DTOs, error codes
├── BekaForge.WorkflowKit.Storage/        .workflowkit/ persistence, hybrid retrieval
├── BekaForge.WorkflowKit.Cache/          Context package caching
├── BekaForge.WorkflowKit.Markdown/       Markdown sync with human-preserving regions
├── BekaForge.WorkflowKit.Server/         Loopback HTTP JSON API
├── BekaForge.WorkflowKit.Cli/            bfwf CLI (cross-platform .NET global tool)
└── BekaForge.WorkflowKit.Mcp/            MCP stdio adapter with project registry
tests/
└── BekaForge.WorkflowKit.Tests/          649 xUnit tests
```

## Build & Test

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## License

MIT — see [LICENSE](LICENSE).
