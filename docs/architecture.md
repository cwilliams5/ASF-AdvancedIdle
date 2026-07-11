# Architecture

How AdvancedIdle is put together and why. Companion docs: `docs/upstream-notes.md` (the
hand-verified ASF behaviors everything rests on, mechanically pinned by pregate),
`docs/smoke-2026-07-09.md` (first live validation night), `SMOKE.md` (the
machine-unverifiable checklist). The original phased design doc (plan.md) is retired;
its history lives in git.

## Component map

```
ASF host (plugin dispatcher, CardsFarmer, config watcher)
  |
  |  7 plugin interfaces (events in) / public API (state reads)
  v
AdvancedIdlePlugin.cs ------ thin glue, one instance per ASF process
  |  per-bot registries (handler, idler, last-blocked flag)
  |  adapters: BotStateAdapter / SteamActionsAdapter / LogAdapter / FileStateStore / SystemClock
  v
BotIdler.cs ---------------- per-bot orchestrator (the shell's brain)
  |  single Pump() entry under one semaphore; one 5-min timer
  |  built ONLY against Seams.cs interfaces -> fully fake-drivable in tests
  v
Core/ ---------------------- pure functions over plain data (no ASF, no clock, no I/O)
     IdlePolicy.cs           guard order: farming > user pause > playing-elsewhere
     Selection.cs            lowest effective playtime, appID tiebreak, filters, cap
     Accrual.cs              local idle credit vs Steam's lagging playtime reports
     Quarantine.cs           sticky-zero benching + systemic-anomaly guard
     Config.cs               settings registry: rows -> parser + defaults + README section

AdvancedIdleHandler.cs ----- our own SteamKit2 ClientMsgHandler on the bot's connection
     PlayGames()             replicates ASF's internal wire behavior (pregate A4 pins it)
     GetOwnedGames()         unified Player.GetOwnedGames, KEEPING playtime_forever
```

## The one mover: Pump

Every trigger funnels into `BotIdler.Pump(reason, forceReassert)` under a single
semaphore. Triggers: farming started/stopped/finished, logon/disconnect, playing-session
blocked-flag transitions, the 5-minute timer, `!aidle rotate`. Pump:

1. Snapshots the bot situation -> `IdlePolicy.Decide` (pure). Non-idle decisions settle
   accrual credit and stand down.
2. If the rotation window elapsed (or no batch yet): `Rotate` - fetch owned games
   (bounded 90s), reconcile accrual, evaluate quarantine, select the new batch, send,
   persist.
3. Otherwise maybe re-send the UNCHANGED batch per the send policy below.

A catch-all wraps the body: timer callbacks discard the task, so an unlogged throw would
be invisible. Silence is not success.

## Send policy (hard-won, see smoke log)

| Trigger | Sends? | Why |
|---|---|---|
| Rotation window elapsed | always | the product: fresh lowest-N batch |
| Farming stopped/finished, logon | forced re-assert | ASF's `ResetGamesPlayed` just overwrote the session (upstream-notes); load-bearing |
| Playing-session callback | only when the BLOCKED flag transitions | the callback echoes our own writes; with a custom name every assert is a 2-send clean-session dance, and reacting to echoes self-sustained a 2-second loop (2026-07-10) |
| 5-minute heartbeat | only if `HeartbeatReassert` (default OFF) | speculative self-heal net; every observed firing was redundant, and with a custom name each send is friend-visible |
| Any forced send | rate floor: max one per 30s | defense-in-depth so no undiscovered echo path can loop |

Re-asserts never reselect - selection runs only at rotation. Rotation is the recovery
ceiling for hypothetical silent clobbers (default 120 min).

## Accrual + quarantine interplay

Steam's reported `playtime_forever` lags. `Accrual` credits in-progress idle minutes
locally so a lagging snapshot cannot re-pick the identical batch forever; credits
evaporate as the server catches up. The same credit is what rotates a never-accruing
title OUT of the batch so `Quarantine` can judge it against a fresh snapshot (2+
completed rotations, 240+ credited minutes, 30+ minutes since last credit) - with the
anomaly guard: if a majority of a healthy-size sample reports zero delta at once, suspect
data lag, bench nothing, log loudly.

## Persistence

`<Bot>_AdvancedIdle.db` beside the bot database: plain-text numeric lines
(`v 1` header; `q <appID>`; `o <appID> <serverAtFirst> <credited> <rotations> <unixSeconds>`;
`l <appID> <baseline> <credit>`), written atomically (temp + move) via the `*Async` file
APIs bridged at the call site.

Why so primitive: the OS-specific (trimmed) ASF hosts ship only the BCL surface ASF
itself uses. A missing member fails the referencing method AT JIT TIME - before its own
try/catch exists - so every store call site is additionally wrapped one frame up, and the
store's entire API surface is restricted to calls observed working on a live trimmed host
(`docs/upstream-notes.md`; enforced by `pregate/check_no_trim_risky_reflection.ps1`).
Persistence loss degrades to fresh state with a warning; it can never cost the feature,
and the feature can never cost the bot.

## Config

One registry (`Core/Config.cs` rows) is the single source of truth: the parser, the
defaults, and the README configuration section all derive from it (a unit test fails the
gate if the README drifts; regenerate via `tools/bless.ps1 -Docs`). Per-bot keys arrive
through `IBotModules.OnBotInitModules`, which re-fires on every config edit: the idler is
disposed and rebuilt from persisted state. Unknown `AdvancedIdle*` keys are ERRORS (a
typo'd setting must never be silently ignored); config errors disable the idler for that
bot, loudly.

## Extension seams (pre-documented, not pre-built)

- **Selection strategies:** the seam is the one pure function (`Selection.SelectBatch`);
  a future strategy (random, round-robin, target-hours) is a new pure function plus a
  registry row - nothing else moves.
- **Multi-bot:** already natural - per-bot config, per-bot idler and state file; the only
  plugin-wide state is the per-bot registries keyed by bot name.
- **Status surfaces:** `BotIdler.DescribeStatus` is the human string; a structured
  snapshot record could feed an IPC endpoint or external widget without touching logic.

## Test layers

- Pure core: unit tests per module (selection, accrual, quarantine, config, policy).
- Orchestrator: `BotIdlerSequenceTests` replays the event sequences from upstream-notes
  through the seams (fake clock/Steam/state/store) - no timers, no ASF.
- Oracles: blessed rotation trace (12 simulated rotations incl. sticky-zero + lag window)
  and the README docs-freshness check.
- `test-all.ps1` = build (both configurations, CI-strict) -> pregate -> tests; CI runs
  the same script. Live-Steam behavior is SMOKE.md, run against a real ASF.
