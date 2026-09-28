# CI/CD & Release process

## Workflows

Four GitHub Actions workflows:

- **`.github/workflows/build.yml`** (`windows-latest`) — runs on push/PR to `main` and `beta`. Restores, lints with `dotnet format --verify-no-changes`, builds, sanity-checks that the VSIX/DLL/pkgdef exist, uploads the VSIX as a build artifact. This is the required status check on `main`'s branch protection.
- **`.github/workflows/test.yml`** (`ubuntu-latest`) — runs on push/PR to `main` and `beta`. Lints the test project with `dotnet format --verify-no-changes`, then `dotnet test` on the net8.0 test project (`SqlFluff.Ssms.Tests`). No MSBuild/SSMS setup needed for either.
- **`.github/workflows/release.yml`** (`windows-latest`) — runs on push to `main` (and manually via `workflow_dispatch`, with a `bump` input to force `patch`/`minor`/`major`). See [Release mechanism](#release-mechanism-no-milestone) below.
- **`.github/workflows/beta-release.yml`** (`windows-latest`) — runs on push to `beta` touching `src/**` (and manually). Publishes a GitHub **prerelease**; see [Beta channel](#beta-channel).

The Windows workflows use `microsoft/setup-msbuild` to find the MSBuild bundled with the runner's Visual Studio 2022 — **do not** hardcode a path to SSMS's MSBuild in CI; SSMS is not installed on GitHub-hosted runners. That was tried and fails (`term not recognized`); only local dev machines that happen to have SSMS (and not full VS) need the SSMS MSBuild path + env var workaround described in [BUILD.md](BUILD.md).

## Release mechanism (no milestone)

Fully automatic, no maintainer version-bump step, and **no milestone to remember to assign**. The only requirement is the one already in [WORKFLOW.md](WORKFLOW.md): reference the issue your PR closes ("Closes #12"). Everything else is computed from GitHub's own PR↔issue link.

On every push to `main`, `release.yml`:

1. Finds the most recent published release tag (`vX.Y.Z`) and its commit date.
2. Queries (via `gh api graphql`) every PR merged into `main` since that date, along with the issue(s) each one closes (`closingIssuesReferences`) and that issue's labels.
3. If a merged PR closes no issue (the `chore/` exception), it falls back to the PR's own labels instead.
4. Categorizes each into **Added** (`enhancement`), **Fixed** (`bug`), **Changed** (`chore`/`documentation`), or **Other** — mirroring [Keep a Changelog](https://keepachangelog.com/).
5. Picks the version bump: `minor` if any PR landed in Added, otherwise `patch`. There's no automatic `major` — force one via the workflow's manual "Run workflow" button and its `bump` input when one is actually needed.
6. If nothing merged since the last tag, the run is a no-op.
7. Otherwise builds, publishes the GitHub Release tagged `vX.Y.Z` with the VSIX attached, using the categorized notes.

This replaced an earlier design that tracked "what's in the next release" via an `Unreleased` GitHub milestone that every issue had to be manually assigned to, and that had to be renamed/rotated after each release. That extra bookkeeping step was easy to forget (an issue not assigned to the milestone just silently didn't ship in notes) and added mutable state with no benefit over reading it straight from merged-PR history, which GitHub already tracks for free.

The version is never read from or written back to `AssemblyInfo.cs`/`source.extension.vsixmanifest` in the repo — `main`'s branch protection blocks even the release workflow's bot identity from pushing a version bump back to `main` without a human-reviewed PR, so the version only ever exists as a git tag / GitHub Release, computed fresh each run and patched into the CI workspace's copy of those two files just before building (never committed). Whatever's checked into the repo is cosmetically whatever a maintainer last set by hand; don't read it as the current version, and don't bother keeping it in sync — check the [latest release](../../../releases/latest) instead. VSIXInstaller reads the version that matters (the one baked into that release's built VSIX) from `source.extension.vsixmanifest` at install time to decide upgrade-vs-"already installed", which is exactly the value the workflow patches in before that specific build.

## Beta channel

Riskier changes (e.g. platform migrations) land on the `beta` branch first: feature branch → PR into `beta` → validate the prerelease in real SSMS → PR `beta` into `main`, which then releases normally.

- **Version:** each push to `beta` that touches `src/**` publishes a prerelease versioned as the latest stable plus a 4th component — `1.15.3.1`, `1.15.3.2`, … — tagged `v1.15.3.1-beta`. That number is deliberate:
  - It's above the stable it's based on, so VSIXInstaller installs it as an upgrade.
  - It's below the next stable (`1.15.4`/`1.16.0`), so that stable installs over the beta.
  - The extension compares only 3 version components (`Version.ToString(3)`), so a beta install never shows a false update notice.
- **Isolation from stable:**
  - The self-updater queries `releases/latest`, which excludes prereleases, so stable users never see a beta.
  - `release.yml` only reads `^\d+\.\d+\.\d+$` tags, so beta tags don't affect stable versioning.
  - `beta-release.yml` doesn't touch issues, PRs, or the changelog at all — those are only resolved by the stable workflow once `beta` reaches `main`.
- **Downgrading:** VSIXInstaller won't install a lower version over a higher one, so going from a beta back to the stable it's based on requires uninstalling the beta first.
