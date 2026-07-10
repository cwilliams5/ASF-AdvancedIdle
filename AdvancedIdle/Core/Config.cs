using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AdvancedIdle.Core;

/// <summary>
///     Parsed per-bot configuration. Immutable; produced only by <see cref="SettingsRegistry.Parse" />.
/// </summary>
public sealed record IdleConfig {
	/// <summary>Steam network cap on concurrently played appIDs. Pinned against upstream by pregate assertion A3.</summary>
	public const byte MaxGamesPlayedConcurrently = 32;

	public static IdleConfig Default { get; } = new();

	public bool Enabled { get; init; }
	public byte BatchSize { get; init; } = 30;
	public uint RotationMinutes { get; init; } = 120;
	public IReadOnlyCollection<uint> Blacklist { get; init; } = [];
	public string? CustomGameName { get; init; }
	public bool IdleWhileFarmerPaused { get; init; }
	public bool QuarantineEnabled { get; init; } = true;

	/// <summary>
	///     Opt-in periodic re-send of the UNCHANGED batch as a self-healing net. Off by default:
	///     every witnessed session-clobber arrives with an event we already hook, rotation bounds
	///     any hypothetical silent one, and with a custom name each send is friend-visible (the
	///     clean-session dance) - speculative armor that proved socially expensive (2026-07-10).
	/// </summary>
	public bool HeartbeatReassert { get; init; }

	/// <summary>A custom display name occupies one of the 32 slots (upstream-notes: PlayGames cap eviction), so it caps the batch at 31.</summary>
	public byte EffectiveBatchSize => (byte) Math.Min(BatchSize, string.IsNullOrEmpty(CustomGameName) ? MaxGamesPlayedConcurrently : MaxGamesPlayedConcurrently - 1);
}

/// <summary>
///     One registry row: the single source of truth for a config key. The parser, the defaults,
///     and the generated README configuration section all derive from these rows (CLAUDE.md).
/// </summary>
public sealed record SettingSpec(string Key, string TypeText, string DefaultText, string Description, Func<JsonElement, IdleConfig, (IdleConfig? Config, string? Error)> Apply);

public static class SettingsRegistry {
	public const string KeyPrefix = "AdvancedIdle";

	public static IReadOnlyList<SettingSpec> Rows { get; } = [
		new(
			$"{KeyPrefix}Enabled", "bool", "false", "Master switch for this bot.",
			static (value, config) => TryBool(value, out bool parsed) ? (config with { Enabled = parsed }, null) : Fail("expected a bool")
		),
		new(
			$"{KeyPrefix}BatchSize", "byte", "30", $"Games idled at once (1-{IdleConfig.MaxGamesPlayedConcurrently}; a custom name occupies one slot).",
			static (value, config) => {
				if (!TryUInt(value, out uint parsed)) {
					return Fail("expected a number");
				}

				return parsed is >= 1 and <= IdleConfig.MaxGamesPlayedConcurrently ? (config with { BatchSize = (byte) parsed }, null) : Fail($"must be 1-{IdleConfig.MaxGamesPlayedConcurrently}");
			}
		),
		new(
			$"{KeyPrefix}RotationMinutes", "uint", "120", "How often the batch is re-picked.",
			static (value, config) => {
				if (!TryUInt(value, out uint parsed)) {
					return Fail("expected a number");
				}

				return parsed >= 5 ? (config with { RotationMinutes = parsed }, null) : Fail("must be >= 5");
			}
		),
		new(
			$"{KeyPrefix}Blacklist", "uint[]", "[]", "AppIDs never idled (cosmetic or problematic titles).",
			static (value, config) => {
				if (value.ValueKind != JsonValueKind.Array) {
					return Fail("expected an array of appIDs");
				}

				HashSet<uint> appIDs = [];

				foreach (JsonElement entry in value.EnumerateArray()) {
					if (!TryUInt(entry, out uint appID) || (appID == 0)) {
						return Fail("entries must be appIDs > 0");
					}

					appIDs.Add(appID);
				}

				return (config with { Blacklist = appIDs }, null);
			}
		),
		new(
			$"{KeyPrefix}CustomGameName", "string", "null", "Optional \"non-Steam game\" label shown alongside the batch.",
			static (value, config) => {
				if (value.ValueKind == JsonValueKind.Null) {
					return (config with { CustomGameName = null }, null);
				}

				if (value.ValueKind != JsonValueKind.String) {
					return Fail("expected a string");
				}

				string? parsed = value.GetString();

				return (config with { CustomGameName = string.IsNullOrWhiteSpace(parsed) ? null : parsed }, null);
			}
		),
		new(
			$"{KeyPrefix}IdleWhileFarmerPaused", "bool", "false", "Also idle while the farmer is paused by user command.",
			static (value, config) => TryBool(value, out bool parsed) ? (config with { IdleWhileFarmerPaused = parsed }, null) : Fail("expected a bool")
		),
		new(
			$"{KeyPrefix}QuarantineEnabled", "bool", "true", "Auto-bench titles that provably never accrue playtime.",
			static (value, config) => TryBool(value, out bool parsed) ? (config with { QuarantineEnabled = parsed }, null) : Fail("expected a bool")
		),
		new(
			$"{KeyPrefix}HeartbeatReassert", "bool", "false", "Also re-send the unchanged batch every 5 minutes as a self-heal net (friend-visible when a custom name is set); off = transitions and rotations only.",
			static (value, config) => TryBool(value, out bool parsed) ? (config with { HeartbeatReassert = parsed }, null) : Fail("expected a bool")
		)
	];

