# Beka Forge Workflow

WorkflowKit is a local-first CLI workflow evidence tool that records phase-based implementation, audit, review, fix, and validation history in inspectable JSON/JSONL files.

Core today is the local CLI plus the `.workflowkit/` evidence it manages. The local HTTP server exposes the same local operations over HTTP, and the MCP host adapts those same operations for MCP clients. Neither HTTP nor MCP is the source of truth, and generated markdown is readable context rather than the source of truth.

## Start Here

Install the CLI:

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
bfwf --help
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Initialize a project and inspect the workflow:

```bash
bfwf init "My Project"
bfwf status
```

`bfwf init` creates a `.workflowkit/` folder in the current project and root instruction files such as `AGENTS.md` and `CLAUDE.md`. Those runtime files are local workflow artifacts and are ignored by this repository's `.gitignore`.

## Source Of Truth

Workflow runtime state lives under `.workflowkit/`.

- `workflow.json` and `phases/` hold current machine-readable state.
- `logs/`, `blockers/`, and `handoffs/` hold append-only history.
- Generated markdown under `.workflowkit/workflow/` is readable context, not the source of truth.

## What It Does

- Core today:
  - Tracks work as explicit `PHASE-NNN` items with a state machine.
  - Stores authoritative workflow state in JSON and append-only history in JSONL.
  - Generates readable markdown from structured state, but markdown is not the source of truth.
  - Enforces validation logging with evidence or an explicit skip reason.
- Local adapters:
  - Exposes the same handler-backed operations through the local HTTP server.
  - Exposes the same handler-backed operations through the MCP host for MCP clients.
- Future or experimental surfaces:
  - TUI, orchestration, and context/caching surfaces exist in the repository, but they are not the core public product promise in this README.

## Common Commands

```bash
bfwf init "Name"
bfwf status
bfwf status --watch
bfwf validate
bfwf phase list
bfwf phase show --phase PHASE-001
bfwf validation plan --phase PHASE-001
bfwf blocker add --phase PHASE-001 --reason "Waiting on dependency"
bfwf doc ledger
bfwf release-report
```

Run `bfwf --help` for the full command surface.

## Install

Install the global tool:

```bash
dotnet tool install --global BekaForge.WorkflowKit.Cli
bfwf --help
```

Update an existing install:

```bash
dotnet tool update --global BekaForge.WorkflowKit.Cli
```

Uninstall the global tool:

```bash
dotnet tool uninstall --global BekaForge.WorkflowKit.Cli
```

Package page:

