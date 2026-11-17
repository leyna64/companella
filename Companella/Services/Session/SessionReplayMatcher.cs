using Companella.Services.Integrations.ManiaTracker;

namespace Companella.Services.Session;

/// <summary>Associates original replay files with local scores; never selects a replay by recency alone.</summary>
internal static class SessionReplayMatcher
{
	internal static bool Matches(string beatmapHash, DateTime finishedAt, ManiaTrackerReplay replay) =>
		replay.BeatmapHash.Equals(beatmapHash, StringComparison.OrdinalIgnoreCase) &&
		Math.Abs((replay.Timestamp.UtcDateTime - finishedAt.ToUniversalTime()).TotalSeconds) <= 5;

	internal static string? Find(string directory, string beatmapHash, DateTime finishedAt)
	{
		var matches = new Dictionary<string, string>();
		foreach (var folder in new[] { Path.Combine(directory, "Data", "r"), Path.Combine(directory, "Replays") })
		{
			if (!Directory.Exists(folder)) continue;
			foreach (var path in Directory.EnumerateFiles(folder, "*.osr"))
			{
				try
				{
					var info = new FileInfo(path);
					if (info.LastWriteTimeUtc < finishedAt.ToUniversalTime().AddMinutes(-1) || info.Length is <= 0 or > 26214400) continue;
					var bytes = File.ReadAllBytes(path);
					var replay = ManiaTrackerReplay.Read(bytes);
					if (!Matches(beatmapHash, finishedAt, replay)) continue;
					var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
					matches.TryAdd(digest, path);
					if (matches.Count > 1) return null;
				}
				catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { }
			}
		}
		return matches.Count == 1 ? matches.Values.Single() : null;
	}
}
