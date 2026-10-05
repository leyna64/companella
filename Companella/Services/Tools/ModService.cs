using System.Globalization;
using System.Text.RegularExpressions;
using Companella.Models.Beatmap;
using Companella.Mods;
using Companella.Services.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Tools;

/// <summary>
/// Service for registering, managing, and executing beatmap mods.
/// Acts as the central point of access for the mod system.
/// </summary>
public class ModService
{
	private readonly Dictionary<string, IMod> _registeredMods = new(StringComparer.OrdinalIgnoreCase);
	private readonly HitObjectSerializer _hitObjectSerializer;

	/// <summary>
	/// Creates a new ModService instance.
	/// </summary>
	public ModService()
	{
		_hitObjectSerializer = new HitObjectSerializer();
	}

	/// <summary>
	/// Creates a new ModService instance with a custom serializer.
	/// </summary>
	/// <param name="hitObjectSerializer">The hit object serializer to use.</param>
	public ModService(HitObjectSerializer hitObjectSerializer)
	{
		_hitObjectSerializer = hitObjectSerializer ?? throw new ArgumentNullException(nameof(hitObjectSerializer));
	}

	/// <summary>
	/// Registers a mod with the service.
	/// </summary>
	/// <param name="mod">The mod to register.</param>
	/// <exception cref="ArgumentException">Thrown if a mod with the same name is already registered.</exception>
	public void RegisterMod(IMod mod)
	{
		ArgumentNullException.ThrowIfNull(mod);

		if (_registeredMods.ContainsKey(mod.Name))
			throw new ArgumentException($"A mod with name '{mod.Name}' is already registered");

		_registeredMods[mod.Name] = mod;
		Logger.Info($"[ModService] Registered mod: {mod.Name} ({mod.Category})");
	}

	/// <summary>
	/// Unregisters a mod from the service.
	/// </summary>
	/// <param name="modName">The name of the mod to unregister.</param>
	/// <returns>True if the mod was unregistered, false if it wasn't found.</returns>
	public bool UnregisterMod(string modName)
	{
		if (string.IsNullOrEmpty(modName))
			return false;

		var removed = _registeredMods.Remove(modName);
		if (removed) Logger.Info($"[ModService] Unregistered mod: {modName}");

		return removed;
	}

	/// <summary>
	/// Gets a registered mod by name.
	/// </summary>
	/// <param name="modName">The name of the mod.</param>
	/// <returns>The mod if found, null otherwise.</returns>
	public IMod? GetMod(string modName)
	{
		if (string.IsNullOrEmpty(modName))
			return null;

		_registeredMods.TryGetValue(modName, out var mod);
		return mod;
	}

	/// <summary>
	/// Gets all registered mods.
	/// </summary>
	/// <returns>A list of all registered mods.</returns>
	public IReadOnlyList<IMod> GetAllMods()
	{
		return _registeredMods.Values.ToList();
	}

