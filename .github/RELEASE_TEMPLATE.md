### AdvancedIdle 0.1.0.0 — first release

AdvancedIdle idles your least-played owned games whenever the bot has nothing to
card-farm, re-picking the batch every rotation period so playtime spreads evenly
across your entire library.

**Highlights**

- **Card farming always wins.** The plugin never pauses or resumes ASF's CardsFarmer —
  it only paints games onto the session while the farmer is naturally idle, and yields
  the moment farming (or a real play session) needs the slot.
- **Even hours across the whole library.** Every `AdvancedIdleRotationMinutes` (default
  120) the batch is re-picked as the `AdvancedIdleBatchSize` (default 30, max 32) owned
  games with the lowest total playtime.
- **Quarantine for games that never accrue.** Titles that provably gain no playtime are
  benched automatically (`AdvancedIdleQuarantineEnabled`), with a systemic-anomaly guard
  so Steam's laggy playtime reporting can't mass-flag a healthy batch.
- **Quiet by default.** The batch is sent at transitions and rotations only; the
  5-minute re-assert heartbeat is opt-in (`AdvancedIdleHeartbeatReassert`, default off)
  because it is friend-visible when a custom name is set.
- **Cosmetics and control.** Optional `AdvancedIdleCustomGameName` label,
  `AdvancedIdleBlacklist` for titles you never want idled, and
  `AdvancedIdleIdleWhileFarmerPaused` for opting into idling during a paused farmer.

**Install**: unpack `AdvancedIdle.zip` into your ASF `plugins/AdvancedIdle/` folder and
set `"AdvancedIdleEnabled": true` in the bot config. Built against ASF 6.3.7.0. The full
configuration reference is in the README.

**Full Changelog**: https://github.com/cwilliams5/ASF-AdvancedIdle/commits/0.1.0.0
