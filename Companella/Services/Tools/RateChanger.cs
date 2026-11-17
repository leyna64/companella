using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Companella.Analyzers.Attributes;
using Companella.Models.Beatmap;
using Companella.Models.Application;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Tools;

/// <summary>
/// Changes the playback rate of a beatmap, including audio and timing.
/// </summary>
[Suppress("CA1310")]
public class RateChanger
{
	private readonly string _ffmpegPath;

	/// <summary>
	/// Default naming format for rate-changed difficulties.
	/// </summary>
	public const string DefaultNameFormat = "[[name]] [[rate]]";

	/// <summary>
	/// Formats a rate for text fields, <c>[[rate]]</c>, and filenames: at least two decimal places; further fractional digits when needed (up to 6 total).
	/// </summary>
	public static string FormatRateForDisplay(double rate) => FormatDecimalMinTwoOptionalFractions(rate);

	/// <summary>
	/// Formats a BPM for text fields and <c>[[bpm]]</c>: at least two decimal places; further fractional digits when needed (up to 6 total).
	/// </summary>
	public static string FormatBpmForDisplay(double bpm) => FormatDecimalMinTwoOptionalFractions(bpm);

	/// <summary>
	/// Formats bulk rate range fields (min, max, step) and other rate-scaled numbers with the same rules as <see cref="FormatRateForDisplay"/>.
	/// </summary>
	public static string FormatDecimalForDisplay(double value) => FormatDecimalMinTwoOptionalFractions(value);

	private static string FormatDecimalMinTwoOptionalFractions(double value)
	{
		var v = Math.Round(value, 6);
		return v.ToString("0.00####", CultureInfo.InvariantCulture);
	}

	public RateChanger(string ffmpegPath = "ffmpeg")
	{
		_ffmpegPath = ffmpegPath;
	}

