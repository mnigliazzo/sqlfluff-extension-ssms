# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A VSIX extension (in-proc VSSDK package, targeting `net48`) that integrates [SQLFluff](https://sqlfluff.com) — a Python SQL linter/formatter — into SQL Server Management Studio 22. SSMS 22 is built on the Visual Studio 2026 platform (VS 18.x — e.g. SSMS 22.10 ships `Microsoft.VisualStudio.Shell.Framework` 18.10), so this is effectively a standard VS extension targeted at `Microsoft.VisualStudio.Ssms` instead of `Microsoft.VisualStudio.Community/Enterprise`. It still compiles against `Microsoft.VisualStudio.SDK` 17.14, the latest SDK metapackage on NuGet; there's no 18.x one, and 17.14 is what VS 2026 extensions target.

SQLFluff itself is never bundled — it's a separate Python tool the user installs (`pip install sqlfluff`) and the extension shells out to.

## Start here

The process and architecture detail live in `docs/`, not in this file — imported below so it's always in context instead of depending on remembering to go read it:

@docs/WORKFLOW.md
@docs/ARCHITECTURE.md
@docs/BUILD.md
@docs/RELEASE.md

`CONTRIBUTING.md` is the human-facing counterpart of `docs/WORKFLOW.md`/`docs/RELEASE.md` for external contributors.

## Active hooks and skills

- **Hook** `.claude/hooks/require-issue-branch.ps1` (`PreToolUse` on `Edit`/`Write`/`NotebookEdit`) — blocks editing under `src/`/`tests/` unless the current branch encodes an issue number (or is `chore/…`). See [docs/WORKFLOW.md](docs/WORKFLOW.md).
- **Hook** `.claude/hooks/check-vsct-version.ps1` (`Stop`, fires at the end of each turn) — if `SqlFluffPackage.vsct` has uncommitted changes without a matching `[ProvideMenuResource]` version bump, forces Claude to keep going instead of finishing silently.
- **Skill** `/start-issue` — file or point to an issue, post the pre-work analysis comment, create the branch.
- **Skill** `/finish-issue` — post the resolution comment, open the PR.
- **Skill** `/release-build` — clean rebuild + VSIX/DLL/pkgdef sanity check before treating a build as release-ready.

## Hard rules that don't live anywhere else

- Commit messages and PR bodies end with the `Co-Authored-By` / Claude Code attribution line given in this session's system reminder.
- Work happens on feature branches merged via PR (`gh pr create` / `gh pr merge --squash --delete-branch`), never direct commits to `main` — see [docs/WORKFLOW.md](docs/WORKFLOW.md) for the full flow.
