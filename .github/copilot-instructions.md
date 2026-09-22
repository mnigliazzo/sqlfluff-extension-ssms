# Copilot instructions for this repo

This is a VSIX extension (in-proc VSSDK package, `net48`) integrating [SQLFluff](https://sqlfluff.com) — a Python SQL linter/formatter — into SQL Server Management Studio 22. SSMS 22 runs the Visual Studio 2022 shell, so this is effectively a standard VS extension targeted at `Microsoft.VisualStudio.Ssms`. SQLFluff itself is never bundled; the extension shells out to a separately-installed `sqlfluff` executable via stdin/stdout — it never reads/writes the SQL file on disk directly, always the live editor buffer.

For the full architecture write-up (execution model, the shared `ViolationStore` feeding the tagger/Error List/Light Bulb, the Fix/Format shared code path, the MEF↔AsyncPackage bridge, known local-vs-CI build gotchas), read **[CLAUDE.md](../CLAUDE.md)** at the repo root before making non-trivial changes. Don't duplicate that content here — read it directly.

## Build & test

```bash
# Main VSIX project — needs a full Visual Studio 2022 MSBuild (or SSMS's own, see CLAUDE.md for the env vars that requires locally). Do NOT use dotnet build for this one.
cd src/SqlFluff.Ssms && dotnet restore && msbuild SqlFluff.Ssms.csproj /p:Configuration=Release

# Unit tests — plain net8.0, no VS SDK needed, safe to run with dotnet directly
dotnet test tests/SqlFluff.Ssms.Tests/SqlFluff.Ssms.Tests.csproj
```

Always do a clean rebuild (`rm -rf bin obj` first) before trusting a built `.vsix` — incremental builds have repackaged a stale manifest before.

## Workflow: everything goes through an Issue and a PR

**Never commit directly to `main`** — it's protected (linear history, required `build`+`test` checks, 1 review). Branch, then PR.

- Branch naming: `feature/…`, `fix/…`, `chore/…`, `docs/…` (matches what's already in the repo's history).
- **User-facing changes need a GitHub Issue first**, assigned to the `Unreleased` milestone, with the right label (`bug`/`enhancement`). Release notes are generated automatically from closed issues in each version's milestone — not from PR descriptions, and not from a changelog file. Reference the issue in the PR ("Closes #N") so merging closes it. Pure internal/CI changes can skip this.
- Full details: **[CONTRIBUTING.md](../CONTRIBUTING.md)** (branching/merge strategy, the issue→milestone→release flow, code style).

## Things that have already bitten this repo — don't repeat them

- `release.yml` had four separate PowerShell/CI bugs (array-vs-scalar comparisons silently producing wrong booleans, an inherited non-zero exit code, `.LineNumber` returning an array with >1 match, and a missing `permissions:` block causing a 403). If you touch that workflow, test the PowerShell logic standalone (`pwsh -File script.ps1`, checking `$LASTEXITCODE`) against realistic input *before* pushing — don't rely on a green run alone.
- CI builds the VSIX with `microsoft/setup-msbuild` (the runner's own Visual Studio), never a hardcoded path to SSMS's MSBuild — SSMS isn't installed on GitHub-hosted runners.
- The tagger and Light Bulb source are exported against the generic MEF `"text"` content type (SSMS has no documented SQL-specific one) and filtered at runtime via `SqlBufferHeuristics.IsLikelySql` — don't assume `[ContentType("text")]` alone means "only SQL buffers."
