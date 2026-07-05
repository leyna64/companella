using Companella.Services.Analysis.InterludePatterns;

namespace Companella.Models.Beatmap;

/// <summary>
/// Contains all detected patterns from a beatmap analysis.
/// </summary>
public class PatternAnalysisResult
{
	/// <summary>
	/// Maximum number of top patterns to display.
	/// </summary>
	public const int MaxTopPatterns = 5;

	/// <summary>
	/// All detected patterns grouped by type.
	/// </summary>
	public Dictionary<PatternType, List<Pattern>> Patterns { get; set; } = new();

	/// <summary>
	/// Total number of patterns detected.
	/// </summary>
	public int TotalPatterns => Patterns.Values.Sum(p => p.Count);

	/// <summary>
	/// Total number of hit objects analyzed.
	/// </summary>
	public int TotalNotes { get; set; }

	/// <summary>
	/// Analysis duration in milliseconds.
	/// </summary>
	public double AnalysisDurationMs { get; set; }

	/// <summary>
	/// Whether the analysis was successful.
	/// </summary>
	public bool Success { get; set; } = true;

	/// <summary>
	/// Error message if analysis failed.
	/// </summary>
	public string? ErrorMessage { get; set; }

	/// <summary>
	/// Interlude-style clustered patterns (primary display source).
	/// </summary>
	public List<InterludePatternCluster> InterludeClusters { get; set; } = new();

	/// <summary>
	/// Interlude map category (e.g. "180 Jumpstream Tech").
	/// </summary>
	public string? InterludeCategory { get; set; }

	/// <summary>
	/// Chart duration in milliseconds (first note to last note).
	/// </summary>
	public float ChartDurationMs { get; set; }

	/// <summary>
	/// Absolute time of the first note in the chart.
	/// </summary>
	public float ChartFirstNoteTimeMs { get; set; }

	/// <summary>
	/// Gets the Interlude map category for display, optionally prefixed with the dominant cluster BPM.
	/// Example: "195 Jumpstream Tech" at 1.0x, "293 Jumpstream Tech" at 1.5x DT.
	/// </summary>
	public string? GetInterludeCategoryDisplay(float rate = 1.0f)
	{
		if (string.IsNullOrWhiteSpace(InterludeCategory))
			return null;

		if (InterludeClusters.Count == 0)
			return InterludeCategory;

		var topCluster = InterludeClusters.MaxBy(c => c.Importance);
		if (topCluster == null || topCluster.Bpm <= 0)
			return InterludeCategory;

		var bpm = topCluster.Bpm * rate;
		var prefix = topCluster.Mixed ? "~" : string.Empty;
		return $"  {prefix}{InterludeCategory} {bpm:F0} BPM";
	}

	/// <summary>
	/// Gets the top 5 pattern clusters sorted by map coverage (%).
	/// </summary>
	public List<TopPattern> GetTopPatterns()
	{
		return GetAllPatternsSorted()
			.Take(MaxTopPatterns)
			.ToList();
	}

	/// <summary>
	/// Gets pattern clusters sorted by map coverage (%).
	/// Falls back to legacy note-percentage sorting when clusters are unavailable.
	/// </summary>
	public List<TopPattern> GetAllPatternsSorted()
	{
		if (InterludeClusters.Count > 0)
			return BuildFromInterludeClusters();

		return BuildLegacyPatternsSorted();
	}

	private List<TopPattern> BuildFromInterludeClusters()
	{
		var totalCoverageMs = InterludeClusters.Sum(c => c.AmountMs);
		if (totalCoverageMs <= 0)
			totalCoverageMs = 1;

		return InterludeClusters
			.Select(cluster => new TopPattern
			{
				Type = InterludePatternEngine.MapSpecificTypeToPatternType(
					cluster.SpecificTypes.Count > 0 ? cluster.SpecificTypes[0].Name : null,
					cluster.Pattern),
				Bpm = cluster.Bpm,
				// Share of detected pattern coverage (sums to 100% across all clusters).
				Percentage = cluster.AmountMs / totalCoverageMs * 100.0,
				NoteCount = 0,
				SpecificName = cluster.DisplayName,
				Mixed = cluster.Mixed,
				Importance = cluster.Importance
			})
			.OrderByDescending(p => p.Percentage)
			.ToList();
	}

