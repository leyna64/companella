// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using Companella.Models.Difficulty;
using Companella.Models.Training;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Maps Daniel difficulty results to Companella dan classification results.
/// </summary>
public static class DanielClassificationMapper
{
	/// <summary>
	/// Converts a Daniel result into a <see cref="DanClassificationResult"/> (greek letter + -/+/none).
	/// </summary>
	public static DanClassificationResult ToClassificationResult(
		DanielDifficultyResult daniel,
		SkillsetScores? msdScores,
		double interludeRating = 0)
	{
		var msd = msdScores != null
			? MsdSkillsetValues.FromSkillsetScores(msdScores)
			: new MsdSkillsetValues(0);

		var (label, variant, danIndex) = ParseDanielDisplayLabel(daniel.DanLabel);

		var rawDan = daniel.DanNumericValue ?? 0;
		var confidence = DanielDanMapper.GetBandConfidence(daniel.StarRating);

		return new DanClassificationResult
		{
			Label = label,
			Variant = variant,
			DanIndex = danIndex,
			MsdValues = msd,
			InterludeRating = interludeRating,
			DominantSkillset = msd.DominantSkillset,
			Confidence = confidence,
			RawModelOutput = (float)rawDan,
			UsedDanielCalculator = true,
			DanielStarRating = daniel.StarRating
		};
	}

	/// <summary>
	/// Maps Daniel tier labels (Low/Mid/High) to Companella display variants (-/none/+).
	/// </summary>
	private static (string Label, string? Variant, int DanIndex) ParseDanielDisplayLabel(string danLabel)
	{
		if (string.IsNullOrWhiteSpace(danLabel) || danLabel.Contains('?'))
			return ("?", null, -1);

		string? variant = null;
		if (danLabel.EndsWith(" Low", StringComparison.OrdinalIgnoreCase))
			variant = "-";
		else if (danLabel.EndsWith(" High", StringComparison.OrdinalIgnoreCase))
			variant = "+";

		var name = danLabel.TrimStart('<').Trim();
		foreach (var tier in new[] { " Low", " Mid", " High" })
		{
			if (!name.EndsWith(tier, StringComparison.OrdinalIgnoreCase))
				continue;

			name = name[..^tier.Length];
			break;
		}

		var danIndex = DanLookup.GetIndex(name.Trim().ToLowerInvariant());
		var label = DanLookup.GetLabel(danIndex) ?? "?";
		return (label, variant, danIndex);
	}
}
