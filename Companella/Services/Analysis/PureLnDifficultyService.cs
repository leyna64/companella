using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Services.Beatmap;

namespace Companella.Services.Analysis;

/// <summary>
/// Estimates pure LN difficulty from Sunny ratings on full and no-LN beatmaps.
/// </summary>
public static class PureLnDifficultyService
{
	private const double _minimumLnPercent = 10.0;

	/// <summary>
	/// Calculates Sunny-based pure LN ratings for a beatmap.
	/// </summary>
	public static PureLnDifficultyRatings Calculate(OsuFile osuFile, float rate = 1.0f)
	{
		var result = new PureLnDifficultyRatings();

		if (osuFile.Mode != 3)
			return result;

		var hitObjects = HitObjectSerializer.Parse(osuFile);
		if (hitObjects.Count == 0)
			return result;

		var lnCount = hitObjects.Count(h => h.IsHold);
		var lnPercent = (double)lnCount / hitObjects.Count * 100.0;
		if (lnPercent < _minimumLnPercent)
			return result;

		var noLnHitObjects = BeatmapNoLnConverter.StripLongNotes(hitObjects);

		var fullSunny = SunnyDifficultyService.CalculateDifficulty(hitObjects, osuFile, rate);
		var noLnSunny = SunnyDifficultyService.CalculateDifficulty(noLnHitObjects, osuFile, rate);
		if (fullSunny < 0 || noLnSunny < 0)
			return result;

		var pureLnSunny = fullSunny - noLnSunny;
		result.PureLnSunny = pureLnSunny;
		result.NoLnSunny = noLnSunny;
		result.LnDifference = pureLnSunny;
		result.LnDifficulty = Math.Pow(Math.Pow(pureLnSunny + 1, 1.22) * Math.Pow(fullSunny - pureLnSunny, 3), 1.0 / 3.0);

		var lnDanEstimate = LnDanEstimator.Estimate(result.LnDifficulty.Value);
		result.EstimatedLnDanName = lnDanEstimate.DanName;
		result.EstimatedLnDanDisplayName = lnDanEstimate.DisplayName;
		result.EstimatedLnDanRaw = lnDanEstimate.RawDan;

		return result;
	}
}
