---
name: release
description: Cut an ASF-AdvancedIdle release (gate, SMOKE, version bump, tag, verify CI artifact, flip stable)
disable-model-invocation: true
---

# Release ASF-AdvancedIdle

Deliberate, human-invoked. Hard stops are non-negotiable — do not proceed past a STOP on
inference or memory. The human may pass context with the invocation (bump size, release-note
focus area) — fold it into the steps below.

Verified mechanics (2026-07-10, read from publish.yml): the tag push triggers publish.yml,
which zips the publish-output CONTENTS (so `AdvancedIdle.dll` sits at the zip ROOT — the
layout ASF's auto-updater requires) and creates the GitHub release IMMEDIATELY (not a
draft) with `prerelease: true`, `makeLatest: false`, `immutableCreate: true`, body from
`.github/RELEASE_TEMPLATE.md`, assets = `AdvancedIdle.zip` + `SHA512SUMS`.

1. **Preconditions.** Working tree clean, `main` pushed, CI green on HEAD
   (`gh run list -R cwilliams5/ASF-AdvancedIdle --limit 2`). Run `pwsh ./test-all.ps1`
   (covers Debug AND Release at CI strictness) and read the output yourself.
2. **STOP — SMOKE.** Ask the human to confirm SMOKE.md was executed against THIS build
   (all 11 steps, or which were consciously skipped and why). Do not accept "it worked
   last week."
3. **Version.** Bump `<Version>` in `Directory.Build.props` (four-part, e.g. `0.2.0.0`).
   First release: `0.1.0.0` is already set — confirm with the human it ships as-is
   rather than bumping. That one edit propagates everywhere: assembly version, generated
   HandshakeInfo constants, the OnLoaded handshake line. **The git tag must equal this string exactly —
   bare, no `v` prefix** (ecosystem norm; FreePackages tags `1.7.0.0`). ASF's updater
   parses the tag as a Version and compares it to the installed plugin's.
4. **Release notes.** Build from `git log <last-tag>..HEAD --oneline` (first release:
   no prior tag exists — use the full `git log --oneline`). Write a human-readable
   summary of what changed and why a user cares; if the human named a focus area,
   emphasize it. End with a changelog link, bare tags matching ours:
   `**Full Changelog**: https://github.com/cwilliams5/ASF-AdvancedIdle/compare/<OLD>...<NEW>`
   (first release instead:
   `**Full Changelog**: https://github.com/cwilliams5/ASF-AdvancedIdle/commits/<Version>`).
   The release body is read from `.github/RELEASE_TEMPLATE.md` AT THE TAG COMMIT
   (publish.yml `bodyFile`) and `immutableCreate` restricts editing after publish — so
   write the notes INTO that file as part of the bump commit; each release's bump
   commit replaces the previous release's notes there.
5. **Commit + tag + push.** Conventional commit for the bump, then
   `git tag <Version> && git push origin main <Version>`. The pre-commit hook runs the
   pregate; CI runs the full gate again.
6. **Verify the actual artifact.** After publish.yml finishes (~6 min):
   `gh release download <Version> -p AdvancedIdle.zip -p SHA512SUMS`, check the zip's
   SHA-512 against SHA512SUMS, extract, verify the dll is at the ZIP ROOT and its
   ProductVersion matches the tag. Tests ran from build output — the release zip is
   the one artifact never otherwise executed.
7. **STOP — flip to stable.** The release is born a PRERELEASE, and ASF's default
   `UpdateChannel: Stable` IGNORES prereleases — user auto-updates arm only when the
   human reviews the release page and flips it:
   `gh release edit <Version> --prerelease=false --latest`.
   (First run: if `immutableCreate` rejects the edit, flip it in the web UI and record
   here which path worked.) Reminder: the updater queries GitHub anonymously — releases
   reach users only while the repo is PUBLIC. Making the repo public is its own human
   decision, not this skill's.
8. **Report.** Show the release URL and a short summary of what shipped and what state
   it's in (prerelease vs stable, verified vs not).
