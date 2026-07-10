using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

#pragma warning disable CA1812 // instantiated by MSTest via [assembly: DiscoverInternals]
[TestClass]
internal sealed class IdlePolicyTests {
	[TestMethod]
	public void GuardOrderIsLoadBearing() {
		// Disabled beats everything.
		Assert.AreEqual(IdleDecision.Disabled, IdlePolicy.Decide(new BotSituation(false, true, false, false, true, false)));

		// Disconnected beats farming.
		Assert.AreEqual(IdleDecision.StandDownDisconnected, IdlePolicy.Decide(new BotSituation(true, false, true, false, true, false)));

		// Farming beats paused and blocked.
		Assert.AreEqual(IdleDecision.StandDownFarming, IdlePolicy.Decide(new BotSituation(true, true, true, true, false, false)));

		// A user pause is respected even when playing would be possible.
		Assert.AreEqual(IdleDecision.StandDownPaused, IdlePolicy.Decide(new BotSituation(true, true, false, true, true, false)));

		// ...unless configured to idle through it, in which case the block still applies...
		Assert.AreEqual(IdleDecision.StandDownBlocked, IdlePolicy.Decide(new BotSituation(true, true, false, true, false, true)));

		// ...and with playing possible it idles.
		Assert.AreEqual(IdleDecision.Idle, IdlePolicy.Decide(new BotSituation(true, true, false, true, true, true)));

		// Playing elsewhere blocks.
		Assert.AreEqual(IdleDecision.StandDownBlocked, IdlePolicy.Decide(new BotSituation(true, true, false, false, false, false)));

		// The happy path.
		Assert.AreEqual(IdleDecision.Idle, IdlePolicy.Decide(new BotSituation(true, true, false, false, true, false)));
	}
}