	/// <summary>
	/// Creates a rate-changed copy of the beatmap.
	/// </summary>
	/// <param name="osuFile">The original beatmap.</param>
	/// <param name="rate">The rate multiplier (e.g., 1.2 for 120%).</param>
	/// <param name="nameFormat">Format string for the new difficulty name.</param>
	/// <param name="pitchAdjust">Whether to adjust pitch with rate (like DT/HT). If false, preserves original pitch.</param>
	/// <param name="customOd">Custom Overall Difficulty value (null to keep original).</param>
	/// <param name="customHp">Custom HP Drain Rate value (null to keep original).</param>
	/// <param name="progressCallback">Callback for progress updates.</param>
	/// <returns>Path to the new .osu file.</returns>
	public async Task<string> CreateRateChangedBeatmapAsync(
		OsuFile osuFile,
		double rate,
		string nameFormat,
		bool pitchAdjust = true,
		double? customOd = null,
		double? customHp = null,
		Action<string>? progressCallback = null)
	{
		if (rate <= 0 || rate > 5)
			throw new ArgumentException("Rate must be between 0.1 and 5.0", nameof(rate));

		progressCallback?.Invoke("Reading original beatmap...");

		// Read original file
		var originalLines = await File.ReadAllLinesAsync(osuFile.FilePath);

		// Calculate new values
		var newBpm = GetDominantBpm(osuFile.TimingPoints) * rate;
		var rateString = FormatRateForDisplay(rate) + "x";

		// Generate new difficulty name
		var newDiffName = FormatDifficultyName(nameFormat, osuFile, rate, newBpm);

		// Generate new audio filename (include _nopitch suffix when pitch is preserved)
		var originalAudioPath = Path.Combine(osuFile.DirectoryPath, osuFile.AudioFilename);
		var audioExt = Path.GetExtension(osuFile.AudioFilename);
		var audioBaseName = Path.GetFileNameWithoutExtension(osuFile.AudioFilename);
		var pitchSuffix = pitchAdjust ? "" : "_nopitch";
		var newAudioFilename = $"{audioBaseName}_{rateString.Replace(".", "_")}{pitchSuffix}{audioExt}";
		var newAudioPath = Path.Combine(osuFile.DirectoryPath, newAudioFilename);

		// Create rate-changed audio if it doesn't exist
		if (!File.Exists(newAudioPath))
		{
			var pitchMode = pitchAdjust ? "with pitch change" : "preserving pitch";
			progressCallback?.Invoke($"Creating {rateString} audio ({pitchMode}) with ffmpeg...");
			await CreateRateChangedAudioAsync(originalAudioPath, newAudioPath, rate, pitchAdjust);
		}
		else
		{
			progressCallback?.Invoke($"Using existing {rateString} audio file...");
		}

		// Generate new .osu filename
		var osuBaseName = Path.GetFileNameWithoutExtension(osuFile.FilePath);
		// Try to extract the part before the difficulty name in brackets
		var match = Regex.Match(osuBaseName, @"^(.+?)\s*\[.+\]$");
		string newOsuBaseName;
		if (match.Success)
			newOsuBaseName = $"{match.Groups[1].Value} [{newDiffName}]";
		else
			newOsuBaseName = $"{osuBaseName} [{newDiffName}]";

		// Sanitize filename - remove invalid characters
		var invalidChars = Path.GetInvalidFileNameChars();
		var sanitizedBaseName =
			string.Join("_", newOsuBaseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
		var newOsuPath = Path.Combine(osuFile.DirectoryPath, sanitizedBaseName + ".osu");

		Logger.Info($"[RateChanger] New .osu path: {newOsuPath}");
		progressCallback?.Invoke("Creating modified .osu file...");

		// Modify the .osu content
		var newLines =
			ModifyOsuContent(originalLines, rate, newDiffName, newAudioFilename, osuFile, customOd, customHp);
		Logger.Info($"[RateChanger] Modified {newLines.Count} lines");

		// Write new .osu file
		try
		{
			await File.WriteAllLinesAsync(newOsuPath, newLines);
			Logger.Info($"[RateChanger] Successfully wrote .osu file");
		}
		catch (Exception ex)
		{
			Logger.Info($"[RateChanger] Failed to write .osu file: {ex.Message}");
			throw;
		}

		// Verify file was created
		if (!File.Exists(newOsuPath)) throw new InvalidOperationException($"Failed to create .osu file: {newOsuPath}");

		// Handle [[msd]] tag - calculate MSD on the NEW file at 1.0x rate, then rename
		if (Regex.IsMatch(nameFormat, @"\[\[msd\]\]", RegexOptions.IgnoreCase))
		{
			progressCallback?.Invoke("Calculating MSD...");
			newOsuPath = await ApplyMsdTagAsync(newOsuPath, osuFile);
		}

		// Handle [[dan]] tag - calculate dan classification on the NEW file at 1.0x rate, then rename
		if (Regex.IsMatch(nameFormat, @"\[\[dan\]\]", RegexOptions.IgnoreCase))
		{
			progressCallback?.Invoke("Calculating Dan...");
			newOsuPath = await ApplyDanTagAsync(newOsuPath, osuFile);
		}

		progressCallback?.Invoke("Rate change complete!");

		return newOsuPath;
	}

	/// <summary>
	/// Calculates MSD on a created beatmap at 1.0x and renames/updates the file with the MSD value.
	/// </summary>
	public static async Task<string> ApplyMsdTagAsync(string osuPath, OsuFile originalOsuFile)
	{
		// Only calculate MSD for supported mania key counts (4K/6K/7K for MinaCalc 5.15+, 4K only for 5.05)
		if (originalOsuFile.Mode != 3 || !ToolPaths.IsKeyCountSupported(originalOsuFile.CircleSize))
		{
			Logger.Info(
				$"[RateChanger] Not a supported mania key count ({ToolPaths.SupportedKeyCountsDisplay}), removing [[msd]] placeholder");
			return await RemoveMsdPlaceholderAsync(osuPath);
		}

		// Check if msd-calculator exists
		if (!ToolPaths.MsdCalculatorExists)
		{
			Logger.Info("[RateChanger] msd-calculator not found, removing [[msd]] placeholder");
			return await RemoveMsdPlaceholderAsync(osuPath);
		}

		try
		{
			// Calculate MSD at 1.0x on the NEW rate-changed file
			var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
			var result = await analyzer.AnalyzeSingleRateAsync(osuPath, 1.0f);

			var msdValue = result.Scores.Overall;
			var msdString = $"{msdValue:F1}msd";

			Logger.Info($"[RateChanger] Calculated MSD: {msdString}");

			// Update file content and rename
			return await ReplaceMsdPlaceholderAsync(osuPath, msdString);
		}
		catch (Exception ex)
		{
			Logger.Info($"[RateChanger] Failed to calculate MSD: {ex.Message}");
			return await RemoveMsdPlaceholderAsync(osuPath);
		}
	}

	/// <summary>
	/// Replaces [[msd]] placeholder in the file and renames it.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	public static async Task<string> ReplaceMsdPlaceholderAsync(string osuPath, string msdString)
	{
		var lines = await File.ReadAllLinesAsync(osuPath);
		var modified = false;
		string? newDiffName = null;

		for (var i = 0; i < lines.Length; i++)
			if (lines[i].TrimStart().StartsWith("Version:"))
			{
				var oldVersion = lines[i];
				lines[i] = Regex.Replace(lines[i], @"\[\[msd\]\]", msdString, RegexOptions.IgnoreCase);
				if (oldVersion != lines[i])
				{
					modified = true;
					newDiffName = lines[i].Substring(lines[i].IndexOf(':') + 1).Trim();
				}

				break;
			}

		if (modified && newDiffName != null)
		{
			// Write updated content
			await File.WriteAllLinesAsync(osuPath, lines);

			// Calculate new filename
			var dir = Path.GetDirectoryName(osuPath)!;
			var oldBaseName = Path.GetFileNameWithoutExtension(osuPath);

			// Replace the difficulty name in brackets
			var match = Regex.Match(oldBaseName, @"^(.+?)\s*\[.+\]$");
			string newBaseName;
			if (match.Success)
				newBaseName = $"{match.Groups[1].Value} [{newDiffName}]";
			else
				newBaseName = oldBaseName.Replace("[[msd]]", msdString, StringComparison.OrdinalIgnoreCase);

			// Sanitize and create new path
			var invalidChars = Path.GetInvalidFileNameChars();
			var sanitizedBaseName =
				string.Join("_", newBaseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
			var newPath = Path.Combine(dir, sanitizedBaseName + ".osu");

			// Rename file if path changed
			if (newPath != osuPath)
			{
				if (File.Exists(newPath))
					File.Delete(newPath);
				File.Move(osuPath, newPath);
				Logger.Info($"[RateChanger] Renamed to: {Path.GetFileName(newPath)}");
				return newPath;
			}
		}

		return osuPath;
	}

	/// <summary>
	/// Removes [[msd]] placeholder from the file when MSD calculation is not possible.
	/// </summary>
	private static async Task<string> RemoveMsdPlaceholderAsync(string osuPath)
	{
		return await ReplaceMsdPlaceholderAsync(osuPath, "");
	}

	/// <summary>
	/// Calculates dan classification on a created beatmap at 1.0x and renames/updates the file with the dan value.
	/// </summary>
	public static async Task<string> ApplyDanTagAsync(string osuPath, OsuFile originalOsuFile)
	{
		// Only calculate dan for supported mania key counts
		if (originalOsuFile.Mode != 3 || !ToolPaths.IsKeyCountSupported(originalOsuFile.CircleSize))
		{
			Logger.Info(
				$"[RateChanger] Not a supported mania key count ({ToolPaths.SupportedKeyCountsDisplay}), removing [[dan]] placeholder");
			return await RemoveDanPlaceholderAsync(osuPath);
		}

		// Check if msd-calculator exists (needed for dan classification)
		if (!ToolPaths.MsdCalculator515Exists)
		{
			Logger.Info("[RateChanger] msd-calculator-515 not found, removing [[dan]] placeholder");
			return await RemoveDanPlaceholderAsync(osuPath);
		}

		try
		{
			// Parse the new file to get updated hit objects
			var parser = new OsuFileParser();
			var newOsuFile = OsuFileParser.Parse(osuPath);

			// Calculate MSD at 1.0x on the NEW rate-changed file using 5.15 calculator
			var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator515);
			var result = await analyzer.AnalyzeSingleRateAsync(osuPath, 1.0f);

			// Classify using dan service (must initialize to load ONNX model)
			var danService = new DanConfigurationService();
			await danService.InitializeAsync();
			var settingsService = new UserSettingsService();
			await settingsService.LoadAsync();
			var calculatorMode = settingsService.Settings.RiceDanCalculator;
			var danResult = danService.ClassifyMap(result.Scores, newOsuFile, 1.0f, calculatorMode);

			// Use DisplayName which includes label + variant (e.g., "Alpha+", "Beta-")
			var danString = danResult.DisplayName;

			Logger.Info($"[RateChanger] Calculated Dan: {danString}");

			// Update file content and rename
			return await ReplaceDanPlaceholderAsync(osuPath, danString);
		}
		catch (Exception ex)
		{
			Logger.Info($"[RateChanger] Failed to calculate Dan: {ex.Message}");
			return await RemoveDanPlaceholderAsync(osuPath);
		}
	}

	/// <summary>
	/// Replaces [[dan]] placeholder in the file and renames it.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	public static async Task<string> ReplaceDanPlaceholderAsync(string osuPath, string danString)
	{
		var lines = await File.ReadAllLinesAsync(osuPath);
		var modified = false;
		string? newDiffName = null;

		for (var i = 0; i < lines.Length; i++)
			if (lines[i].TrimStart().StartsWith("Version:"))
			{
				var oldVersion = lines[i];
				lines[i] = Regex.Replace(lines[i], @"\[\[dan\]\]", danString, RegexOptions.IgnoreCase);
				if (oldVersion != lines[i])
				{
					modified = true;
					newDiffName = lines[i].Substring(lines[i].IndexOf(':') + 1).Trim();
				}

				break;
			}

		if (modified && newDiffName != null)
		{
			// Write updated content
			await File.WriteAllLinesAsync(osuPath, lines);

			// Calculate new filename
			var dir = Path.GetDirectoryName(osuPath)!;
			var oldBaseName = Path.GetFileNameWithoutExtension(osuPath);

			// Replace the difficulty name in brackets
			var match = Regex.Match(oldBaseName, @"^(.+?)\s*\[.+\]$");
			string newBaseName;
			if (match.Success)
				newBaseName = $"{match.Groups[1].Value} [{newDiffName}]";
			else
				newBaseName = oldBaseName.Replace("[[dan]]", danString, StringComparison.OrdinalIgnoreCase);

			// Sanitize and create new path
			var invalidChars = Path.GetInvalidFileNameChars();
			var sanitizedBaseName =
				string.Join("_", newBaseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
			var newPath = Path.Combine(dir, sanitizedBaseName + ".osu");

			// Rename file if path changed
			if (newPath != osuPath)
			{
				if (File.Exists(newPath))
					File.Delete(newPath);
				File.Move(osuPath, newPath);
				Logger.Info($"[RateChanger] Renamed to: {Path.GetFileName(newPath)}");
				return newPath;
			}
		}

		return osuPath;
	}

	/// <summary>
	/// Removes [[dan]] placeholder from the file when dan calculation is not possible.
	/// </summary>
	private static async Task<string> RemoveDanPlaceholderAsync(string osuPath)
	{
		return await ReplaceDanPlaceholderAsync(osuPath, "");
	}

	/// <summary>
	/// Creates multiple rate-changed copies of the beatmap for a range of rates.
	/// </summary>
	/// <param name="osuFile">The original beatmap.</param>
	/// <param name="minRate">Minimum rate (0.1 to 3.0).</param>
	/// <param name="maxRate">Maximum rate (0.1 to 3.0).</param>
	/// <param name="step">Rate increment step (>= 0.01).</param>
	/// <param name="nameFormat">Format string for the new difficulty names.</param>
	/// <param name="pitchAdjust">Whether to adjust pitch with rate (like DT/HT). If false, preserves original pitch.</param>
	/// <param name="customOd">Custom Overall Difficulty value (null to keep original).</param>
	/// <param name="customHp">Custom HP Drain Rate value (null to keep original).</param>
	/// <param name="excludeBaseRate">Whether to exclude 1.0x from the generated rates.</param>
	/// <param name="progressCallback">Callback for progress updates.</param>
	/// <returns>List of paths to the new .osu files.</returns>
	public async Task<List<string>> CreateBulkRateChangedBeatmapsAsync(
		OsuFile osuFile,
		double minRate,
		double maxRate,
		double step,
		string nameFormat,
		bool pitchAdjust = true,
		double? customOd = null,
		double? customHp = null,
		bool excludeBaseRate = false,
		Action<string>? progressCallback = null)
	{
		// Validate inputs
		minRate = Math.Clamp(minRate, 0.1, 3.0);
		maxRate = Math.Clamp(maxRate, 0.1, 3.0);
		step = Math.Max(step, 0.01);

		if (maxRate < minRate)
			maxRate = minRate;

		// Calculate all rates
		var rates = new List<double>();
		for (var rate = minRate; rate < maxRate - 0.001; rate += step) rates.Add(Math.Round(rate, 2));

		// Always include max rate
		if (rates.Count == 0 || Math.Abs(rates[^1] - maxRate) > 0.001) rates.Add(Math.Round(maxRate, 2));

		// Filter out base rate (1.0x) if excluded
		if (excludeBaseRate) rates = rates.Where(r => Math.Abs(r - 1.0) > 0.001).ToList();

		var createdFiles = new List<string>();
		var total = rates.Count;

		progressCallback?.Invoke($"Creating {total} rate-changed beatmaps...");

		for (var i = 0; i < rates.Count; i++)
		{
			var rate = rates[i];
			var rateString = FormatRateForDisplay(rate) + "x";

			progressCallback?.Invoke($"Creating {rateString} ({i + 1}/{total})...");

			try
			{
				var newPath = await CreateRateChangedBeatmapAsync(
					osuFile,
					rate,
					nameFormat,
					pitchAdjust,
					customOd,
					customHp,
					null // Don't pass sub-progress to avoid too many updates
				);
				createdFiles.Add(newPath);
			}
			catch (Exception ex)
			{
				Logger.Info($"[RateChanger] Failed to create {rateString}: {ex.Message}");
				progressCallback?.Invoke($"Warning: Failed to create {rateString} - {ex.Message}");
			}
		}

		progressCallback?.Invoke($"Bulk rate change complete! Created {createdFiles.Count}/{total} beatmaps.");

		return createdFiles;
	}

	/// <summary>
	/// Formats the difficulty name using the provided format string.
	/// Supports: [[name]], [[rate]], [[bpm]], [[od]], [[hp]], [[cs]], [[ar]], [[msd]], [[dan]]
	/// [[rate]] and [[bpm]] use the same rules as <see cref="FormatRateForDisplay"/> / <see cref="FormatBpmForDisplay"/> (min. two decimals; up to six when needed).
	/// Note: [[msd]] and [[dan]] are kept as placeholders and replaced after file creation.
	/// </summary>
	public static string FormatDifficultyName(string format, OsuFile osuFile, double rate, double newBpm)
	{
		var rateString = FormatRateForDisplay(rate) + "x";
		var bpmString = FormatBpmForDisplay(newBpm) + "bpm";

		var result = format;
		result = Regex.Replace(result, @"\[\[name\]\]", osuFile.Version, RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\[\[rate\]\]", rateString, RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\[\[bpm\]\]", bpmString, RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\[\[od\]\]", $"OD{osuFile.OverallDifficulty:0.#}", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\[\[hp\]\]", $"HP{osuFile.HPDrainRate:0.#}", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\[\[cs\]\]", $"CS{osuFile.CircleSize:0.#}", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\[\[ar\]\]", $"AR{osuFile.ApproachRate:0.#}", RegexOptions.IgnoreCase);

		// [[msd]] and [[dan]] are kept as placeholders - they will be replaced after the file is created
		// by ApplyMsdTagAsync and ApplyDanTagAsync which calculate values at 1.0x on the new file

		return result.Trim();
	}

	/// <summary>
	/// Creates a rate-changed audio file using ffmpeg.
	/// </summary>
	/// <param name="inputPath">Path to the input audio file.</param>
	/// <param name="outputPath">Path for the output audio file.</param>
	/// <param name="rate">The rate multiplier.</param>
	/// <param name="pitchAdjust">If true, pitch changes with rate (like DT/HT). If false, preserves original pitch.</param>
	private async Task CreateRateChangedAudioAsync(string inputPath, string outputPath, double rate,
		bool pitchAdjust = true)
	{
		if (!File.Exists(inputPath))
			throw new FileNotFoundException($"Audio file not found: {inputPath}");

		string arguments;
		var targetRate = 44100;

		if (pitchAdjust)
		{
			// For rate change with pitch shift (like DT/HT), use asetrate + aresample
			// This changes both speed and pitch proportionally
			// We first resample to 44100 to normalize, then apply the rate trick
			// Build ffmpeg arguments:
			// 1. aresample=44100 - normalize input to known sample rate
			// 2. asetrate=44100*rate - trick ffmpeg into thinking sample rate is different
			// 3. aresample=44100 - resample back, which changes speed+pitch
			var rateStr = rate.ToString("0.######", CultureInfo.InvariantCulture);
			arguments =
				$"-y -i \"{inputPath}\" -af \"aresample={targetRate},asetrate={targetRate}*{rateStr},aresample={targetRate}\" -q:a 0 \"{outputPath}\"";
		}
		else
		{
			// For rate change without pitch shift, use atempo filter
			// atempo only accepts values between 0.5 and 2.0, so we chain multiple filters for rates outside this range
			var atempoFilter = BuildAtempoFilter(rate);
			arguments = $"-y -i \"{inputPath}\" -af \"{atempoFilter}\" -q:a 0 \"{outputPath}\"";
		}

		Logger.Info($"[RateChanger] Running: {_ffmpegPath} {arguments}");

		var startInfo = new ProcessStartInfo
		{
			FileName = _ffmpegPath,
			Arguments = arguments,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using var process = new Process { StartInfo = startInfo };
		var errorBuilder = new System.Text.StringBuilder();

		process.ErrorDataReceived += (sender, e) =>
		{
			if (e.Data != null)
			{
				errorBuilder.AppendLine(e.Data);
				// ffmpeg outputs progress to stderr
				if (e.Data.Contains("time=") || e.Data.Contains("size=")) Logger.Info($"[ffmpeg] {e.Data}");
			}
		};

		process.Start();
		process.BeginErrorReadLine();

		var completed = await Task.Run(() => process.WaitForExit(300000)); // 5 minute timeout

		if (!completed)
		{
			process.Kill();
			throw new TimeoutException("ffmpeg timed out after 5 minutes");
		}

		if (process.ExitCode != 0)
			throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}:\n{errorBuilder}");

		if (!File.Exists(outputPath))
			throw new InvalidOperationException($"ffmpeg did not create output file: {outputPath}");

		Logger.Info($"[RateChanger] Audio created: {Path.GetFileName(outputPath)}");
	}

	/// <summary>
	/// Builds an atempo filter chain for the given rate.
	/// atempo filter only accepts values between 0.5 and 2.0, so we chain multiple filters for rates outside this range.
	/// </summary>
	/// <param name="rate">The rate multiplier (e.g., 1.2 for 120%).</param>
	/// <returns>The atempo filter string (e.g., "atempo=1.2" or "atempo=2.0,atempo=1.25" for 2.5x).</returns>
	private static string BuildAtempoFilter(double rate)
	{
		var filters = new List<string>();
		var remainingRate = rate;

		// Handle rates > 2.0 by chaining atempo=2.0 filters
		while (remainingRate > 2.0)
		{
			filters.Add("atempo=2.0");
			remainingRate /= 2.0;
		}

		// Handle rates < 0.5 by chaining atempo=0.5 filters
		while (remainingRate < 0.5)
		{
			filters.Add("atempo=0.5");
			remainingRate /= 0.5;
		}

		// Add the final atempo filter for the remaining rate (now between 0.5 and 2.0)
		var rateStr = remainingRate.ToString("0.######", CultureInfo.InvariantCulture);
		filters.Add($"atempo={rateStr}");

		return string.Join(",", filters);
	}

	/// <summary>
	/// Modifies the .osu file content for the new rate.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	private static List<string> ModifyOsuContent(string[] lines, double rate, string newDiffName,
		string newAudioFilename, OsuFile osuFile, double? customOd = null, double? customHp = null)
	{
		var result = new List<string>();
		var currentSection = "";

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			// Track current section
			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				currentSection = trimmed;
				result.Add(line);
				continue;
			}

			// Modify based on section
			switch (currentSection)
			{
				case "[General]":
					if (trimmed.StartsWith("AudioFilename:"))
					{
						result.Add($"AudioFilename: {newAudioFilename}");
						continue;
					}
					else if (trimmed.StartsWith("PreviewTime:"))
					{
						// Scale preview time
						var parts = trimmed.Split(':');
						if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var previewTime))
						{
							var newPreviewTime = (int)(previewTime / rate);
							result.Add($"PreviewTime: {newPreviewTime}");
							continue;
						}
					}

					break;

				case "[Editor]":
					if (trimmed.StartsWith("Bookmarks:"))
					{
						// Scale bookmark times
						var colonIndex = trimmed.IndexOf(':');
						if (colonIndex >= 0)
						{
							var bookmarksStr = trimmed.Substring(colonIndex + 1).Trim();
							if (!string.IsNullOrEmpty(bookmarksStr))
							{
								var bookmarkParts = bookmarksStr.Split(',');
								var newBookmarks = new List<string>();
								foreach (var part in bookmarkParts)
									if (int.TryParse(part.Trim(), out var bookmarkTime))
									{
										var newBookmarkTime = (int)(bookmarkTime / rate);
										newBookmarks.Add(newBookmarkTime.ToString(CultureInfo.InvariantCulture));
									}

								if (newBookmarks.Count > 0)
								{
									result.Add($"Bookmarks: {string.Join(",", newBookmarks)}");
									continue;
								}
							}
						}
					}

					break;

				case "[Difficulty]":
					if (customOd.HasValue && trimmed.StartsWith("OverallDifficulty:"))
					{
						result.Add(
							$"OverallDifficulty:{customOd.Value.ToString("0.0#", CultureInfo.InvariantCulture)}");
						continue;
					}
					else if (customHp.HasValue && trimmed.StartsWith("HPDrainRate:"))
					{
						result.Add($"HPDrainRate:{customHp.Value.ToString("0.0#", CultureInfo.InvariantCulture)}");
						continue;
					}

					break;

				case "[Metadata]":
					if (trimmed.StartsWith("Version:"))
					{
						result.Add($"Version:{newDiffName}");
						continue;
					}
					else if (trimmed.StartsWith("BeatmapID:", StringComparison.OrdinalIgnoreCase))
					{
						// Clear beatmap ID for new difficulty
						result.Add("BeatmapID:0");
						continue;
					}
					else if (trimmed.StartsWith("BeatmapSetID:", StringComparison.OrdinalIgnoreCase))
					{
						result.Add("BeatmapSetID:-1");
						continue;
					}

					break;

				case "[TimingPoints]":
					if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
					{
						var modifiedTp = ModifyTimingPointLine(trimmed, rate);
						if (modifiedTp != null)
						{
							result.Add(modifiedTp);
							continue;
						}
					}

					break;

				case "[HitObjects]":
					if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
					{
						var modifiedHo = ModifyHitObjectLine(trimmed, rate);
						if (modifiedHo != null)
						{
							result.Add(modifiedHo);
							continue;
						}
					}

					break;

				case "[Events]":
					// Modify event times (breaks, storyboard, etc.)
					if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
					{
						var modifiedEvent = ModifyEventLine(trimmed, rate);
						if (modifiedEvent != null)
						{
							result.Add(modifiedEvent);
							continue;
						}
					}

					break;
			}

			result.Add(line);
		}

		return result;
	}

	/// <summary>
	/// Applies pack-level metadata, difficulty name, HP/OD overrides, and optional audio filename
	/// without changing timing or hit objects. Used for map pack export at 1.0x rate.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	public static List<string> ApplyPackMetadataAndDifficultyOverrides(
		IEnumerable<string> lines,
		string versionName,
		double? customOd,
		double? customHp,
		string title,
		string artist,
		string creator,
		string? tags,
		string? source,
		string? audioFilename = null,
		bool resetOnlineIds = true)
	{
		var result = new List<string>();
		var currentSection = string.Empty;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				currentSection = trimmed;
				result.Add(line);
				continue;
			}

