namespace AdvancedIdle.Core;

/// <summary>One owned game as reported by Steam. Plain data; the shell fills it from the unified GetOwnedGames response.</summary>
public sealed record GameSnapshot(uint AppId, string Name, uint PlaytimeForeverMinutes);
