using System;
using System.IO;

namespace AdvancedIdle.Tests;

internal static class TestPaths {
	/// <summary>Walks up from the test output directory to the repo root (the directory holding both CLAUDE.md and README.md).</summary>
	internal static string RepoRoot() {
		DirectoryInfo? dir = new(AppContext.BaseDirectory);

		while (dir != null) {
			if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) && File.Exists(Path.Combine(dir.FullName, "README.md"))) {
				return dir.FullName;
			}

			dir = dir.Parent;
		}

		throw new InvalidOperationException("Could not locate the repo root from " + AppContext.BaseDirectory);
	}

	internal static bool BlessRequested(string variable) => Environment.GetEnvironmentVariable(variable) == "1";

	internal static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