	/// <summary>
	/// Gets all registered mods in a specific category.
	/// </summary>
	/// <param name="category">The category to filter by.</param>
	/// <returns>A list of mods in the specified category.</returns>
	public IReadOnlyList<IMod> GetModsByCategory(string category)
	{
		return _registeredMods.Values
			.Where(m => string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
			.ToList();
	}

	/// <summary>
	/// Gets all unique categories of registered mods.
	/// </summary>
	/// <returns>A list of category names.</returns>
	public IReadOnlyList<string> GetCategories()
	{
		return _registeredMods.Values
			.Select(m => m.Category)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(c => c)
			.ToList();
	}

	/// <summary>
	/// Applies a mod to an osu file and writes the result to a new file.
	/// </summary>
	/// <param name="mod">The mod to apply.</param>
	/// <param name="osuFile">The source osu file.</param>
	/// <param name="outputPath">The path for the output file (optional, auto-generated if null).</param>
	/// <param name="progressCallback">Optional callback for progress updates.</param>
	/// <returns>The result of applying the mod.</returns>
	public static async Task<ModResult> ApplyModAsync(
		IMod mod,
		OsuFile osuFile,
		string? outputPath = null,
		Action<string>? progressCallback = null)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentNullException.ThrowIfNull(osuFile);

		try
		{
			progressCallback?.Invoke($"Parsing hit objects...");

			// Parse hit objects
			var hitObjects = await Task.Run(() => HitObjectSerializer.Parse(osuFile));

			progressCallback?.Invoke($"Applying {mod.Name}...");

			// Create context
			var context = new ModContext(osuFile, hitObjects);

			// Apply mod
			var result = await Task.Run(() => mod.Apply(context));

			if (!result.Success) return result;

			if (result.AudioReverse != null)
			{
				progressCallback?.Invoke("Checking ffmpeg...");
				var rateChanger = new RateChanger();
				if (!await rateChanger.CheckFfmpegAvailableAsync())
					return ModResult.Failed(
						"ffmpeg not found. Install ffmpeg and ensure it is on PATH to use Reverse.");
			}

			progressCallback?.Invoke("Writing output file...");

			// Generate output path if not provided
			outputPath ??= GenerateOutputPath(osuFile, mod);

			// Write the modified beatmap
			await WriteModifiedBeatmapAsync(
				osuFile,
				result.ModifiedHitObjects!,
				outputPath,
				context.KeyCount,
				mod,
				result.ModifiedTimingPoints,
				result.AudioReverse);

			result.OutputFilePath = outputPath;

			if (result.AudioReverse != null)
				try
				{
					await ProcessReversedAudioAsync(outputPath, progressCallback);
				}
				catch (Exception ex)
				{
					Logger.Info($"[ModService] Reverse audio failed: {ex.Message}");
					try
					{
						File.Delete(outputPath);
					}
					catch
					{
						// ignored
					}

					return ModResult.Failed($"Reverse audio failed: {ex.Message}");
				}

			progressCallback?.Invoke($"Mod applied successfully!");
			Logger.Info($"[ModService] Mod '{mod.Name}' applied. Output: {outputPath}");

			return result;
		}
		catch (Exception ex)
		{
			Logger.Info($"[ModService] Error applying mod '{mod.Name}': {ex.Message}");
			return ModResult.Failed($"Error applying mod: {ex.Message}");
		}
	}

	/// <summary>
	/// Applies a mod by name to an osu file.
	/// </summary>
	/// <param name="modName">The name of the mod to apply.</param>
	/// <param name="osuFile">The source osu file.</param>
	/// <param name="outputPath">The path for the output file (optional).</param>
	/// <param name="progressCallback">Optional callback for progress updates.</param>
	/// <returns>The result of applying the mod.</returns>
	public async Task<ModResult> ApplyModByNameAsync(
		string modName,
		OsuFile osuFile,
		string? outputPath = null,
		Action<string>? progressCallback = null)
	{
		ArgumentNullException.ThrowIfNull(modName);
		ArgumentNullException.ThrowIfNull(osuFile);

		var mod = GetMod(modName) ?? throw new ArgumentException($"Mod '{modName}' not found");

		return await ApplyModAsync(mod, osuFile, outputPath, progressCallback);
	}

