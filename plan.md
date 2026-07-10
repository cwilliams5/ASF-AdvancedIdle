# plan.md — AdvancedIdle v1

Design settled 2026-07-09 (research + decisions in the bootstrap session; upstream facts
in `docs/upstream-notes.md`). Phases run in order; each ends with `test-all.ps1` green and
a commit. User-gated items are marked.

## v1 semantics

When the bot is connected, not farming, not user-paused (unless configured), and playing
is possible: idle the `BatchSize` owned games with the lowest `playtime_forever`,
re-picking every `RotationMinutes`. Farm-cards-first is preserved by construction (we
never touch the farmer). ASF's `GamesPlayedWhileIdle` stays empty — our write lands after
ASF's clear at every transition (upstream-notes: event ordering).

### Selection (pure)

- Input: snapshot rows `(appid, playtimeForeverMinutes)`, config, quarantine set, local
  accrual credits, last batch.
- Filter: blacklist, quarantine. Sort: effective playtime asc, tiebreak appid asc
  (deterministic). Take `BatchSize` (31 if `CustomGameName` set).
- **Local accrual credit:** Steam's reported playtime lags (~up to 30 min). We credit
  in-progress idle time locally (batch members += minutes since batch assert while
  unblocked) before sorting, so a lagging snapshot cannot re-pick the identical batch
  forever. Credits are dropped once the server-reported number catches up past them.
- With a mostly-zero-playtime library this naturally "raises the floor": each rotation
  lifts 30 titles above the tie, and the next 30 rotate in.

### Quarantine (auto, on by default)

Failure class witnessed in the VaporChill era: titles that never accrue playtime become
permanent lowest-N residents and deadlock rotation.

- **Signal:** a title is quarantine-eligible only when server-reported delta == 0 across
  >= 2 completed rotations AND >= 4 hours of locally-credited unblocked idling, judged
  against a snapshot taken >= 30 min after that title last left the batch (never against
  a snapshot that could simply be lagging).
