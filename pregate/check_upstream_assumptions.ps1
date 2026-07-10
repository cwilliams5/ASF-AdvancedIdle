# INCIDENT: this plugin's design rests on ASF behaviors that were verified BY HAND against
# ASF 6.3.7.0 during the 2026-07-09 research pass (map: docs/upstream-notes.md). The
# compiler only catches signature drift; it cannot catch event reordering or wire-behavior
# edits. This check re-verifies the hand-checked keystones against the in-tree submodule on
# every run, so an ASF bump that invalidates one FAILS LOUDLY here instead of shipping a
# silently-broken idle loop.
#
# If this check fires after an ASF submodule bump:
#   1. Re-read the cited upstream source (docs/upstream-notes.md maps each assertion).
#   2. Adapt OUR code to the new upstream reality FIRST.
#   3. Only then update the assertion or pinned hash below, in the same commit, with a
#      commit message naming the upstream change. Re-pinning to silence an unexplained
#      diff is the cage-modifier antipattern (CLAUDE.md).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fail = @()

$botCsPath = Join-Path $root 'ArchiSteamFarm/ArchiSteamFarm/Steam/Bot.cs'
$farmerCsPath = Join-Path $root 'ArchiSteamFarm/ArchiSteamFarm/Steam/Cards/CardsFarmer.cs'
$archiCsPath = Join-Path $root 'ArchiSteamFarm/ArchiSteamFarm/Steam/Integration/ArchiHandler.cs'

if (-not (Test-Path $botCsPath)) {
    Write-Host 'ASF submodule not initialized (ArchiSteamFarm/ is empty) - run: git submodule update --init --recursive'
    exit 1
}

$botCs = Get-Content -Raw $botCsPath
$farmerCs = Get-Content -Raw $farmerCsPath
$archiCs = Get-Content -Raw $archiCsPath

# --- A1: Bot.OnFarmingStopped() calls ResetGamesPlayed() BEFORE the plugin event fires.
# Our batch write happens inside OnBotFarmingStopped/Finished handlers; it only "lands
# last" (CLAUDE.md tattoo 2) if ASF's own games-played write comes first.
$m = [regex]::Match($botCs, 'internal async Task OnFarmingStopped\(\)\s*\{(?<body>[\s\S]*?)\n\t\}')

if (-not $m.Success) {
    $fail += 'A1: could not locate Bot.OnFarmingStopped() - method moved or renamed'
} else {
    $body = $m.Groups['body'].Value
    $reset = $body.IndexOf('ResetGamesPlayed()')
    $evt = $body.IndexOf('OnBotFarmingStopped(')

    if (($reset -lt 0) -or ($evt -lt 0) -or ($reset -gt $evt)) {
        $fail += 'A1: ResetGamesPlayed no longer precedes the OnBotFarmingStopped plugin event - the last-write-wins model is broken'
    }
}

# --- A2: "nothing to farm" still signals plugins via Bot.OnFarmingFinished(false).
# This is the went-idle event that triggers our batch assertion when there are no cards.
$sf = [regex]::Match($farmerCs, 'internal async Task StartFarming\(\)\s*\{(?<body>[\s\S]*?)\n\t\}')

if (-not $sf.Success) {
    $fail += 'A2: could not locate CardsFarmer.StartFarming()'
} elseif ($sf.Groups['body'].Value -notmatch 'OnFarmingFinished\(false\)') {
    $fail += 'A2: StartFarming no longer fires OnFarmingFinished(false) on nothing-to-farm - we lose the went-idle signal'
}

# --- A3: Steam's concurrent-play cap is still 32 (we size and validate batches against it).
if ($archiCs -notmatch 'MaxGamesPlayedConcurrently\s*=\s*32') {
    $fail += 'A3: ArchiHandler.MaxGamesPlayedConcurrently is no longer 32 - update our batch cap, config validation, and docs'
}

# --- A4: ArchiHandler.PlayGames() body hash. Our handler REPLICATES this method's wire
# behavior (clean-session custom-name workaround, dedupe, appid>0 filter, cap eviction).
# ANY upstream edit to it deserves eyes: diff against the previous pin, port behavior
# changes into AdvancedIdleHandler, then re-pin. Pinned against ASF 6.3.7.0.
$pg = [regex]::Match($archiCs, 'internal async Task PlayGames\([\s\S]*?\n\t\}')

if (-not $pg.Success) {
    $fail += 'A4: could not locate ArchiHandler.PlayGames()'
} else {
    $text = $pg.Value -replace "`r", ''
    $sha = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '').ToLowerInvariant()
    # Pinned 2026-07-09 against ASF 6.3.7.0 (bootstrap).
    $pinned = '5fa1196faff1fbec524448a0b1f032bcb79617d664823adfa3ee8c96fbc3bb77'

    if ($sha -ne $pinned) {
        $fail += "A4: ArchiHandler.PlayGames body changed upstream (sha256 $sha != pinned $pinned) - diff it, port any behavior change into AdvancedIdleHandler, then update the pin"
    }
}

if ($fail.Count -gt 0) {
    $fail | ForEach-Object { Write-Host $_ }
    Write-Host 'See docs/upstream-notes.md for the assertion-to-source map and the re-pin protocol.'
    exit 1
}

exit 0
