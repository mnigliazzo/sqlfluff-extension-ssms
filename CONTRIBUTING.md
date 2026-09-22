# Contributing to SQLFluff for SSMS

Thanks for your interest! Here's how you can help.

## Reporting Bugs

- Use [GitHub Issues](../../issues)
- Describe what you expected vs. what happened
- Include your SSMS version, SQLFluff version, and OS
- Paste error messages from the SQLFluff output pane

## Suggesting Features

Open an issue with:
- Use case: what you're trying to do
- Why it matters
- Any workarounds you've found

## Issues, Milestones & Releases

Release notes are generated automatically from closed Issues — not from PR descriptions or a hand-maintained changelog. For a change to show up correctly in the next release:

1. **File an Issue** for it (bug report or feature request — the templates already apply the right label: `bug` or `enhancement`).
2. **Assign it to the `Unreleased` milestone.** This is what marks it as "part of the next release." Anything not in a milestone won't appear in release notes, even if it's merged.
3. **Reference the issue in your PR** (e.g. "Closes #12"), so merging the PR closes the issue automatically.
4. Pure maintenance/CI/tooling changes with no user-facing effect can skip this — they don't need an issue, and won't appear in release notes either way. (Label such an issue `chore` if you do want it tracked and categorized under "Changed".)

**Cutting a release** (maintainers): rename the `Unreleased` milestone to the target version (`vX.Y.Z`) and close it, bump the version in `AssemblyInfo.cs` and `source.extension.vsixmanifest`, and merge that to `main`. The release workflow looks up the milestone by that exact name, pulls its closed issues, and groups them into **Added** (`enhancement`), **Fixed** (`bug`), **Changed** (`chore`/`documentation`), or **Other** — mirroring [Keep a Changelog](https://keepachangelog.com/)'s categories. Create a fresh `Unreleased` milestone afterward for whatever comes next.

## Branching & Merge Strategy

`main` is the only long-lived branch and is protected:

- No direct pushes — all changes land through a pull request.
- The `build` (restore, build, VSIX/DLL/pkgdef sanity check) and `test` (unit tests) CI checks must pass before merging.
- At least one approving review is required.
- Force-pushes and branch deletion are disabled on `main`; history is linear (no merge commits — PRs are squash-merged).
- Conversations on a PR must be resolved before it can be merged.

**If you're an external contributor**: fork the repo, branch off your fork's `main`, and open the PR against `mnigliazzo/sqlfluff-extension-ssms:main`. GitHub Actions runs the `build` workflow automatically on PRs from forks (it only reads/builds the code — it never needs repo secrets), so you'll see the same status check maintainers do.

**If you have push access to this repo** (maintainers): branch directly here instead of forking — same rules apply, just without the fork indirection.

**Note on the approval requirement**: with a single maintainer, there's currently no one else to approve PRs. `main`'s protection has `enforce_admins` disabled specifically so the repo owner can merge with `gh pr merge --admin` (or the "merge without waiting for requirements" option in the GitHub UI) when there's no second reviewer available — the `build` check must still pass either way. This is a documented exception, not the default path: use it sparingly, and prefer a real review once there's more than one maintainer.

Branch naming follows the prefix that best describes the change, matching what this repo's history already uses:

| Prefix | For |
|---|---|
| `feature/…` | New functionality (e.g. `feature/auto-save-after-fix`) |
| `fix/…` | Bug fixes, including CI/tooling fixes (e.g. `fix/ci-msbuild-path`) |
| `chore/…` | Maintenance that isn't a feature or fix (e.g. `chore/changelog-and-release-workflow`) |
| `docs/…` | Documentation only (e.g. `docs/claude-md`) |

Each PR is merged with **squash merge**, and the source branch is deleted immediately after (`gh pr merge --squash --delete-branch`). This keeps `main`'s history one commit per change, easy to bisect, and matching the `CHANGELOG.md` entries one-to-one.

### Code Changes

1. If the change is user-facing, file an Issue first (or check one doesn't already exist) and assign it to the `Unreleased` milestone — see [Issues, Milestones & Releases](#issues-milestones--releases)
2. Fork (or branch, if you have push access) and clone the repo
3. Create a branch using the naming convention above: `git checkout -b feature/my-feature`
4. Make your changes (follow the existing code style)
5. Test locally: build and install the VSIX
6. Commit with a clear message
7. Push and open a pull request against `main`, referencing the issue (e.g. "Closes #12")

### Build Locally

```bash
cd src\SqlFluff.Ssms
dotnet restore
msbuild SqlFluff.Ssms.csproj /p:Configuration=Release
```

VSIX output: `bin\Release\SqlFluff.Ssms.vsix`

### Run Tests

```bash
dotnet test tests/SqlFluff.Ssms.Tests/SqlFluff.Ssms.Tests.csproj
```

No VS SDK or SSMS/Visual Studio MSBuild needed for this — it's a plain `net8.0` xUnit project. It only covers pure logic (argument quoting, JSON parsing) that doesn't touch the VS editor APIs; if you add new logic like that, add it to a standalone file in `Core/` and cover it with a test the same way.

## Code Style

- C# 9+ features OK (.NET 4.8 base class library)
- No external dependencies beyond VS SDK and SQLFluff
- Keep it minimal — one fix per PR when possible
- Comments only for the "why", not the "what"

## Pull Request Process

1. Keep PRs focused (one feature or fix per PR)
2. Update the README if there are user-facing changes
3. Reference the related issue (e.g. "Closes #2") — this is what makes the change show up in release notes, see [Issues, Milestones & Releases](#issues-milestones--releases)
4. Make sure the `build` and `test` checks pass and all review conversations are resolved — both are required before `main` will allow the merge
5. One of the maintainers will review, approve, and merge (squash)

## Questions?

Open an issue or ask in a pull request. All contributors are welcome.
