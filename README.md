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
- **Light Bulb integration** (`Alt+.`): quick actions on a squiggle, including a "Fix this issue" scoped to just that violation's rule, plus whole-document Fix/Format
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
   VSIXInstaller.exe SqlFluff.Ssms.vsix
   ```
3. Restart SSMS

## Usage

**Tools > SQLFluff**:
- **Lint (Selection or Document)** — Check and show issues in the selection, or the whole document
- **Fix (Selection or Document)** — Apply all fixable rules to the selection or document
- **Format (Selection or Document)** — Apply only the safe, stable subset of rules to the selection or document
- **Clear Diagnostics** — Remove diagnostics
- **Lint/Fix/Format All Files in Folder** — run against every `.sql` file under the open folder instead of just the active document (Fix/Format confirm before rewriting files on disk, and skip any file with unsaved editor changes)
- **Options** — Configure the extension

**Keyboard**:
- `Ctrl+K, Ctrl+Shift+L` — Lint
- `Ctrl+K, Ctrl+Shift+F` — Fix
- `Ctrl+K, Ctrl+Shift+D` — Format

**Light bulb** (`Alt+.`) on a squiggle for quick actions, including a fix scoped to just that violation's rule.

> The query editor's right-click context menu is SSMS's own custom menu, not extensible by third-party extensions — see [#27](../../issues/27) for why there's no right-click entry here.

## Configuration

**Tools > Options > SQLFluff**:

| Setting | Default |
|---------|---------|
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

The VSIX is never built or committed by hand — it's always produced by the CI pipeline, straight from a clean checkout of `main`. Release notes come from closed Issues, not from the CHANGELOG or PR descriptions — see [CONTRIBUTING.md](CONTRIBUTING.md#issues-milestones--releases) for how to file an issue so it shows up correctly.

1. User-facing changes start as a GitHub Issue, assigned to the `Unreleased` [milestone](../../milestones), labeled `enhancement`/`bug`/etc.
2. Branch, PR (referencing the issue, e.g. "Closes #12"), review, merge — as described above.
3. When ready to cut a release: rename the `Unreleased` milestone to the target version (e.g. `v1.3.0`) and close it, then bump the version in both:
   - `src\SqlFluff.Ssms\Properties\AssemblyInfo.cs` (`AssemblyVersion` / `AssemblyFileVersion`)
   - `src\SqlFluff.Ssms\source.extension.vsixmanifest` (`Identity Version`)
4. Merge that version bump to `main`. On push, the **build** workflow compiles and sanity-checks the VSIX, and the **release** workflow:
   - Skips silently if a release for that version's tag (`vX.Y.Z`) already exists (so pushes that don't bump the version don't create duplicate releases)
   - Looks up the milestone named `vX.Y.Z`, lists its closed issues grouped by label into Added/Fixed/Changed/Other, and uses that as the release notes
   - Creates a GitHub Release tagged `vX.Y.Z` with the freshly built `SqlFluff.Ssms.vsix` attached
5. Create a new `Unreleased` milestone for what comes next.

## Support & Feedback

- **Questions**: Open a [GitHub Issue](../../issues)
- **Bugs**: Report with steps to reproduce
- **Feature ideas**: Share in Issues or Discussions

---

**Happy linting! 🎯**