- [nuget.org/packages/BekaForge.WorkflowKit.Cli](https://www.nuget.org/packages/BekaForge.WorkflowKit.Cli/)

## Support Matrix

| Surface | Status | Evidence | Notes |
| --- | --- | --- | --- |
| Local CLI and global tool on Windows with .NET 8 SDK | Verified | `dotnet build`, `dotnet test`, `dotnet pack`, and the documented `dotnet tool install/update/uninstall` smoke path | This is the primary public surface for this repository today. |
| Local CLI packaging shims for `osx-x64` and `linux-x64` | Limited | Tool shim runtime identifiers are present in the CLI project | Runtime packaging is configured, but this workflow does not yet treat macOS or Linux as smoke-validated release surfaces without additional evidence. |
| Local HTTP adapter | Limited | Handler-backed local adapter over the same CLI operations | Useful for local automation, but it is not the source of truth and is not treated as a separate public product. |
| Local MCP adapter | Limited | Handler-backed local adapter over the same CLI operations | Useful for local tool integration, but it is not the source of truth and is not treated as a separate public product. |
| TUI, orchestration, and other experimental repository surfaces | Unsupported | Explicitly outside the narrow public product promise in this README | These surfaces remain in the repo, but they are not release-gated as part of the core public tool promise. |

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

## Adapter Commands

```bash
bfwf sync-markdown
bfwf validation log --phase PHASE-001 --type AutomatedCommand --result Passed --summary "Tests passed"
bfwf changeset validate --file roadmap.changeset.json
bfwf changeset apply --file roadmap.changeset.json --dry-run
```

Documentation and release-readiness surfaces:

```bash
bfwf doc add --title "Persona policy MVP" --summary "Persona recommendation and validation commands are implemented." --doc-status Verified --claims "Personas are guidance only; they do not bypass workflow safety." --evidence-ids "VAL-001" --operations "workflow.recommend_persona,workflow.validate_persona_task" --commands "recommend-persona,validate-persona-task"
bfwf docs set --section documentation-policy --content manual
bfwf doc ledger
bfwf doc draft
bfwf doc coverage
bfwf release-report
bfwf validate-public-release
```

`documentation-policy` controls whether missing documentation is ignored (`off`), reported as warnings (`manual`, default), or allowed to block public release (`required`).

Run `bfwf --help` for the full command surface.

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

## ChangeSet Imports

Use ChangeSet JSON files when workflow mutations would be awkward to express as long shell arguments. They still route through validated handler-backed operations.

MVP operation types:

- `createPhase`
- `setNextAction`
- `syncMarkdown`

Example:

```json
{
  "schemaVersion": "1.0",
  "title": "Roadmap import",
  "description": "Create two ordered phases and focus the next action.",
  "operations": [
    {
      "type": "createPhase",
      "refId": "core",
      "parameters": {
        "title": "Core phase",
        "summary": "Create the core phase",
        "objective": "Implement the core workflow slice",
        "scope": "Core models, handlers, and tests",
        "acceptanceCriteria": ["Core phase exists"]
      }
    },
    {
      "type": "createPhase",
      "refId": "followup",
      "parameters": {
        "title": "Follow-up phase",
        "summary": "Create the dependent phase",
        "objective": "Implement the follow-up slice",
        "scope": "CLI and documentation",
        "dependencies": ["$ref:core"],
        "dependsOnPhaseIds": ["$ref:core"]
      }
    },
    {
      "type": "setNextAction",
      "parameters": {
        "phaseId": "$ref:followup",
        "actor": "Implementer",
        "description": "Implement the follow-up phase",
        "operationHint": "workflow.apply_changeset"
      }
    }
  ]
}
```

Commands:

```bash
bfwf changeset validate --file roadmap.changeset.json
bfwf changeset apply --file roadmap.changeset.json --dry-run
bfwf changeset apply --file roadmap.changeset.json --sync-markdown
```

Validation rejects unknown operation types, raw file mutation operations such as `writeFile`, duplicate or forward `refId` references, missing required phase contract fields, missing dependencies, and existing explicit phase IDs. Dry-run and failed validation create zero phases.

Example files live under `examples/changesets/`.

## Agent Setup

Agents should read `.workflowkit/workflow/Rules.md` before doing workflow-tracked work.

For tools that support instruction files, point them at:

- `AGENTS.md` for Codex-compatible setups
- `CLAUDE.md` for Claude Code
- your tool's equivalent rules file for other environments

## MCP and HTTP

These are local adapter surfaces over the same handler-backed operations used by the CLI. They are useful when another local tool needs WorkflowKit access, but the CLI and `.workflowkit/` state remain the primary product surface.

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
dotnet build BekaForge.WorkflowKit.slnx -c Release
dotnet test tests/BekaForge.WorkflowKit.Tests/BekaForge.WorkflowKit.Tests.csproj -c Release
```

`dotnet build` restores automatically, so an explicit `dotnet restore` is usually unnecessary unless you want a separate restore step.

## Contributing

Read `.workflowkit/workflow/Rules.md` before workflow-tracked work.

- Treat `.workflowkit/` JSON and JSONL files as workflow state owned by handlers. Do not edit them directly; use `bfwf`, the local HTTP API, or mapped MCP operations for workflow writes.
- Keep workflow history append-only. New implementation, fix, review, audit, validation, blocker, and handoff records should append through the supported operations rather than rewriting prior entries.
- Keep operation metadata and descriptors in sync with implementation. If you add or change a workflow write surface, update the shared handler, dispatcher registration, and operation metadata before exposing new CLI, HTTP, or MCP entry points.
- Prefer focused validation for the slice you changed and document results honestly. For README-only work, static inspection is usually the appropriate minimum.

## Release Notes

Use GitHub pushes and NuGet publishes separately:

- Push to GitHub whenever you want source control history, review, or collaboration.
- Publish NuGet only when package consumers need a new version.
- Bump the package version before every NuGet publish.

The CLI package version lives in `src/BekaForge.WorkflowKit.Cli/BekaForge.WorkflowKit.Cli.csproj`.

Current release target:

- Package ID: `BekaForge.WorkflowKit.Cli`
- Tool command: `bfwf`
- Version: `1.0.3`
- Public package URL: [nuget.org/packages/BekaForge.WorkflowKit.Cli/1.0.3](https://www.nuget.org/packages/BekaForge.WorkflowKit.Cli/1.0.3)

Copy-paste release flow:

```powershell
$version = "1.0.3"
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
dotnet tool update --global BekaForge.WorkflowKit.Cli --version 1.0.3
```

## License

MIT. See [LICENSE](LICENSE).