- **Systemic-anomaly guard:** if > 50% of an evaluated batch flags in one evaluation,
  treat it as playtime-data lag or an idle-session failure, quarantine nothing, log the
  anomaly. One title failing = that title's problem; the whole batch "failing" = our
  problem (or Steam's).
- Persisted; `!aidle quarantine` lists, `!aidle unquarantine` clears.

### Orchestrator (state machine, thin shell around the pure core)

States: `Disabled, WaitingSteam, Asserting, StandingDown(farming), Blocked(playing
elsewhere), UserPaused`. Triggers: `IBotCardsFarmerInfo` events (primary),
`IBotConnection.OnBotLoggedOn/Disconnected`, our `PlayingSessionStateCallback`
subscription (covers the one event-less ASF transition — upstream-notes), rotation timer,
re-assert heartbeat (~5 min; self-heals any missed transition; a games-played send is one
cheap CM message). Every assert re-checks guards immediately before sending, and delays
briefly after transitions so ASF's own `ResetGamesPlayed` (with its internal 2s wait)
lands first.

### Steam I/O (the handler)

One `ClientMsgHandler` registered via `IBotSteamClient.OnBotSteamHandlersInit`:
- `SendGamesPlayed(batch, customName)` — replicates `ArchiHandler.PlayGames` wire
  behavior exactly (upstream-notes; pinned by pregate A4).
- `GetOwnedGamesWithPlaytime()` — unified `Player.GetOwnedGames`, keeping
  `playtime_forever` that ASF's public wrapper drops.
Plus `OnBotSteamCallbacksInit` -> subscribe `SteamUser.PlayingSessionStateCallback`.

### Config registry (single source of truth)

`Config.cs` holds spec rows (key, type, default, validation, description). The parser
(`IBotModules.OnBotInitModules`, re-fires on config reload -> dispose + rebuild per-bot
state), the defaults, and the README `CONFIG:BEGIN/END` section all derive from the rows.
A unit test regenerates the README section in-memory and fails on drift (generated-doc
freshness rides the test suite). v1 rows: `Enabled, BatchSize, RotationMinutes,
Blacklist, CustomGameName, IdleWhileFarmerPaused, QuarantineEnabled`.

### Persistence

One `SerializableFile` per bot (`Bot.GetFilePath("<bot>_AdvancedIdle", EFileType.Database)`
pattern): quarantine set + rotation/accrual bookkeeping. Survives restarts (SMOKE #10).

## Test plan

- **Unit (pure core):** selection (filters, tiebreak, accrual credit, batch-size capping),
  config parsing (each row: valid/invalid/absent), quarantine decisions incl. the anomaly
  guard.
- **State machine:** orchestrator with injected fakes (clock, sender, snapshot source,
  bot-state view) — event sequences from upstream-notes replayed as tests (farming
  start/stop, block/unblock, reload, disconnect).
- **Blessed rotation trace:** synthetic 200-game library fixture + fake clock, simulate
  ~10 rotations (including one sticky-zero title and one lag window), emit a normalized
  trace, diff against the committed baseline. Deliberate re-bless = flag + justified
  commit. This is the repo's end-to-end determinism oracle.
- **Docs freshness:** README config section == registry-generated text.
- **Live:** SMOKE.md (10 steps) — machine-unverifiable Steam behavior; release
  precondition.

## Phases

- **Phase 0 — scaffold + cage.** DONE (2026-07-09). Template instantiated (repo
  `cwilliams5/ASF-AdvancedIdle`, ASF pinned 6.3.7.0), CLAUDE.md, pregate runner + 4
  checks, test-all, CI rewired to the gate, README, SMOKE.md, /release skill, this plan.
  Post-hoc additions from live incidents: pre-commit hook (pipe-swallowed gate exit),
  untracked-file scan coverage, secrets check self-violation fix.
- **Phase 1 — pure core + tests.** DONE (2026-07-09). Config registry + parser,
  selection, accrual, quarantine; MSTest project (mirrors ASF's own test stack) wired
  into the slnx; README docs-freshness oracle + rotation-trace baseline blessed and
  read-verified (sticky-zero benched at r4, lag rotations bench nothing, floor raised
  417..536 from 0..239).
- **Phase 2 — ASF shell.** DONE (2026-07-09). Plugin class (7 interfaces +
  version-handshake OnLoaded line), AdvancedIdleHandler (games-played send replicating
  upstream wire behavior + unified owned-games keeping playtime), BotIdler orchestrator
  behind seams (pure IdlePolicy + fake-driven sequence test), `!aidle` commands
  (status/rotate/quarantine/unquarantine), atomic JSON persistence beside the bot db.
- **Phase 3 — live smoke.** MOSTLY DONE (2026-07-09 overnight, user-blessed unattended run
  on the real ASF; evidence: `docs/smoke-2026-07-09.md`). Passed: handshake, went-idle
  assert, rotation-on-schedule, config hot-reload, restart resilience, persistence,
  coexistence with static GPWI + FreePackages; fixed live: forced transition re-asserts +
  the trimmed-host (win-x64) API saga. Remaining [USER-GATED]: real card-drop preemption,
  human-at-keyboard yield test, `!aidle` command spot-checks, custom-name display,
  quarantine watching for real sticky-zeros over the coming days.
- **Phase 4 — release + visibility.** [USER-GATED] Exercise /release end-to-end (tag ->
  CI zip -> install), decide public, enable ASF plugin auto-update if public.

## Extension slots (pre-documented, not pre-built)

- **Selection strategies:** the seam is the one pure function; a future strategy is a new
  function + a registry row (`SelectionStrategy`) — nothing else moves.
- **Multi-bot:** already natural (per-bot config + per-bot state); no global state
  outside the plugin-wide handler registry keyed by bot.
- **Status surfaces:** the orchestrator's status snapshot is a plain record — an IPC/API
  or external widget (e.g. an app-bar tile) can serialize it without touching logic.
