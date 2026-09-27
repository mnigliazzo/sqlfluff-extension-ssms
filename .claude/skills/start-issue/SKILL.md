---
name: start-issue
description: Begin work on a GitHub issue the issue-first way — file it if it doesn't exist, post the pre-work analysis as a comment, then create and check out the branch. Use before touching anything under src/ or tests/ in this repo (required by docs/WORKFLOW.md and enforced by .claude/hooks/require-issue-branch.ps1).
---

# start-issue

Full process: [docs/WORKFLOW.md](../../../docs/WORKFLOW.md).

## Input

An issue number (`123`), or a short description of the work if no issue exists yet.

## Steps

1. **Resolve the issue.**
   - Given a number: `gh issue view <n>` to confirm it exists and read its current description.
   - Given a description: search first (`gh issue list --search "<keywords>"`) to avoid a duplicate. If none exists, create one with `gh issue create` using the fitting template (`bug_report.md` / `feature_request.md` under `.github/ISSUE_TEMPLATE/`) so it gets the right label (`bug`/`enhancement`).
   - Pure maintenance/CI/tooling with no user-facing effect can skip this — go straight to step 3 with a `chore/` branch, no issue required.

2. **Post the analysis comment** on the issue (`gh issue comment <n> --body "..."`) *before* writing any code: what you found investigating it (repro, root cause, relevant code paths), the approach you're about to take, and why — this is the evidence trail docs/WORKFLOW.md asks for. Don't skip this even if the fix looks trivial; a one-line "confirmed root cause is X, fixing by Y" is enough for a small issue.

3. **Create and check out the branch**, named `<feature|fix|docs>/<issue#>-<short-slug>` (or `chore/<short-slug>` if step 1 was skipped):
   ```
   git checkout -b feature/<n>-<slug>
   ```
   This satisfies `.claude/hooks/require-issue-branch.ps1`, which otherwise blocks `Edit`/`Write` under `src/`/`tests/`.

Once this is done, proceed with the implementation. Use `finish-issue` when it's ready for a PR.
