using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedIdle.Core;

public static class Selection {
	/// <summary>
	///     Picks the batch to idle: lowest effective playtime first (server-reported + live local
	///     credit), deterministic appID tiebreak, minus blacklist and quarantine. With a mostly-zero
	///     library this naturally raises the floor - each rotation's credit lifts the batch above
	///     the tie so the next lowest set rotates in (docs/architecture.md).
	/// </summary>
	public static IReadOnlyList<uint> SelectBatch(IReadOnlyList<GameSnapshot> library, IdleConfig config, IReadOnlyCollection<uint> quarantined, IReadOnlyDictionary<uint, AccrualEntry> accrual) {
		ArgumentNullException.ThrowIfNull(library);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(quarantined);
		ArgumentNullException.ThrowIfNull(accrual);

		return library
			.Where(game => (game.AppId != 0) && !config.Blacklist.Contains(game.AppId) && !quarantined.Contains(game.AppId))
			.OrderBy(game => Accrual.EffectiveMinutes(accrual, game.AppId, game.PlaytimeForeverMinutes))
			.ThenBy(static game => game.AppId)
			.Take(config.EffectiveBatchSize)
			.Select(static game => game.AppId)
			.ToArray();
	}
}
