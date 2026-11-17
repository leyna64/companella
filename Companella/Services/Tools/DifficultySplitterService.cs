using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Services.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Tools;

/// <summary>
/// Result of splitting a beatmap into region-based difficulty copies.
/// </summary>
public class DifficultySplitterResult
{
	public bool Success { get; init; }

	public string? ErrorMessage { get; init; }

	public List<DifficultySplitterPairResult> PairResults { get; init; } = new();
}

public class DifficultySplitterPairResult
{
	public Guid PairId { get; init; }

	public string VersionName { get; init; } = string.Empty;

	public bool Success { get; init; }

	public string? ErrorMessage { get; init; }

	public string? OutputPath { get; init; }

	public int NoteCount { get; init; }
}

/// <summary>
/// Splits a beatmap difficulty into region-scoped copies based on named marker pairs.
/// Each region is two markers sharing the same name (start/end by time).
/// </summary>
public class DifficultySplitterService
{
	/// <summary>
	/// Builds exportable regions from markers grouped by name. Only names with exactly two markers qualify.
	/// </summary>
	public static List<DifficultyRegionPair> BuildRegionsFromMarkers(
		IReadOnlyList<DifficultyRegionMarker> markers,
		IReadOnlyList<DifficultyRegionPair>? existingPairs = null)
	{
		existingPairs ??= Array.Empty<DifficultyRegionPair>();
		var regions = new List<DifficultyRegionPair>();

		foreach (var group in markers.GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase))
		{
			var name = group.Key;
			if (string.IsNullOrWhiteSpace(name))
				continue;

			var ordered = group.OrderBy(m => m.TimeMs).ToList();
			if (ordered.Count != 2)
				continue;

			var existing = existingPairs.FirstOrDefault(p =>
				string.Equals(p.VersionName, name, StringComparison.OrdinalIgnoreCase) ||
				(p.StartMarkerId == ordered[0].Id && p.EndMarkerId == ordered[1].Id) ||
				(p.StartMarkerId == ordered[1].Id && p.EndMarkerId == ordered[0].Id));

			regions.Add(new DifficultyRegionPair
			{
				Id = existing?.Id ?? Guid.NewGuid(),
				StartMarkerId = ordered[0].Id,
				EndMarkerId = ordered[1].Id,
				VersionName = name
			});
		}

