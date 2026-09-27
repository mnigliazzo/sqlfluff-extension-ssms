<#
Stop hook — fires once when Claude is about to finish responding. Warns (forces Claude to
keep going, doesn't hard-block) if SqlFluffPackage.vsct has uncommitted changes without a
matching bump to [ProvideMenuResource("Menus.ctmenu", N)] in SqlFluffPackage.cs. VS only
re-merges command UI when N changes; see docs/ARCHITECTURE.md "Menus and toolbar".

This used to be a PostToolUse hook, but PostToolUse output is never shown to Claude (only
logged) per Claude Code's hooks reference — only Stop/SubagentStop (among others) actually
deliver `systemMessage` into Claude's context, via `continue: true`. Always exits 0.
#>

$input_json = [Console]::In.ReadToEnd() | ConvertFrom-Json

$repoRoot = $env:CLAUDE_PROJECT_DIR
if (-not $repoRoot) { $repoRoot = git -C $input_json.cwd rev-parse --show-toplevel 2>$null }
if (-not $repoRoot) { exit 0 }

$vsctChanged = git -C $repoRoot status --porcelain -- '*.vsct' 2>$null
if (-not $vsctChanged) { exit 0 }

function Get-MenuResourceVersion($content) {
  $match = [regex]::Match($content, 'ProvideMenuResource\("Menus\.ctmenu",\s*(\d+)\)')
  if ($match.Success) { return [int]$match.Groups[1].Value }
  return $null
}

# This attribute lives beside SqlFluffPackage.cs's [ProvideMenuResource], not in the .vsct
# itself — search the whole repo's tracked C# for it.
$attrFile = git -C $repoRoot grep -l 'ProvideMenuResource' -- '*.cs' 2>$null | Select-Object -First 1
if (-not $attrFile) { exit 0 }

$headContent = git -C $repoRoot show "HEAD:$attrFile" 2>$null
$workingContent = Get-Content -Path (Join-Path $repoRoot $attrFile) -Raw -ErrorAction SilentlyContinue
if (-not $headContent -or -not $workingContent) { exit 0 }

$headVersion = Get-MenuResourceVersion $headContent
$workingVersion = Get-MenuResourceVersion $workingContent
if ($null -eq $headVersion -or $null -eq $workingVersion) { exit 0 }

if ($workingVersion -le $headVersion) {
  $result = @{
    continue      = $true
    stopReason    = "other"
    systemMessage = "SqlFluffPackage.vsct has uncommitted changes but [ProvideMenuResource(`"Menus.ctmenu`", N)] in $attrFile is still at $headVersion. VS only re-merges command UI (new toolbar buttons, menu items) when N changes -- bump it before finishing. See docs/ARCHITECTURE.md 'Menus and toolbar'."
  }
  $result | ConvertTo-Json -Depth 5
}

exit 0
