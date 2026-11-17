using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Models.Training;
using Companella.Services.Analysis;
using Companella.Services.Analysis.Daniel;

namespace Companella.Services.Tools;

/// <summary>
/// Computes live DAN and Interlude pattern analysis for region-filtered note subsets.
/// </summary>
public class DifficultySplitterRegionAnalyzer
{
	private readonly DanConfigurationService _danConfigService;

	public DifficultySplitterRegionAnalyzer(DanConfigurationService danConfigService)
	{
		_danConfigService = danConfigService ?? throw new ArgumentNullException(nameof(danConfigService));
	}

	public DifficultyRegionAnalysisResult AnalyzePair(
		Guid pairId,
		IReadOnlyList<HitObject> allNotes,
		OsuFile source,
		double startMs,
		double endMs,
		RiceDanCalculatorMode calculatorMode)
	{
		var keyCount = (int)source.CircleSize;
		var filtered = DifficultySplitterService.FilterRegionNotes(allNotes, startMs, endMs);

		if (filtered.Count == 0)
			return DifficultyRegionAnalysisResult.Invalid(pairId, "Region contains no notes.");

		var patterns = PatternFinder.FindAllPatterns(filtered, keyCount);
		if (!patterns.Success)
		{
			return DifficultyRegionAnalysisResult.Invalid(
				pairId,
				patterns.ErrorMessage ?? "Pattern analysis failed.");
		}

		var dan = ClassifyRegion(filtered, source, keyCount, calculatorMode);
		return DifficultyRegionAnalysisResult.Success(pairId, filtered.Count, patterns, dan);
	}

	private DanClassificationResult? ClassifyRegion(
		List<HitObject> filtered,
		OsuFile source,
		int keyCount,
		RiceDanCalculatorMode calculatorMode)
	{
		double interludeRating = 0;
		double sunnyRating = 0;

		try
		{
			interludeRating = InterludeDifficultyService.CalculateDifficulty(
				filtered,
				source.TimingPoints,
				rate: 1.0f,
				keyCount);
		}
		catch
		{
			// ignored — ONNX can still fall back
		}

		try
		{
			sunnyRating = SunnyDifficultyService.CalculateDifficulty(filtered, source, rate: 1.0f);
		}
		catch
		{
			// ignored
		}

		if (calculatorMode == RiceDanCalculatorMode.Daniel && keyCount == 4)
		{
			var daniel = DanielDifficultyService.Calculate(filtered, keyCount);
			if (daniel.IsValid && !daniel.IsBelowAlphaThreshold)
				return DanielClassificationMapper.ToClassificationResult(daniel, msdScores: null, interludeRating);
		}

		if (!_danConfigService.IsModelLoaded)
			return null;

		try
		{
			return _danConfigService.ClassifyMap(null, interludeRating, sunnyRating);
		}
		catch (DanConfigurationService.ModelNotLoadedException)
		{
			return null;
		}
	}
}
