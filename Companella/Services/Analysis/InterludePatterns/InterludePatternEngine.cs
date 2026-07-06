using Companella.Models.Beatmap;

namespace Companella.Services.Analysis.InterludePatterns;

/// <summary>
/// Interlude/YAVSRG pattern analysis engine.
/// Port of Prelude.Calculator.Patterns (FindPatterns, Primitives, Clustering, Summary).
/// </summary>
public static class InterludePatternEngine
{
	private const float _patternStabilityThreshold = 5.0f;
	private const float _bpmClusterThreshold = 5.0f;
	private const float _msPerMinute = 60_000.0f;
	private const float _densitySensitivity = 0.9f;
	private const float _svAmountThreshold = 2000.0f;
	private const float _minClusterBpm = 25.0f;

	public static InterludePatternReport Analyze(List<HitObject> hitObjects, int keyCount)
	{
		var rows = BuildRows(hitObjects, keyCount);
		if (rows.Count == 0)
		{
			return new InterludePatternReport
			{
				DurationMs = 0,
				TotalRows = 0
			};
		}

		var density = CalculateDensity(rows, keyCount);
		for (var i = 0; i < rows.Count; i++)
			rows[i] = rows[i] with { Density = density[i] };

		var firstNoteTime = (float)hitObjects.Min(h => h.Time);
		var chartDuration = Math.Max(0, (float)hitObjects.Max(h => h.Time) - firstNoteTime);
		// Pattern times are relative to the first note; last-note bound must match.
		var relativeLastNoteTime = rows.Count > 0 ? rows[^1].Time : chartDuration;
		var specificPatterns = InterludePatternRecognisers.ForKeyCount(keyCount);
		var foundPatterns = FindPatterns(rows, specificPatterns, relativeLastNoteTime);
		var clusters = ClusterPatterns(foundPatterns);
		var prunedClusters = PruneClusters(clusters);
		var category = CategoriseChart(prunedClusters, 0.0f);

		return new InterludePatternReport
		{
			FoundPatterns = foundPatterns,
			Clusters = prunedClusters,
			Category = category,
			DurationMs = chartDuration,
			FirstNoteTimeMs = firstNoteTime,
			TotalRows = rows.Count
		};
	}

	public static float MsPerBeatToBpm(float msPerBeat)
	{
		if (msPerBeat <= 0)
			return 0;

		return (float)Math.Round(_msPerMinute / msPerBeat);
	}

	public static double CalculateBpmFromDelta(double deltaMs)
	{
		if (deltaMs <= 0)
			return 0;

		return 15000.0 / deltaMs;
	}

	private static List<InterludeRowInfo> BuildRows(List<HitObject> hitObjects, int keyCount)
	{
		var noteRows = hitObjects
			.Where(h => h.Type is HitObjectType.Circle or HitObjectType.Hold)
			.GroupBy(h => h.Time)
			.OrderBy(g => g.Key)
			.Select(g => new
			{
				Time = (float)g.Key,
				Columns = g.Select(h => h.Column).Distinct().OrderBy(c => c).ToArray()
			})
			.Where(r => r.Columns.Length > 0)
			.ToList();

		if (noteRows.Count == 0)
			return [];

		var result = new List<InterludeRowInfo>();
		var previousRow = noteRows[0].Columns;
		var previousTime = noteRows[0].Time;

		for (var i = 1; i < noteRows.Count; i++)
		{
			var current = noteRows[i];
			var currentRow = current.Columns;
			var (direction, isRoll) = DetectDirection(previousRow, currentRow);
			var overlap = CountOverlap(previousRow, currentRow);

			result.Add(new InterludeRowInfo
			{
				Index = i,
				Time = current.Time - noteRows[0].Time,
				MsPerBeat = (current.Time - previousTime) * 4.0f,
				Notes = currentRow.Length,
				Jacks = overlap,
				Direction = direction,
				Roll = isRoll,
				RawNotes = currentRow
			});

			previousRow = currentRow;
			previousTime = current.Time;
		}

		return result;
	}

	private static int CountOverlap(int[] previousRow, int[] currentRow)
	{
		var previous = previousRow.ToHashSet();
		var overlap = 0;

		foreach (var column in currentRow)
		{
			if (previous.Contains(column))
				overlap++;
		}

		return overlap;
	}