			switch (currentSection)
			{
				case "[General]":
					if (audioFilename != null && trimmed.StartsWith("AudioFilename:"))
					{
						result.Add($"AudioFilename:{audioFilename}");
						continue;
					}

					break;

				case "[Metadata]":
					if (trimmed.StartsWith("Title:"))
					{
						result.Add($"Title:{title}");
						continue;
					}

					if (trimmed.StartsWith("TitleUnicode:"))
					{
						result.Add($"TitleUnicode:{title}");
						continue;
					}

					if (trimmed.StartsWith("Artist:"))
					{
						result.Add($"Artist:{artist}");
						continue;
					}

					if (trimmed.StartsWith("ArtistUnicode:"))
					{
						result.Add($"ArtistUnicode:{artist}");
						continue;
					}

					if (trimmed.StartsWith("Creator:"))
					{
						result.Add($"Creator:{creator}");
						continue;
					}

					if (trimmed.StartsWith("Tags:"))
					{
						result.Add($"Tags:{tags ?? ""}");
						continue;
					}

					if (trimmed.StartsWith("Source:"))
					{
						result.Add($"Source:{source ?? ""}");
						continue;
					}

					if (trimmed.StartsWith("Version:"))
					{
						result.Add($"Version:{versionName}");
						continue;
					}

					if (resetOnlineIds && trimmed.StartsWith("BeatmapID:", StringComparison.OrdinalIgnoreCase))
					{
						result.Add("BeatmapID:0");
						continue;
					}

					if (resetOnlineIds && trimmed.StartsWith("BeatmapSetID:", StringComparison.OrdinalIgnoreCase))
					{
						result.Add("BeatmapSetID:-1");
						continue;
					}

					break;

				case "[Difficulty]":
					if (customOd.HasValue && trimmed.StartsWith("OverallDifficulty:"))
					{
						result.Add(
							$"OverallDifficulty:{customOd.Value.ToString("0.0#", CultureInfo.InvariantCulture)}");
						continue;
					}

					if (customHp.HasValue && trimmed.StartsWith("HPDrainRate:"))
					{
						result.Add($"HPDrainRate:{customHp.Value.ToString("0.0#", CultureInfo.InvariantCulture)}");
						continue;
					}

					break;
			}

			result.Add(line);
		}

		return resetOnlineIds ? EnsureOnlineBeatmapIdsReset(result) : result;
	}

