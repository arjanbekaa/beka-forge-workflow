# Beka Forge Workflow

Beka Forge Workflow is a local, cross-platform developer tool centered on the .NET CLI, dispatcher-backed workflow operations, terminal visibility, and append-only audit history.

Compatibility note: `.workflowkit` remains the v1 source-of-truth folder, `bfwf` remains supported, and future `bfk` support must be additive and backward-compatible.

## Status

Roadmap state now extends through PHASE-026. PHASE-019 through PHASE-026 are implemented: handler-only offline writes, CLI productization, naming compatibility, rich terminal output, TUI, budget-aware hybrid context retrieval, documentation/release packaging, and the global MCP adapter with project registry support.


## Quick Start

```bash
# Restore packages
dotnet restore

# Build all projects
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release
```

## Agent Setup (Codex, Claude Code, Cursor, Copilot, etc.)

`bfwf init` creates `AGENTS.md` and `workflow/Rules.md` at the project root.
`AGENTS.md` contains one pointer:

> Read `workflow/Rules.md` first before making workflow-related changes.

To teach your AI coding tool about Beka Forge Workflow, add that same line to the
instructions file your tool reads:

| Tool | Instructions file | What to do |
|------|------------------|------------|
| **Codex (OpenAI)** | `AGENTS.md` | Already created by `bfwf init` |
| **Claude Code** | `CLAUDE.md` | Copy or symlink: `cp AGENTS.md CLAUDE.md` |
| **Cursor** | `.cursor/rules/` | Add a rule file pointing to `workflow/Rules.md` |
| **GitHub Copilot** | `.github/copilot-instructions.md` | Add the pointer line |
| **Windsurf** | `.windsurfrules` | Add the pointer line |
| **Cline** | `.clinerules` | Add the pointer line |
| **Any other agent** | (varies) | Add: `Read workflow/Rules.md first.` |

The canonical workflow rules live in `workflow/Rules.md`. `AGENTS.md` (and any
equivalent you create) is user-owned — `bfwf sync-markdown` only touches the
generated `<!-- BEKAFORGE:BEGIN ... -->` region inside it.

## If Your Local .NET SDK Is Broken

If `dotnet restore` fails with `Value cannot be null. (Parameter 'path1')` or `InstallerBase..cctor()` errors:

1. **Repair the SDK** (admin terminal):
   ```powershell
   # Option A: Repair via winget
   winget install Microsoft.DotNet.SDK.8 --force

   # Option B: Download and run the installer in repair mode
   # https://dotnet.microsoft.com/download/dotnet/8.0
   ```

2. **Verify the fix:**
   ```bash
   dotnet --info          # Should print SDK/runtime versions without errors
   dotnet new console -o test-app && cd test-app && dotnet run  # Quick smoke test
   ```

3. **If repair fails**, uninstall all .NET SDKs from `Apps & Features`, then install .NET 8 SDK fresh.

## Project Structure

```
src/
  BekaForge.WorkflowKit.Core/          Domain models, state machine
  BekaForge.WorkflowKit.AgentContracts/ Operation DTOs, error codes
  BekaForge.WorkflowKit.Storage/       .workflowkit/ file persistence with legacy .bekaforge compatibility
  BekaForge.WorkflowKit.Markdown/      Human-readable markdown sync
  BekaForge.WorkflowKit.Server/       Loopback HTTP JSON API
  BekaForge.WorkflowKit.Cli/          bfwf CLI, planned as the primary cross-platform product surface
tests/
  BekaForge.WorkflowKit.Tests/        xUnit test suite
docs/
  Architecture.md                      System design
  ImplementationPlan.md                Phase plan
  Phase1Contract.md                    Phase 1 acceptance criteria
```

## Running the Server

```bash
cd src/BekaForge.WorkflowKit.Server
dotnet run -- --root /path/to/workflow --port 5000
```

Endpoints:
- `GET  /api/health` — health, manifest coverage, index status
- `GET  /api/workflow/operations` — registered operation names
- `POST /api/workflow/{operation-name}` — dispatch any operation
- `POST /api/shutdown` — graceful shutdown

## Using the CLI (bfwf, future bfk)

The CLI is the primary Beka Forge Workflow surface. The existing `bfwf` command remains supported; `bfk` is a future preferred alias, not a breaking replacement.

PHASE-022 uses `Spectre.Console` for rich terminal output. It was selected because it is cross-platform, global-tool friendly, and keeps formatting in the presentation layer while `--json` stays machine-clean and `--plain` remains available for dumb terminals, CI, and agent automation.

PHASE-023 adds the optional terminal dashboard:

```bash
bfwf tui
```

The TUI is read-only by default and routes every write-capable action through existing dispatcher operations.

```bash
cd src/BekaForge.WorkflowKit.Cli
dotnet run -- init "My Asset"
dotnet run -- status
dotnet run -- validate
dotnet run -- sync-markdown
dotnet run -- manifest
dotnet run -- recommend --task "log implementation" --phase PHASE-001
dotnet run -- context --phase PHASE-001
dotnet run -- budget --budget Medium
dotnet run -- validate-request --operation workflow.create_implementation_log
dotnet run -- index-health
dotnet run -- phase show --phase PHASE-022
dotnet run -- status --watch --interval 5
dotnet run -- help
```

