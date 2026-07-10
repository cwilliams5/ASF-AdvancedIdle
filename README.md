# ASF-AdvancedIdle

An **[ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)** plugin that idles
your least-played owned games whenever the bot has nothing to card-farm, re-picking the
batch on a rotation timer so playtime spreads evenly across your **entire** library —
a dynamic replacement for the static `GamesPlayedWhileIdle` list.

## How it behaves

- **Card farming always wins.** AdvancedIdle never pauses or resumes the farmer; it only
  plays games while ASF is naturally idle, and stands down the moment farming starts.
- **Lowest playtime first.** Every rotation it fetches your owned games with playtime
  (over the bot's existing Steam connection — no Web API key), filters your blacklist and
  the quarantine, and idles the N games with the least total playtime. Over time this
  raises the whole library's floor evenly.
- **Yields to real play.** When you start a game on your own machine, Steam blocks the
  bot session and the plugin waits; when you stop, it resumes automatically.
- **Respects your commands.** If you `!pause` or `!play` something manually, the plugin
  stands down until the farmer is unpaused.
- **Quarantine.** Titles that provably never accrue playtime are automatically benched so
  they stop occupying batch slots (with an anomaly guard so laggy Steam playtime data
  can't mass-quarantine a healthy batch). Inspect/clear via bot commands.

## Requirements

- ASF V6.3.7.0 or later, **generic** flavor recommended (the standard advice for custom
  plugins).
- The bot account must own (licenses count, F2P included) whatever it idles — guaranteed
  by construction, since candidates come from the account's own owned-games list.
- Steam caps concurrent play at 32 appIDs (31 if you configure a custom display name).

## Installation

1. Download `AdvancedIdle.zip` from the releases page.
2. Extract into your ASF `plugins/AdvancedIdle/` directory.
3. Add configuration to your bot's config file (below), then restart ASF.

## Configuration

Per-bot properties, added to the bot's `.json` config (defaults shown):

<!-- CONFIG:BEGIN (generated from Config.cs registry; do not hand-edit once the generator lands) -->
| Property | Type | Default | Meaning |
|---|---|---|---|
| `AdvancedIdleEnabled` | bool | `false` | Master switch for this bot. |
| `AdvancedIdleBatchSize` | uint | `30` | Games idled at once (1-32; 31 max with a custom name). |
| `AdvancedIdleRotationMinutes` | uint | `120` | How often the batch is re-picked. |
| `AdvancedIdleBlacklist` | uint[] | `[]` | AppIDs never idled (cosmetic or problematic titles). |
| `AdvancedIdleCustomGameName` | string | `null` | Optional "non-Steam game" label shown alongside the batch. |
| `AdvancedIdleIdleWhileFarmerPaused` | bool | `false` | Also idle while the farmer is paused by user command. |
| `AdvancedIdleQuarantineEnabled` | bool | `true` | Auto-bench titles that provably never accrue playtime. |
<!-- CONFIG:END -->

## Commands

| Command | Access | Effect |
|---|---|---|
| `!aidle status` | FamilySharing | Current batch, next rotation, snapshot age, quarantine count. |
| `!aidle rotate` | Master | Force a re-pick now. |
| `!aidle quarantine` | Master | List quarantined appIDs. |
| `!aidle unquarantine <appIDs>` | Master | Clear entries from the quarantine. |

## Building

```
git clone --recurse-submodules https://github.com/cwilliams5/ASF-AdvancedIdle.git
cd ASF-AdvancedIdle
pwsh ./test-all.ps1   # build -> pregate -> tests: the same gate CI runs
```

Debug builds copy the plugin into the ASF submodule's own `plugins/` directory, so you can
run the in-tree ASF directly for live testing. Development conventions live in
`CLAUDE.md`; the ASF behaviors this plugin depends on are documented (and mechanically
pinned) in `docs/upstream-notes.md`.

## License

Apache-2.0. Built from the official
[ASF-PluginTemplate](https://github.com/JustArchiNET/ASF-PluginTemplate).
