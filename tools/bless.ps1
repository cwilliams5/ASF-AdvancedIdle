# Deliberate re-blessing entry point for the two committed baselines: the README config
# section (-Docs) and the rotation trace (-Trace). Re-blessing to silence an unexplained
# diff is the cage-modifier antipattern (CLAUDE.md) - read the diff first, bless second,
# and commit with a message naming the intended change.
param(
    [switch]$Docs,
    [switch]$Trace
)

$ErrorActionPreference = 'Stop'

if (-not $Docs -and -not $Trace) {
    Write-Host 'Usage: tools/bless.ps1 [-Docs] [-Trace]'
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    if ($Docs) { $env:ADVANCEDIDLE_BLESS_DOCS = '1' }
    if ($Trace) { $env:ADVANCEDIDLE_BLESS_TRACE = '1' }

    dotnet test AdvancedIdle.Tests --nologo --filter 'FullyQualifiedName~DocsFreshness|FullyQualifiedName~RotationTrace'
    $code = $LASTEXITCODE
} finally {
    if ($Docs) { Remove-Item Env:ADVANCEDIDLE_BLESS_DOCS -ErrorAction SilentlyContinue }
    if ($Trace) { Remove-Item Env:ADVANCEDIDLE_BLESS_TRACE -ErrorAction SilentlyContinue }

    Pop-Location
}

if ($code -ne 0) {
    exit 1
}

Write-Host 'bless: done - now READ the diff (git diff) before committing.'
exit 0
