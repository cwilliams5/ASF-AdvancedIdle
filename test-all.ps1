# THE gate. Human, AI, and CI all run this same sequence: build -> pregate -> tests.
# A green test after a red build line is no information (a stale binary "passes"
# against yesterday's behavior), so every stage's exit code gates the next stage.
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

try {
    Write-Host "=== [1/3] build ($Configuration) ==="
    dotnet build -c $Configuration --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'test-all: BUILD FAILED - stopping'
        exit 1
    }

    Write-Host '=== [2/3] pregate ==='
    pwsh -NoProfile -File (Join-Path $PSScriptRoot 'pregate/pregate.ps1')

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'test-all: PREGATE FAILED - stopping'
        exit 1
    }

    Write-Host '=== [3/3] tests ==='
    dotnet test --no-build -c $Configuration --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'test-all: TESTS FAILED'
        exit 1
    }

    Write-Host 'test-all: ALL GREEN'
    exit 0
} finally {
    Pop-Location
}
