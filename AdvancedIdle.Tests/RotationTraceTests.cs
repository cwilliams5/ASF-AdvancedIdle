using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

/// <summary>
///     The repo's end-to-end determinism oracle (plan.md): a synthetic library driven through
///     selection + accrual + quarantine across simulated rotations, including one sticky-zero
///     title and a playtime-reporting lag window. The normalized trace diffs against a blessed
///     baseline - the layer that catches "correct refactoring logic with incorrect ripple
///     effects" in the core, which unit tests miss.
/// </summary>
#pragma warning disable CA1812 // instantiated by MSTest via [assembly: DiscoverInternals]
[TestClass]
internal sealed class RotationTraceTests {
	private const uint StickyAppId = 1013; // never accrues server-side (the VaporChill-era failure class)

	[TestMethod]
	public void RotationTraceMatchesBlessedBaseline() {
		string trace = Simulate();
		string baselinePath = Path.Combine(TestPaths.RepoRoot(), "AdvancedIdle.Tests", "baselines", "rotation_trace.txt");

		if (TestPaths.BlessRequested("ADVANCEDIDLE_BLESS_TRACE")) {
			Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
			File.WriteAllText(baselinePath, trace);

			return;
		}

		Assert.IsTrue(File.Exists(baselinePath), "No blessed baseline. Generate via tools/bless.ps1 -Trace, READ the trace, then commit it.");

		string expected = TestPaths.NormalizeNewlines(File.ReadAllText(baselinePath));

		Assert.AreEqual(expected, TestPaths.NormalizeNewlines(trace), "Rotation trace drifted from the blessed baseline. If the behavior change is intended, re-bless via tools/bless.ps1 -Trace AFTER reading the diff - re-blessing to silence an unexplained diff is the cage-modifier antipattern (CLAUDE.md).");
	}

	private static string Simulate() {
		IdleConfig config = IdleConfig.Default with { Enabled = true, BatchSize = 10, RotationMinutes = 120 };
		QuarantineRules rules = QuarantineRules.Default;

		// 40 games, appIDs 1000-1039, deterministic initial playtimes spread over 0..239 minutes.
		Dictionary<uint, uint> trueServer = [];

		for (uint i = 0; i < 40; i++) {
			trueServer[1000 + i] = (i * 37) % 240;
		}

		Dictionary<uint, uint> reported = new(trueServer);
		IReadOnlyDictionary<uint, AccrualEntry> ledger = new Dictionary<uint, AccrualEntry>();
		Dictionary<uint, ObservedApp> observations = [];
		SortedSet<uint> quarantined = [];
		StringBuilder trace = new();

		for (int rotation = 1; rotation <= 12; rotation++) {
			bool lag = rotation is 5 or 6; // Steam playtime reporting freezes for two rotations

			List<GameSnapshot> library = reported.OrderBy(static pair => pair.Key).Select(static pair => new GameSnapshot(pair.Key, "game", pair.Value)).ToList();
			IReadOnlyList<uint> batch = Selection.SelectBatch(library, config, quarantined, ledger);

			// The whole rotation idles unblocked: credit locally; the server accrues too (except sticky).
			ledger = Accrual.Credit(ledger, batch, config.RotationMinutes, appID => reported[appID]);

			foreach (uint appID in batch) {
				if (appID != StickyAppId) {
					trueServer[appID] += config.RotationMinutes;
				}

				observations[appID] = observations.TryGetValue(appID, out ObservedApp? existing)
					? existing with { CreditedIdleMinutes = existing.CreditedIdleMinutes + config.RotationMinutes, CompletedRotations = existing.CompletedRotations + 1, MinutesSinceLastCredit = 0 }
					: new ObservedApp(reported[appID], config.RotationMinutes, 1, 0);
			}

			foreach (uint appID in observations.Keys.Where(appID => !batch.Contains(appID)).ToList()) {
				observations[appID] = observations[appID] with { MinutesSinceLastCredit = observations[appID].MinutesSinceLastCredit + config.RotationMinutes };
			}

			if (!lag) {
				reported = new Dictionary<uint, uint>(trueServer);
			}

			ledger = Accrual.Reconcile(ledger, reported);

			QuarantineDecision decision = Quarantine.Evaluate(observations, reported, rules);

			foreach (uint appID in decision.ToQuarantine) {
				quarantined.Add(appID);
				observations.Remove(appID);
			}

			trace.AppendLine(string.Create(CultureInfo.InvariantCulture, $"r={rotation:D2} lag={(lag ? 1 : 0)} batch=[{string.Join(',', batch)}] evaluated={decision.EvaluatedCount} newQuarantine=[{string.Join(',', decision.ToQuarantine)}] anomaly={(decision.AnomalyDetected ? 1 : 0)}"));
		}

		uint min = trueServer.Where(static pair => pair.Key != StickyAppId).Min(static pair => pair.Value);
		uint max = trueServer.Where(static pair => pair.Key != StickyAppId).Max(static pair => pair.Value);
		trace.AppendLine(string.Create(CultureInfo.InvariantCulture, $"final quarantined=[{string.Join(',', quarantined)}] trueServerMin={min} trueServerMax={max}"));

		return trace.ToString();
	}
}
