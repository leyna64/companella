using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Services.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Tools;

/// <summary>
/// Result of building a map pack.
/// </summary>
public class MapPackBuildResult
{
	public bool Success { get; set; }
	public string? OutputFolder { get; set; }
	public string? FirstOsuPath { get; set; }
	public string? ErrorMessage { get; set; }
	public List<string> WrittenOsuPaths { get; set; } = new();
}

/// <summary>
/// Creates and loads map packs: multi-difficulty beatmapsets sourced from unrelated maps.
/// </summary>
public class MapPackManagerService
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly RateChanger _rateChanger;

	public MapPackManagerService(string ffmpegPath = "ffmpeg")
	{
		_rateChanger = new RateChanger(ffmpegPath);
	}

	public Task<bool> CheckFfmpegAvailableAsync() => _rateChanger.CheckFfmpegAvailableAsync();

	/// <summary>
	/// Builds a map pack into the specified Songs folder.
	/// </summary>
	public async Task<MapPackBuildResult> BuildMapPackAsync(
		MapPackDefinition definition,
		string songsFolder,
		Action<string>? progressCallback = null,
		CancellationToken cancellationToken = default)
	{
		if (definition.Entries.Count == 0)
			return new MapPackBuildResult { ErrorMessage = "No entries in pack." };

		var missingSources = definition.Entries.Where(e => e.SourceMissing || e.OsuFile == null).ToList();
		if (missingSources.Count > 0)
		{
			var names = string.Join(", ", missingSources.Select(e => e.EffectiveVersion));
			return new MapPackBuildResult { ErrorMessage = $"Missing source file(s): {names}" };
		}

		var needsFfmpeg = definition.Entries.Any(e => Math.Abs(e.Rate - 1.0) > 0.01);
		if (needsFfmpeg && !await CheckFfmpegAvailableAsync())
			return new MapPackBuildResult { ErrorMessage = "ffmpeg is required for rate changes." };

		var outputFolder = ResolveOutputFolder(definition, songsFolder);
		Directory.CreateDirectory(outputFolder);

		var result = new MapPackBuildResult { OutputFolder = outputFolder };
		var copiedAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var isInPlaceRebuild = IsInPlaceRebuild(definition, outputFolder);
		var tempRateFiles = new List<string>();

		try
		{
			for (var i = 0; i < definition.Entries.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var entry = definition.Entries[i];
				progressCallback?.Invoke(
					$"Building [{entry.EffectiveVersion}] ({i + 1}/{definition.Entries.Count})...");

				var workingOsu = entry.OsuFile!;
				string workingPath = workingOsu.FilePath;
				var applyHpOdOnMetadataPass = true;

				if (Math.Abs(entry.Rate - 1.0) > 0.01)
				{
					progressCallback?.Invoke($"  Creating {entry.Rate:0.0#}x rate version...");
					workingPath = await _rateChanger.CreateRateChangedBeatmapAsync(
						workingOsu,
						entry.Rate,
						"[[name]] [[rate]]",
						entry.RatePitchAdjust,
						entry.OD,
						entry.HP,
						null);
					tempRateFiles.Add(workingPath);
					workingOsu = OsuFileParser.Parse(workingPath);
					applyHpOdOnMetadataPass = false;
				}

				var sourceDir = workingOsu.DirectoryPath;
				var audioSourcePath = Path.Combine(sourceDir, workingOsu.AudioFilename);
				var localAudioName = CopyAssetToFolder(
					audioSourcePath, outputFolder, $"entry_{i + 1:D3}_", copiedAssets);
				if (localAudioName == null)
					return new MapPackBuildResult
					{ ErrorMessage = $"Audio file not found for [{entry.EffectiveVersion}]." };

				var originalLines = await File.ReadAllLinesAsync(workingPath, cancellationToken);
				var modifiedLines = RateChanger.ApplyPackMetadataAndDifficultyOverrides(
					originalLines,
					entry.EffectiveVersion,
					applyHpOdOnMetadataPass ? entry.OD : null,
					applyHpOdOnMetadataPass ? entry.HP : null,
					definition.Title,
					definition.Artist,
					definition.Creator,
					definition.GetExportTags(),
					definition.Source,
					localAudioName,
					definition.ResetOnlineIdsOnExport);

				modifiedLines = RewriteEventAssetPaths(modifiedLines, sourceDir, outputFolder, $"entry_{i + 1:D3}_",
					copiedAssets);

				var osuFileName = BuildOsuFileName(definition.Artist, definition.Title, entry.EffectiveVersion);
				var outputOsuPath = Path.Combine(outputFolder, osuFileName);
				if (File.Exists(outputOsuPath) && !isInPlaceRebuild)
					outputOsuPath = GetUniquePath(outputOsuPath);

				await File.WriteAllLinesAsync(outputOsuPath, modifiedLines, cancellationToken);
				result.WrittenOsuPaths.Add(outputOsuPath);
				if (result.FirstOsuPath == null)
					result.FirstOsuPath = outputOsuPath;

				entry.SortOrder = i;
			}

			definition.LoadedFolderPath = outputFolder;
			await SaveSidecarAsync(definition, outputFolder, cancellationToken);

			result.Success = true;
			progressCallback?.Invoke($"Map pack built: {result.WrittenOsuPaths.Count} difficulties.");
		}
		catch (OperationCanceledException)
		{
			result.ErrorMessage = "Build cancelled.";
		}
		catch (Exception ex)
		{
			Logger.Info($"[MapPack] Build failed: {ex.Message}");
			result.ErrorMessage = ex.Message;
		}

		return result;
	}

	/// <summary>
	/// Loads a pack from its sidecar file in the given folder.
	/// </summary>
	public static MapPackDefinition? LoadFromSidecar(string folderPath)
	{
		var sidecarPath = Path.Combine(folderPath, MapPackDefinition.SidecarFileName);
		if (!File.Exists(sidecarPath))
			return null;

		try
		{
			var json = File.ReadAllText(sidecarPath);
			var definition = JsonSerializer.Deserialize<MapPackDefinition>(json, _jsonOptions);
			if (definition == null)
				return null;

			definition.LoadedFolderPath = folderPath;
			RefreshEntriesFromSources(definition);
			return definition;
		}
		catch (Exception ex)
		{
			Logger.Info($"[MapPack] Failed to load sidecar: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Loads a pack from the beatmapset folder containing the given map.
	/// </summary>
	public static MapPackDefinition LoadFromBeatmapFolder(string beatmapFilePath)
	{
		var folderPath = Path.GetDirectoryName(beatmapFilePath);
		if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
			return new MapPackDefinition();

		return LoadFromSidecar(folderPath) ?? LoadFromFolder(folderPath);
	}

	/// <summary>
	/// Loads a pack by scanning .osu files in a folder (fallback when no sidecar exists).
	/// </summary>
	public static MapPackDefinition LoadFromFolder(string folderPath)
	{
		var definition = new MapPackDefinition
		{
			LoadedFolderPath = folderPath,
			OutputFolderName = Path.GetFileName(folderPath) ?? "Map Pack"
		};

		var osuFiles = Directory.GetFiles(folderPath, "*.osu", SearchOption.TopDirectoryOnly)
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (osuFiles.Count == 0)
			return definition;

		var first = OsuFileParser.Parse(osuFiles[0]);
		definition.Title = ExtractTitleFromFolderName(folderPath) ?? first.Title;
		definition.Artist = MapPackDefinition.DefaultArtist;
		definition.Creator = first.Creator;
		definition.Tags = first.Tags;
		definition.Source = first.Source;

		for (var i = 0; i < osuFiles.Count; i++)
		{
			var osu = OsuFileParser.Parse(osuFiles[i]);
			var entry = MapPackEntry.FromOsuFile(osu);
			entry.SourceFilePath = osuFiles[i];
			entry.VersionOverride = osu.Version;
			entry.HP = osu.HPDrainRate;
			entry.OD = osu.OverallDifficulty;
			entry.SortOrder = i;
			definition.Entries.Add(entry);
		}

		return definition;
	}

	public static async Task SaveSidecarAsync(MapPackDefinition definition, string folderPath,
		CancellationToken cancellationToken = default)
	{
		var sidecarPath = Path.Combine(folderPath, MapPackDefinition.SidecarFileName);
		var json = JsonSerializer.Serialize(definition, _jsonOptions);
		await File.WriteAllTextAsync(sidecarPath, json, cancellationToken);
	}

	private static void RefreshEntriesFromSources(MapPackDefinition definition)
	{
		for (var i = 0; i < definition.Entries.Count; i++)
		{
			definition.Entries[i].SortOrder = i;
			definition.Entries[i].RefreshFromSource();
		}
	}

	private static string ResolveOutputFolder(MapPackDefinition definition, string songsFolder)
	{
		if (!string.IsNullOrEmpty(definition.LoadedFolderPath) && Directory.Exists(definition.LoadedFolderPath))
			return definition.LoadedFolderPath;

		return Path.Combine(songsFolder, definition.GetEffectiveOutputFolderName());
	}

	private static bool IsInPlaceRebuild(MapPackDefinition definition, string outputFolder)
	{
		if (string.IsNullOrEmpty(definition.LoadedFolderPath))
			return false;

		try
		{
			return string.Equals(
				Path.GetFullPath(outputFolder),
				Path.GetFullPath(definition.LoadedFolderPath),
				StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static string? ExtractTitleFromFolderName(string folderPath)
	{
		var folderName = Path.GetFileName(folderPath);
		if (string.IsNullOrWhiteSpace(folderName))
			return null;

		const string prefix = "Various Artists - ";
		if (folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return folderName[prefix.Length..].Trim();

		var dashIndex = folderName.IndexOf(" - ", StringComparison.Ordinal);
		if (dashIndex > 0 && dashIndex < folderName.Length - 3)
			return folderName[(dashIndex + 3)..].Trim();

		return null;
	}

	private static string BuildOsuFileName(string artist, string title, string version)
	{
		var baseName = $"{artist} - {title} [{version}]";
		return SanitizeFileName(baseName) + ".osu";
	}

	private static string SanitizeFileName(string name)
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
	}

	private static string GetUniquePath(string path)
	{
		if (!File.Exists(path))
			return path;

		var dir = Path.GetDirectoryName(path) ?? "";
		var name = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);
		var counter = 1;
		string candidate;
		do
		{
			candidate = Path.Combine(dir, $"{name}_{counter}{ext}");
			counter++;
		} while (File.Exists(candidate));

		return candidate;
	}

	private static string? CopyAssetToFolder(
		string sourcePath,
		string outputFolder,
		string prefix,
		Dictionary<string, string> copiedAssets)
	{
		if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
			return null;

		var fileName = Path.GetFileName(sourcePath);
		if (copiedAssets.TryGetValue(sourcePath, out var existingName))
			return existingName;

		var destName = prefix + fileName;
		var destPath = Path.Combine(outputFolder, destName);
		if (File.Exists(destPath))
		{
			var stem = Path.GetFileNameWithoutExtension(fileName);
			var ext = Path.GetExtension(fileName);
			destName = $"{prefix}{stem}_{Guid.NewGuid():N}{ext}";
			destPath = Path.Combine(outputFolder, destName);
		}

		File.Copy(sourcePath, destPath, true);
		copiedAssets[sourcePath] = destName;
		return destName;
	}

	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	private static List<string> RewriteEventAssetPaths(
		List<string> lines,
		string sourceDir,
		string outputFolder,
		string prefix,
		Dictionary<string, string> copiedAssets)
	{
		var result = new List<string>();
		var inEvents = false;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();
			if (trimmed == "[Events]")
			{
				inEvents = true;
				result.Add(line);
				continue;
			}

			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				inEvents = false;
				result.Add(line);
				continue;
			}

			if (!inEvents || string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
			{
				result.Add(line);
				continue;
			}

			var rewritten = RewriteEventLineAssets(line, sourceDir, outputFolder, prefix, copiedAssets);
			result.Add(rewritten);
		}

		return result;
	}

	private static string RewriteEventLineAssets(
		string line,
		string sourceDir,
		string outputFolder,
		string prefix,
		Dictionary<string, string> copiedAssets)
	{
		return Regex.Replace(line, "\"([^\"]+\\.(?:jpg|jpeg|png|wav|mp3|ogg|webp))\"", match =>
		{
			var assetName = match.Groups[1].Value;
			var sourcePath = Path.Combine(sourceDir, assetName);
			if (!File.Exists(sourcePath))
				return match.Value;

			var localName = CopyAssetToFolder(sourcePath, outputFolder, prefix, copiedAssets);
			return localName != null ? $"\"{localName}\"" : match.Value;
		}, RegexOptions.IgnoreCase);
	}
}