	/// <summary>
	///     Parses the bot config's extension properties. Unknown keys carrying our prefix are ERRORS
	///     (they are always typos - a silently-ignored setting is the failure mode this guards).
	///     Keys without the prefix belong to ASF or other plugins and are ignored.
	/// </summary>
	public static (IdleConfig Config, IReadOnlyList<string> Errors) Parse(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
		IdleConfig config = IdleConfig.Default;

		if (additionalConfigProperties == null) {
			return (config, []);
		}

		List<string> errors = [];

		foreach ((string key, JsonElement value) in additionalConfigProperties) {
			if (!key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			SettingSpec? spec = Rows.FirstOrDefault(row => row.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

			if (spec == null) {
				errors.Add($"{key}: unknown {KeyPrefix} property (typo?); known: {string.Join(", ", Rows.Select(static row => row.Key))}");

				continue;
			}

			(IdleConfig? parsed, string? error) = spec.Apply(value, config);

			if ((parsed == null) || !string.IsNullOrEmpty(error)) {
				errors.Add($"{spec.Key}: {error ?? "invalid value"}");

				continue;
			}

			config = parsed;
		}

		return (config, errors);
	}

	/// <summary>Generates the README configuration section, markers included. DocsFreshnessTests gates drift.</summary>
	public static string GenerateReadmeSection() {
		StringBuilder result = new();
		result.AppendLine("<!-- CONFIG:BEGIN (generated from Core/Config.cs registry; regenerate via tools/bless.ps1 -Docs) -->");
		result.AppendLine("| Property | Type | Default | Meaning |");
		result.AppendLine("|---|---|---|---|");

		foreach (SettingSpec row in Rows) {
			result.AppendLine(CultureInfo.InvariantCulture, $"| `{row.Key}` | {row.TypeText} | `{row.DefaultText}` | {row.Description} |");
		}

		result.Append("<!-- CONFIG:END -->");

		return result.ToString();
	}

	private static (IdleConfig?, string?) Fail(string error) => (null, error);

	private static bool TryBool(JsonElement value, out bool result) {
		switch (value.ValueKind) {
			case JsonValueKind.True:
				result = true;

				return true;
			case JsonValueKind.False:
				result = false;

				return true;
			default:
				result = false;

				return false;
		}
	}

	private static bool TryUInt(JsonElement value, out uint result) {
		result = 0;

		return (value.ValueKind == JsonValueKind.Number) && value.TryGetUInt32(out result);
	}
}
