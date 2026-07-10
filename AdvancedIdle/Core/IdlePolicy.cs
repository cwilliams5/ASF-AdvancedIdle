using System;

namespace AdvancedIdle.Core;

public enum IdleDecision : byte {
	Disabled,
	StandDownDisconnected,
	StandDownFarming,
	StandDownPaused,
	StandDownBlocked,
	Idle
}

/// <summary>Everything the decision needs, as plain booleans - the shell snapshots these from ASF.</summary>
public sealed record BotSituation(bool Enabled, bool Connected, bool Farming, bool FarmerPaused, bool PlayingPossible, bool IdleWhileFarmerPaused);

/// <summary>
///     The guard order is load-bearing (CLAUDE.md tattoo 2): farming always wins; a user pause is
///     respected before the playing-elsewhere lock is even consulted, so `!play`/`!pause` never
///     get stomped by a rotation tick.
/// </summary>
public static class IdlePolicy {
	public static IdleDecision Decide(BotSituation situation) {
		ArgumentNullException.ThrowIfNull(situation);

		if (!situation.Enabled) {
			return IdleDecision.Disabled;
		}

		if (!situation.Connected) {
			return IdleDecision.StandDownDisconnected;
		}

		if (situation.Farming) {
			return IdleDecision.StandDownFarming;
		}

		if (situation.FarmerPaused && !situation.IdleWhileFarmerPaused) {
			return IdleDecision.StandDownPaused;
		}

		return !situation.PlayingPossible ? IdleDecision.StandDownBlocked : IdleDecision.Idle;
	}
}
