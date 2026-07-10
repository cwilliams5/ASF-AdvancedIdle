# SMOKE.md — live-Steam choreography no check can see

Machine-unverifiable behavior (real Steam network, real client interplay). Run against a
real ASF install with `AdvancedIdleEnabled` on one bot. Every step is a release
precondition; when a new interactive failure is found, add it here first, then ask what
part can be pushed down into a check or unit test.

1. **Handshake.** Deploy the freshly built plugin, start ASF. The load line reports
   plugin name + version + git sha + the ASF version it was compiled against, and the sha
   matches `git describe` of the working tree. (Kills the stale-DLL class — if this line
   is wrong, STOP; everything after it tests the wrong binary.)
2. **Went-idle assertion.** With no cards to farm, logs show a batch of N appIDs sent
   within ~30s of logon. Steam profile (web, incognito) shows the account in-game.
3. **Batch composition.** `!aidle status` lists N games; spot-check two against the
   library: they are genuinely among the lowest-playtime, not blacklisted, not
   quarantined.
4. **Farming preemption.** Trigger farming (e.g. a refundable purchase with drops, or any
   account state with cards remaining): plugin logs stand-down, farmer plays its own set;
   when farming finishes, the batch re-asserts without a restart.
5. **Yield to real play.** Launch any game on your own client: plugin stops asserting
   (logs show blocked). Quit the game: batch re-asserts within the debounce window
   (no manual action).
6. **User pause respected.** `!pause` -> plugin stands down (unless
   `IdleWhileFarmerPaused`). `!resume` -> normal behavior returns.
7. **Rotation.** Reduce `AdvancedIdleRotationMinutes` to 5 temporarily: batch changes on
   schedule; previously idled titles show increased playtime in the next snapshot
   (allow for Steam's reporting lag).
8. **Custom name.** With `AdvancedIdleCustomGameName` set, the profile shows the label
   and batch is capped at 31.
9. **Config reload.** Edit a config value while ASF runs: plugin rebuilds its per-bot
   state (log line), no duplicate timers (only one rotation heartbeat firing afterward).
10. **Restart resilience.** Restart ASF: quarantine and accrual state survived
    (persisted file), batch re-asserts on logon.
11. **No send-echo loop.** With `AdvancedIdleCustomGameName` SET (the custom-name
    clean-session dance makes every assert flip session state twice), watch >= 10 minutes:
    after the initial transition burst, sends happen only on real transitions and the
    5-minute heartbeat - never a self-sustaining re-assert loop (witnessed 2026-07-10:
    2-second loop from unfiltered playing-session echoes).
