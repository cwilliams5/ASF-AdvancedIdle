using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedIdle.Core;

namespace AdvancedIdle;

/// <summary>
///     Per-bot orchestrator: thin shell around the pure core (CLAUDE.md tattoo 3). All entry
///     points funnel into <see cref="Pump" /> under one semaphore; every send re-checks the guards
///     immediately before sending, and every send is idempotent (tattoo 2). One heartbeat timer
///     covers rotation, re-assertion, and self-healing after missed transitions.
/// </summary>
public sealed class BotIdler : IDisposable {
	private const uint HeartbeatMinutes = 5;
	private const uint ReassertMinutes = 5;
	private const double CompletedRotationFraction = 0.9;
	private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(90);

	private readonly IIdleClock Clock;
	private readonly IdleConfig Config;
	private readonly IIdleLog Log;
	private readonly IIdleSteamActions Steam;
	private readonly IBotStateView State;
	private readonly IStateStore Store;
	private readonly SemaphoreSlim Sync = new(1, 1);
	private readonly Timer? Heartbeat;

	private bool AssertActive;
	private DateTime BatchAssertedUtc = DateTime.MinValue;
	private IReadOnlyList<uint> CurrentBatch = [];
	private DateTime CreditStartUtc = DateTime.MinValue;
	private bool Disposed;
	private DateTime LastSendUtc = DateTime.MinValue;
	private IReadOnlyDictionary<uint, AccrualEntry> Ledger;
	private IReadOnlyList<GameSnapshot> Library = [];
	private Dictionary<uint, ObservationState> Observations;
	private HashSet<uint> Quarantined;
	private DateTime SnapshotUtc = DateTime.MinValue;
	private uint TenureCreditedMinutes;

	public BotIdler(IdleConfig config, IBotStateView state, IIdleSteamActions steam, IStateStore store, IIdleLog log, IIdleClock clock, bool startHeartbeat = true) {
		Config = config ?? throw new ArgumentNullException(nameof(config));
		State = state ?? throw new ArgumentNullException(nameof(state));
		Steam = steam ?? throw new ArgumentNullException(nameof(steam));
		Store = store ?? throw new ArgumentNullException(nameof(store));
		Log = log ?? throw new ArgumentNullException(nameof(log));
		Clock = clock ?? throw new ArgumentNullException(nameof(clock));

		PersistedState persisted = store.Load();
		Quarantined = persisted.Quarantined;
		Observations = persisted.Observations;
		Ledger = persisted.Ledger;

		if (startHeartbeat) {
			Heartbeat = new Timer(_ => _ = Pump("heartbeat"), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(HeartbeatMinutes));
		}
	}

	public void Dispose() {
		if (Disposed) {
			return;
		}

		Disposed = true;
		Heartbeat?.Dispose();
		Sync.Dispose();
	}

	public string DescribeStatus() {
		DateTime now = Clock.UtcNow;
		IdleDecision decision = Decide();
		double nextRotationMinutes = (CurrentBatch.Count > 0) && (BatchAssertedUtc > DateTime.MinValue) ? Math.Max(0, Config.RotationMinutes - (now - BatchAssertedUtc).TotalMinutes) : 0;
		double snapshotAgeMinutes = SnapshotUtc > DateTime.MinValue ? (now - SnapshotUtc).TotalMinutes : -1;

		return $"state={decision} batch={CurrentBatch.Count} [{string.Join(",", CurrentBatch.Take(8))}{(CurrentBatch.Count > 8 ? ",..." : "")}] nextRotationIn={nextRotationMinutes:F0}m snapshotAge={snapshotAgeMinutes:F0}m library={Library.Count} quarantined={Quarantined.Count} blacklist={Config.Blacklist.Count}";
	}

	public IReadOnlyCollection<uint> QuarantinedAppIds => Quarantined.OrderBy(static appID => appID).ToArray();

	public async Task ForceRotation() {
		await Sync.WaitAsync().ConfigureAwait(false);

		try {
			BatchAssertedUtc = DateTime.MinValue;
		} finally {
			Sync.Release();
		}

		await Pump("forced rotation").ConfigureAwait(false);
	}