### MCP host

Beka Forge Workflow now exposes the dispatcher through an MCP stdio host:

```bash
# Global multi-project mode. Each tool call must provide root or projectId.
dotnet run --project src/BekaForge.WorkflowKit.Cli -- mcp

# Single-project mode for local debugging.
dotnet run --project src/BekaForge.WorkflowKit.Cli -- mcp --root E:\path\to\project
```

Tool names match the canonical `workflow.*` operation names. In global mode, a client may target a project either by:

- `root`: absolute workflow project path
- `projectId`: entry from `%APPDATA%/BekaForge/mcp-registry.json`

The MCP layer resolves the target root first, then routes every call through `OperationDispatcher`. It does not write `.workflowkit` files directly.

### Client setup

Claude Desktop example:

```json
{
  "mcpServers": {
    "workflowkit": {
      "command": "bfwf",
      "args": ["mcp", "--root", "E:\\path\\to\\workflow"]
    }
  }
}
```

Global multi-project Claude Desktop example:

```json
{
  "mcpServers": {
    "workflowkit": {
      "command": "bfwf",
      "args": ["mcp"]
    }
  }
}
```

Codex or other MCP clients should launch the same stdio command:

```bash
bfwf mcp --root E:\path\to\workflow
```

or, for explicit multi-project routing:

```bash
bfwf mcp
```

Generic client request example:

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"workflow.get_state","arguments":{"root":"E:\\path\\to\\workflow"}}}
```

Full CLI reference:

| Command | Description |
|---|---|
| `bfwf init "Name"` | Initialize a new workflow project |
| `bfwf status` | Print workflow state and phase list |
| `bfwf validate` | Validate workflow consistency |
| `bfwf sync-markdown` | Regenerate markdown docs |
| `bfwf manifest` | Print operation manifest grouped by access level |
| `bfwf recommend --task "..."` | Recommend operations for a task description |
| `bfwf context [--phase PHASE-NNN]` | Print context pointers for a phase or workflow |
| `bfwf context --budget Low\|Medium\|High\|Full` | Override context budget for one request |
| `bfwf budget [--budget Low\|Medium\|High\|Full]` | Show or set project budget profile |
| `bfwf validate-request --operation "..."` | Validate an operation request for safety |
| `bfwf index-health` | Rebuild index and show health summary |
| `bfwf tui` | Open the interactive terminal dashboard |
| `bfwf log implementation\|audit\|review\|test\|fix` | Create append-only log records |
| `bfwf phase status\|assign` | Manage phase state and assignment |
| `bfwf blocker add\|resolve` | Record or resolve blockers |
| `bfwf server start\|stop\|status` | Manage loopback HTTP server |

## Installation and Packaging

The release path is a .NET global tool. JavaScript package or bootstrapper distribution is not part of the current roadmap.

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
bfwf --help
```

For local prerelease validation, pack and install the CLI package from the repository output once packaging metadata is finalized. The WPF dashboard remains an optional Windows companion and is not required for CLI/TUI usage.

Local package/install flow:

```bash
dotnet pack src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj -c Release
dotnet tool install --global --add-source src/BekaForge.WorkflowKit.Cli/bin/Release BekaForge.WorkflowKit.Cli
bfwf --help
```

Tool update/remove:

```bash
dotnet tool update --global --add-source src/BekaForge.WorkflowKit.Cli/bin/Release BekaForge.WorkflowKit.Cli
dotnet tool uninstall --global BekaForge.WorkflowKit.Cli
```

## CI

GitHub Actions runs `dotnet restore`, `dotnet build`, and `dotnet test` on every push. See `.github/workflows/ci.yml`.

## Architecture Summary

Beka Forge Workflow is structured around four read-model layers that help AI agents navigate the project:

- **Operation Manifest** — code-owned metadata describing every `workflow.*` operation, its access level (Read/Append/Write/Regenerate), and write-target safety metadata.
- **Tool Routing** — intent-to-operation guidance: given a task description, recommends which operation to call.
- **Context Index** — rebuildable SQLite/search layer under `.workflowkit/index/` derived from authoritative JSON/JSONL sources.
- **Pointer and Slice APIs** — read-only access to exact files, records, JSON Pointer values, and generated markdown regions. Never exposes raw editable line ranges.
- **Budget-Aware Retrieval (PHASE-024)** — `workflow.get_relevant_context` is budget-aware and pointer-first, using local hybrid retrieval/reranking, budget reports, token estimates, and rebuildable read-model indexes before any LLM receives context. Vector search remains optional/deferred unless it can be added safely.

Source of truth: `.workflowkit/workflow.json`, `.workflowkit/phases/*.json`, and append-only JSONL logs. The index and generated manifest are rebuildable read models — if they disagree with source JSON/JSONL, source JSON/JSONL wins.

All write guidance routes through safe Beka Forge Workflow operations. Write targets identify operation handlers, not raw file paths or line ranges.

