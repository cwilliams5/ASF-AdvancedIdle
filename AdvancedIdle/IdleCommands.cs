using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;

namespace AdvancedIdle;

internal static class IdleCommands {
	/// <summary>Returns null to decline (not our command / insufficient access - ASF convention: pretend it doesn't exist).</summary>
	internal static async Task<string?> Handle(Bot bot, ConcurrentDictionary<string, BotIdler> idlers, EAccess access, string[] args) {
		if ((args.Length == 0) || !args[0].Equals("AIDLE", StringComparison.OrdinalIgnoreCase)) {
			return null;
		}

		idlers.TryGetValue(bot.BotName, out BotIdler? idler);
		string subcommand = args.Length > 1 ? args[1].ToUpperInvariant() : "STATUS";

		switch (subcommand) {
			case "STATUS" when access >= EAccess.FamilySharing:
				return idler == null ? "AdvancedIdle: not enabled for this bot" : $"AdvancedIdle: {idler.DescribeStatus()}";
			case "ROTATE" when access >= EAccess.Master:
				if (idler == null) {
					return "AdvancedIdle: not enabled for this bot";
				}

				await idler.ForceRotation().ConfigureAwait(false);

				return "AdvancedIdle: rotated";
			case "QUARANTINE" when access >= EAccess.Master:
				if (idler == null) {
					return "AdvancedIdle: not enabled for this bot";
				}

				IReadOnlyCollection<uint> benched = idler.QuarantinedAppIds;

				return benched.Count == 0 ? "AdvancedIdle: quarantine is empty" : $"AdvancedIdle: quarantined ({benched.Count}): {string.Join(",", benched)}";
			case "UNQUARANTINE" when access >= EAccess.Master:
				if (idler == null) {
					return "AdvancedIdle: not enabled for this bot";
				}

				if (args.Length < 3) {
					return "AdvancedIdle: usage: aidle unquarantine <appID[,appID...]|all>";
				}

				HashSet<uint> targets = [];

				if (args[2].Equals("ALL", StringComparison.OrdinalIgnoreCase)) {
					targets.UnionWith(idler.QuarantinedAppIds);
				} else {
					foreach (string token in args.Skip(2).SelectMany(static argument => argument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))) {
						if (!uint.TryParse(token, out uint appID) || (appID == 0)) {
							return $"AdvancedIdle: not an appID: {token}";
						}

						targets.Add(appID);
					}
				}

				int removed = await idler.Unquarantine(targets).ConfigureAwait(false);

				return $"AdvancedIdle: unquarantined {removed} appID(s)";
			default:
				return access >= EAccess.FamilySharing ? "AdvancedIdle: subcommands: status | rotate | quarantine | unquarantine <appIDs|all>" : null;
		}
	}
}
