using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdvancedIdle.Core;

namespace AdvancedIdle;

// The seams BotIdler is built against (CLAUDE.md tattoo 3: thin shell, testable state machine).
// Production adapters over Bot/AdvancedIdleHandler live in AdvancedIdlePlugin; tests inject fakes.

public interface IIdleClock {
	DateTime UtcNow { get; }
}

public interface IBotStateView {
	bool Connected { get; }
	bool Farming { get; }
	bool FarmerPaused { get; }
	bool PlayingPossible { get; }
}

public interface IIdleSteamActions {
	Task PlayGames(IReadOnlyCollection<uint> appIDs, string? gameName);

	Task<IReadOnlyList<GameSnapshot>?> FetchOwnedGames();
}

public interface IIdleLog {
	void Info(string message);

	void Warn(string message);
}

public interface IStateStore {
	PersistedState Load();

	void Save(PersistedState state);
}

/// <summary>Per-app observation as persisted: absolute timestamp instead of a relative age, so downtime doesn't corrupt the quarantine margin.</summary>
public sealed record ObservationState(uint ServerMinutesAtFirstObservation, uint CreditedIdleMinutes, uint CompletedRotations, DateTime LastCreditUtc);

public sealed record PersistedState {
	public HashSet<uint> Quarantined { get; init; } = [];
	public Dictionary<uint, ObservationState> Observations { get; init; } = [];
	public Dictionary<uint, AccrualEntry> Ledger { get; init; } = [];
}
