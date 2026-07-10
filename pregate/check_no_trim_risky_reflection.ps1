# INCIDENT (2026-07-09, first live smoke): OnLoaded threw MissingMethodException on the
# user's real ASF because the win-x64 (OS-specific, trimmed) host runtime lacks the
# attribute-reflection extension surface (Assembly.GetCustomAttribute<T> and friends). The
# plugin-dev wiki warns about exactly this class for OS-specific ASF builds. Build-time-known
# values (versions, git sha) belong in generated compile-time constants (HandshakeInfo.g.cs,
# see AdvancedIdle.csproj GenerateHandshakeInfo) - never in runtime attribute reflection.
#
# INCIDENT 2 (same smoke, minutes later): raw JsonSerializer.Serialize threw the same
# MissingMethodException in state persistence on the same host. JSON in plugin sources goes
# through ASF's JsonUtilities extensions (ToJsonText / ToJsonObject) - the surface ASF's own
# SerializableFile runs on trimmed hosts, proven by FreePackages persisting on this install.
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

    if ($content -match 'JsonSerializer\.(Serialize|Deserialize)') {
        $bad += "$($f): uses raw JsonSerializer - use ASF's ToJsonText/ToJsonObject (JsonUtilities), the trimmed-host-proven surface"
    }

    # INCIDENT 3 (same night, the root cause of three persist failures): the sync file APIs do
    # not exist on the trimmed host - ASF's tree contains ONLY the *Async variants, so trimming
    # removed the sync ones, and any method referencing them dies at JIT time. The trailing \(
    # keeps XxxAsync( calls legal.
    if ($content -match 'File\.(ReadAllText|WriteAllText|ReadAllLines|WriteAllLines|AppendAllText)\(') {
        $bad += "$($f): uses a sync File text API - trimmed ASF hosts ship only the *Async variants; bridge with .GetAwaiter().GetResult() at the call site"
    }
}

if ($bad.Count -gt 0) {
    $bad | ForEach-Object { Write-Host $_ }
    Write-Host 'Move build-time-known values into HandshakeInfo-style generated constants, or prove the surface on a trimmed win-x64 host first.'
    exit 1
}

exit 0
