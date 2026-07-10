using System;
using System.Collections.Generic;
using System.IO;
using AdvancedIdle;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

#pragma warning disable CA1812 // instantiated by MSTest via [assembly: DiscoverInternals]
[TestClass]
internal sealed class StateStoreTests {
	[TestMethod]
	public void RoundTripsPopulatedState() {
		string path = Path.Combine(Path.GetTempPath(), $"advancedidle-test-{Guid.NewGuid():N}.db");

		try {
			FileStateStore store = new(path, new NullLog());

			// Whole-second timestamps: the format stores Unix seconds by design.
			DateTime lastCredit = new(2026, 7, 9, 22, 46, 22, DateTimeKind.Utc);

			PersistedState original = new() {
				Quarantined = { 1013, 440 },
				Observations = { [570] = new ObservationState(7, 240, 2, lastCredit), [730] = new ObservationState(0, 120, 1, lastCredit.AddMinutes(-30)) },
				Ledger = { [570] = new AccrualEntry(7, 55) }
			};

			store.Save(original);
			PersistedState loaded = store.Load();

			CollectionAssert.AreEquivalent(new[] { 440u, 1013u }, new List<uint>(loaded.Quarantined));
			Assert.AreEqual(original.Observations[570], loaded.Observations[570]);
			Assert.AreEqual(original.Observations[730], loaded.Observations[730]);
			Assert.AreEqual(original.Ledger[570], loaded.Ledger[570]);
			Assert.AreEqual(2, loaded.Observations.Count);
		} finally {
			if (File.Exists(path)) {
				File.Delete(path);
			}
		}
	}

	[TestMethod]
	public void MissingFileYieldsFreshState() {
		FileStateStore store = new(Path.Combine(Path.GetTempPath(), $"advancedidle-missing-{Guid.NewGuid():N}.db"), new NullLog());

		PersistedState state = store.Load();

		Assert.AreEqual(0, state.Quarantined.Count);
		Assert.AreEqual(0, state.Observations.Count);
		Assert.AreEqual(0, state.Ledger.Count);
	}

	[TestMethod]
	public void CorruptFileDegradesToFreshStateWithWarning() {
		string path = Path.Combine(Path.GetTempPath(), $"advancedidle-corrupt-{Guid.NewGuid():N}.db");

		try {
			File.WriteAllText(path, "{not json");
			ListLog log = new();
			FileStateStore store = new(path, log);

			PersistedState state = store.Load();

			Assert.AreEqual(0, state.Quarantined.Count);
			Assert.AreEqual(1, log.Warnings.Count);
		} finally {
			File.Delete(path);
		}
	}

	private sealed class NullLog : IIdleLog {
		public void Info(string message) { }

		public void Warn(string message) { }
	}

	private sealed class ListLog : IIdleLog {
		public List<string> Warnings { get; } = [];

		public void Info(string message) { }

		public void Warn(string message) => Warnings.Add(message);
	}
}
