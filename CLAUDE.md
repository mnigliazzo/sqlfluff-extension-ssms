# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A VSIX extension (in-proc VSSDK package, targeting `net48`) that integrates [SQLFluff](https://sqlfluff.com) — a Python SQL linter/formatter — into SQL Server Management Studio 22. SSMS 22 is built on the Visual Studio 2022 shell (`Microsoft.VisualStudio.Shell.*` v18.x), so this is effectively a standard VS extension targeted at `Microsoft.VisualStudio.Ssms` instead of `Microsoft.VisualStudio.Community/Enterprise`.

SQLFluff itself is never bundled — it's a separate Python tool the user installs (`pip install sqlfluff`) and the extension shells out to.

## Build commands

There is only one project: `src/SqlFluff.Ssms/SqlFluff.Ssms.csproj`.

**Local development** (this repo was authored on a machine with SSMS 22 but no full Visual Studio, so local builds use SSMS's own bundled MSBuild):

```powershell
cd src\SqlFluff.Ssms
dotnet restore SqlFluff.Ssms.csproj
$env:MSBuildSDKsPath = "C:\Program Files\dotnet\sdk\<version>\Sdks"
$env:DOTNET_HOST_PATH = "C:\Program Files\dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\MSBuild\Current\Bin\amd64\MSBuild.exe" SqlFluff.Ssms.csproj /p:Configuration=Release
```

The three env vars are required *only* when building with SSMS's bundled MSBuild — without them it fails to resolve the SDK-style project's implicit imports. If building with a full Visual Studio 2022 install instead, plain `msbuild SqlFluff.Ssms.csproj /p:Configuration=Release` works and the env vars aren't needed (this is what CI does — see below).

Output: `src\SqlFluff.Ssms\bin\Release\SqlFluff.Ssms.vsix` (plus the loose `.dll`/`.pkgdef`).

**Always do a clean rebuild before treating a `.vsix` as release-ready** (`Remove-Item bin,obj -Recurse -Force` first). An incremental build has been observed to repackage a stale `extension.vsixmanifest` (wrong version number) even after the source manifest was edited.

## Tests

`tests/SqlFluff.Ssms.Tests/SqlFluff.Ssms.Tests.csproj` is a separate, plain `net8.0` xUnit project — no VS SDK, no VSSDK.BuildTools, no SSMS/Visual Studio MSBuild needed:

```bash
dotnet test tests/SqlFluff.Ssms.Tests/SqlFluff.Ssms.Tests.csproj
```

It only covers the pure logic that can be extracted without touching `Process`/VS SDK types — currently `Core/ArgumentQuoting.cs` (Windows command-line quoting) and `Core/LintJsonParser.cs` (parsing `sqlfluff lint --format json` output). These files are compiled directly into the test project via linked `<Compile Include>` entries rather than a `ProjectReference` to the main VSIX project — a `ProjectReference` would drag in `SqlFluff.Ssms.csproj`'s VSSDK targets and require the same SSMS/VS MSBuild the tests are trying to avoid needing.

**When adding new pure logic to `Core/`** (parsing, string manipulation, anything that doesn't touch `ITextBuffer`/`Process`/VS SDK types), prefer putting it in its own file so it can be linked into the test project the same way, and add tests for it. Logic that inherently needs the VS SDK (editor services, tagging, commands) has no test coverage and isn't expected to — there's no reasonable way to unit test that without a running SSMS/VS host.

## CI/CD

Three GitHub Actions workflows:

- **`.github/workflows/build.yml`** (`windows-latest`) — runs on push/PR to `main`. Restores, builds, sanity-checks that the VSIX/DLL/pkgdef exist, uploads the VSIX as a build artifact. This is the required status check on `main`'s branch protection.
- **`.github/workflows/test.yml`** (`ubuntu-latest`) — runs on push/PR to `main`. Just `dotnet test` on the `net8.0` test project; no MSBuild/SSMS setup needed.
- **`.github/workflows/release.yml`** (`windows-latest`) — runs on push to `main` (and manually via `workflow_dispatch`, with a `bump` input to force `patch`/`minor`/`major`). Looks up the `Unreleased` milestone; if it has no closed issues, the run is a no-op (a push with nothing user-facing just doesn't cut a release). Otherwise it computes the next version itself — `minor` if any closed issue is labeled `enhancement`, else `patch` — from the latest published release tag, builds with that version patched into the *workspace copy* of `AssemblyInfo.cs`/`source.extension.vsixmanifest` (never committed — see [Release process](#release-process)), groups the closed issues by label into Added/Fixed/Changed/Other (see [Issues, Milestones & Releases](CONTRIBUTING.md#issues-milestones--releases) in CONTRIBUTING.md), publishes a GitHub Release tagged `vX.Y.Z` with the built VSIX attached, and rotates `Unreleased` to `vX.Y.Z` (closed) plus a fresh `Unreleased`. Needs `permissions: contents: write` (create the release/tag) and `issues: write` (read issues, rename/close/create milestones) at the job level — the default `GITHUB_TOKEN` is read-only otherwise and these calls fail with a 403.

Both workflows use `microsoft/setup-msbuild` to find the MSBuild bundled with the runner's Visual Studio 2022 — **do not** hardcode a path to SSMS's MSBuild in CI; SSMS is not installed on GitHub-hosted runners. That was tried and fails (`term not recognized`); only local dev machines that happen to have SSMS (and not full VS) need the SSMS MSBuild path + env var workaround described above.

## Release process

Fully automatic, no maintainer version-bump step — that used to be manual (bump `AssemblyInfo.cs` + `source.extension.vsixmanifest`, land it as its own PR) and was easy to forget, which is exactly why it's gone. The VSIX is never hand-built-and-committed either; it's always produced fresh by CI from a clean checkout.

1. Land your change via a feature branch + PR (see below), referencing the Issue it closes (e.g. "Closes #12") — that's what makes it show up in the next release's notes, per [Issues, Milestones & Releases](CONTRIBUTING.md#issues-milestones--releases).
2. Merging to `main` re-runs the release workflow on every push. If the `Unreleased` milestone has closed issues, it computes the version, builds, publishes the GitHub Release, and rotates the milestone — see the `release.yml` bullet under [CI/CD](#cicd) above for the full mechanics.

**`AssemblyInfo.cs` and `source.extension.vsixmanifest` in the repo do not carry the real version** — main's branch protection blocks even the release workflow's bot identity from pushing a version bump back to `main` without a human-reviewed PR, so the version only ever exists as a git tag / GitHub Release, computed fresh each run and patched into the CI workspace's copy of those two files just before building (never committed). Whatever's checked into the repo is cosmetically whatever a maintainer last set by hand; don't read it as the current version, and don't bother keeping it in sync — check the [latest release](../../releases/latest) instead. VSIXInstaller reads the version that matters (the one baked into that release's built VSIX) from `source.extension.vsixmanifest` at install time to decide upgrade-vs-"already installed", which is exactly the value the workflow patches in before that specific build.

## Working conventions in this repo

- Work happens on feature branches, merged via PR (`gh pr create` / `gh pr merge --squash --delete-branch`), not direct commits to `main`.
- Issues track backlog items (e.g. deferred/reverted features) via `gh issue create`.
- Commit messages and PR bodies end with the `Co-Authored-By` / Claude Code attribution line given in this session's system reminder.

## Architecture

### Execution model: everything goes through stdin/stdout

The extension **never reads or writes the SQL file on disk** for linting/fixing. `LintService` pulls the current text straight from the VS editor's `ITextBuffer.CurrentSnapshot` (so it works on unsaved changes, unsaved-new documents, whatever's in memory) and pipes it to `sqlfluff lint|fix|format --stdin-filename <path> -` via `SqlFluffRunner` (`Core/SqlFluffRunner.cs`), which manages the child `Process`, writes UTF-8 bytes to `StandardInput`, and parses either the JSON lint output or the rewritten SQL text from `StandardOutput`. `--stdin-filename` is only used by sqlfluff to infer file type/config location, not to actually read that path.

`SqlFluffRunner` also handles executable discovery (PATH → common Python `Scripts` directories → `py -m sqlfluff` fallback), caches the resolved launch command, and exposes `CheckAvailabilityAsync` (used once at package startup to warn early if sqlfluff isn't reachable, rather than waiting for the first Lint/Fix to fail).

### Diagnostics flow: one shared store, multiple consumers

`Editor/ViolationStore.cs` is a `ConditionalWeakTable<ITextBuffer, …>`-backed store keyed by text buffer — the single source of truth for "what did sqlfluff find in this buffer, and where (as a `Span` on a specific `ITextSnapshot`)". Three independent consumers read from it and get notified of changes via a per-buffer subscription (not a static event, to avoid pinning buffers in memory):

1. `Editor/SqlFluffTaggerProvider.cs` — `ITagger<IErrorTag>` implementation that turns violations into editor squiggles.
2. `Services/ErrorListService.cs` — wraps a VS `ErrorListProvider` to mirror the same violations into the Error List window, with click-to-navigate.
3. `Editor/SqlFluffSuggestedActionsSource.cs` — `ISuggestedActionsSourceProvider`/`ISuggestedAction` implementation that surfaces "Fix with SQLFluff" / "Format with SQLFluff" in the native Light Bulb (`Alt+.`) menu when the cursor is on a violation span.

`LintService.LintAsync` is what populates the store (via `SqlFluffRunner.LintAsync` + `ViolationStore.ToSpan` to map sqlfluff's 1-based line/column positions onto a snapshot `Span`), and `LintService.Clear` / `ClearDiagnostics` empties it.

### Fix and Format are the same code path with a different verb

`LintService.RewriteAsync` (private, called by the public `FixAsync`/`FormatAsync`) is shared logic parameterized by a `RewriteMode` enum. The only difference between them is which `SqlFluffRunner` method they call (`FixAsync` → `sqlfluff fix`, all fixable rules; `FormatAsync` → `sqlfluff format`, a stable safe subset) — everything else (selection-vs-document targeting, diffing old/new text, applying a minimal edit, re-linting afterward) is identical. When adding a third rewrite-style command, extend this enum/method rather than duplicating the flow.

`ApplyMinimalEdit` (bottom of `LintService.cs`) computes the common prefix/suffix between old and new text and replaces only the differing middle, so the editor's caret position, scroll position, and undo stack stay sane instead of a full-buffer replace.

Fix/Format apply changes to the in-memory buffer only — they do **not** save the document. The status bar prompts the user to save manually. (An opt-in auto-save setting was attempted once and reverted for complexity; see issue #2 in the tracker if picking that back up.)

### Package wiring and cross-component access

`SqlFluffPackage.cs` is the `AsyncPackage` entry point. It owns the long-lived service instances (`LintService`, `ErrorListService`, `EditorServices`) and wires up:

- Menu/toolbar/context-menu commands (defined in `SqlFluffPackage.vsct`, IDs in `PackageGuids.cs`) via `OleMenuCommandService`.
- `Services/DocumentEvents.cs`, an `IVsRunningDocTableEvents3` implementation that triggers lint-on-save and drives the lint-while-typing debounce (via `LintService.Schedule`), and cleans up Error List entries when a document's last lock is released (i.e., it's closed). There is no lint-on-open — it was removed as unreliable/unwanted; `DocumentEvents` still tracks each buffer on first show, but only to subscribe the lint-while-typing handler.

Because `Editor/SqlFluffSuggestedActionsSource.cs` is composed by MEF (no constructor injection available), it reaches the package's services through `SqlFluffPackage.Instance`, a static singleton set in `InitializeAsync` / cleared in `Dispose`. This is the one deliberate exception to normal DI in this codebase — needed because MEF components and `AsyncPackage` are wired up through entirely separate mechanisms in the VS extensibility model.

### Extension self-update

The extension itself (not SQLFluff) can update in-place from SSMS, since it has no VS Marketplace listing to drive auto-update the normal way — it's only ever distributed as a `.vsix` attached to a GitHub Release (see [Release process](#release-process)). `Core/UpdateInfoParser.cs` is pure logic (parses the JSON from GitHub's `GET /repos/.../releases/latest`, compares versions) and is unit tested; `Core/ExtensionUpdater.cs` does the actual `HttpClient` fetch/download and launches the downloaded `.vsix` via `Process.Start(UseShellExecute: true)` — i.e. whatever's registered to open a `.vsix` (VSIXInstaller, normally), identical to a user double-clicking a manually downloaded one. `SqlFluffPackage.CheckForUpdatesAsync` is the only caller and has two modes: a silent startup check (gated by the "Check for updates on startup" option, default on) that just logs/sets status if a newer release exists, and the explicit **SQLFluff > Check for Updates...** command, which additionally prompts and, on confirmation, downloads and launches the installer. Neither path ever downloads or installs without an explicit user "yes" to that prompt.

### Settings

`Core/SqlFluffSettings.cs` is a plain POCO snapshot of user options, produced by `Options/SqlFluffOptionsPage.cs` (`DialogPage.ToSettings()`) — a fresh instance is read at the start of each lint/fix/format operation rather than passed around as mutable global state. `SqlFluffOptionsPage` shows up in SSMS under Tools > Options > SQLFluff > General.

### Editor/services split

- `Core/` — SQLFluff process execution and settings model; no VS editor types, could in principle be unit tested standalone (though nothing currently does).
- `Editor/` — MEF-composed editor extensibility points (tagger, suggested actions, the violation store they both read).
- `Services/` — package-owned, non-MEF services (`LintService` orchestration, RDT/document-lifecycle glue, Error List, output pane/status bar logging via `OutputLog`, and `EditorServices` for locating the active SQL view / resolving a buffer's file path).
