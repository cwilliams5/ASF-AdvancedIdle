# CLAUDE.md — ASF-AdvancedIdle

## What this is

AdvancedIdle is a third-party ArchiSteamFarm plugin: when a bot has nothing to
card-farm, it idles the N owned games with the lowest total playtime, re-picking
the batch every rotation period so hours spread evenly across the whole library.
It permanently preempts ASF's static `GamesPlayedWhileIdle`. **This repo may go
public at any moment — every commit is written to that bar**: no secrets, no
personal data, no machine-specific paths, placeholder identities in examples.

## Architecture tattoos

1. **Never pause or resume the CardsFarmer.** Card farming has absolute
   priority; we only paint games onto the session while the farmer is naturally
   idle. `Bot.Actions.Play()` is FORBIDDEN — it permanently pauses farming
   (that is the `!play` command's semantics, not ours). We send
   `CMsgClientGamesPlayed` through our own SteamKit2 `ClientMsgHandler` instead.
2. **Last write wins; ours must always be able to land last, and be
   idempotent.** ASF re-asserts its (empty) idle set at every transition via its
   private `ResetGamesPlayed()`, and fires plugin events AFTER it. We write our
   batch in those events plus a re-assert heartbeat. Any code path that sets
   games must be safe to repeat and must gate on: connected, `!NowFarming`,
   `!Paused` (unless configured), `IsPlayingPossible`.
3. **Pure core, thin shell.** Batch selection, quarantine decisions, accrual
   bookkeeping, and config parsing are pure functions over plain data (no ASF
   types, no clock, no I/O) — unit-tested and exercised by the blessed rotation
   trace. The plugin class and handler are dumb glue. New logic goes in the
   core unless it literally cannot.

## Upstream discipline

Our correctness rests on hand-verified ASF behaviors (event ordering, the
went-idle signal, the 32-game cap, the PlayGames wire shape). Those facts are
pinned by `pregate/check_upstream_assumptions.ps1` and mapped to source in
`docs/upstream-notes.md`. When an ASF submodule bump fires that check: re-read
the cited upstream source, adapt OUR code first, and only then update the
check/pin — in the same commit, naming the upstream change.

Config keys exist only as rows in the settings registry (`Config.cs`); the
parser, defaults, and the README configuration section all derive from the rows.
Never hand-edit a generated doc section.

## Build & test

- `dotnet build` at repo root (first build compiles the ASF submodule; takes
  minutes once, seconds after). Debug builds auto-copy the plugin into the
  submodule's own `ArchiSteamFarm/.../plugins/AdvancedIdle/` for local runs.
- `pwsh ./test-all.ps1` is THE gate: build (exit-code checked) → pregate →
  tests. Human, AI, and CI run this same script — run it after every change and
  read the failures yourself; never ask the user to verify what a machine can.
  Never gate a commit on the gate's output *piped through another command* — a
  pipe swallows the exit code (that exact mistake landed a red-gated commit
  once). `git config core.hooksPath .githooks` once per clone: the pre-commit
  hook re-runs the pregate so a red gate physically can't be committed past.
- Live-Steam behavior (yielding to real play, farming preemption, profile
  display) is machine-unverifiable — that choreography lives in `SMOKE.md` and
  is a release precondition.

## Pregate system

`pregate/pregate.ps1` auto-discovers `pregate/check_*.ps1`; any failure blocks
the suite. Dropping a new `check_*.ps1` file IS registration. Checks encode
witnessed failures — each header names its incident.

### Philosophy
- **Prefer checks over rules.** Machines enforce; rules explain judgment.
- **Test before fix.** When a bug class is discovered, write the check FIRST
  while the bug is live proof, then fix.
- **Checks grow from failures.** Real development reveals new classes of bugs
  to gate.
- **Cage-modifier warning.** If a check fires on your change, fix the code, not
  the check. Modifying a check, allowlist, or pinned hash to make a failure
  disappear is the one move that always requires the human's eyes.

## Doc map

`docs/architecture.md` (component map + send policy + extension seams) ·
`docs/upstream-notes.md` (pinned ASF facts + re-pin protocol) · `SMOKE.md` (live
checklist) · `docs/smoke-2026-07-09.md` (first-night evidence). Machine-local
operator context (install paths, deploy recipe, user sensitivities) lives outside
the repo in the local assistant's persistent memory — nothing machine-specific is
tracked in-tree; recall it before any live-deploy work.

## Judgment rules (can't be checks)

- Steam-network behavior can differ from protocol shape — when logs disagree
  with expectations, trust a live SMOKE run over reasoning.
- Playtime reporting lags. Never conclude "not accruing" from one snapshot;
  quarantine logic keeps its systemic-anomaly guard (if most of a batch flags
  at once, suspect data lag, not thirty broken games).
- Check `docs/upstream-notes.md` and the in-tree ASF source (`ArchiSteamFarm/`,
  wiki included as a nested submodule) before guessing at ASF semantics.

## Additional Context

**Updating this file:** Main CLAUDE.md is for "tattoo" rules needed in every
session. Domain-specific lessons go in `.claude/rules/`. Before adding anything,
ask: "Would removing this cause Claude to make mistakes?" Prefer building static
analysis checks (`pregate/check_*.ps1`, auto-discovered by pre-gate) over adding
rules — machines enforce, rules explain judgment.