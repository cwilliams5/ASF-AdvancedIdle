using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

[TestClass]
public sealed class ConfigTests {
	[TestMethod]
	public void NullPropertiesYieldDefaults() {
		(IdleConfig config, IReadOnlyList<string> errors) = SettingsRegistry.Parse(null);

		Assert.AreEqual(IdleConfig.Default, config);
		Assert.AreEqual(0, errors.Count);
		Assert.IsFalse(config.Enabled);
		Assert.AreEqual((byte) 30, config.BatchSize);
		Assert.AreEqual(120u, config.RotationMinutes);
		Assert.IsTrue(config.QuarantineEnabled);
	}

	[TestMethod]
	public void AllKeysParse() {
		(IdleConfig config, IReadOnlyList<string> errors) = Parse("""{"AdvancedIdleEnabled":true,"AdvancedIdleBatchSize":10,"AdvancedIdleRotationMinutes":30,"AdvancedIdleBlacklist":[440,570],"AdvancedIdleCustomGameName":"resting","AdvancedIdleIdleWhileFarmerPaused":true,"AdvancedIdleQuarantineEnabled":false}""");

		Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
		Assert.IsTrue(config.Enabled);
		Assert.AreEqual((byte) 10, config.BatchSize);
		Assert.AreEqual(30u, config.RotationMinutes);
		CollectionAssert.AreEquivalent(new[] { 440u, 570u }, config.Blacklist.ToArray());
		Assert.AreEqual("resting", config.CustomGameName);
		Assert.IsTrue(config.IdleWhileFarmerPaused);
		Assert.IsFalse(config.QuarantineEnabled);
	}

	[TestMethod]
	public void WrongTypesAreErrors() {
		(_, IReadOnlyList<string> errors) = Parse("""{"AdvancedIdleEnabled":"yes","AdvancedIdleBatchSize":"many","AdvancedIdleBlacklist":123}""");

		Assert.AreEqual(3, errors.Count, string.Join("; ", errors));
	}

	[TestMethod]
	public void BatchSizeRangeEnforced() {
		(_, IReadOnlyList<string> lowErrors) = Parse("""{"AdvancedIdleBatchSize":0}""");
		(_, IReadOnlyList<string> highErrors) = Parse("""{"AdvancedIdleBatchSize":33}""");
		(IdleConfig max, IReadOnlyList<string> maxErrors) = Parse("""{"AdvancedIdleBatchSize":32}""");

		Assert.AreEqual(1, lowErrors.Count);
		Assert.AreEqual(1, highErrors.Count);
		Assert.AreEqual(0, maxErrors.Count);
		Assert.AreEqual((byte) 32, max.BatchSize);
	}

	[TestMethod]
	public void RotationMinutesMinimumEnforced() {
		(_, IReadOnlyList<string> errors) = Parse("""{"AdvancedIdleRotationMinutes":4}""");

		Assert.AreEqual(1, errors.Count);
	}

	[TestMethod]
	public void BlacklistRejectsZeroAppId() {
		(_, IReadOnlyList<string> errors) = Parse("""{"AdvancedIdleBlacklist":[440,0]}""");

		Assert.AreEqual(1, errors.Count);
	}

	[TestMethod]
	public void UnknownPrefixedKeyIsATypoError() {
		// The failure mode this guards: a user typos a key and their setting is silently ignored.
		(_, IReadOnlyList<string> errors) = Parse("""{"AdvancedIdleBatchSze":10}""");

		Assert.AreEqual(1, errors.Count);
		StringAssert.Contains(errors[0], "unknown", StringComparison.Ordinal);
	}

	[TestMethod]
	public void ForeignKeysAreIgnored() {
		(_, IReadOnlyList<string> errors) = Parse("""{"EnableFreePackages":true,"SomethingElse":1}""");

		Assert.AreEqual(0, errors.Count);
	}

	[TestMethod]
	public void CustomGameNameOccupiesASlot() {
		(IdleConfig named, _) = Parse("""{"AdvancedIdleBatchSize":32,"AdvancedIdleCustomGameName":"x"}""");
		(IdleConfig unnamed, _) = Parse("""{"AdvancedIdleBatchSize":32}""");
		(IdleConfig small, _) = Parse("""{"AdvancedIdleBatchSize":10,"AdvancedIdleCustomGameName":"x"}""");

		Assert.AreEqual((byte) 31, named.EffectiveBatchSize);
		Assert.AreEqual((byte) 32, unnamed.EffectiveBatchSize);
		Assert.AreEqual((byte) 10, small.EffectiveBatchSize);
	}

	[TestMethod]
	public void BlankCustomGameNameMeansNone() {
		(IdleConfig config, IReadOnlyList<string> errors) = Parse("""{"AdvancedIdleCustomGameName":"  "}""");

		Assert.AreEqual(0, errors.Count);
		Assert.IsNull(config.CustomGameName);
	}

	private static (IdleConfig Config, IReadOnlyList<string> Errors) Parse(string json) {
		using JsonDocument document = JsonDocument.Parse(json);

		Dictionary<string, JsonElement> props = [];

		foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
			props[property.Name] = property.Value.Clone();
		}

		return SettingsRegistry.Parse(props);
	}
}
