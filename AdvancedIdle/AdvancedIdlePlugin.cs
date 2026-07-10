using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using AdvancedIdle.Core;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using SteamKit2;

namespace AdvancedIdle;

#pragma warning disable CA1812 // ASF instantiates this class at runtime via MEF
[Export(typeof(IPlugin))]
[UsedImplicitly]
internal sealed class AdvancedIdlePlugin : IGitHubPluginUpdates, IBot, IBotModules, IBotSteamClient, IBotCardsFarmerInfo, IBotConnection, IBotCommand2 {
	/// <summary>After a farming/occupation transition, ASF's own (clearing) games-played write goes first; we land after it (CLAUDE.md tattoo 2). ASF itself notes Steam may not respect send order, hence the margin.</summary>
	private const int PostTransitionDelayMs = 3000;

	private const int LogonSettleDelayMs = 8000;

	private static readonly ConcurrentDictionary<string, AdvancedIdleHandler> Handlers = new();
	private static readonly ConcurrentDictionary<string, BotIdler> Idlers = new();

	public string Name => "AdvancedIdle";
	public string RepositoryName => "cwilliams5/ASF-AdvancedIdle";
	public Version Version => typeof(AdvancedIdlePlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task OnLoaded() {
		// The version handshake (SMOKE.md step 1): this line alone identifies a stale DLL.
		// Compile-time constants, NOT attribute reflection: the trimmed win-x64 ASF host lacks
		// the reflection-extension surface (MissingMethodException here, live smoke 2026-07-09).
		Version? runningAsf = typeof(ASF).Assembly.GetName().Version;

		ASF.ArchiLogger.LogGenericInfo($"AdvancedIdle {HandshakeInfo.InformationalVersion} | built against ASF {HandshakeInfo.BuiltAgainstAsf} | running on ASF {runningAsf}");

		return Task.CompletedTask;
	}

	public Task OnBotInit(Bot bot) => Task.CompletedTask;

	public Task OnBotDestroy(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Handlers.TryRemove(bot.BotName, out _);

		if (Idlers.TryRemove(bot.BotName, out BotIdler? idler)) {
			idler.Dispose();
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		AdvancedIdleHandler handler = new();
		Handlers[bot.BotName] = handler;

		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([handler]);
	}

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(callbackManager);

		// Covers the one ASF transition with no plugin event (upstream-notes "the one uncovered
		// transition"): account freed, farmer declines to resume, ResetGamesPlayed runs silently.
		callbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(_ => PumpSoon(bot, PostTransitionDelayMs, "playing-session change", true));

		return Task.CompletedTask;
	}

	public Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		ArgumentNullException.ThrowIfNull(bot);

		// Re-fires on every runtime config edit: dispose + rebuild per-bot state (SMOKE.md step 9).
		if (Idlers.TryRemove(bot.BotName, out BotIdler? existing)) {
			existing.Dispose();
		}

		(IdleConfig config, IReadOnlyList<string> errors) = SettingsRegistry.Parse(additionalConfigProperties);

		if (errors.Count > 0) {
			foreach (string error in errors) {
				bot.ArchiLogger.LogGenericError($"AdvancedIdle config: {error}");
			}

			bot.ArchiLogger.LogGenericError("AdvancedIdle: disabled for this bot until the config errors above are fixed (fail-loud beats silently misbehaving)");

			return Task.CompletedTask;
		}

		if (!config.Enabled) {
			return Task.CompletedTask;
		}

		if (!Handlers.TryGetValue(bot.BotName, out AdvancedIdleHandler? handler)) {
			bot.ArchiLogger.LogGenericError("AdvancedIdle: no Steam handler registered for this bot (init order changed upstream?)");

			return Task.CompletedTask;
		}

		LogAdapter log = new(bot);
		string statePath = Bot.GetFilePath($"{bot.BotName}_AdvancedIdle", Bot.EFileType.Database) ?? throw new InvalidOperationException(nameof(statePath));

		Idlers[bot.BotName] = new BotIdler(config, new BotStateAdapter(bot), new SteamActionsAdapter(bot, handler), new FileStateStore(statePath, log), log, SystemClock.Instance);

		bot.ArchiLogger.LogGenericInfo($"AdvancedIdle enabled: batch={config.EffectiveBatchSize} rotation={config.RotationMinutes}m blacklist={config.Blacklist.Count} quarantine={(config.QuarantineEnabled ? "on" : "off")}");

		return Task.CompletedTask;
	}

