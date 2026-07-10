using System;
using System.IO;
using System.Linq;
using AdvancedIdle.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedIdle.Tests;

[TestClass]
public sealed class DocsFreshnessTests {
	[TestMethod]
	public void ReadmeConfigSectionMatchesRegistry() {
		string readmePath = Path.Combine(TestPaths.RepoRoot(), "README.md");
		string[] lines = File.ReadAllLines(readmePath);

		int begin = Array.FindIndex(lines, static line => line.StartsWith("<!-- CONFIG:BEGIN", StringComparison.Ordinal));
		int end = Array.FindIndex(lines, static line => line.StartsWith("<!-- CONFIG:END", StringComparison.Ordinal));

		Assert.IsTrue((begin >= 0) && (end > begin), "README.md is missing the CONFIG:BEGIN/END markers");

		string expected = TestPaths.NormalizeNewlines(SettingsRegistry.GenerateReadmeSection());

		if (TestPaths.BlessRequested("ADVANCEDIDLE_BLESS_DOCS")) {
			string[] updated = lines.Take(begin).Concat(expected.Split('\n')).Concat(lines.Skip(end + 1)).ToArray();
			File.WriteAllLines(readmePath, updated);

			return;
		}

		string actual = TestPaths.NormalizeNewlines(string.Join('\n', lines[begin..(end + 1)]));

		Assert.AreEqual(expected, actual, "README config section drifted from the Core/Config.cs registry. If the registry change is intended, regenerate via tools/bless.ps1 -Docs; never hand-edit the generated section.");
	}
}
