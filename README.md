# SQLFluff for SSMS

Integrates SQLFluff (the SQL linter and formatter) into SQL Server Management Studio 22.

## Features

- **Lint on save/open**: Diagnostics in Error List with squiggles in the editor
- **Auto-fix**: Format or fix violations with one keystroke  
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
- **Lint Document** — Check and show issues
- **Fix / Format** — Apply fixes to selection or document
- **Clear Diagnostics** — Remove diagnostics
- **Options** — Configure the extension

**Keyboard**:
- `Ctrl+K, Ctrl+Shift+L` — Lint
- `Ctrl+K, Ctrl+Shift+F` — Fix

**Right-click editor** for quick Lint/Fix.

## Configuration

**Tools > Options > SQLFluff**:

| Setting | Default |
|---------|---------|
| Executable | `sqlfluff` (auto-detect) |
| Dialect | `tsql` |
| Lint on open | ✓ |
| Lint on save | ✓ |
| Lint while typing | ✗ |

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

See [CHANGELOG.md](CHANGELOG.md) for the full history of changes. All releases are also published on the [Releases page](../../releases), each with the compiled VSIX attached.

### Release Process

The VSIX is never built or committed by hand — it's always produced by the CI pipeline, straight from a clean checkout of `main`.

1. Open a feature branch and PR as described above.
2. As part of that PR (or a dedicated follow-up), add an entry to `CHANGELOG.md` under `## [Unreleased]` and bump the version in both:
   - `src\SqlFluff.Ssms\Properties\AssemblyInfo.cs` (`AssemblyVersion` / `AssemblyFileVersion`)
   - `src\SqlFluff.Ssms\source.extension.vsixmanifest` (`Identity Version`)
3. Merge the PR to `main`.
4. On push to `main`, the **build** workflow compiles and sanity-checks the VSIX, and the **release** workflow:
   - Skips silently if a release for that version's tag (`vX.Y.Z`) already exists (so pushes that don't bump the version don't create duplicate releases)
   - Otherwise extracts that version's section from `CHANGELOG.md` as the release notes
   - Creates a GitHub Release tagged `vX.Y.Z` with the freshly built `SqlFluff.Ssms.vsix` attached

## Support & Feedback

- **Questions**: Open a [GitHub Issue](../../issues)
- **Bugs**: Report with steps to reproduce
- **Feature ideas**: Share in Issues or Discussions

---

**Happy linting! 🎯**