	private List<TopPattern> BuildLegacyPatternsSorted()
	{
		var allPatterns = new List<TopPattern>();

		if (TotalNotes == 0 || Patterns.Count == 0)
			return allPatterns;

		foreach (var kvp in Patterns)
		{
			if (kvp.Value.Count == 0)
				continue;

			var patternType = kvp.Key;
			var patterns = kvp.Value;
			var notesInPattern = patterns.Sum(p => p.NoteCount);
			var percentage = notesInPattern / (double)TotalNotes * 100.0;
			var totalWeightedBpm = patterns.Sum(p => p.Bpm * p.NoteCount);
			var dominantBpm = notesInPattern > 0 ? totalWeightedBpm / notesInPattern : 0;

			if (dominantBpm <= 0 && percentage < 5.0)
				continue;

			allPatterns.Add(new TopPattern
			{
				Type = patternType,
				Bpm = dominantBpm,
				Percentage = percentage,
				NoteCount = notesInPattern
			});
		}

		return allPatterns
			.OrderByDescending(p => p.Percentage)
			.ToList();
	}

	/// <summary>
	/// Gets the count of a specific pattern type.
	/// </summary>
	public int GetPatternCount(PatternType type)
	{
		return Patterns.TryGetValue(type, out var list) ? list.Count : 0;
	}

	/// <summary>
	/// Gets the BPM range for a specific pattern type.
	/// </summary>
	public (double Min, double Max) GetBpmRange(PatternType type)
	{
		if (!Patterns.TryGetValue(type, out var list) || list.Count == 0)
			return (0, 0);

		var nonZeroBpms = list.Where(p => p.Bpm > 0).Select(p => p.Bpm).ToList();
		if (nonZeroBpms.Count == 0)
			return (0, 0);

		return (nonZeroBpms.Min(), nonZeroBpms.Max());
	}

	/// <summary>
	/// Gets the dominant pattern type (highest percentage of map).
	/// </summary>
	public PatternType? GetDominantPattern()
	{
		var top = GetTopPatterns();
		return top.Count > 0 ? top[0].Type : null;
	}

	/// <summary>
	/// Creates a failed result with an error message.
	/// </summary>
	public static PatternAnalysisResult Failed(string errorMessage)
	{
		return new PatternAnalysisResult
		{
			Success = false,
			ErrorMessage = errorMessage
		};
	}
}

/// <summary>
/// Represents a top pattern type-BPM pair for display.
/// </summary>
public class TopPattern
{
	/// <summary>
	/// The pattern type.
	/// </summary>
	public PatternType Type { get; set; }

	/// <summary>
	/// The dominant BPM for this pattern type (weighted average by note count).
	/// </summary>
	public double Bpm { get; set; }

	/// <summary>
	/// Percentage of map notes that are part of this pattern type.
	/// </summary>
	public double Percentage { get; set; }

	/// <summary>
	/// Total notes in all instances of this pattern type.
	/// </summary>
	public int NoteCount { get; set; }

	/// <summary>
	/// Interlude-specific display name when available.
	/// </summary>
	public string? SpecificName { get; set; }

	/// <summary>
	/// Whether this cluster has unstable BPM spacing.
	/// </summary>
	public bool Mixed { get; set; }

	/// <summary>
	/// Interlude importance score used for sorting.
	/// </summary>
	public float Importance { get; set; }

	/// <summary>
	/// Gets the short name for display.
	/// </summary>
	public string ShortName => SpecificName ?? Type switch
	{
		PatternType.Trill => "Trill",
		PatternType.Jack => "Jack",
		PatternType.Minijack => "Minijack",
		PatternType.Stream => "Stream",
		PatternType.Jump => "Jump",
		PatternType.Hand => "Hand",
		PatternType.Quad => "Quad",
		PatternType.Jumpstream => "JS",
		PatternType.Handstream => "HS",
		PatternType.Chordjack => "CJ",
		PatternType.Roll => "Roll",
		PatternType.Bracket => "Bracket",
		PatternType.Jumptrill => "JT",
		_ => Type.ToString()
	};

	/// <summary>
	/// Gets the BPM display string.
	/// Returns "-" for patterns with no inherent BPM.
	/// </summary>
	public string BpmDisplay => Bpm > 0 ? $"{Bpm:F0}" : "-";

	/// <summary>
	/// Gets the percentage display string.
	/// </summary>
	public string PercentageDisplay => $"{Percentage:F0}%";

	/// <summary>
	/// Gets a compact display string matching Interlude format.
	/// </summary>
	public string CompactDisplay => Mixed
		? $"~{Bpm:F0} Mixed {ShortName}"
		: Bpm > 0
			? $"{Bpm:F0} {ShortName}"
			: ShortName;
}