	private static (InterludeDirection Direction, bool IsRoll) DetectDirection(int[] previousRow, int[] currentRow)
	{
		var pLeft = previousRow[0];
		var pRight = previousRow[^1];
		var cLeft = currentRow[0];
		var cRight = currentRow[^1];
		var leftChange = cLeft - pLeft;
		var rightChange = cRight - pRight;

		var direction =
			leftChange > 0
				? rightChange > 0 ? InterludeDirection.Right : InterludeDirection.Inwards
				: leftChange < 0
					? rightChange < 0 ? InterludeDirection.Left : InterludeDirection.Outwards
					: rightChange < 0
						? InterludeDirection.Inwards
						: rightChange > 0 ? InterludeDirection.Outwards : InterludeDirection.None;

		var isRoll = pLeft > cRight || pRight < cLeft;
		return (direction, isRoll);
	}

	private static float[] CalculateDensity(List<InterludeRowInfo> rows, int keyCount)
	{
		var columnDensities = new float[keyCount];
		var columnSince = Enumerable.Repeat(float.NegativeInfinity, keyCount).ToArray();
		var densities = new float[rows.Count];

		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			var time = row.Time;

			foreach (var column in row.RawNotes)
			{
				var delta = time - columnSince[column];
				var nextDensity = delta > 0 && float.IsFinite(delta)
					? 1000.0f / delta
					: 0.0f;
				columnDensities[column] = columnDensities[column] * _densitySensitivity + nextDensity * (1.0f - _densitySensitivity);
				columnSince[column] = time;
			}

			densities[i] = columnDensities.Max();
		}

