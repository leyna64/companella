using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;

namespace Companella.Services.Session;

/// <summary>
/// Helpers for reading play stats from osu! memory when direct accuracy is unavailable.
/// </summary>
internal static class SessionPlayMemoryHelper
{
	internal readonly record struct PlayStats(double Accuracy, int Misses, int TotalHits, int Score);

	internal static PlayStats ReadPlayStats(StructuredOsuMemoryReader memoryReader, bool resultsScreen = false)
	{
		if (resultsScreen)
		{
			var result = ReadFromResultsScreen(memoryReader);
			if (result.TotalHits > 0) return result;
		}
		var player = new Player();
		if (!memoryReader.TryRead(player))
			return default;

		return BuildStats(player);
	}

	private static PlayStats ReadFromResultsScreen(StructuredOsuMemoryReader memoryReader)
	{
		var resultsScreen = new ResultsScreen();
		if (!memoryReader.TryRead(resultsScreen))
			return default;

		return BuildStats(resultsScreen);
	}

	internal static PlayStats BuildStats(OsuMemoryDataProvider.OsuMemoryModels.Abstract.RulesetPlayData data)
	{
		var misses = data.HitMiss;
		var totalHits = data.Hit300 + data.Hit100 + data.Hit50 + data.HitGeki + data.HitKatu + misses;
		// Empty/reset player structures can report 100%. They are not evidence of a score.
		if (data.Mode != 3 || totalHits == 0) return default;
		var accuracy = ComputeManiaAccuracy(data.HitGeki, data.Hit300, data.HitKatu, data.Hit100, data.Hit50, misses);

		return new PlayStats(accuracy, misses, totalHits, data.Score);
	}

	/// <summary>
	/// Computes osu!mania v1 accuracy from judgement counts.
	/// </summary>
	internal static double ComputeManiaAccuracy(int hitGeki, int hit300, int hitKatu, int hit100, int hit50,
		int hitMiss)
	{
		var total = hitGeki + hit300 + hitKatu + hit100 + hit50 + hitMiss;
		if (total == 0)
			return 0;

		var score = (hitGeki + hit300) * 300.0 + hitKatu * 200.0 + hit100 * 100.0 + hit50 * 50.0;
		return score / (total * 300.0) * 100.0;
	}
}