	/// <summary>
	/// Generates an output path for a modded beatmap.
	/// </summary>
	/// <param name="osuFile">The source osu file.</param>
	/// <param name="mod">The mod being applied.</param>
	/// <returns>The generated output path.</returns>
	private static string GenerateOutputPath(OsuFile osuFile, IMod mod)
	{
		var directory = osuFile.DirectoryPath;
		var originalBaseName = Path.GetFileNameWithoutExtension(osuFile.FilePath);
		var modSuffix = mod is LaneSwapMod laneSwapMod ? $"[{laneSwapMod.GetLaneOrder()}]" : $"[+{mod.Icon}]";

		// Try to extract the part before the difficulty name in brackets
		var match = Regex.Match(originalBaseName, @"^(.+?)\s*\[(.+)\]$");
		string newBaseName;

		if (match.Success)
		{
			var prefix = match.Groups[1].Value;
			var originalDiff = match.Groups[2].Value;
			newBaseName = $"{prefix} [{originalDiff} {modSuffix}]";
		}
		else
		{
			newBaseName = $"{originalBaseName} {modSuffix}";
		}

		// Sanitize filename
		var invalidChars = Path.GetInvalidFileNameChars();
		var sanitizedBaseName =
			string.Join("_", newBaseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

		return Path.Combine(directory, sanitizedBaseName + ".osu");
	}

	/// <summary>
	/// Writes a modified beatmap to a new file.
	/// </summary>
	public static async Task WriteModifiedBeatmapAsync(
		OsuFile originalFile,
		List<HitObject> modifiedHitObjects,
		string outputPath,
		int keyCount,
		IMod mod,
		List<TimingPoint>? modifiedTimingPoints = null,
		ModAudioReverseSpec? reverseWrite = null)
	{
		ArgumentNullException.ThrowIfNull(originalFile);
		ArgumentNullException.ThrowIfNull(modifiedHitObjects);
		ArgumentNullException.ThrowIfNull(outputPath);
		ArgumentNullException.ThrowIfNull(mod);

		// Read original file
		var lines = await File.ReadAllLinesAsync(originalFile.FilePath);
		var result = new List<string>();
		var modSuffix = mod is LaneSwapMod laneSwapMod ? $"[{laneSwapMod.GetLaneOrder()}]" : $"[+{mod.Icon}]";

		var inTimingPointsSection = false;
		var timingPointsWritten = false;
		var inHitObjectsSection = false;
		var hitObjectsWritten = false;
		var currentSection = "";

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			// Check for section header
			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				if (inTimingPointsSection && !timingPointsWritten && modifiedTimingPoints != null)
				{
					OsuFileWriter.AppendTimingPointsLines(result, modifiedTimingPoints);
					timingPointsWritten = true;
				}

				// If we were in HitObjects section, write modified hit objects before leaving
				if (inHitObjectsSection && !hitObjectsWritten)
				{
					WriteHitObjects(result, modifiedHitObjects, keyCount);
					hitObjectsWritten = true;
				}

				currentSection = trimmed;
				inTimingPointsSection = trimmed == "[TimingPoints]";
				inHitObjectsSection = trimmed == "[HitObjects]";
				result.Add(line);
				continue;
			}

			// If in TimingPoints section, skip original lines when replacing with modified list
			if (inTimingPointsSection && modifiedTimingPoints != null)
				if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//", StringComparison.Ordinal))
					continue;

			// If in HitObjects section, skip original lines (we'll write new ones)
			if (inHitObjectsSection)
				// Skip original hit object data, but keep comments
				if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//", StringComparison.Ordinal))
					continue;

			// Modify metadata section to update Version and clear BeatmapID
			if (trimmed.StartsWith("Version:", StringComparison.Ordinal))
			{
				var originalVersion = trimmed.Substring("Version:".Length).Trim();
				result.Add($"Version:{originalVersion} {modSuffix}");
				continue;
			}

			if (trimmed.StartsWith("BeatmapID:", StringComparison.Ordinal))
			{
				result.Add("BeatmapID:0");
				continue;
			}

			if (reverseWrite != null &&
				string.Equals(currentSection, "[General]", StringComparison.OrdinalIgnoreCase) &&
				trimmed.StartsWith("PreviewTime:", StringComparison.OrdinalIgnoreCase))
			{
				var colon = trimmed.IndexOf(':');
				if (colon >= 0 &&
					int.TryParse(trimmed.AsSpan(colon + 1).Trim(), out var pt) && pt >= 0)
				{
					var mirrored = (int)Math.Round(reverseWrite.AnchorDurationMs - pt);
					result.Add($"PreviewTime: {mirrored}");
				}
				else
					result.Add(line);

				continue;
			}

			if (reverseWrite != null &&
				string.Equals(currentSection, "[Editor]", StringComparison.OrdinalIgnoreCase) &&
				trimmed.StartsWith("Bookmarks:", StringComparison.OrdinalIgnoreCase))
			{
				var colon = trimmed.IndexOf(':');
				if (colon >= 0)
				{
					var rest = trimmed[(colon + 1)..].Trim();
					if (string.IsNullOrEmpty(rest))
						result.Add(line);
					else
					{
						var mirrored = new List<int>();
						foreach (var p in rest.Split(','))
							if (int.TryParse(p.Trim(), out var b))
								mirrored.Add((int)Math.Round(reverseWrite.AnchorDurationMs - b));

						mirrored.Sort();
						result.Add(
							$"Bookmarks: {string.Join(",", mirrored.Select(x => x.ToString(CultureInfo.InvariantCulture)))}");
					}
				}
				else
					result.Add(line);

				continue;
			}

			if (reverseWrite != null &&
				string.Equals(currentSection, "[Events]", StringComparison.OrdinalIgnoreCase) &&
				trimmed.StartsWith("2,", StringComparison.Ordinal))
			{
				var parts = trimmed.Split(',');
				if (parts.Length >= 3 &&
					double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bs) &&
					double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var be))
				{
					var ns = reverseWrite.AnchorDurationMs - be;
					var ne = reverseWrite.AnchorDurationMs - bs;
					if (ns > ne)
						(ns, ne) = (ne, ns);

					result.Add(string.Format(CultureInfo.InvariantCulture, "2,{0},{1}", (int)Math.Round(ns),
						(int)Math.Round(ne)));
					continue;
				}
			}

			if (reverseWrite != null &&
				string.Equals(currentSection, "[Events]", StringComparison.OrdinalIgnoreCase) &&
				trimmed.StartsWith("1,", StringComparison.Ordinal))
			{
				var videoStart = Regex.Match(trimmed, @"^1,(\d+)");
				if (videoStart.Success)
				{
					var newStart = (int)Math.Round(reverseWrite.AnchorDurationMs -
												   double.Parse(videoStart.Groups[1].Value, CultureInfo.InvariantCulture));
					var newLine = Regex.Replace(trimmed, @"^1,\d+", $"1,{newStart}", RegexOptions.None);
					result.Add(newLine);
					continue;
				}
			}

			result.Add(line);
		}

		if (inTimingPointsSection && !timingPointsWritten && modifiedTimingPoints != null)
			OsuFileWriter.AppendTimingPointsLines(result, modifiedTimingPoints);

		// If file ends while still in HitObjects section
		if (inHitObjectsSection && !hitObjectsWritten) WriteHitObjects(result, modifiedHitObjects, keyCount);

		// Write output file
		await File.WriteAllLinesAsync(outputPath, result);
	}

	/// <summary>
	/// Reverses the audio referenced by the written .osu (same filename as source) and updates <c>AudioFilename</c>.
	/// </summary>
	private static async Task ProcessReversedAudioAsync(string outputOsuPath, Action<string>? progressCallback)
	{
		var dir = Path.GetDirectoryName(outputOsuPath);
		if (string.IsNullOrEmpty(dir))
			throw new InvalidOperationException("Invalid beatmap output path.");

		var lines = await File.ReadAllLinesAsync(outputOsuPath);
		string? audioFilename = null;
		foreach (var line in lines)
		{
			var t = line.Trim();
			if (t.StartsWith("AudioFilename:", StringComparison.OrdinalIgnoreCase))
			{
				audioFilename = t.Substring("AudioFilename:".Length).Trim();
				break;
			}
		}

		if (string.IsNullOrEmpty(audioFilename))
			throw new InvalidOperationException("No AudioFilename in beatmap.");

		var inputAudio = Path.Combine(dir, audioFilename);
		if (!File.Exists(inputAudio))
			throw new FileNotFoundException($"Audio file not found: {inputAudio}");

		var ext = Path.GetExtension(audioFilename);
		var baseName = Path.GetFileNameWithoutExtension(audioFilename);
		var newFilename = $"{baseName}_rev{ext}";
		var outputAudio = Path.Combine(dir, newFilename);

		await AudioReverseUtilities.CreateReversedAudioFileAsync(inputAudio, outputAudio, progressCallback: progressCallback);

		for (var i = 0; i < lines.Length; i++)
		{
			var t = lines[i].Trim();
			if (!t.StartsWith("AudioFilename:", StringComparison.OrdinalIgnoreCase))
				continue;

			lines[i] = $"AudioFilename: {newFilename}";
			break;
		}

		await File.WriteAllLinesAsync(outputOsuPath, lines);
		Logger.Info($"[ModService] Reversed audio written: {newFilename}");
	}

	/// <summary>
	/// Writes hit objects to the result list.
	/// </summary>
	public static void WriteHitObjects(List<string> result, List<HitObject> hitObjects, int keyCount)
	{
		var serialized = HitObjectSerializer.SerializeAll(hitObjects, keyCount);
		foreach (var line in serialized) result.Add(line);
	}

	/// <summary>
	/// Creates a ModContext for an OsuFile without applying a mod.
	/// Useful for testing or previewing.
	/// </summary>
	/// <param name="osuFile">The osu file to create context for.</param>
	/// <returns>A mod context with parsed hit objects.</returns>
	public static ModContext CreateContext(OsuFile osuFile)
	{
		var hitObjects = HitObjectSerializer.Parse(osuFile);
		return new ModContext(osuFile, hitObjects);
	}
}