		return densities;
	}

	private static List<InterludeFoundPattern> FindPatterns(
		List<InterludeRowInfo> rows,
		InterludeSpecificPatterns specificPatterns,
		float lastNoteTime)
	{
		var results = new List<InterludeFoundPattern>();
		var remainingStart = 0;

		while (remainingStart < rows.Count)
		{
			var remaining = rows.Skip(remainingStart).ToList();

			TryAddPattern(results, remaining, specificPatterns.Stream, InterludeCorePattern.Stream, specificPatterns, lastNoteTime);
			TryAddPattern(results, remaining, specificPatterns.Chordstream, InterludeCorePattern.Chordstream, specificPatterns, lastNoteTime);
			TryAddJackPattern(results, remaining, specificPatterns.Jack, lastNoteTime);

			remainingStart++;
		}

		return results;
	}

	private static void TryAddPattern(
		List<InterludeFoundPattern> results,
		List<InterludeRowInfo> remaining,
		List<(string Name, InterludePatternRecogniser Recogniser)> specificRecognisers,
		InterludeCorePattern corePattern,
		InterludeSpecificPatterns specificPatterns,
		float lastNoteTime)
	{
		var coreLength = corePattern switch
		{
			InterludeCorePattern.Stream => InterludePatternRecognisers.Core.Stream(remaining),
			InterludeCorePattern.Chordstream => InterludePatternRecognisers.Core.Chordstream(remaining),
			_ => 0
		};

		if (coreLength == 0)
			return;

		var (length, specificType) = ResolveSpecificMatch(coreLength, specificRecognisers, remaining);
		var matched = remaining.Take(length).ToList();
		var meanMsPerBeat = matched.Average(r => r.MsPerBeat);
		var mixed = matched.Any(r => Math.Abs(r.MsPerBeat - meanMsPerBeat) >= _patternStabilityThreshold);
		var end = remaining.Count > length
			? remaining[length].Time
			: lastNoteTime;

		results.Add(new InterludeFoundPattern
		{
			Pattern = corePattern,
			SpecificType = specificType,
			Mixed = mixed,
			Start = remaining[0].Time,
			End = end,
			MsPerBeat = meanMsPerBeat,
			Density = matched.Average(r => r.Density)
		});
	}

	private static void TryAddJackPattern(
		List<InterludeFoundPattern> results,
		List<InterludeRowInfo> remaining,
		List<(string Name, InterludePatternRecogniser Recogniser)> jackRecognisers,
		float lastNoteTime)
	{
		var coreLength = InterludePatternRecognisers.Core.Jacks(remaining);
		if (coreLength == 0)
			return;

		var (length, specificType) = ResolveSpecificMatch(coreLength, jackRecognisers, remaining);
		var matched = remaining.Take(length).ToList();
		var meanMsPerBeat = matched.Average(r => r.MsPerBeat);
		var mixed = matched.Any(r => Math.Abs(r.MsPerBeat - meanMsPerBeat) >= _patternStabilityThreshold);
		var nextTime = remaining.Count > length ? remaining[length].Time : lastNoteTime;
		var end = Math.Max(remaining[0].Time + remaining[0].MsPerBeat * 0.5f, nextTime);

		results.Add(new InterludeFoundPattern
		{
			Pattern = InterludeCorePattern.Jacks,
			SpecificType = specificType,
			Mixed = mixed,
			Start = remaining[0].Time,
			End = end,
			MsPerBeat = meanMsPerBeat,
			Density = matched.Average(r => r.Density)
		});
	}

	private static (int Length, string? SpecificType) ResolveSpecificMatch(
		int coreLength,
		List<(string Name, InterludePatternRecogniser Recogniser)> recognisers,
		List<InterludeRowInfo> remaining)
	{
		foreach (var (name, recogniser) in recognisers)
		{
			var specificLength = recogniser(remaining);
			if (specificLength > 0)
				return (Math.Max(coreLength, specificLength), name);
		}

		return (coreLength, null);
	}

	private sealed class ClusterBuilder
	{
		public float SumMsPerBeat { get; set; }
		public float OriginalMsPerBeat { get; init; }
		public int Count { get; set; }
		public int? Bpm { get; private set; }

		public void Add(float msPerBeat) => SumMsPerBeat += msPerBeat;

		public void Calculate()
		{
			if (Count <= 0)
				return;

			Bpm = (int)Math.Round(_msPerMinute / (SumMsPerBeat / Count));
		}

		public int Value => Bpm ?? 0;
	}

	private static List<InterludePatternCluster> ClusterPatterns(List<InterludeFoundPattern> patterns)
	{
		var nonMixedClusters = new List<ClusterBuilder>();
		var mixedClusters = new Dictionary<InterludeCorePattern, ClusterBuilder>();

		var patternsWithClusters = patterns.Select(pattern =>
		{
			ClusterBuilder builder;
			if (pattern.Mixed)
			{
				if (!mixedClusters.TryGetValue(pattern.Pattern, out var mixedBuilder))
				{
					mixedBuilder = new ClusterBuilder
					{
						Count = 0,
						SumMsPerBeat = 0,
						OriginalMsPerBeat = pattern.MsPerBeat
					};
					mixedClusters[pattern.Pattern] = mixedBuilder;
				}

				builder = mixedBuilder;
			}
			else
			{
				var existing = nonMixedClusters.FirstOrDefault(c =>
					Math.Abs(c.OriginalMsPerBeat - pattern.MsPerBeat) < _bpmClusterThreshold);

				if (existing != null)
				{
					builder = existing;
				}
				else
				{
					builder = new ClusterBuilder
					{
						Count = 0,
						SumMsPerBeat = 0,
						OriginalMsPerBeat = pattern.MsPerBeat
					};
					nonMixedClusters.Add(builder);
				}
			}

			builder.Count++;
			builder.Add(pattern.MsPerBeat);
			return (pattern, builder);
		}).ToList();

		foreach (var cluster in nonMixedClusters)
			cluster.Calculate();

		foreach (var cluster in mixedClusters.Values)
			cluster.Calculate();

		return patternsWithClusters
			.GroupBy(x => (x.pattern.Pattern, x.pattern.Mixed, x.builder.Value))
			.Select(group =>
			{
				var (patternType, mixed, bpm) = group.Key;
				var data = group.Select(x => x.pattern).ToList();
				var amount = PatternAmount(data.Select(p => (p.Start, p.End)).ToList());
				var specificTypes = data
					.Where(p => p.SpecificType != null)
					.GroupBy(p => p.SpecificType!)
					.Select(g => (g.Key, (float)g.Count() / data.Count))
					.OrderByDescending(x => x.Item2)
					.ToList();

				var displayName = ResolveDisplayName(patternType, specificTypes);

				return new InterludePatternCluster
				{
					Pattern = patternType,
					DisplayName = displayName,
					SpecificTypes = specificTypes,
					Bpm = bpm,
					Mixed = mixed,
					AmountMs = amount,
					Importance = amount * RatingMultiplier(patternType) * bpm
				};
			})
			.Where(c => c.Bpm > _minClusterBpm)
			.OrderByDescending(c => c.AmountMs)
			.ToList();
	}

	private static List<InterludePatternCluster> PruneClusters(List<InterludePatternCluster> clusters)
	{
		bool CanBePruned(InterludePatternCluster cluster) =>
			clusters.Any(other =>
				other.Pattern == cluster.Pattern &&
				other.AmountMs * 0.5f > cluster.AmountMs &&
				other.Bpm > cluster.Bpm);

		var kept = clusters.Where(c => !CanBePruned(c)).ToList();

		var pruned = kept
			.Where(c => c.Pattern == InterludeCorePattern.Stream).Take(3)
			.Concat(kept.Where(c => c.Pattern == InterludeCorePattern.Chordstream).Take(3))
			.Concat(kept.Where(c => c.Pattern == InterludeCorePattern.Jacks).Take(3))
			.OrderByDescending(c => c.Importance)
			.ToList();

		return pruned;
	}

	private static float PatternAmount(List<(float Start, float End)> intervals)
	{
		if (intervals.Count == 0)
			return 0;

		var sorted = intervals.OrderBy(i => i.Start).ToList();
		var total = 0.0f;
		var currentStart = sorted[0].Start;
		var currentEnd = sorted[0].End;

		for (var i = 1; i < sorted.Count; i++)
		{
			var (start, end) = sorted[i];
			if (currentEnd < end)
			{
				total += currentEnd - currentStart;
				currentStart = start;
				currentEnd = end;
			}
			else
			{
				currentEnd = Math.Max(currentEnd, end);
			}
		}

		total += currentEnd - currentStart;
		return total;
	}

	private static float RatingMultiplier(InterludeCorePattern pattern) => pattern switch
	{
		InterludeCorePattern.Stream => 1.0f / 3.0f,
		InterludeCorePattern.Chordstream => 0.5f,
		InterludeCorePattern.Jacks => 1.0f,
		_ => 1.0f
	};

	private static string ResolveDisplayName(
		InterludeCorePattern pattern,
		List<(string Name, float Fraction)> specificTypes)
	{
		if (specificTypes.Count > 0 && specificTypes[0].Fraction > 0.4f)
			return specificTypes[0].Name;

		if (specificTypes.Count >= 2 &&
			specificTypes[0].Name == "Jumpstream" &&
			specificTypes[1].Name == "Handstream" &&
			specificTypes[1].Fraction / specificTypes[0].Fraction > 0.4f)
			return "Jump/Handstream";

		return pattern.ToString();
	}

	private static string CategoriseChart(List<InterludePatternCluster> clusters, float svAmountMs)
	{
		if (clusters.Count == 0)
			return svAmountMs >= _svAmountThreshold ? "SV" : "Uncategorised";

		var topImportance = clusters[0].Importance;
		var important = clusters.TakeWhile(c => c.Importance / topImportance > 0.5f).ToList();
		var cluster1 = important[0];
		var cluster2 = important.Count > 1 ? important[1] : null;

		var isHybrid = cluster2 != null && (
			(cluster2.Pattern == InterludeCorePattern.Jacks && cluster1.Pattern is InterludeCorePattern.Stream or InterludeCorePattern.Chordstream) ||
			(cluster2.Pattern is InterludeCorePattern.Stream or InterludeCorePattern.Chordstream && cluster1.Pattern == InterludeCorePattern.Jacks));

		var isTech = cluster1.Mixed;
		var isSv = svAmountMs >= _svAmountThreshold;

		var name = ResolveDisplayName(cluster1.Pattern, cluster1.SpecificTypes.ToList());

		return $"{name}{(isHybrid ? " Hybrid" : "")}{(isTech ? " Tech" : "")}{(isSv ? " + SV" : "")}";
	}

	public static PatternType MapSpecificTypeToPatternType(string? specificType, InterludeCorePattern corePattern)
	{
		if (specificType != null)
		{
			return specificType switch
			{
				"Trills" or "Minitrills" => PatternType.Trill,
				"Rolls" => PatternType.Roll,
				"Handstream" => PatternType.Handstream,
				"Jumpstream" or "Jumpstream/Handstream" => PatternType.Jumpstream,
				"Split Trill" => PatternType.Jumptrill,
				"Jumptrill" => PatternType.Jumptrill,
				"Longjacks" => PatternType.Jack,
				"Quadstream" => PatternType.Quad,
				"Gluts" => PatternType.Jack,
				"Chordjacks" => PatternType.Chordjack,
				"Minijacks" => PatternType.Minijack,
				"Brackets" => PatternType.Bracket,
				"Double Stream" or "Dense Chordstream" or "Light Chordstream" => PatternType.Jumpstream,
				_ => MapCorePattern(corePattern)
			};
		}

		return MapCorePattern(corePattern);
	}

	private static PatternType MapCorePattern(InterludeCorePattern corePattern) => corePattern switch
	{
		InterludeCorePattern.Stream => PatternType.Stream,
		InterludeCorePattern.Chordstream => PatternType.Jumpstream,
		InterludeCorePattern.Jacks => PatternType.Jack,
		_ => PatternType.Stream
	};
}
