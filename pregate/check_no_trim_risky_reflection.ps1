# INCIDENT (2026-07-09, first live smoke): OnLoaded threw MissingMethodException on the
# user's real ASF because the win-x64 (OS-specific, trimmed) host runtime lacks the
# attribute-reflection extension surface (Assembly.GetCustomAttribute<T> and friends). The
# plugin-dev wiki warns about exactly this class for OS-specific ASF builds. Build-time-known
# values (versions, git sha) belong in generated compile-time constants (HandshakeInfo.g.cs,
# see AdvancedIdle.csproj GenerateHandshakeInfo) - never in runtime attribute reflection.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Plugin sources only: tests run on our own untrimmed host and may reflect freely.
$tracked = git -C $root ls-files 'AdvancedIdle/*.cs'
$untracked = git -C $root ls-files --others --exclude-standard 'AdvancedIdle/*.cs'
$bad = @()

foreach ($f in (@($tracked) + @($untracked))) {
    $content = Get-Content -Raw (Join-Path $root $f)

    if ($content -match 'GetCustomAttribute') {
        $bad += "$($f): uses attribute reflection (GetCustomAttribute*) - trimmed ASF hosts may lack it"
    }
}

if ($bad.Count -gt 0) {
    $bad | ForEach-Object { Write-Host $_ }
    Write-Host 'Move build-time-known values into HandshakeInfo-style generated constants, or prove the surface on a trimmed win-x64 host first.'
    exit 1
}

exit 0
