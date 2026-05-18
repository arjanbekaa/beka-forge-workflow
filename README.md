# Beka Forge Workflow

Beka Forge Workflow is a local-first .NET CLI for structured AI-assisted development. It gives agents and humans a shared workflow with phases, contracts, append-only logs, validation, blockers, and generated context files under `.workflowkit/`.

It is designed for Codex, Claude Code, Cursor, GitHub Copilot, Windsurf, Cline, and any MCP-compatible client.

## Install

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
bfwf --help
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

To update an existing install:

```bash
dotnet tool update --global BekaForge.WorkflowKit.Cli
```

Package page:

- [nuget.org/packages/BekaForge.WorkflowKit.Cli](https://www.nuget.org/packages/BekaForge.WorkflowKit.Cli/)

## Quick Start

```bash
bfwf init "My Project"
bfwf status
```

`bfwf init` creates a `.workflowkit/` folder in the current project and root instruction files such as `AGENTS.md` and `CLAUDE.md`. Those runtime files are local workflow artifacts and are ignored by this repository's `.gitignore`.

## What It Does

- Tracks work as explicit `PHASE-NNN` items with a state machine.
- Stores machine-readable state in JSON and append-only history in JSONL.
- Generates human-readable markdown from structured state.
- Forces honest validation logging with evidence.
- Supports CLI, HTTP, and MCP access through the same dispatcher.
- Provides context retrieval, caching, and a terminal UI.
- Includes a deterministic orchestration runtime with human-attention escalation.

## Repository Layout

```text
src/
  BekaForge.WorkflowKit.Core/           Domain models and state machine rules
  BekaForge.WorkflowKit.AgentContracts/ Shared DTOs and operation contracts
  BekaForge.WorkflowKit.Storage/        .workflowkit persistence and retrieval
  BekaForge.WorkflowKit.Cache/          In-memory context package cache
  BekaForge.WorkflowKit.Markdown/       Markdown generation and merge logic
  BekaForge.WorkflowKit.Server/         HTTP API and operation handlers
  BekaForge.WorkflowKit.Cli/            bfwf CLI and terminal UI
  BekaForge.WorkflowKit.Mcp/            MCP host and tool mapping
tests/
  BekaForge.WorkflowKit.Tests/          xUnit coverage
```

## Workflow Data

All workflow runtime state lives under `.workflowkit/`.

```text
.workflowkit/
  workflow.json
  phases/
  logs/
  blockers/
  handoffs/
  evidence/
  index/
  workflow/
```

Current-state files are updated atomically. Historical logs are append-only. Read models such as SQLite indexes and generated markdown are rebuildable and are not the source of truth.

## Core Commands

```bash
bfwf init "Name"
bfwf status
bfwf status --watch
bfwf tui
bfwf validate
bfwf sync-markdown
bfwf phase list
bfwf phase show --phase PHASE-001
bfwf phase status --phase PHASE-001 --state ReadyForImplementation
bfwf log implementation --phase PHASE-001 --summary "Implemented feature"
bfwf validation plan --phase PHASE-001
bfwf validation log --phase PHASE-001 --type AutomatedCommand --result Passed --summary "Tests passed"
bfwf blocker add --phase PHASE-001 --reason "Waiting on dependency"
bfwf mcp --root /path/to/project
bfwf server start
bfwf orchestration start --phase PHASE-001
bfwf orchestration attention --session ORS-001
```

Run `bfwf --help` for the full command surface.

## Orchestration

The orchestration runtime coordinates delegated implementation, audit, review, validation, and fix loops without creating a second workflow state machine.

- All orchestration writes still go through shared handlers.
- Sessions expose machine-readable attention flags and a derived attention outcome.
- Human-required validation, blocked environments, external-tool dependencies, and retry-budget exhaustion remain explicit instead of being hidden inside a nominal pass.

Useful commands:

```bash
bfwf orchestration start --phase PHASE-NNN --objective "Implement the phase" --scope "Server and tests"
bfwf orchestration status --session ORS-NNN
bfwf orchestration attention --session ORS-NNN
bfwf orchestration request-human --session ORS-NNN --human-validation-required --reason "Manual validation is required"
bfwf orchestration clear-attention --session ORS-NNN --flags HumanValidationRequired
```

## Validation Model

Beka Forge Workflow enforces honest validation:

- `Passed` and `PassedWithWarnings` require evidence.
- Human-only validation cannot be marked passed by an LLM actor.
- Skipped validation requires a reason.
- `SkippedNotPossible` requires an explicit risk note.

Useful commands:

```bash
bfwf validation plan --phase PHASE-NNN
bfwf validation request-user --phase PHASE-NNN
bfwf validation log --phase PHASE-NNN --type AutomatedCommand --result Passed --summary "All tests passed"
bfwf validation skip --phase PHASE-NNN --reason "No validation needed" --approved-by HumanOwner
```

## Agent Setup

Agents should read `.workflowkit/workflow/Rules.md` before doing workflow-tracked work.

For tools that support instruction files, point them at:

- `AGENTS.md` for Codex-compatible setups
- `CLAUDE.md` for Claude Code
- your tool's equivalent rules file for other environments

## MCP and HTTP

Start MCP on a single project:

```bash
bfwf mcp --root /path/to/project
```

Start the local HTTP server:

```bash
bfwf server start
```

Important HTTP routes:

- `GET /api/health`
- `GET /api/workflow/operations`
- `POST /api/workflow/{operation-name}`
- `POST /api/shutdown`

## Build and Test

```bash
dotnet restore
dotnet build BekaForge.WorkflowKit.slnx -c Release
dotnet test tests/BekaForge.WorkflowKit.Tests/BekaForge.WorkflowKit.Tests.csproj -c Release
```

## Release Notes

Use GitHub pushes and NuGet publishes separately:

- Push to GitHub whenever you want source control history, review, or collaboration.
- Publish NuGet only when package consumers need a new version.
- Bump the package version before every NuGet publish.

The CLI package version lives in `src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj`.

Current release target:

- Package ID: `BekaForge.WorkflowKit.Cli`
- Tool command: `bfwf`
- Version: `1.0.2`
- Public package URL: [nuget.org/packages/BekaForge.WorkflowKit.Cli/1.0.2](https://www.nuget.org/packages/BekaForge.WorkflowKit.Cli/1.0.2)

Copy-paste release flow:

```powershell
$version = "1.0.2"
$packageId = "BekaForge.WorkflowKit.Cli"
$nupkgDir = ".artifacts/nuget"
$source = "https://api.nuget.org/v3/index.json"

dotnet test tests/BekaForge.WorkflowKit.Tests/BekaForge.WorkflowKit.Tests.csproj -c Release
dotnet tool uninstall --global $packageId
dotnet pack src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj -c Release -o $nupkgDir
dotnet nuget push "$nupkgDir/$packageId.$version.nupkg" --api-key $env:NUGET_API_KEY --source $source --skip-duplicate
dotnet tool install --global $packageId --version $version
bfwf --help
```

If you prefer to update an existing public install instead of uninstalling first:

```powershell
dotnet tool update --global BekaForge.WorkflowKit.Cli --version 1.0.2
```

## License

MIT. See [LICENSE](LICENSE).
