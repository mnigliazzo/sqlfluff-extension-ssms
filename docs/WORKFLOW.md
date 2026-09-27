# Development workflow: issue-first, evidence in the issue

This is the process for any change to `src/` or `tests/`, whether done by a human or by Claude Code in this repo. It exists so that *why* and *how* a change happened lives in the GitHub Issue thread — not in a maintainer's memory, a chat log, or a CLAUDE.md rule someone has to remember to re-read.

## 1. There is always an issue before there is code

No code change starts without a GitHub Issue. Either:

- File one (`gh issue create`, using `.github/ISSUE_TEMPLATE/bug_report.md` or `feature_request.md` so it gets the right label), or
- Point to an existing open one.

Pure maintenance/CI/tooling changes with no user-facing effect can skip this (same exception as always) — see the `chore/` branch prefix below.

`.claude/skills/start-issue` does steps 1–3 of this section for you.

## 2. Branch naming encodes the issue number

| Prefix | For | Example |
|---|---|---|
| `feature/<issue#>-…` | New functionality | `feature/91-auto-save-toggle` |
| `fix/<issue#>-…` | Bug fixes | `fix/88-error-list-duplicate-rows` |
| `docs/<issue#>-…` | Documentation only | `docs/95-workflow-doc` |
| `chore/…` | Maintenance/CI/tooling with no user-facing effect — no issue number required | `chore/pin-action-versions` |

`.claude/hooks/require-issue-branch.ps1` (a `PreToolUse` hook, see `.claude/settings.json`) **blocks** any `Edit`/`Write`/`NotebookEdit` under `src/` or `tests/` unless the current branch is `feature/<n>-…`, `fix/<n>-…`, `docs/<n>-…`, or `chore/…`. Editing on `main` is always blocked for those paths — create the branch first.

## 3. Analysis goes in the issue before code does

Before writing the fix, post a comment on the issue (`gh issue comment`) describing what you found: repro steps, root cause, the approach you're about to take, and why. This is the "evidence" — it's what lets someone (including future-you) reconstruct the reasoning without re-deriving it, and it's what makes this feel like a colleague thinking out loud in the thread rather than a black box producing a diff.

`.claude/skills/start-issue` posts this comment as part of creating the branch, so the analysis exists *before* the first line of code is touched.

## 4. Implement, then close the loop in the issue

Once the change works, post a second comment on the issue describing how it was actually resolved — what changed, any deviation from the original plan in step 3 and why, and how it was verified. `.claude/skills/finish-issue` does this and then opens the PR.

## 5. The PR references the issue, it doesn't repeat it

The PR description (`.github/pull_request_template.md`) stays short and links to the issue (`Closes #N`) rather than duplicating the analysis/resolution narrative — that detail already lives in the issue thread. Referencing the issue is also what the release workflow uses to categorize and version the change — see [RELEASE.md](RELEASE.md).

## 6. Every PR gets a code-review pass

Run `/code-review` on the diff before or right after opening the PR — no exceptions, whether the PR was written by a human or by Claude Code. Fix what it finds and push the fixes to the same branch before merging. `.claude/skills/finish-issue` includes this as its last step.

## Summary

```
gh issue create (or pick an existing #N)
        │
        ▼
/start-issue N        → branch feature/N-slug + analysis comment on #N
        │
        ▼
  edit src/ tests/    → allowed only on that branch (hook-enforced)
        │
        ▼
/finish-issue N       → resolution comment on #N + gh pr create "Closes #N"
        │
        ▼
  /code-review        → fix findings, push to the same branch
        │
        ▼
  PR merged to main   → release workflow reads #N's labels for changelog/version
```
