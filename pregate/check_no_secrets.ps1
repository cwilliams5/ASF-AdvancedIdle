# INCIDENT: this project's direct ancestor (VaporChill, private) shipped a real Steam Web
# API key hardcoded in source, plus a hardcoded SteamID64. This repo is written
# public-shaped from commit #1 (CLAUDE.md), so key-shaped strings are gated mechanically.
# If a match is a false positive, add the file to the allowlist below WITH a justification.
#
# INCIDENT 2 (2026-07-09, this check's first firing): it caught its own failure-message,
# which contained a matching example ID - checks obey the rules they enforce. Same day it
# emerged that scanning only `git ls-files` lets a brand-new file ride through the gate
# into its own first commit, so untracked-but-not-ignored files are scanned too.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Allowlist: repo-relative paths, each entry justified by a comment.
$allow = @(
    # (none yet)
)

$exts = @('.cs', '.md', '.ps1', '.sh', '.json', '.json5', '.props', '.csproj', '.yml', '.yaml', '.txt', '.slnx', '.resx')

$patterns = @(
    @{ Name = 'Steam Web API key shape (32 hex chars)'; Regex = '\b[0-9A-Fa-f]{32}\b' },
    @{ Name = 'hardcoded SteamID64';                    Regex = '\b7656119\d{10}\b' },
    @{ Name = 'GitHub token';                           Regex = '\bgh[pousr]_[A-Za-z0-9]{20,}\b' }
)

$tracked = git -C $root ls-files
$untracked = git -C $root ls-files --others --exclude-standard

$files = (@($tracked) + @($untracked)) | Where-Object {
    ($exts -contains [System.IO.Path]::GetExtension($_)) -and ($allow -notcontains $_)
}

$bad = @()

foreach ($f in $files) {
    $path = Join-Path $root $f

    if (-not (Test-Path $path)) { continue }

    $content = Get-Content -Raw $path

    if (-not $content) { continue }

    foreach ($p in $patterns) {
        $m = [regex]::Match($content, $p.Regex)

        if ($m.Success) {
            $bad += "$($f): $($p.Name): '$($m.Value)'"
        }
    }
}

if ($bad.Count -gt 0) {
    $bad | ForEach-Object { Write-Host $_ }
    Write-Host 'Remove the secret or use an obviously-fake placeholder (user@example.com, 7656119XXXXXXXXXX). False positive? Allowlist the file in this check WITH a justification.'
    exit 1
}

exit 0