		return regions;
	}
	/// <summary>
	/// Filters hit objects to a time region, clamping hold tails to the region end.
	/// </summary>
	public static List<HitObject> FilterRegionNotes(
		IReadOnlyList<HitObject> allNotes,
		double startMs,
		double endMs)
	{
		var kept = new List<HitObject>();

		foreach (var note in allNotes)
		{
			if (note.Time < startMs || note.Time > endMs)
				continue;

			var copy = note.Clone();

			if (copy.IsHold && copy.EndTime > endMs)
				copy.EndTime = endMs;

			if (copy.IsHold && copy.EndTime <= copy.Time)
			{
				copy.Type = HitObjectType.Circle;
				copy.EndTime = copy.Time;
			}

			kept.Add(copy);
		}

		return kept;
	}

	/// <summary>
	/// Snaps a timestamp to the nearest note head or hold tail within the given threshold.
	/// </summary>
	public static double SnapToNearestNote(
		double timeMs,
		IReadOnlyList<HitObject> notes,
		double thresholdMs = 250)
	{
		if (notes.Count == 0)
			return timeMs;

		var bestTime = timeMs;
		var bestDistance = double.MaxValue;

		foreach (var note in notes)
		{
			foreach (var candidate in note.IsHold
					 ? new[] { note.Time, note.EndTime }
					 : new[] { note.Time })
			{
				var distance = Math.Abs(candidate - timeMs);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestTime = candidate;
				}
			}
		}

		return bestDistance <= thresholdMs ? bestTime : timeMs;
	}

	public static async Task<DifficultySplitterResult> SplitAsync(
		OsuFile source,
		IReadOnlyList<DifficultyRegionMarker> markers,
		IReadOnlyList<DifficultyRegionPair> pairs,
		Action<string>? progressCallback = null)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (string.IsNullOrWhiteSpace(source.FilePath) || !File.Exists(source.FilePath))
		{
			return new DifficultySplitterResult
			{
				Success = false,
				ErrorMessage = "Source beatmap file not found."
			};
		}

		if (pairs.Count == 0)
		{
			return new DifficultySplitterResult
			{
				Success = false,
				ErrorMessage = "No region pairs to export."
			};
		}

		var markerLookup = markers.ToDictionary(m => m.Id);
		var allNotes = HitObjectSerializer.Parse(source);
		var keyCount = (int)source.CircleSize;
		var pairResults = new List<DifficultySplitterPairResult>();
		var usedVersionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var pair in pairs)
		{
			var validation = ValidatePair(pair, markerLookup, usedVersionNames);
			if (validation != null)
			{
				pairResults.Add(new DifficultySplitterPairResult
				{
					PairId = pair.Id,
					VersionName = pair.VersionName,
					Success = false,
					ErrorMessage = validation
				});
				continue;
			}

			usedVersionNames.Add(pair.VersionName.Trim());

			var startMs = markerLookup[pair.StartMarkerId].TimeMs;
			var endMs = markerLookup[pair.EndMarkerId].TimeMs;
			var filtered = FilterRegionNotes(allNotes, startMs, endMs);

			if (filtered.Count == 0)
			{
				pairResults.Add(new DifficultySplitterPairResult
				{
					PairId = pair.Id,
					VersionName = pair.VersionName,
					Success = false,
					ErrorMessage = "Region contains no notes.",
					NoteCount = 0
				});
				continue;
			}

			try
			{
				progressCallback?.Invoke($"Writing {pair.VersionName}...");
				var outputPath = GetOutputPath(source, pair.VersionName);
				await WriteSplitBeatmapAsync(source, filtered, keyCount, pair.VersionName, outputPath);

				pairResults.Add(new DifficultySplitterPairResult
				{
					PairId = pair.Id,
					VersionName = pair.VersionName,
					Success = true,
					OutputPath = outputPath,
					NoteCount = filtered.Count
				});

				Logger.Info($"[DifficultySplitter] Wrote {filtered.Count} notes to {Path.GetFileName(outputPath)}");
			}
			catch (Exception ex)
			{
				pairResults.Add(new DifficultySplitterPairResult
				{
					PairId = pair.Id,
					VersionName = pair.VersionName,
					Success = false,
					ErrorMessage = ex.Message
				});
			}
		}

		return new DifficultySplitterResult
		{
			Success = pairResults.Any(r => r.Success),
			PairResults = pairResults,
			ErrorMessage = pairResults.All(r => !r.Success)
				? "No regions were exported successfully."
				: null
		};
	}

	public static string? ValidatePair(
		DifficultyRegionPair pair,
		IReadOnlyDictionary<Guid, DifficultyRegionMarker> markerLookup,
		IReadOnlySet<string>? usedVersionNames = null)
	{
		if (string.IsNullOrWhiteSpace(pair.VersionName))
			return "Version name is required.";

		if (!markerLookup.TryGetValue(pair.StartMarkerId, out var startMarker))
			return "Start marker not found.";

		if (!markerLookup.TryGetValue(pair.EndMarkerId, out var endMarker))
			return "End marker not found.";

		if (Math.Abs(startMarker.TimeMs - endMarker.TimeMs) < 0.001)
			return "Region markers must be at different times.";

		if (usedVersionNames != null && usedVersionNames.Contains(pair.VersionName.Trim()))
			return "Duplicate version name.";

		return null;
	}

	public static (double StartMs, double EndMs)? ResolvePairBounds(
		DifficultyRegionPair pair,
		IReadOnlyDictionary<Guid, DifficultyRegionMarker> markerLookup)
	{
		if (!markerLookup.TryGetValue(pair.StartMarkerId, out var start))
			return null;

		if (!markerLookup.TryGetValue(pair.EndMarkerId, out var end))
			return null;

		var startMs = Math.Min(start.TimeMs, end.TimeMs);
		var endMs = Math.Max(start.TimeMs, end.TimeMs);
		if (startMs >= endMs)
			return null;

		return (startMs, endMs);
	}

	private static string GetOutputPath(OsuFile source, string versionName)
	{
		var osuBaseName = Path.GetFileNameWithoutExtension(source.FilePath);
		var candidate = $"{osuBaseName} ({versionName.Trim()})";
		var invalidChars = Path.GetInvalidFileNameChars();
		var sanitized = string.Join("_", candidate.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
		var directory = source.DirectoryPath;
		var path = Path.Combine(directory, sanitized + ".osu");

		if (!File.Exists(path))
			return path;

		for (var i = 2; i < 100; i++)
		{
			var numbered = Path.Combine(directory, $"{sanitized} ({i}).osu");
			if (!File.Exists(numbered))
				return numbered;
		}

		return path;
	}

	private static async Task WriteSplitBeatmapAsync(
		OsuFile originalFile,
		List<HitObject> modifiedHitObjects,
		int keyCount,
		string versionName,
		string outputPath)
	{
		var lines = await File.ReadAllLinesAsync(originalFile.FilePath);
		var result = new List<string>();
		var inHitObjectsSection = false;
		var hitObjectsWritten = false;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				if (inHitObjectsSection && !hitObjectsWritten)
				{
					ModService.WriteHitObjects(result, modifiedHitObjects, keyCount);
					hitObjectsWritten = true;
				}

				inHitObjectsSection = trimmed == "[HitObjects]";
				result.Add(line);
				continue;
			}

			if (inHitObjectsSection)
			{
				if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//", StringComparison.Ordinal))
					continue;
			}

			if (trimmed.StartsWith("Version:", StringComparison.Ordinal))
			{
				result.Add($"Version:{versionName.Trim()}");
				continue;
			}

			if (trimmed.StartsWith("BeatmapID:", StringComparison.Ordinal))
			{
				result.Add("BeatmapID:0");
				continue;
			}

			result.Add(line);
		}

		if (inHitObjectsSection && !hitObjectsWritten)
			ModService.WriteHitObjects(result, modifiedHitObjects, keyCount);

		await File.WriteAllLinesAsync(outputPath, result);
	}
}
