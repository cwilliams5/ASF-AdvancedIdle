using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedIdle.Core;
using ArchiSteamFarm.Core;
using SteamKit2;
using SteamKit2.Internal;

namespace AdvancedIdle;

/// <summary>
///     Our own SteamKit2 handler on the bot's existing connection - the sanctioned door
///     (docs/upstream-notes.md "Why not the obvious APIs"): ASF's PlayGames is internal and
///     Actions.Play would permanently pause the farmer. Send-only; callbacks are subscribed via
///     the CallbackManager in the plugin.
/// </summary>
public sealed class AdvancedIdleHandler : ClientMsgHandler {
	/// <summary>ASF waits Bot.CallbackSleep (internal) before the named send; we mirror the intent with our own constant.</summary>
	private const int CustomNameCleanSessionDelayMs = 2000;

	private readonly object ServiceLock = new();

	private Player? UnifiedPlayerService;

	public override void HandleMsg(IPacketMsg packetMsg) { }

	/// <summary>
	///     Replicates ArchiHandler.PlayGames wire behavior (upstream-notes; pregate A4 pins the
	///     upstream body): clean-session custom-name workaround, dedupe, appID>0 filter, cap
	///     eviction of the name slot. Known divergence: upstream stamps client_os_type from the
	///     internal Bot.OSType; the logon session already carries the OS, so we omit it.
	/// </summary>
	internal async Task PlayGames(IReadOnlyCollection<uint> gameIDs, string? gameName = null) {
		ArgumentNullException.ThrowIfNull(gameIDs);

		if (Client == null) {
			throw new InvalidOperationException(nameof(Client));
		}

		if (!Client.IsConnected) {
			return;
		}

		ClientMsgProtobuf<CMsgClientGamesPlayed> request = new(EMsg.ClientGamesPlayedWithDataBlob);

		if (!string.IsNullOrEmpty(gameName)) {
			// Steam displays a custom name reliably only when sent on a clean non-playing session.
			Client.Send(request);

			await Task.Delay(CustomNameCleanSessionDelayMs).ConfigureAwait(false);

			request.Body.games_played.Add(
				new CMsgClientGamesPlayed.GamePlayed {
					game_extra_info = gameName,
					game_id = new GameID {
						AppType = GameID.GameType.Shortcut,
						ModID = uint.MaxValue
					}
				}
			);
		}

		foreach (uint gameID in gameIDs.Distinct().Where(static gameID => gameID > 0)) {
			if (request.Body.games_played.Count >= IdleConfig.MaxGamesPlayedConcurrently) {
				if (string.IsNullOrEmpty(gameName)) {
					// Selection already caps at EffectiveBatchSize; defensive rather than throwing like upstream.
					break;
				}

				// Make room by ditching the custom name slot, exactly like upstream.
				gameName = null;

				request.Body.games_played.RemoveAt(0);
			}

			request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(gameID) });
		}

		Client.Send(request);
	}

	/// <summary>
	///     Same unified Player.GetOwnedGames request ASF issues (ArchiHandler.cs:352-358), but
	///     keeping playtime_forever, which ASF's public wrapper drops (upstream-notes).
	/// </summary>
	internal async Task<IReadOnlyList<GameSnapshot>?> GetOwnedGames(ulong steamID) {
		if ((steamID == 0) || (Client == null)) {
			throw new InvalidOperationException(nameof(steamID));
		}

		if (!Client.IsConnected) {
			return null;
		}

		Player? playerService;

		lock (ServiceLock) {
			playerService = UnifiedPlayerService ??= Client.GetHandler<SteamUnifiedMessages>()?.CreateService<Player>();
		}

		if (playerService == null) {
			return null;
		}

		CPlayer_GetOwnedGames_Request request = new() {
			steamid = steamID,
			include_appinfo = true,
			include_free_sub = true,
			include_played_free_games = true,
			skip_unvetted_apps = false
		};

		SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetOwnedGames_Response> response;

		try {
			response = await playerService.GetOwnedGames(request).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception) {
			return null;
		}

		if (response.Result != EResult.OK) {
			return null;
		}

		return response.Body.games.Select(static game => new GameSnapshot((uint) game.appid, game.name ?? "", (uint) Math.Max(0, game.playtime_forever))).ToList();
	}
}
