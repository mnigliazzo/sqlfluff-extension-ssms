---
name: finish-issue
description: Close the loop on an issue started with start-issue — post the resolution comment documenting how it was actually fixed, then open the PR referencing it. Use once the change is implemented, tested locally, and ready for review.
---

# finish-issue

Full process: [docs/WORKFLOW.md](../../../docs/WORKFLOW.md).

## Input

The issue number the current branch is for (it's in the branch name: `feature/<n>-slug` → `<n>`). If the branch is `chore/…` with no issue, skip step 1 and go straight to step 2.

## Steps

1. **Post the resolution comment** on the issue (`gh issue comment <n> --body "..."`): what actually changed, any deviation from the plan posted by `start-issue` and why, and how you verified it (tests run, manual repro steps confirmed fixed). This is what makes the thread readable as a finished piece of collaborative work, not just a diff that appeared.

2. **Open the PR**, using `.github/pull_request_template.md`'s shape but keeping it short — it links to the issue rather than repeating the analysis/resolution detail that now lives there:
   ```
   gh pr create --title "..." --body "Closes #<n>

   See #<n> for analysis and how this was resolved.

   ## Testing
   - [ ] Built locally
   - [ ] Installed VSIX and tested in SSMS"
   ```
   For a `chore/` branch with no issue, drop the `Closes #<n>` line and describe the change directly in the PR body instead.

3. Run `dotnet format --verify-no-changes` locally (see [docs/BUILD.md](../../../docs/BUILD.md#lint)) and make sure the `build`/`test` CI checks — which now include that same lint check — are expected to pass before asking for review.

4. **Run `/code-review` on the diff before (or immediately after) opening the PR.** This is not optional — every PR out of this repo gets a code-review pass, whether by a human or by Claude Code, before it's considered ready. Fix what it finds, then push the fixes onto the same branch/PR.
