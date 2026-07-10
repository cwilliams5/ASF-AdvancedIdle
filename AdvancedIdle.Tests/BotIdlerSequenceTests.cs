using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedIdle;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

/// <summary>
///     Event sequences from docs/upstream-notes.md replayed against the orchestrator through its
///     seams (fake clock, fake Steam, fake bot state) - no timers, Pump driven directly.
/// </summary>
[TestClass]
public sealed class BotIdlerSequenceTests {
	[TestMethod]
	public async Task FullLifecycle() {
		FakeClock clock = new() { UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
		FakeState state = new() { Connected = true, Farming = false, FarmerPaused = false, PlayingPossible = true };

		FakeSteam steam = new() {
			Library = [
				new GameSnapshot(1, "a", 0),
				new GameSnapshot(2, "b", 10),
				new GameSnapshot(3, "c", 20),
				new GameSnapshot(4, "d", 30),
				new GameSnapshot(5, "e", 40)
			]
		};

		IdleConfig config = IdleConfig.Default with { Enabled = true, BatchSize = 3, RotationMinutes = 60 };
		using BotIdler idler = new(config, state, steam, new MemoryStore(), new ListLog(), clock, startHeartbeat: false);

		// Went idle (farming finished) -> first rotation asserts the three lowest.
		await idler.Pump("farming finished");
		Assert.AreEqual(1, steam.Sends.Count);
		CollectionAssert.AreEqual(new[] { 1u, 2u, 3u }, steam.Sends[0].AppIds);

		// Farming starts -> stand down, no send.
		state.Farming = true;
		await idler.Pump("farming started");
		Assert.AreEqual(1, steam.Sends.Count);

		// Farming ends 10 minutes later -> rotation not due, but the re-assert heartbeat sends the same batch.
		state.Farming = false;
		clock.Advance(TimeSpan.FromMinutes(10));
		await idler.Pump("farming finished");
		Assert.AreEqual(2, steam.Sends.Count);
		CollectionAssert.AreEqual(new[] { 1u, 2u, 3u }, steam.Sends[1].AppIds);

		// Past the rotation window -> new batch; local accrual credit must have lifted the idled
		// three above the untouched two even though the server (fake) never reported progress.
		clock.Advance(TimeSpan.FromMinutes(55));
		await idler.Pump("heartbeat");
		Assert.AreEqual(3, steam.Sends.Count);
		CollectionAssert.AreEqual(new[] { 4u, 5u, 1u }, steam.Sends[2].AppIds);

		// Playing elsewhere -> stand down; freed -> re-assert without waiting for rotation.
		state.PlayingPossible = false;
		await idler.Pump("playing-session change");
		Assert.AreEqual(3, steam.Sends.Count);

		state.PlayingPossible = true;
		clock.Advance(TimeSpan.FromMinutes(6));
		await idler.Pump("playing-session change");
		Assert.AreEqual(4, steam.Sends.Count);
		CollectionAssert.AreEqual(new[] { 4u, 5u, 1u }, steam.Sends[3].AppIds);

		// Disabled config never sends.
		using BotIdler disabled = new(IdleConfig.Default, state, steam, new MemoryStore(), new ListLog(), clock, startHeartbeat: false);
		await disabled.Pump("anything");
		Assert.AreEqual(4, steam.Sends.Count);
	}

	private sealed class FakeClock : IIdleClock {
		public DateTime UtcNow { get; set; }

		public void Advance(TimeSpan delta) => UtcNow += delta;
	}

	private sealed class FakeState : IBotStateView {
		public bool Connected { get; set; }
		public bool Farming { get; set; }
		public bool FarmerPaused { get; set; }
		public bool PlayingPossible { get; set; }
	}

	private sealed class FakeSteam : IIdleSteamActions {
		public IReadOnlyList<GameSnapshot> Library { get; set; } = [];
		public List<(uint[] AppIds, string? Name)> Sends { get; } = [];

		public Task PlayGames(IReadOnlyCollection<uint> appIDs, string? gameName) {
			Sends.Add((appIDs.ToArray(), gameName));

			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<GameSnapshot>?> FetchOwnedGames() => Task.FromResult<IReadOnlyList<GameSnapshot>?>(Library);
	}

	private sealed class MemoryStore : IStateStore {
		private PersistedState State = new();

		public PersistedState Load() => State;

		public void Save(PersistedState state) => State = state;
	}

	private sealed class ListLog : IIdleLog {
		public List<string> Lines { get; } = [];

		public void Info(string message) => Lines.Add($"I {message}");

		public void Warn(string message) => Lines.Add($"W {message}");
	}
}
