using System.Collections.Generic;
using System.Linq;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

[TestClass]
public sealed class QuarantineTests {
	private static readonly QuarantineRules Rules = QuarantineRules.Default;

	[TestMethod]
	public void GenuineStickyZeroIsBenched() {
		// A lone sticky-zero with a small sample must still bench (the anomaly guard needs a
		// minimum sample before it may veto).
		Dictionary<uint, ObservedApp> observations = new() { [1] = Observed(serverAtFirst: 7, credited: 240, rotations: 2, sinceLastCredit: 45) };

		QuarantineDecision decision = Quarantine.Evaluate(observations, Fresh((1, 7)), Rules);

		Assert.IsFalse(decision.AnomalyDetected);
		CollectionAssert.AreEqual(new[] { 1u }, decision.ToQuarantine.ToArray());
	}

	[TestMethod]
	public void AccruingAppIsNotBenched() {
		Dictionary<uint, ObservedApp> observations = new() { [1] = Observed(7, 240, 2, 45) };

		QuarantineDecision decision = Quarantine.Evaluate(observations, Fresh((1, 8)), Rules);

		Assert.AreEqual(0, decision.ToQuarantine.Count);
		Assert.AreEqual(1, decision.EvaluatedCount);
	}

	[TestMethod]
	public void UnderThresholdsIsNotEvaluated() {
		Dictionary<uint, ObservedApp> observations = new() {
			[1] = Observed(0, 240, 1, 45),   // too few rotations
			[2] = Observed(0, 120, 2, 45),   // too little credited idle
			[3] = Observed(0, 240, 2, 10)    // snapshot could still lag its idling
		};

		QuarantineDecision decision = Quarantine.Evaluate(observations, Fresh((1, 0), (2, 0), (3, 0)), Rules);

		Assert.AreEqual(0, decision.EvaluatedCount);
		Assert.AreEqual(0, decision.ToQuarantine.Count);
		Assert.IsFalse(decision.AnomalyDetected);
	}

	[TestMethod]
	public void MissingFromSnapshotIsSkipped() {
		Dictionary<uint, ObservedApp> observations = new() { [1] = Observed(0, 240, 2, 45) };

		QuarantineDecision decision = Quarantine.Evaluate(observations, Fresh(), Rules);

		Assert.AreEqual(0, decision.EvaluatedCount);
	}

	[TestMethod]
	public void SystemicZeroDeltaIsAnAnomalyNotAMassBench() {
		// Most of a healthy-sized sample "failing" at once = playtime-data lag or a broken idle
		// session; bench nothing, surface the anomaly.
		Dictionary<uint, ObservedApp> observations = [];

		for (uint appID = 1; appID <= 10; appID++) {
			observations[appID] = Observed(0, 240, 2, 45);
		}

		// 7 of 10 report zero delta.
		QuarantineDecision decision = Quarantine.Evaluate(observations, Fresh(
			(1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0), (7, 0),
			(8, 120), (9, 120), (10, 120)
		), Rules);

		Assert.IsTrue(decision.AnomalyDetected);
		Assert.AreEqual(0, decision.ToQuarantine.Count);
		Assert.AreEqual(10, decision.EvaluatedCount);
	}

	[TestMethod]
	public void MinorityZeroDeltaBenchesJustThose() {
		Dictionary<uint, ObservedApp> observations = [];

		for (uint appID = 1; appID <= 10; appID++) {
			observations[appID] = Observed(0, 240, 2, 45);
		}

		QuarantineDecision decision = Quarantine.Evaluate(observations, Fresh(
			(1, 0), (2, 0),
			(3, 120), (4, 120), (5, 120), (6, 120), (7, 120), (8, 120), (9, 120), (10, 120)
		), Rules);

		Assert.IsFalse(decision.AnomalyDetected);
		CollectionAssert.AreEqual(new[] { 1u, 2u }, decision.ToQuarantine.ToArray());
	}

	private static ObservedApp Observed(uint serverAtFirst, uint credited, uint rotations, uint sinceLastCredit) => new(serverAtFirst, credited, rotations, sinceLastCredit);

	private static Dictionary<uint, uint> Fresh(params (uint AppId, uint Minutes)[] entries) => entries.ToDictionary(static entry => entry.AppId, static entry => entry.Minutes);
}
