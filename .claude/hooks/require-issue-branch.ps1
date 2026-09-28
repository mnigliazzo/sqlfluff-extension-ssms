<#
PreToolUse hook (Edit|Write|NotebookEdit) — blocks editing src/ or tests/ unless the current
branch encodes an issue number, per docs/WORKFLOW.md. See CLAUDE.md "Active hooks and skills".
Exit 2 + stderr = hard block; exit 0 = allow.
#>

$input_json = [Console]::In.ReadToEnd() | ConvertFrom-Json

$filePath = $input_json.tool_input.file_path
if (-not $filePath) { $filePath = $input_json.tool_input.notebook_path }
if (-not $filePath) { exit 0 }

$repoRoot = $env:CLAUDE_PROJECT_DIR
if (-not $repoRoot) { $repoRoot = git -C $input_json.cwd rev-parse --show-toplevel 2>$null }
if (-not $repoRoot) { exit 0 }

$repoRoot = ($repoRoot -replace '\\', '/').TrimEnd('/') + '/'
$normalizedPath = $filePath -replace '\\', '/'

$relativePath = $normalizedPath
if ($normalizedPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
  # $repoRoot always ends in '/' here, so this only matches at a real directory boundary --
  # a sibling folder whose name happens to share the repo path as a string prefix (e.g.
  # "sqlfluff-extension-ssms-scratch") can't be mistaken for a path inside the repo.
  $relativePath = $normalizedPath.Substring($repoRoot.Length)
}

if ($relativePath -notmatch '^(src|tests)/') {
  exit 0
}

$branch = git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null
if (-not $branch) { exit 0 }

if ($branch -eq 'main') {
  Write-Error "Blocked: '$relativePath' is under src/ or tests/, but the current branch is 'main'. Create an issue-linked branch first (see docs/WORKFLOW.md), e.g. via the /start-issue skill."
  exit 2
}

if ($branch -match '^(feature|fix|docs)/[0-9]+-' -or $branch -like 'chore/*') {
  exit 0
}

Write-Error "Blocked: branch '$branch' doesn't reference an issue number. Rename it to '<feature|fix|docs>/<issue#>-slug' (or 'chore/...' for issue-less maintenance work) — see docs/WORKFLOW.md. The /start-issue skill creates this branch for you."
exit 2
