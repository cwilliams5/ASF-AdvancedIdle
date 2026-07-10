# INCIDENT: build artifacts (bin/, obj/, packaged zips, stray DLLs) have been swept into
# commits by automation in sibling projects (engineering-cage lesson: the AI's commits are
# inputs to the same machine as its code). The ban is mechanical, not a convention.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Allowlist: repo-relative paths of binaries that are genuinely intended (e.g. committed
# test fixtures). Each entry needs a justification comment.
$allow = @(
    # (none yet)
)

$tracked = git -C $root ls-files
$bad = $tracked | Where-Object {
    ($_ -match '(^|/)(bin|obj|out)/' -or $_ -match '\.(zip|dll|pdb|exe|nupkg)$') -and ($allow -notcontains $_)
}

if ($bad) {
    $bad | ForEach-Object { Write-Host "tracked build artifact: $_" }
    Write-Host 'git rm --cached the artifact and extend .gitignore. If a binary is genuinely intended (a committed fixture), allowlist it in this check WITH a justification.'
    exit 1
}

exit 0
