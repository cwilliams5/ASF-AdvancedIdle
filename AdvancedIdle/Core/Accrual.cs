using System;
using System.Collections.Generic;

namespace AdvancedIdle.Core;

/// <summary>
///     Local playtime credit for a single app: what the server reported when we started
///     crediting, plus the idle minutes we have credited locally since.
/// </summary>
public sealed record AccrualEntry(uint ServerBaselineMinutes, uint CreditedMinutes);

/// <summary>
///     Steam's reported playtime_forever lags real time (upstream-notes). Without local credit,
///     a lagging snapshot re-picks the identical lowest-N batch forever. These pure functions
///     maintain per-app credit that selection adds on top of server-reported playtime, and that
///     evaporates as the server catches up.
/// </summary>
public static class Accrual {
	/// <summary>Credits <paramref name="minutes" /> of idling to every batch member.</summary>
	public static IReadOnlyDictionary<uint, AccrualEntry> Credit(IReadOnlyDictionary<uint, AccrualEntry> ledger, IEnumerable<uint> batch, uint minutes, Func<uint, uint> serverMinutesOf) {
		ArgumentNullException.ThrowIfNull(ledger);
		ArgumentNullException.ThrowIfNull(batch);
		ArgumentNullException.ThrowIfNull(serverMinutesOf);

		Dictionary<uint, AccrualEntry> result = new(ledger);

		foreach (uint appID in batch) {
			result[appID] = result.TryGetValue(appID, out AccrualEntry? entry) ? entry with { CreditedMinutes = entry.CreditedMinutes + minutes } : new AccrualEntry(serverMinutesOf(appID), minutes);
		}

		return result;
	}

	/// <summary>
	///     Absorbs server catch-up: once reported playtime reaches (baseline + credit), the credit
	///     served its purpose and is dropped; partial catch-up shrinks it. A server value that
	///     regressed below the baseline keeps the entry untouched (report glitch, not catch-up).
	/// </summary>
	public static IReadOnlyDictionary<uint, AccrualEntry> Reconcile(IReadOnlyDictionary<uint, AccrualEntry> ledger, IReadOnlyDictionary<uint, uint> serverMinutes) {
		ArgumentNullException.ThrowIfNull(ledger);
		ArgumentNullException.ThrowIfNull(serverMinutes);

		Dictionary<uint, AccrualEntry> result = [];

		foreach ((uint appID, AccrualEntry entry) in ledger) {
			if (!serverMinutes.TryGetValue(appID, out uint reported)) {
				result[appID] = entry;

				continue;
			}

			uint believed = entry.ServerBaselineMinutes + entry.CreditedMinutes;

			if (reported >= believed) {
				continue;
			}

			result[appID] = reported > entry.ServerBaselineMinutes ? new AccrualEntry(reported, believed - reported) : entry;
		}

		return result;
	}

	/// <summary>Playtime selection should sort by: server-reported, plus any still-live local credit.</summary>
	public static uint EffectiveMinutes(IReadOnlyDictionary<uint, AccrualEntry> ledger, uint appID, uint serverMinutes) {
		ArgumentNullException.ThrowIfNull(ledger);

		return ledger.TryGetValue(appID, out AccrualEntry? entry) ? Math.Max(serverMinutes, entry.ServerBaselineMinutes + entry.CreditedMinutes) : serverMinutes;
	}
}
