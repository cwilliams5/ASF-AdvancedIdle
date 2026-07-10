using System.Collections.Generic;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

[TestClass]
public sealed class AccrualTests {
	[TestMethod]
	public void CreditCreatesAndAccumulates() {
		IReadOnlyDictionary<uint, AccrualEntry> ledger = new Dictionary<uint, AccrualEntry>();

		ledger = Accrual.Credit(ledger, [1, 2], 60, static _ => 10);
		ledger = Accrual.Credit(ledger, [1], 30, static _ => 999);

		Assert.AreEqual(new AccrualEntry(10, 90), ledger[1]);
		Assert.AreEqual(new AccrualEntry(10, 60), ledger[2]);
	}

	[TestMethod]
	public void ReconcileDropsFullyAbsorbedCredit() {
		IReadOnlyDictionary<uint, AccrualEntry> ledger = new Dictionary<uint, AccrualEntry> { [1] = new(10, 60) };

		ledger = Accrual.Reconcile(ledger, new Dictionary<uint, uint> { [1] = 70 });

		Assert.IsFalse(ledger.ContainsKey(1));
	}

	[TestMethod]
	public void ReconcileShrinksOnPartialCatchUp() {
		IReadOnlyDictionary<uint, AccrualEntry> ledger = new Dictionary<uint, AccrualEntry> { [1] = new(10, 60) };

		ledger = Accrual.Reconcile(ledger, new Dictionary<uint, uint> { [1] = 40 });

		Assert.AreEqual(new AccrualEntry(40, 30), ledger[1]);
	}

	[TestMethod]
	public void ReconcileKeepsEntryWhenServerIsStale() {
		IReadOnlyDictionary<uint, AccrualEntry> ledger = new Dictionary<uint, AccrualEntry> { [1] = new(10, 60) };

		ledger = Accrual.Reconcile(ledger, new Dictionary<uint, uint> { [1] = 10 });

		Assert.AreEqual(new AccrualEntry(10, 60), ledger[1]);
	}

	[TestMethod]
	public void EffectiveMinutesIsMaxOfServerAndBelief() {
		IReadOnlyDictionary<uint, AccrualEntry> ledger = new Dictionary<uint, AccrualEntry> { [1] = new(10, 60) };

		Assert.AreEqual(70u, Accrual.EffectiveMinutes(ledger, 1, 10));
		Assert.AreEqual(200u, Accrual.EffectiveMinutes(ledger, 1, 200));
		Assert.AreEqual(5u, Accrual.EffectiveMinutes(ledger, 2, 5));
	}
}
