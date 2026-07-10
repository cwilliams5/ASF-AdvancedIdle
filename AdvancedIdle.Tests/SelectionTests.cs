using System.Collections.Generic;
using System.Linq;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

#pragma warning disable CA1812 // instantiated by MSTest via [assembly: DiscoverInternals]
[TestClass]
internal sealed class SelectionTests {
	private static readonly IReadOnlyDictionary<uint, AccrualEntry> NoAccrual = new Dictionary<uint, AccrualEntry>();

	[TestMethod]
	public void PicksLowestPlaytimeWithAppIdTiebreak() {
		List<GameSnapshot> library = [
			new(30, "c", 5),
			new(10, "a", 5),
			new(20, "b", 0),
			new(40, "d", 99)
		];

		IReadOnlyList<uint> batch = Selection.SelectBatch(library, Config(batchSize: 3), [], NoAccrual);

		CollectionAssert.AreEqual(new[] { 20u, 10u, 30u }, batch.ToArray());
	}

	[TestMethod]
	public void BlacklistAndQuarantineExcluded() {
		List<GameSnapshot> library = [
			new(1, "a", 0),
			new(2, "b", 0),
			new(3, "c", 0),
			new(4, "d", 0)
		];

		IdleConfig config = Config(batchSize: 4) with { Blacklist = new HashSet<uint> { 1 } };

		IReadOnlyList<uint> batch = Selection.SelectBatch(library, config, [3], NoAccrual);

		CollectionAssert.AreEqual(new[] { 2u, 4u }, batch.ToArray());
	}

	[TestMethod]
	public void AccrualCreditShiftsOrder() {
		List<GameSnapshot> library = [
			new(1, "a", 0),
			new(2, "b", 10)
		];

		Dictionary<uint, AccrualEntry> accrual = new() { [1] = new AccrualEntry(0, 60) };

		IReadOnlyList<uint> batch = Selection.SelectBatch(library, Config(batchSize: 1), [], accrual);

		CollectionAssert.AreEqual(new[] { 2u }, batch.ToArray());
	}

	[TestMethod]
	public void SmallLibraryYieldsSmallBatch() {
		List<GameSnapshot> library = [new(1, "a", 0)];

		IReadOnlyList<uint> batch = Selection.SelectBatch(library, Config(batchSize: 30), [], NoAccrual);

		Assert.AreEqual(1, batch.Count);
	}

	[TestMethod]
	public void AppIdZeroNeverSelected() {
		List<GameSnapshot> library = [new(0, "broken", 0), new(1, "a", 0)];

		IReadOnlyList<uint> batch = Selection.SelectBatch(library, Config(batchSize: 2), [], NoAccrual);

		CollectionAssert.AreEqual(new[] { 1u }, batch.ToArray());
	}

	private static IdleConfig Config(byte batchSize) => IdleConfig.Default with { Enabled = true, BatchSize = batchSize };
}
