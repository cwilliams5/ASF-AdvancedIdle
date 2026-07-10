# THE gate. Human, AI, and CI all run this same sequence: build -> pregate -> tests.
# A green test after a red build line is no information (a stale binary "passes"
# against yesterday's behavior), so every stage's exit code gates the next stage.
#
# Both configurations by default. INCIDENT (2026-07-09): Release escalates analyzer
# severities beyond Debug (CA1515/CA1861 failed CI's Release lane after a Debug-only local
# gate was green) - the local gate must cover every axis CI runs. CI's matrix passes a
# single -Configurations value per job.
param(
    [string[]]$Configurations = @('Debug', 'Release')
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

try {
    foreach ($configuration in $Configurations) {
        Write-Host "=== [1/3] build ($configuration) ==="
        # ContinuousIntegrationBuild matches CI/publish strictness (analyzer escalation).
        dotnet build -c $configuration -p:ContinuousIntegrationBuild=true --nologo

        if ($LASTEXITCODE -ne 0) {
            Write-Host "test-all: BUILD FAILED ($configuration) - stopping"
            exit 1
        }
    }

    Write-Host '=== [2/3] pregate ==='
    pwsh -NoProfile -File (Join-Path $PSScriptRoot 'pregate/pregate.ps1')

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'test-all: PREGATE FAILED - stopping'
        exit 1
    }

    foreach ($configuration in $Configurations) {
        Write-Host "=== [3/3] tests ($configuration) ==="
        dotnet test --no-build -c $configuration --nologo

        if ($LASTEXITCODE -ne 0) {
            Write-Host "test-all: TESTS FAILED ($configuration)"
            exit 1
        }
    }

    Write-Host 'test-all: ALL GREEN'
    exit 0
} finally {
    Pop-Location
}
