// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using Companella.Models.Beatmap;
using Companella.Services.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Public facade for Daniel rice difficulty and dan estimation.
/// Daniel SR/dan uses only <c>algorithm.py</c> strain math — not MinaCalc and not Daniel's Etterna <c>msd.exe</c>.
/// MSD in the original Daniel app is display-only (skillset breakdown and VIBRO detection).
/// </summary>
public static class DanielDifficultyService
{
	private const int _requiredKeyCount = 4;

	/// <summary>
	/// Calculates Daniel SR and dan estimate from an osu file.
	/// </summary>
	public static DanielDifficultyResult Calculate(OsuFile osuFile, float rate = 1.0f)
	{
		if (osuFile.Mode != 3)
			return DanielDifficultyResult.Invalid("Not a mania beatmap.");

		if (string.IsNullOrWhiteSpace(osuFile.FilePath) || !File.Exists(osuFile.FilePath))
			return CalculateFromHitObjects(osuFile, rate);

		try
		{
			var (keyCount, notes) = DanielOsuFileParser.ParseFile(osuFile.FilePath, rate);
			if (keyCount != _requiredKeyCount)
				return DanielDifficultyResult.Invalid($"Daniel only supports 4K maps (keycount={keyCount}).");

			return CalculateFromNotes(notes, keyCount);
		}
		catch (Exception ex)
		{
			return DanielDifficultyResult.Invalid($"Failed to parse beatmap for Daniel: {ex.Message}");
		}
	}

	private static DanielDifficultyResult CalculateFromHitObjects(OsuFile osuFile, float rate)
	{
		var keyCount = (int)osuFile.CircleSize;
		if (keyCount != _requiredKeyCount)
			return DanielDifficultyResult.Invalid($"Daniel only supports 4K maps (keycount={keyCount}).");

		var hitObjects = HitObjectSerializer.Parse(osuFile);
		return Calculate(hitObjects, keyCount, rate);
	}

	private static DanielDifficultyResult CalculateFromNotes(List<DanielNote> notes, int keyCount)
	{
		if (notes.Count == 0)
			return DanielDifficultyResult.Invalid("Beatmap has no hit objects.");

		var preprocess = DanielPreprocessor.Preprocess(notes, keyCount);
		return BuildResult(preprocess);
	}

	/// <summary>
	/// Calculates Daniel SR and dan estimate from parsed hit objects.
	/// </summary>
	public static DanielDifficultyResult Calculate(IReadOnlyList<HitObject> hitObjects, int keyCount, float rate = 1.0f)
	{
		if (keyCount != _requiredKeyCount)
			return DanielDifficultyResult.Invalid($"Daniel only supports 4K maps (keycount={keyCount}).");

		if (hitObjects.Count == 0)
			return DanielDifficultyResult.Invalid("Beatmap has no hit objects.");

		var preprocess = DanielPreprocessor.Preprocess(hitObjects, keyCount, rate);
		return BuildResult(preprocess);
	}

	private static DanielDifficultyResult BuildResult(DanielPreprocessResult preprocess)
	{
		var rating = DanielStarRatingCalculator.Calculate(preprocess);
		var evalSr = DanielEvalTransform.Apply(
			rating.BaseStarRating,
			rating.Pass1StarRating,
			rating.OriginalDurationMs);
		var factorAverages = DanielComponents.FactorAverages(rating.AllCorners, rating.Factors);

		Logger.Debug(
			$"[DanielPass] eval durationMs={rating.OriginalDurationMs} baseSR={rating.BaseStarRating:F4} pass1SR={rating.Pass1StarRating:F4} evalSR={evalSr:F4}");

		var (danLabel, danNumeric) = DanielDanMapper.GetDanFromDiff(evalSr);
		double? danNumericValue = double.TryParse(
			danNumeric,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var parsed)
			? parsed
			: null;

		return new DanielDifficultyResult
		{
			IsValid = true,
			StarRating = evalSr,
			BaseStarRating = rating.BaseStarRating,
			Pass1StarRating = rating.Pass1StarRating,
			DanLabel = danLabel,
			DanNumeric = danNumeric,
			DanNumericValue = danNumericValue,
			IsBelowAlphaThreshold = DanielDanMapper.IsBelowAlphaThreshold(evalSr),
			FactorAverages = factorAverages
		};
	}
}
