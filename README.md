# SQLFluff for SSMS

[![Build](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/build.yml/badge.svg)](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/build.yml)
[![Release](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/release.yml/badge.svg)](https://github.com/mnigliazzo/sqlfluff-extension-ssms/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/mnigliazzo/sqlfluff-extension-ssms)](https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Integrates SQLFluff (the SQL linter and formatter) into SQL Server Management Studio 22.

## Features

- **Lint on save**: Diagnostics in Error List with squiggles in the editor (optionally also while typing)
- **Lint**: Check the selection, or the whole document when nothing is selected
- **Fix**: Apply all of SQLFluff's fixable rules to the selection or document
- **Format**: Apply only SQLFluff's safe, stable subset of rules (like a formatter, not a full auto-fixer)
- **Folder-wide commands**: run Lint/Fix/Format across every `.sql` file under the open folder, not just the active document
- **Light Bulb integration** (`Alt+.`): quick actions on a squiggle, including a "Fix this issue" that fixes just that one violation (best-effort — see below), a "Suppress this issue" that inserts an inline `-- noqa: <rule>` comment (sqlfluff's own suppression mechanism, so a pipeline running plain `sqlfluff` honors it the same way), plus whole-document Fix/Format
- **Optional auto-save**: have Fix/Format save the document automatically (off by default — see [Configuration](#configuration))
- **Configurable**: Dialect, rules, exclusions, and triggers
- **T-SQL ready**: Ships with `tsql` as the default dialect

## Installation

### Prerequisites

- SQL Server Management Studio 22.x
- Python 3.7+ with SQLFluff:
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

## Usage

**Tools > SQLFluff**:
- **Lint (Selection or Document)** — Check and show issues in the selection, or the whole document
- **Fix (Selection or Document)** — Apply all fixable rules to the selection or document
- **Format (Selection or Document)** — Apply only the safe, stable subset of rules to the selection or document
- **Clear Diagnostics** — Remove diagnostics
- **Lint/Fix/Format All Files in Folder** — run against every `.sql` file under the open folder instead of just the active document. Fix/Format confirm before rewriting files on disk and skip any file with unsaved editor changes. Progress is reported in the status bar as each file is processed (`fixing 12/80 — path\to\file.sql`); running the same command again while it's in progress cancels it.
- **Options** — Configure the extension

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
| Format on save | ✗ (runs Format before the file is written when you save, like "format on save" in other editors) |
| Lint on save | ✓ |
| Lint while typing | ✗ |
| Report violations as | Warning |

**Config file priority**: a `.sqlfluff` found by walking up from the open document's folder (or, for an unsaved new document, from the currently open folder/project root) always wins over the "Config file" set here. That Options setting is only a fallback for documents with no `.sqlfluff` findable near them at all.

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