	public async Task<int> Unquarantine(IReadOnlyCollection<uint> appIDs) {
		ArgumentNullException.ThrowIfNull(appIDs);

		await Sync.WaitAsync().ConfigureAwait(false);

		try {
			int removed = appIDs.Count(appID => Quarantined.Remove(appID));

			if (removed > 0) {
				Persist();
			}

			return removed;
		} finally {
			Sync.Release();
		}
	}

	/// <summary>
	///     The single mover. Safe to call from any event, any time, any number of times.
	///     <paramref name="forceReassert" /> is passed by transition triggers (farming stopped,
	///     account freed, logon): ASF's own ResetGamesPlayed has just overwritten the session
	///     (live smoke: their GamesPlayedWhileIdle held for 5 minutes until the heartbeat), so
	///     the re-assert must not wait for the heartbeat window.
	/// </summary>
	public async Task Pump(string reason, bool forceReassert = false) {
		if (Disposed) {
			return;
		}

		await Sync.WaitAsync().ConfigureAwait(false);

		try {
			if (Disposed) {
				return;
			}

			DateTime now = Clock.UtcNow;
			IdleDecision decision = Decide();

			if (decision != IdleDecision.Idle) {
				if (AssertActive) {
					SettleCredit(now);
					AssertActive = false;
					Log.Info($"standing down ({decision}, trigger: {reason})");
				}

				return;
			}

			bool rotationDue = (CurrentBatch.Count == 0) || (BatchAssertedUtc == DateTime.MinValue) || ((now - BatchAssertedUtc).TotalMinutes >= Config.RotationMinutes);

			if (rotationDue) {
				await Rotate(now, reason).ConfigureAwait(false);

				return;
			}

			if (AssertActive) {
				SettleCredit(now);
			}

			// Re-assert: one cheap idempotent CM message. Forced on transitions (ASF wrote over
			// us); on the heartbeat it self-heals any transition we missed (e.g. ASF's event-less
			// ResetGamesPlayed path, upstream-notes).
			if ((CurrentBatch.Count > 0) && (forceReassert || ((now - LastSendUtc).TotalMinutes >= ReassertMinutes))) {
				await Send(now, $"re-assert ({reason})").ConfigureAwait(false);
			}
		} catch (Exception e) {
			// Timer callbacks discard this task - without this catch, a throw anywhere in the
			// pump would be completely invisible (no log, no crash) and unfixable from the field.
			// Silence is not success.
			Log.Warn($"pump crashed ({reason}): {e}");
		} finally {
			Sync.Release();
		}
	}

	private IdleDecision Decide() => IdlePolicy.Decide(new BotSituation(Config.Enabled, State.Connected, State.Farming, State.FarmerPaused, State.PlayingPossible, Config.IdleWhileFarmerPaused));

	private async Task Rotate(DateTime now, string reason) {
		// Close out the outgoing batch: settle credit, award completed rotations.
		if (AssertActive) {
			SettleCredit(now);
			AssertActive = false;
		}

		if ((CurrentBatch.Count > 0) && (TenureCreditedMinutes >= CompletedRotationFraction * Config.RotationMinutes)) {
			foreach (uint appID in CurrentBatch) {
				if (Observations.TryGetValue(appID, out ObservationState? observed)) {
					Observations[appID] = observed with { CompletedRotations = observed.CompletedRotations + 1 };
				}
			}
		}

		TenureCreditedMinutes = 0;

		// Fresh snapshot; on failure keep the stale one (selection tolerates, retry next pump).
		// Bounded wait: SteamKit jobs can heartbeat-extend indefinitely, and an unbounded await
		// here would hold the pump semaphore (and thus all idling) hostage to one flaky call.
		Task<IReadOnlyList<GameSnapshot>?> fetchTask = Steam.FetchOwnedGames();
		Task finished = await Task.WhenAny(fetchTask, Task.Delay(FetchTimeout)).ConfigureAwait(false);
		IReadOnlyList<GameSnapshot>? fetched = finished == fetchTask ? await fetchTask.ConfigureAwait(false) : null;

		if (fetched is { Count: > 0 }) {
			Library = fetched;
			SnapshotUtc = now;
			Ledger = Accrual.Reconcile(Ledger, BuildReported());
		} else {
			Log.Warn(finished == fetchTask ? "owned-games fetch failed, using previous snapshot" : "owned-games fetch timed out, using previous snapshot");
		}

		if (Library.Count == 0) {
			Log.Warn("no library snapshot yet, nothing to idle");

			return;
		}

		if (Config.QuarantineEnabled) {
			EvaluateQuarantine(now);
		}

		IReadOnlyList<uint> batch = Selection.SelectBatch(Library, Config, Quarantined, Ledger);

		if (batch.Count == 0) {
			Log.Warn("selection produced an empty batch (library filtered away entirely?)");

			return;
		}

		CurrentBatch = batch;
		BatchAssertedUtc = now;

		Dictionary<uint, uint> reported = BuildReported();

		foreach (uint appID in batch) {
			if (!Observations.ContainsKey(appID)) {
				Observations[appID] = new ObservationState(reported.GetValueOrDefault(appID), 0, 0, now);
			}
		}

		await Send(now, $"rotation ({reason})").ConfigureAwait(false);
		Persist();
	}

