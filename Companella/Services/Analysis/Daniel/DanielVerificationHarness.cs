// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Companella.Models.Beatmap;
using Companella.Services.Beatmap;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Compares C# Daniel output against golden reference JSON from tools/daniel_reference_dump.py.
/// Drop beatmaps under tools/DanielTestMaps (any subfolder); both scripts scan that tree recursively.
/// </summary>
public static class DanielVerificationHarness
{
	private const double _srTolerance = 1e-9;

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public sealed class ReferenceEntry
	{
		[JsonPropertyName("path")]
		public string Path { get; set; } = string.Empty;

		[JsonPropertyName("mod")]
		public string Mod { get; set; } = "NM";

		[JsonPropertyName("sr")]
		public double Sr { get; set; }

		[JsonPropertyName("dan_label")]
		public string DanLabel { get; set; } = string.Empty;

		[JsonPropertyName("dan_numeric")]
		public string DanNumeric { get; set; } = string.Empty;

		[JsonPropertyName("below_alpha")]
		public bool BelowAlpha { get; set; }

		[JsonPropertyName("alpha_lower_bound")]
		public double AlphaLowerBound { get; set; }
	}

	public sealed class VerificationResult
	{
		public required string MapPath { get; init; }
		public bool Passed { get; init; }
		public string? Message { get; init; }
		public double ExpectedSr { get; init; }
		public double ActualSr { get; init; }
	}

	/// <summary>
	/// Verifies C# Daniel output against a reference JSON file.
	/// When <paramref name="mapsDirectory"/> contains .osu files, only those maps are checked
	/// (discovered recursively). Maps in the folder without a reference entry fail.
	/// When the folder is empty or missing, every reference entry is verified.
	/// </summary>
	public static IReadOnlyList<VerificationResult> VerifyFromReferenceFile(
		string referenceJsonPath,
		string? mapsDirectory = null)
	{
		var json = File.ReadAllText(referenceJsonPath);
		var entries = JsonSerializer.Deserialize<List<ReferenceEntry>>(json, _jsonOptions) ?? [];
		var referenceByPath = entries.ToDictionary(
			e => NormalizePath(e.Path),
			e => e,
			StringComparer.OrdinalIgnoreCase);

		var discoveredMaps = CollectOsuFiles(mapsDirectory).ToList();
		if (discoveredMaps.Count == 0)
			return entries.Select(VerifyEntry).ToList();

		var results = new List<VerificationResult>();
		foreach (var mapPath in discoveredMaps)
		{
			if (referenceByPath.TryGetValue(mapPath, out var entry))
				results.Add(VerifyEntry(entry));
			else
			{
				results.Add(new VerificationResult
				{
					MapPath = mapPath,
					Passed = false,
					Message = "No reference entry. Run tools/daniel_reference_dump.py or tools/daniel_verify.ps1 first."
				});
			}
		}

		return results;
	}

	private static string NormalizePath(string path) =>
		Path.GetFullPath(path);

	private static IEnumerable<string> CollectOsuFiles(string? mapsDirectory)
	{
		if (string.IsNullOrWhiteSpace(mapsDirectory) || !Directory.Exists(mapsDirectory))
			yield break;

		foreach (var path in Directory.EnumerateFiles(mapsDirectory, "*.osu", SearchOption.AllDirectories))
			yield return NormalizePath(path);
	}

	/// <summary>
	/// Verifies a single beatmap against a reference entry.
	/// </summary>
	public static VerificationResult VerifyEntry(ReferenceEntry entry)
	{
		if (!File.Exists(entry.Path))
		{
			return new VerificationResult
			{
				MapPath = entry.Path,
				Passed = false,
				Message = "Map file not found.",
				ExpectedSr = entry.Sr
			};
		}

		var osuFile = OsuFileParser.Parse(entry.Path);
		var rate = entry.Mod switch
		{
			"DT" => 1.5f,
			"HT" => 0.75f,
			_ => 1.0f
		};

		var result = DanielDifficultyService.Calculate(osuFile, rate);
		if (!result.IsValid)
		{
			return new VerificationResult
			{
				MapPath = entry.Path,
				Passed = false,
				Message = result.ErrorMessage ?? "Invalid result",
				ExpectedSr = entry.Sr,
				ActualSr = result.BaseStarRating
			};
		}

		var srDiff = Math.Abs(result.BaseStarRating - entry.Sr);
		if (srDiff > _srTolerance)
		{
			return new VerificationResult
			{
				MapPath = entry.Path,
				Passed = false,
				Message = $"SR mismatch: expected {entry.Sr}, got {result.BaseStarRating} (diff {srDiff})",
				ExpectedSr = entry.Sr,
				ActualSr = result.BaseStarRating
			};
		}

		var (expectedDanLabel, expectedDanNumeric) = DanielDanMapper.GetDanFromDiff(result.BaseStarRating);
		if (!string.Equals(expectedDanLabel, entry.DanLabel, StringComparison.Ordinal))
		{
			return new VerificationResult
			{
				MapPath = entry.Path,
				Passed = false,
				Message = $"Dan label mismatch: expected '{entry.DanLabel}', got '{expectedDanLabel}'",
				ExpectedSr = entry.Sr,
				ActualSr = result.BaseStarRating
			};
		}

		if (!DanNumericMatches(expectedDanNumeric, entry.DanNumeric))
		{
			return new VerificationResult
			{
				MapPath = entry.Path,
				Passed = false,
				Message = $"Dan numeric mismatch: expected '{entry.DanNumeric}', got '{expectedDanNumeric}'",
				ExpectedSr = entry.Sr,
				ActualSr = result.BaseStarRating
			};
		}

		var belowAlpha = DanielDanMapper.IsBelowAlphaThreshold(result.BaseStarRating);
		if (belowAlpha != entry.BelowAlpha)
		{
			return new VerificationResult
			{
				MapPath = entry.Path,
				Passed = false,
				Message = $"Below-alpha mismatch: expected {entry.BelowAlpha}, got {belowAlpha}",
				ExpectedSr = entry.Sr,
				ActualSr = result.BaseStarRating
			};
		}

		return new VerificationResult
		{
			MapPath = entry.Path,
			Passed = true,
			ExpectedSr = entry.Sr,
			ActualSr = result.BaseStarRating
		};
	}

	/// <summary>
	/// Formats verification results for console output.
	/// </summary>
	public static string FormatReport(IReadOnlyList<VerificationResult> results)
	{
		var lines = new List<string>();
		foreach (var result in results)
		{
			var status = result.Passed ? "PASS" : "FAIL";
			lines.Add($"[{status}] {result.MapPath}");
			if (!result.Passed && result.Message != null)
				lines.Add($"       {result.Message}");
		}

		var passed = results.Count(r => r.Passed);
		lines.Add($"Summary: {passed}/{results.Count} passed");
		return string.Join(Environment.NewLine, lines);
	}

	private static bool DanNumericMatches(string expected, string actual)
	{
		if (string.Equals(expected, actual, StringComparison.Ordinal))
			return true;

		if (expected == "N/A" || actual == "N/A")
			return false;

		return double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedValue)
			&& double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualValue)
			&& Math.Abs(expectedValue - actualValue) < 1e-9;
	}
}