	/// <summary>
	/// Ensures exported beatmaps have local-only online IDs (BeatmapID 0, BeatmapSetID -1).
	/// Inserts missing ID lines into the Metadata section when absent.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	public static List<string> EnsureOnlineBeatmapIdsReset(List<string> lines)
	{
		var output = new List<string>();
		var inMetadata = false;
		var wroteBeatmapId = false;
		var wroteBeatmapSetId = false;
		var metadataLines = new List<string>();

		void FlushMetadata()
		{
			if (!inMetadata)
				return;

			if (!wroteBeatmapId)
				metadataLines.Add("BeatmapID:0");
			if (!wroteBeatmapSetId)
				metadataLines.Add("BeatmapSetID:-1");

			output.AddRange(metadataLines);
			metadataLines.Clear();
			inMetadata = false;
			wroteBeatmapId = false;
			wroteBeatmapSetId = false;
		}

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			if (trimmed == "[Metadata]")
			{
				FlushMetadata();
				output.Add(line);
				inMetadata = true;
				continue;
			}

			if (inMetadata && trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				FlushMetadata();
				output.Add(line);
				continue;
			}

			if (inMetadata)
			{
				if (trimmed.StartsWith("BeatmapID:", StringComparison.OrdinalIgnoreCase))
				{
					metadataLines.Add("BeatmapID:0");
					wroteBeatmapId = true;
				}
				else if (trimmed.StartsWith("BeatmapSetID:", StringComparison.OrdinalIgnoreCase))
				{
					metadataLines.Add("BeatmapSetID:-1");
					wroteBeatmapSetId = true;
				}
				else
				{
					metadataLines.Add(line);
				}

				continue;
			}

			output.Add(line);
		}

