# Upstream notes — how ASF's idle pipeline actually works

Hand-verified against **ASF 6.3.7.0** (the pinned submodule) on 2026-07-09. Every fact
below was read from source, not the wiki. `pregate/check_upstream_assumptions.ps1` pins
the load-bearing ones (A1–A4); this file is the map from each assertion to the source.
File paths are relative to the `ArchiSteamFarm/` submodule.

## The idle pipeline

Everything funnels through private `Bot.ResetGamesPlayed()` (`ArchiSteamFarm/Steam/Bot.cs:3824`):

- Guards: returns unless `IsConnectedAndLoggedOn && !CardsFarmer.NowFarming`; when
  `GamesPlayedWhileIdle` is non-empty it additionally requires `IsPlayingPossible`, waits
  2s (callback-ordering guard), and honors `MinFarmingDelayAfterBlock` after a
  playing-elsewhere block.
- Then: `ArchiHandler.PlayGames(BotConfig.GamesPlayedWhileIdle, BotConfig.CustomGamePlayedWhileIdle)`.
  With an **empty** GPWI it still calls `PlayGames([])` — i.e. it actively clears the
  played-games state. That is why our batch write must always be able to land after it.

Call sites:

| Where | When | File:line |
|---|---|---|
| `Bot.OnFarmingStopped()` | every farming stop/finish | Bot.cs:1621 |
| `Bot.OnLoggedOn` handler | logon while farmer is paused | Bot.cs:3494 |
| `Bot.CheckOccupationStatus()` | account freed, farmer did not resume | Bot.cs:1043 |

## Event ordering (assertion A1)

```csharp
// Bot.cs:1620
internal async Task OnFarmingStopped() {
    await ResetGamesPlayed().ConfigureAwait(false);                    // ASF writes (clears) first
    await PluginsCore.OnBotFarmingStopped(this).ConfigureAwait(false); // then plugins run
}
```

`Bot.OnFarmingFinished(farmedSomething)` (Bot.cs:1604) calls `OnFarmingStopped()` first,
then fires `PluginsCore.OnBotFarmingFinished`. So in every farming-transition path, plugin
event handlers run **after** ASF's own games-played write — our write lands last.

## The went-idle signal (assertion A2)

`CardsFarmer.StartFarming()` (`Steam/Cards/CardsFarmer.cs:318`) calls
`Bot.OnFarmingFinished(false)` on both nothing-to-farm paths (lines 325, 345). Per the
`IBotCardsFarmerInfo` doc: *"This method will also be called when there is nothing to
idle or idling is unavailable."* This is our primary trigger.

## The one uncovered transition

When the user stops playing on their real client, `OnPlayingSessionState` /
`OnSharedLibraryLockStatus` (Bot.cs:3527/3553) → `CheckOccupationStatus()` (Bot.cs:1026)
→ `CardsFarmer.Resume(false)`. If `Resume` returns **false** without starting farming
(`!userAction && !ShouldResumeFarming`, CardsFarmer.cs:304, or permanently paused),
`ResetGamesPlayed()` runs with **no plugin event anywhere in the chain**.

Mitigation (by design, both): subscribe to `SteamUser.PlayingSessionStateCallback`
ourselves via `IBotSteamClient.OnBotSteamCallbacksInit`, and keep a slow re-assert
heartbeat so any missed transition self-heals.

## Why not the obvious APIs

- `ArchiHandler.PlayGames` is `internal` (ArchiHandler.cs:952); `InternalsVisibleTo`
  covers only official plugins (AssemblyInfo.cs:33-45). Not reachable, and reflection is
  the fragility we refuse.
- `Bot.Actions.Play()` (`Steam/Interaction/Actions.cs:330`) force-calls
  `CardsFarmer.Pause(true)` — a **permanent** pause (only `!resume`/restart undoes it).
  That is `!play` command semantics and would break farm-cards-first priority. FORBIDDEN
  (CLAUDE.md tattoo 1).
- `BotConfig.GamesPlayedWhileIdle` is `{ get; init; }` — not runtime-mutable.

The sanctioned door: `IBotSteamClient.OnBotSteamHandlersInit` registers our own
`ClientMsgHandler` on the bot's `SteamClient`; from it we send `CMsgClientGamesPlayed`
ourselves and reach `SteamUnifiedMessages` for owned-games queries.

## PlayGames wire behavior we replicate (assertion A4)

From `ArchiHandler.PlayGames` (ArchiHandler.cs:952-1008):

1. Message: `ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob)`
   with `client_os_type = unchecked((uint) Bot.OSType)`.
2. Custom display name workaround: first `Send` an **empty** games-played message, wait
   `Bot.CallbackSleep`, then add the name entry as a shortcut GameID
   (`AppType = Shortcut, ModID = uint.MaxValue`, `game_extra_info = name`) — Steam
   displays the custom name reliably only on a clean non-playing session.
3. Dedupe appids, skip `gameID == 0`.
4. Cap: at `MaxGamesPlayedConcurrently` (32, ArchiHandler.cs:56 — assertion A3) entries,
   evict the custom-name slot to make room (name costs one of the 32; hence "31 + name").
5. Single `Client.Send(request)` at the end.

Games not owned by the account: Steam may skip or reject them — selection must only ever
emit owned appids (guaranteed: our source is the account's own owned-games response).

## Owned games + playtime

ASF's public `ArchiHandler.GetOwnedGames(steamID)` (ArchiHandler.cs:339) uses unified
`Player.GetOwnedGames` but flattens to `Dictionary<appid, name>` — **playtime is dropped**
(line 370). We issue the same unified request ourselves (via
`Client.GetHandler<SteamUnifiedMessages>()`) and read `playtime_forever` (minutes) from
the response. Request params ASF uses (mirror them, ArchiHandler.cs:352-358):
`include_appinfo, include_free_sub, include_played_free_games, skip_unvetted_apps=false`.

Caveats: never-touched free games may be absent from the response (same universe
VaporChill saw); `playtime_forever` lags real time by up to ~30 min — see the local
accrual credit + quarantine anomaly guard in docs/architecture.md.

## Public surface we rely on

| Member | Declaration | Visibility |
|---|---|---|
| `Bot.IsPlayingPossible` | Bot.cs:142 | public, `[PublicAPI]` (`!PlayingBlocked && !LibraryLocked`) |
| `CardsFarmer.NowFarming` | CardsFarmer.cs:148 | public, `[PublicAPI]` |
| `CardsFarmer.Paused` | CardsFarmer.cs:154 | public, `[PublicAPI]` |
| `Bot.CardsFarmer`, `Bot.ArchiLogger`, `Bot.BotName`, `Bot.GetFilePath` | Bot.cs | public |
| Plugin interfaces | `Plugins/Interfaces/*.cs` | all `[PublicAPI]` |

Note: `Bot.PlayingWasBlocked` is **internal** — we cannot read it; we implement our own
post-block debounce instead of reusing ASF's.

## Re-pin protocol (when the pregate check fires on an ASF bump)

1. `git -C ArchiSteamFarm diff <old-tag> <new-tag> -- <cited file>` for the failing assertion.
2. Understand the upstream change; adapt our code (handler/orchestrator) first.
3. Update the assertion or A4 hash in the same commit; the commit message names the
   upstream change. Update this file if the fact itself changed.
