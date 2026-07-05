using Companella.Models.Beatmap;
using Companella.Services.Analysis.InterludePatterns;

namespace Companella.Services.Analysis;

/// <summary>
/// Detects patterns in osu!mania beatmaps using the YAVSRG/Interlude Prelude algorithm.
/// BPM is derived from row spacing (MsPerBeat), not map timing points.
/// </summary>
public class PatternFinder
{
	public PatternFinder()
	{
	}

	public PatternFinder(BpmCalculator bpmCalculator)
	{
	}

	public PatternFinder(List<TimingPoint> timingPoints)
	{
	}

	public PatternFinder(OsuFile osuFile)
	{
	}

	/// <summary>
	/// Calculates effective BPM from note delta (assuming 1/4 notes).
	/// Formula: BPM = 15000 / delta_ms
	/// </summary>
	public static double CalculateBpmFromDelta(double deltaMs)
		=> InterludePatternEngine.CalculateBpmFromDelta(deltaMs);

	/// <summary>
	/// Calculates effective BPM from a list of times (chord/note events).
	/// </summary>
	public static double CalculateBpmFromTimes(List<double> times)
	{
		if (times.Count < 2)
			return 0;

		var deltas = new List<double>();
		for (var i = 1; i < times.Count; i++)
		{
			var delta = times[i] - times[i - 1];
			if (delta > 0)
				deltas.Add(delta);
		}

		if (deltas.Count == 0)
			return 0;

		return CalculateBpmFromDelta(deltas.Average());
	}

	public static List<HitObject> ParseHitObjects(OsuFile osuFile)
	{
		var hitObjects = new List<HitObject>();

		if (!osuFile.RawSections.TryGetValue("HitObjects", out var lines))
			return hitObjects;

		var keyCount = (int)osuFile.CircleSize;

		foreach (var line in lines)
		{
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal))
				continue;

			var hitObject = HitObject.Parse(line, keyCount);
			if (hitObject != null)
				hitObjects.Add(hitObject);
		}

		return hitObjects.OrderBy(h => h.Time).ToList();
	}

	public static PatternAnalysisResult FindAllPatterns(OsuFile osuFile)
	{
		var hitObjects = ParseHitObjects(osuFile);
		return FindAllPatterns(hitObjects, (int)osuFile.CircleSize);
	}

	public static PatternAnalysisResult FindAllPatterns(List<HitObject> hitObjects, int keyCount = 4)
	{
		var result = new PatternAnalysisResult
		{
			TotalNotes = hitObjects.Count,
			Patterns = new Dictionary<PatternType, List<Pattern>>()
		};

		if (hitObjects.Count == 0)
			return result;

		var startTime = DateTime.UtcNow;

		try
		{
			var report = InterludePatternEngine.Analyze(hitObjects, keyCount);
			PopulateFromInterludeReport(result, report, hitObjects, report.FirstNoteTimeMs);
			result.Success = true;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
		}

		result.AnalysisDurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
		return result;
	}

	private static void PopulateFromInterludeReport(
		PatternAnalysisResult result,
		InterludePatternReport report,
		List<HitObject> hitObjects,
		float firstNoteTimeMs)
	{
		result.InterludeClusters = report.Clusters.ToList();
		result.InterludeCategory = report.Category;
		result.ChartDurationMs = report.DurationMs;
		result.ChartFirstNoteTimeMs = report.FirstNoteTimeMs;

		foreach (var found in report.FoundPatterns)
		{
			var patternType = InterludePatternEngine.MapSpecificTypeToPatternType(found.SpecificType, found.Pattern);
			var pattern = new Pattern
			{
				Type = patternType,
				StartTime = found.Start,
				EndTime = found.End,
				Bpm = found.MsPerBeat > 0 ? InterludePatternEngine.MsPerBeatToBpm(found.MsPerBeat) : 0,
				NoteCount = EstimateNoteCount(found, hitObjects, firstNoteTimeMs),
				SpecificName = found.SpecificType,
				Mixed = found.Mixed,
				CorePattern = found.Pattern.ToString()
			};

			if (!result.Patterns.TryGetValue(patternType, out var list))
			{
				list = new List<Pattern>();
				result.Patterns[patternType] = list;
			}

			list.Add(pattern);
		}
	}

	private static int EstimateNoteCount(InterludeFoundPattern found, List<HitObject> hitObjects, float firstNoteTimeMs)
	{
		var start = firstNoteTimeMs + found.Start;
		var end = firstNoteTimeMs + found.End;
		return hitObjects.Count(h => h.Time >= start && h.Time <= end);
	}
}
