# SQLFluff for SSMS

[![Build](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/build.yml/badge.svg)](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/build.yml)
[![Release](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/release.yml/badge.svg)](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/mnigliazzo/sqlfluff-extension-ssms)](https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Integrates SQLFluff (the SQL linter and formatter) into SQL Server Management Studio 22.

## Why SQLFluff?

SSMS's built-in formatter and "poor man's SQL formatter"-style tools only reformat locally, inside the IDE, with no way to enforce the same rules anywhere else. SQLFluff is a standalone CLI, so:

- **The same tool runs in CI**: whatever `sqlfluff lint`/`fix` says in SSMS is exactly what a pipeline running plain `sqlfluff` against the repo will say — no separate "IDE formatter" and "CI linter" that can silently drift apart.
- **Rules are configurable, not fixed**: a project's `.sqlfluff` controls which rules run, per-rule settings, and dialect-specific behavior — this extension doesn't hardcode a style, it just shells out to whatever config the project already has (or falls back to Options if there's none).
- **Not tied to T-SQL**: SQLFluff supports many dialects (`tsql`, `postgres`, `snowflake`, `bigquery`, ...), so the same extension/config approach isn't a dead end if a project ever needs a different one.
- **Deterministic, not AI-generated**: linting and fixing are rule-based, not an LLM guessing at formatting — the same input always produces the same output, and every change traces back to a specific, documented rule code instead of an unexplainable model decision.

### Why wrap SQLFluff instead of bundling/reimplementing it?

This extension never ships its own copy of SQLFluff or reimplements its rules — it always shells out to whatever `sqlfluff` the user has installed (see [SQLFluff tool setup](#sqlfluff-tool-setup)). That's deliberate, so this project can just ride on SQLFluff's own ongoing development instead of duplicating it:

- SQLFluff ships new rules, dialects, and bugfixes on its own release cycle, maintained by a much larger community than this extension has. Wrapping the real CLI means this extension automatically picks all of that up the moment the user upgrades `sqlfluff` via pip — it doesn't need its own release to catch up, and doesn't need to re-solve problems SQLFluff has already solved.
- It's what makes the CI-parity point above actually true. If the extension reimplemented or vendored its own partial copy of SQLFluff's rule engine, it could drift from what a pipeline running the real `sqlfluff` enforces; shelling out to the same executable makes that drift impossible by construction.

## Features

- **Lint on open, on save, and while typing**: Diagnostics in Error List with squiggles in the editor, matching how linters in other IDEs (e.g. ESLint in VS Code) behave — all independently configurable, on by default
- **Lint**: Check the selection, or the whole document when nothing is selected
- **Fix**: Apply all of SQLFluff's fixable rules to the selection or document
- **Format**: Apply only SQLFluff's safe, stable subset of rules (like a formatter, not a full auto-fixer)
- **Format on open, and Format/Fix on save**: optionally auto-rewrite on those triggers too, like VS Code/ESLint's "format/fix on save" — all off by default, and **Fix on save in particular can rewrite structure, not just style, on every save with no per-change review** (see [Configuration](#configuration) before turning it on)
- **Folder-wide commands**: run Lint/Fix/Format across every `.sql` file under the open folder, not just the active document
- **Light Bulb integration** (`Alt+.`): quick actions on a squiggle, including a "Fix this issue" that fixes just that one violation (best-effort — see below), a "Suppress this issue" that inserts an inline `-- noqa: <rule>` comment (sqlfluff's own suppression mechanism, so a pipeline running plain `sqlfluff` honors it the same way), plus whole-document Fix/Format
- **Optional auto-save**: have Fix/Format save the document automatically (off by default — see [Configuration](#configuration))
- **Configurable**: Dialect, rules, exclusions, and triggers
- **T-SQL ready**: Ships with `tsql` as the default dialect
- **Update checks from within SSMS**: SQLFluff > Check for Updates... checks GitHub for a newer release and offers to download and install it — no separate manual download needed (see [Updating](#updating))
- **No manual `pip install` needed**: on startup (and via SQLFluff > Install/Update SQLFluff Tool...), the extension checks whether the `sqlfluff` tool itself is installed and up to date, and offers to install/upgrade it via pip if not — see [SQLFluff tool setup](#sqlfluff-tool-setup)
- **In-product help**: SQLFluff > Extension Help / Documentation opens this README in your browser; SQLFluff > SQLFluff Documentation prints `sqlfluff --help` to the output pane (works offline) plus a link to the full docs.sqlfluff.com reference for rules/dialects/config
- **AI assistant integration**: a separate MCP server (`SqlFluff.Mcp`, not installed by this VSIX) exposes the same lint/fix/format pipeline to GitHub Copilot in SSMS — see [AI assistant integration (MCP)](#ai-assistant-integration-mcp)

## Installation

### Prerequisites

- SQL Server Management Studio 22.x
- Python 3.7+ with pip (needed so the extension can install/update SQLFluff itself for you — see below). If you'd rather install SQLFluff yourself ahead of time:
  ```bash
  pip install sqlfluff
  ```

### Install

1. Download `SqlFluff.Ssms.vsix` from Releases
2. Double-click the file or run:
   ```bash
   .\SqlFluff.Ssms.vsix
   ```
3. Restart SSMS

### Updating

Once installed, new versions no longer require a manual download: **SQLFluff > Check for Updates...** checks GitHub for the latest release and, if it's newer than what's installed, offers to download and launch the installer for you (the same installer that runs when you double-click a `.vsix` — SSMS may need to close for it to finish). The extension also checks silently on startup and notes an available update in the SQLFluff output pane and status bar without downloading anything on its own; disable that with **Check for updates on startup** in Options (see [Configuration](#configuration)).

### SQLFluff tool setup

This is about the `sqlfluff` *tool* itself (the Python linter this extension shells out to) — separate from the extension update check above. Every time SSMS starts, and any time you run **SQLFluff > Install/Update SQLFluff Tool...**, the extension:

1. Checks whether `sqlfluff` is reachable at all. If not, it prompts to install it now via `pip install sqlfluff` (this needs Python and pip already on `PATH` — installing Python itself is out of scope).
2. If it is reachable, checks its version against the latest release on PyPI and, if outdated, prompts to upgrade via `pip install --upgrade sqlfluff`.

Either prompt, if accepted, runs pip in the background and streams its output to the SQLFluff output pane; declining either one just leaves a note in the status bar and output pane instead of pip installing/upgrading anything. Disable the startup check entirely with **Check SQLFluff tool on startup** in Options (see [Configuration](#configuration)) — the manual command still runs it on demand either way.

## Usage

**Tools > SQLFluff**:
- **Lint (Selection or Document)** — Check and show issues in the selection, or the whole document
- **Fix (Selection or Document)** — Apply all fixable rules to the selection or document
- **Format (Selection or Document)** — Apply only the safe, stable subset of rules to the selection or document
- **Clear Diagnostics** — Remove diagnostics
- **Lint/Fix/Format All Files in Folder** — run against every `.sql` file under the open folder instead of just the active document. Fix/Format confirm before rewriting files on disk and skip any file with unsaved editor changes. Progress is reported in the status bar as each file is processed (`fixing 12/80 — path\to\file.sql`); running the same command again while it's in progress cancels it.
- **Options** — Configure the extension
- **Check for Updates...** — Check GitHub for a newer version of the extension and, if found, offer to download and install it (see [Updating](#updating))
- **Install/Update SQLFluff Tool...** — Check whether the `sqlfluff` tool is installed and up to date, and offer to install/upgrade it via pip if not (see [SQLFluff tool setup](#sqlfluff-tool-setup))

**Keyboard**:
- `Ctrl+K, Ctrl+Shift+L` — Lint
- `Ctrl+K, Ctrl+Shift+F` — Fix
- `Ctrl+K, Ctrl+Shift+D` — Format

**Light bulb** (`Alt+.`) on a squiggle for quick actions, including "Fix this issue". SQLFluff can only fix by rule across a whole span of SQL, not by violation instance, so this runs a scoped fix for just that rule and then keeps only the change touching the clicked line — other occurrences of the same rule elsewhere in the file are left alone.

> The query editor's right-click context menu is SSMS's own custom menu, not extensible by third-party extensions — see [#27](../../issues/27) for why there's no right-click entry here.

### Folder-wide commands: encoding

Files open in the editor are always read from the live buffer, so this only applies to closed files read straight off disk. UTF-8 (with or without a byte-order mark) and UTF-16 (with a byte-order mark) round-trip correctly. A file with no byte-order mark that isn't valid UTF-8 (e.g. a BOM-less ANSI/Windows-1252 file with special characters) is skipped rather than risk corrupting it — it shows up as "skipped" in the Output pane summary. Save it as UTF-8 to include it in the batch.

## Configuration

**Tools > Options > SQLFluff**:

| Setting | Default |
|---------|---------|
| Extension version | *(read-only)* — the installed extension's own version. Also logged once to the SQLFluff output pane when SSMS starts. There's no in-product listing of third-party extensions in SSMS 22 to check this otherwise. |
| Executable | `sqlfluff` (auto-detect) |
| Dialect | `tsql` |
| Config file | *(empty)* — fallback only, see below |
| Timeout | 60 seconds |
| Auto-save after fix | ✗ (Fix/Format leave the document dirty; you save manually) |
| Lint on open | ✓ (diagnostics only — never rewrites the document) |
| Format on open | ✗ (rewrites the buffer, marking it dirty, the moment a file is shown — off by default since it happens with no explicit action from you) |
| Lint on save | ✓ |
| Format on save | ✗ (runs Format before the file is written when you save, like "format on save" in other editors) |
| Fix on save | ✗ — **caution**: unlike Format, Fix can rewrite structure, not just style, on every save with no per-change review; try Fix manually first and review the diff before enabling this. Wins over Format on save if both are on |
| Lint while typing | ✓ |
| Report violations as | Warning |
| Check for updates on startup | ✓ — silent unless a newer release is found (see [Updating](#updating)); never downloads or installs anything on its own |
| Check SQLFluff tool on startup | ✓ — checks whether the `sqlfluff` tool is installed and up to date, prompting to install/upgrade via pip if not (see [SQLFluff tool setup](#sqlfluff-tool-setup)); turn off on a machine without PyPI access, or to manage sqlfluff yourself |

**Config file priority**: a `.sqlfluff` found by walking up from the open document's folder (or, for an unsaved new document, from the currently open folder/project root) always wins over the "Config file" set here. That Options setting is only a fallback for documents with no `.sqlfluff` findable near them at all.

## AI assistant integration (MCP)

This VSIX doesn't install or configure this on its own — it's a separate, standalone piece you set up once if you want it.

[`src/SqlFluff.Mcp`](src/SqlFluff.Mcp) is a second, independent front-end over the same lint/fix/format pipeline, exposed as an [MCP](https://modelcontextprotocol.io) server over stdio, so GitHub Copilot (or any other MCP-capable AI assistant) in SSMS can run SQL it just wrote through the project's actual `.sqlfluff` config before handing it back to you — instead of guessing at formatting. It has no dependency on this VSIX and isn't installed by it; see its own [README](src/SqlFluff.Mcp/README.md) for the full tool reference.

**Setup:**

1. Build it (requires the [.NET 8 SDK](https://dotnet.microsoft.com/download), nothing SSMS/VS-specific):
   ```bash
   dotnet build src\SqlFluff.Mcp\SqlFluff.Mcp.csproj --configuration Release
   ```
   Output: `src\SqlFluff.Mcp\bin\Release\net8.0\SqlFluff.Mcp.dll`
2. Register it with Copilot in SSMS — either:
   - **From Copilot Chat**: open the **Tools** panel → **+** → **Add custom MCP server** → Server ID `sqlfluff`, Type `stdio`, Command `dotnet`, Args the full path to the DLL from step 1. New tools are added disabled by default — enable them in the same panel.
   - **By hand**: create/edit `%USERPROFILE%\.mcp.json`:
     ```json
     {
       "servers": {
         "sqlfluff": {
           "type": "stdio",
           "command": "dotnet",
           "args": ["C:\\full\\path\\to\\SqlFluff.Mcp.dll"]
         }
       }
     }
     ```
     SSMS picks up the change and initializes the server automatically on save.

See Microsoft's [Use MCP servers with GitHub Copilot in SQL Server Management Studio](https://learn.microsoft.com/ssms/github-copilot/mcp-servers) for the host side of this (registry install, per-solution vs. global config, tool approval).

**Why this isn't wired up automatically:** the VSIX installs into a version/instance-specific folder that changes on every update (including this extension's own self-update), so anything the installer wrote into `.mcp.json` pointing at that path would go stale the next time the extension updates or is reinstalled. Doing this safely means bundling the MCP server inside the VSIX and having it copy itself to a stable, version-independent location on every load — which hasn't been built yet (see [its README](src/SqlFluff.Mcp/README.md) for the current state).

## Building

```bash
cd src\SqlFluff.Ssms
dotnet restore
msbuild SqlFluff.Ssms.csproj /p:Configuration=Release
```

Output: `bin\Release\SqlFluff.Ssms.vsix`

## License

MIT

## Contributing

This is an open-source project. We welcome bug reports, feature requests, and pull requests from the community.

- **Report bugs**: [GitHub Issues](../../issues)
- **Suggest features**: [GitHub Discussions or Issues](../../issues)
- **Contribute code**: See [CONTRIBUTING.md](CONTRIBUTING.md)

## Code of Conduct

All contributors are expected to follow the [Code of Conduct](/.github/CODE_OF_CONDUCT.md).

## Changelog

Release notes live on the [Releases page](../../releases), each with the compiled VSIX attached. [CHANGELOG.md](CHANGELOG.md) has the hand-written history through v1.2.1; every release after that is generated automatically from closed Issues (see below).

### Release Process

Fully automatic — there's no manual version-bump or milestone-rename step. The VSIX is never built or committed by hand either; it's always produced by the CI pipeline, straight from a clean checkout of `main`. Release notes come from closed Issues, not from the CHANGELOG or PR descriptions — see [CONTRIBUTING.md](CONTRIBUTING.md#issues-milestones--releases) for how to file an issue so it shows up correctly.

1. User-facing changes start as a GitHub Issue, assigned to the `Unreleased` [milestone](../../milestones), labeled `enhancement`/`bug`/etc.
2. Branch, PR (referencing the issue, e.g. "Closes #12"), review, merge — as described above.
3. Every push to `main` re-runs the **release** workflow, which:
   - Does nothing if the `Unreleased` milestone has no closed issues (so a push with nothing user-facing just doesn't cut a release)
   - Otherwise computes the next version itself — `minor` if any closed issue is labeled `enhancement`, else `patch` — from the latest published release tag (no file in the repo holds the version; `main`'s branch protection blocks the workflow from pushing a bump back to it anyway)
   - Builds, groups the closed issues by label into Added/Fixed/Changed/Other, and publishes a GitHub Release tagged `vX.Y.Z` with the freshly built `SqlFluff.Ssms.vsix` attached
   - Rotates `Unreleased` to `vX.Y.Z` (closed) and creates a fresh `Unreleased` for what comes next
4. A `major` bump has no automatic signal — trigger the workflow manually ("Run workflow" on the Release workflow in the Actions tab) with its `bump` input set to `major` (or `minor`/`patch`) when one is actually needed.

## Support & Feedback

- **Questions**: Open a [GitHub Issue](../../issues)
- **Bugs**: Report with steps to reproduce
- **Feature ideas**: Share in Issues or Discussions

---

**Happy linting! 🎯**
