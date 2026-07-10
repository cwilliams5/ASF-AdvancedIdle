# Pregate runner: auto-discovers pregate/check_*.ps1 and runs each in a child pwsh.
# Any failure blocks the entire test suite (see CLAUDE.md "Pregate system").
# Dropping a new check_*.ps1 file into pregate/ IS registration - no wiring needed.
$ErrorActionPreference = 'Stop'

$checks = Get-ChildItem -Path $PSScriptRoot -Filter 'check_*.ps1' | Sort-Object Name

if ($checks.Count -eq 0) {
    Write-Host 'pregate: no checks found'
    exit 0
}

$failed = 0

foreach ($check in $checks) {
    $out = & pwsh -NoProfile -File $check.FullName 2>&1

    if ($LASTEXITCODE -ne 0) {
        $failed++
        Write-Host "[FAIL] $($check.Name)"
        $out | ForEach-Object { Write-Host "       $_" }
    } else {
        Write-Host "[ ok ] $($check.Name)"
    }
}

if ($failed -gt 0) {
    Write-Host "pregate: $failed check(s) FAILED - suite blocked"
    exit 1
}

Write-Host "pregate: all $($checks.Count) checks passed"
exit 0