	// Transition triggers force the re-assert: ASF's ResetGamesPlayed has just (re)written the
	// session over us (live smoke: their GamesPlayedWhileIdle held the session for 5 minutes
	// until the heartbeat took it back).
	public Task OnBotFarmingStarted(Bot bot) => PumpSoon(bot, 0, "farming started");

	public Task OnBotFarmingStopped(Bot bot) => PumpSoon(bot, PostTransitionDelayMs, "farming stopped", true);

	public Task OnBotFarmingFinished(Bot bot, bool farmedSomething) => PumpSoon(bot, PostTransitionDelayMs, "farming finished", true);

	public Task OnBotLoggedOn(Bot bot) => PumpSoon(bot, LogonSettleDelayMs, "logged on", true);

	public Task OnBotDisconnected(Bot bot, EResult reason) => PumpSoon(bot, 0, "disconnected");

	public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(args);

		return await IdleCommands.Handle(bot, Idlers, access, args).ConfigureAwait(false);
	}

	private static Task PumpSoon(Bot bot, int delayMs, string reason, bool forceReassert = false) {
		ArgumentNullException.ThrowIfNull(bot);

		if (Idlers.TryGetValue(bot.BotName, out BotIdler? idler)) {
			Utilities.InBackground(async () => {
					if (delayMs > 0) {
						await Task.Delay(delayMs).ConfigureAwait(false);
					}

					await idler.Pump(reason, forceReassert).ConfigureAwait(false);
				}
			);
		}

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812

internal sealed class BotStateAdapter(Bot bot) : IBotStateView {
	public bool Connected => bot.IsConnectedAndLoggedOn;
	public bool Farming => bot.CardsFarmer.NowFarming;
	public bool FarmerPaused => bot.CardsFarmer.Paused;
	public bool PlayingPossible => bot.IsPlayingPossible;
}

internal sealed class SteamActionsAdapter(Bot bot, AdvancedIdleHandler handler) : IIdleSteamActions {
	public Task PlayGames(IReadOnlyCollection<uint> appIDs, string? gameName) => handler.PlayGames(appIDs, gameName);

	public Task<IReadOnlyList<GameSnapshot>?> FetchOwnedGames() => handler.GetOwnedGames(bot.SteamID);
}

internal sealed class LogAdapter(Bot bot) : IIdleLog {
	public void Info(string message) => bot.ArchiLogger.LogGenericInfo($"AdvancedIdle: {message}");

	public void Warn(string message) => bot.ArchiLogger.LogGenericWarning($"AdvancedIdle: {message}");
}

internal sealed class SystemClock : IIdleClock {
	internal static readonly SystemClock Instance = new();

	public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
///     Atomic JSON persistence beside the bot's own database file. Rare writes; a corrupt/missing
///     file degrades to fresh state with a warning, never a crash. Serialization goes through
///     ASF's JsonUtilities (its own options/resolver), NOT raw JsonSerializer - the trimmed
///     win-x64 host threw MissingMethodException on the default reflection path (live smoke
///     2026-07-09), while ASF's surface is what SerializableFile itself runs on such hosts.
/// </summary>
internal sealed class FileStateStore(string filePath, IIdleLog log) : IStateStore {
	public PersistedState Load() {
		try {
			if (!File.Exists(filePath)) {
				return new PersistedState();
			}

			return File.ReadAllText(filePath).ToJsonObject<PersistedState>() ?? new PersistedState();
		} catch (Exception e) {
			log.Warn($"state load failed, starting fresh: {e.Message}");

			return new PersistedState();
		}
	}

	public void Save(PersistedState state) {
		string temp = $"{filePath}.new";
		File.WriteAllText(temp, state.ToJsonText());
		File.Move(temp, filePath, true);
	}
}