	private async Task Send(DateTime now, string reason) {
		await Steam.PlayGames(CurrentBatch, Config.CustomGameName).ConfigureAwait(false);
		LastSendUtc = now;

		if (!AssertActive) {
			AssertActive = true;
			CreditStartUtc = now;
		}

		Log.Info($"idling {CurrentBatch.Count} games: {reason}");
	}

	private void SettleCredit(DateTime now) {
		uint minutes = (uint) Math.Max(0, (now - CreditStartUtc).TotalMinutes);

		if (minutes == 0) {
			return;
		}

		Dictionary<uint, uint> reported = BuildReported();
		Ledger = Accrual.Credit(Ledger, CurrentBatch, minutes, appID => reported.GetValueOrDefault(appID));
		TenureCreditedMinutes += minutes;

		foreach (uint appID in CurrentBatch) {
			Observations[appID] = Observations.TryGetValue(appID, out ObservationState? observed)
				? observed with { CreditedIdleMinutes = observed.CreditedIdleMinutes + minutes, LastCreditUtc = now }
				: new ObservationState(reported.GetValueOrDefault(appID), minutes, 0, now);
		}

		CreditStartUtc = now;
	}

	private void EvaluateQuarantine(DateTime now) {
		Dictionary<uint, ObservedApp> observed = Observations.ToDictionary(
			static pair => pair.Key,
			pair => new ObservedApp(pair.Value.ServerMinutesAtFirstObservation, pair.Value.CreditedIdleMinutes, pair.Value.CompletedRotations, (uint) Math.Max(0, (now - pair.Value.LastCreditUtc).TotalMinutes))
		);

		QuarantineDecision decision = Quarantine.Evaluate(observed, BuildReported(), QuarantineRules.Default);

		if (decision.AnomalyDetected) {
			Log.Warn($"quarantine anomaly guard tripped: majority of {decision.EvaluatedCount} evaluated titles report zero accrual - suspecting playtime-data lag or a broken idle session, benching nothing");

			return;
		}

		if (decision.ToQuarantine.Count == 0) {
			return;
		}

		foreach (uint appID in decision.ToQuarantine) {
			Quarantined.Add(appID);
			Observations.Remove(appID);
		}

		Log.Warn($"quarantined {decision.ToQuarantine.Count} never-accruing title(s): {string.Join(",", decision.ToQuarantine)} (inspect: !aidle quarantine; undo: !aidle unquarantine)");
		Persist();
	}

	/// <summary>
	///     Dup-safe library index (keep the max playtime on duplicates). ToDictionary would throw
	///     on a duplicate appID, and nothing guarantees Steam's 30k-game response never repeats
	///     one - a wire response is not a place to assume uniqueness invariants.
	/// </summary>
	private Dictionary<uint, uint> BuildReported() {
		Dictionary<uint, uint> reported = new(Library.Count);

		foreach (GameSnapshot game in Library) {
			reported[game.AppId] = reported.TryGetValue(game.AppId, out uint existing) ? Math.Max(existing, game.PlaytimeForeverMinutes) : game.PlaytimeForeverMinutes;
		}

		return reported;
	}

	private void Persist() {
		try {
			Store.Save(new PersistedState { Quarantined = Quarantined, Observations = Observations, Ledger = new Dictionary<uint, AccrualEntry>(Ledger) });
		} catch (Exception e) {
			Log.Warn($"state persist failed: {e.Message}");
		}
	}
}
