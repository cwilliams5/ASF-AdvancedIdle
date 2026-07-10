# INCIDENT: a script authored under pwsh 7 (UTF-8) broke under Windows PowerShell 5.1
# (CP-1252), where an em-dash byte parses as a closing quote - everything worked for the
# author and broke only for the user (engineering-cage lesson: verify under the user's
# interpreter; recurred across sibling projects). All shell scripts in this repo stay
# pure ASCII so no interpreter or codepage can reinterpret them.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Untracked-but-not-ignored files scan too: a new script must not ride through the gate
# into its own first commit (same gap as check_no_secrets INCIDENT 2).
$tracked = git -C $root ls-files '*.ps1' '*.sh'
$untracked = git -C $root ls-files --others --exclude-standard '*.ps1' '*.sh'
$files = @($tracked) + @($untracked)
$bad = @()

foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $root $f))

    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -gt 127) {
            $bad += "$($f): non-ASCII byte 0x$($bytes[$i].ToString('X2')) at offset $i"
            break
        }
    }
}

if ($bad.Count -gt 0) {
    $bad | ForEach-Object { Write-Host $_ }
    Write-Host 'Replace with ASCII equivalents (hyphens, straight quotes).'
    exit 1
}

exit 0