		FlushMetadata();
		return output;
	}

	/// <summary>
	/// Modifies a timing point line for the new rate.
	/// </summary>
	private static string? ModifyTimingPointLine(string line, double rate)
	{
		var parts = line.Split(',');
		if (parts.Length < 2)
			return line;

		try
		{
			// Parse time and beat length
			var time = double.Parse(parts[0], CultureInfo.InvariantCulture);
			var beatLength = double.Parse(parts[1], CultureInfo.InvariantCulture);
			var isUninherited = parts.Length > 6 && parts[6] == "1";

			// Scale time
			var newTime = time / rate;

			// Scale beat length for uninherited points (BPM)
			var newBeatLength = beatLength;
			if (isUninherited && beatLength > 0)
				// BeatLength = 60000 / BPM, so for faster rate we need smaller beatLength
				newBeatLength = beatLength / rate;

			// Rebuild the line
			parts[0] = newTime.ToString("0.###", CultureInfo.InvariantCulture);
			parts[1] = newBeatLength.ToString("0.############", CultureInfo.InvariantCulture);

			return string.Join(",", parts);
		}
		catch
		{
			return line;
		}
	}

	/// <summary>
	/// Modifies a hit object line for the new rate.
	/// </summary>
	private static string? ModifyHitObjectLine(string line, double rate)
	{
		var parts = line.Split(',');
		if (parts.Length < 3)
			return line;

		try
		{
			// Time is the 3rd element (index 2)
			var time = double.Parse(parts[2], CultureInfo.InvariantCulture);
			var newTime = time / rate;
			parts[2] = ((int)Math.Round(newTime)).ToString(CultureInfo.InvariantCulture);

			// For hold notes (mania) or sliders, there might be end times
			// Hold notes have format: x,y,time,type,hitSound,endTime:hitSample
			if (parts.Length >= 6)
			{
				var lastPart = parts[5];
				if (lastPart.Contains(':'))
				{
					var endParts = lastPart.Split(':');
					if (long.TryParse(endParts[0], out var endTime))
					{
						var newEndTime = (long)(endTime / rate);
						endParts[0] = newEndTime.ToString(CultureInfo.InvariantCulture);
						parts[5] = string.Join(":", endParts);
					}
				}
			}

			return string.Join(",", parts);
		}
		catch
		{
			return line;
		}
	}

	/// <summary>
	/// Modifies an event line for the new rate.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness")]
	private static string? ModifyEventLine(string line, double rate)
	{
		// Break format: 2,startTime,endTime
		if (line.StartsWith("2,") || line.StartsWith("Break,"))
		{
			var parts = line.Split(',');
			if (parts.Length >= 3)
				try
				{
					if (int.TryParse(parts[1], out var startTime)) parts[1] = ((int)(startTime / rate)).ToString(CultureInfo.InvariantCulture);

					if (int.TryParse(parts[2], out var endTime)) parts[2] = ((int)(endTime / rate)).ToString(CultureInfo.InvariantCulture);

					return string.Join(",", parts);
				}
				catch
				{
				}
		}

		return line;
	}

	/// <summary>
	/// Gets the dominant BPM from timing points.
	/// </summary>
	private static double GetDominantBpm(List<TimingPoint> timingPoints)
	{
		var uninherited = timingPoints.Where(tp => tp.Uninherited && tp.BeatLength > 0).ToList();
		if (uninherited.Count == 0)
			return 120;
		if (uninherited.Count == 1)
			return uninherited[0].Bpm;

		// Find BPM with longest duration
		var bpmDurations = new Dictionary<double, double>();
		for (var i = 0; i < uninherited.Count; i++)
		{
			var bpm = Math.Round(uninherited[i].Bpm, 1);
			var duration = i < uninherited.Count - 1
				? uninherited[i + 1].Time - uninherited[i].Time
				: 60000;
			if (!bpmDurations.ContainsKey(bpm))
				bpmDurations[bpm] = 0;
			bpmDurations[bpm] += duration;
		}

		return bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
	}

	/// <summary>
	/// Checks if ffmpeg is available.
	/// </summary>
	public async Task<bool> CheckFfmpegAvailableAsync()
	{
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = _ffmpegPath,
				Arguments = "-version",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using var process = new Process { StartInfo = startInfo };
			process.Start();
			await Task.Run(() => process.WaitForExit(5000));
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Calculates the suggested rate to achieve a target MSD from a current MSD.
	/// Uses a simple approximation: MSD scales roughly linearly with rate.
	/// </summary>
	/// <param name="currentMsd">The current MSD at 1.0x rate.</param>
	/// <param name="targetMsd">The target MSD to achieve.</param>
	/// <returns>Suggested rate clamped to valid range (0.7 to 2.0), or null if no valid rate.</returns>
	public static float? GetSuggestedRate(float currentMsd, float targetMsd)
	{
		if (currentMsd <= 0 || targetMsd <= 0)
			return null;

		// MSD scales roughly linearly with rate
		// A simple approximation: targetMSD / currentMSD ~ suggestedRate
		var suggestedRate = targetMsd / currentMsd;

		// Clamp to valid rate range
		if (suggestedRate < 0.7f || suggestedRate > 2.0f)
			return null;

		// Round to nearest 0.05 for cleaner values
		suggestedRate = MathF.Round(suggestedRate * 20) / 20;

		return Math.Clamp(suggestedRate, 0.7f, 2.0f);
	}

	/// <summary>
	/// Standard rates supported by the MSD calculator.
	/// </summary>
	public static readonly float[] StandardRates =
		{ 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f };

	/// <summary>
	/// Gets the nearest standard rate to a calculated rate.
	/// </summary>
	public static float GetNearestStandardRate(float rate)
	{
		return StandardRates.OrderBy(r => Math.Abs(r - rate)).First();
	}
}
