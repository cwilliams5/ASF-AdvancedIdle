using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedIdle.Core;

/// <summary>What we have observed about one app across its time in idle batches.</summary>
/// <param name="ServerMinutesAtFirstObservation">Reported playtime when we first started idling it.</param>
/// <param name="CreditedIdleMinutes">Total unblocked idle minutes we credited to it.</param>
/// <param name="CompletedRotations">Full rotations it spent in the batch.</param>
/// <param name="MinutesSinceLastCredit">How long ago its last credit window closed - guards against judging with a snapshot that merely lags the idling.</param>
public sealed record ObservedApp(uint ServerMinutesAtFirstObservation, uint CreditedIdleMinutes, uint CompletedRotations, uint MinutesSinceLastCredit);

public sealed record QuarantineRules(uint MinCompletedRotations = 2, uint MinCreditedIdleMinutes = 240, uint MinMinutesSinceLastCredit = 30, double AnomalyFraction = 0.5, int MinSampleForAnomaly = 4) {
	public static QuarantineRules Default { get; } = new();
}

public sealed record QuarantineDecision(IReadOnlyCollection<uint> ToQuarantine, bool AnomalyDetected, int EvaluatedCount);

/// <summary>
///     Failure class witnessed in the VaporChill era: titles that never accrue playtime become
///     permanent lowest-N residents and (30 of them) deadlock rotation entirely. A title is benched
///     only on strong evidence: enough completed rotations, enough credited idle time, and a fresh
///     snapshot taken well after its last credit window - never against data that could simply lag.
/// </summary>
public static class Quarantine {
	public static QuarantineDecision Evaluate(IReadOnlyDictionary<uint, ObservedApp> observations, IReadOnlyDictionary<uint, uint> freshServerMinutes, QuarantineRules rules) {
		ArgumentNullException.ThrowIfNull(observations);
		ArgumentNullException.ThrowIfNull(freshServerMinutes);
		ArgumentNullException.ThrowIfNull(rules);

		List<uint> evaluated = [];
		List<uint> candidates = [];

		foreach ((uint appID, ObservedApp observed) in observations) {
			if ((observed.CompletedRotations < rules.MinCompletedRotations) || (observed.CreditedIdleMinutes < rules.MinCreditedIdleMinutes) || (observed.MinutesSinceLastCredit < rules.MinMinutesSinceLastCredit)) {
				continue;
			}

			if (!freshServerMinutes.TryGetValue(appID, out uint reported)) {
				continue;
			}

			evaluated.Add(appID);

			if (reported <= observed.ServerMinutesAtFirstObservation) {
				candidates.Add(appID);
			}
		}

		// Systemic-anomaly guard: one title failing to accrue is that title's problem; most of an
		// evaluated sample "failing" at once means playtime-data lag or a broken idle session - our
		// problem (or Steam's), so bench nothing and surface it. Small samples cannot establish
		// "systemic", hence the minimum sample size (a lone genuine sticky-zero must still bench).
		bool anomaly = (evaluated.Count >= rules.MinSampleForAnomaly) && (candidates.Count > rules.AnomalyFraction * evaluated.Count);

		return new QuarantineDecision(anomaly ? [] : candidates.OrderBy(static appID => appID).ToArray(), anomaly, evaluated.Count);
	}
}
