using Companella.Models.Beatmap;
using Companella.Models.Training;
using System.Globalization;

namespace Companella.Models.Difficulty;

/// <summary>
/// Represents a map difficulty classification/level.
/// Uses numeric labels 1-10, then unicode greek letters.
/// </summary>
public class MapClassification
{
	/// <summary>
	/// The difficulty level index (1-20).
	/// 1-10 are numeric, 11-20 are greek letters.
	/// </summary>
	public int LevelIndex { get; set; }

	/// <summary>
	/// The display label for the level.
	/// "1" through "10" for numeric levels.
	/// Greek letters for levels 11-20.
	/// </summary>
	public string Level => GetLevelLabel(LevelIndex);

	/// <summary>
	/// Confidence score for the classification (0.0 - 1.0).
	/// </summary>
	public double Confidence { get; set; }

	/// <summary>
	/// Primary reason for this classification.
	/// </summary>
	public string Reason { get; set; } = string.Empty;

	/// <summary>
	/// Secondary factors that influenced the classification.
	/// </summary>
	public List<string> Factors { get; set; } = new();

	/// <summary>
	/// The dominant skillset that influenced this classification.
	/// </summary>
	public string DominantSkillset { get; set; } = string.Empty;

	/// <summary>
	/// The dominant pattern type that influenced this classification.
	/// </summary>
	public PatternType? DominantPattern { get; set; }

	/// <summary>
	/// The peak BPM of patterns in the map.
	/// </summary>
	public double PeakBpm { get; set; }

	/// <summary>
	/// Gets the level label for a given index.
	/// </summary>
	/// <param name="levelIndex">Level index (1-20).</param>
	/// <returns>Level label string.</returns>
	public static string GetLevelLabel(int levelIndex)
	{
		return DanLabelFormatter.GetLevelLabel(levelIndex);
	}

	/// <summary>
	/// Gets all available level labels in order.
	/// </summary>
	public static IReadOnlyList<string> GetAllLevelLabels()
	{
		var labels = new List<string>();
		for (var i = 1; i <= 20; i++) labels.Add(GetLevelLabel(i));

		return labels;
	}

	/// <summary>
	/// Minimum level index (1).
	/// </summary>
	public const int MinLevel = 1;

	/// <summary>
	/// Maximum level index (20 = kappa).
	/// </summary>
	public const int MaxLevel = 20;

	/// <summary>
	/// Level index where Greek letters start (11 = alpha).
	/// </summary>
	public const int GreekStartLevel = 11;

	/// <summary>
	/// Parses a level label back to its index.
	/// </summary>
	public static int ParseLevelLabel(string label)
	{
		if (string.IsNullOrEmpty(label))
			return 0;

		if (int.TryParse(label, out var numericLevel))
			return Math.Clamp(numericLevel, 1, 10);

		var normalized = DanLabelFormatter.NormalizeLabel(label);
		var greekLabels = new[]
		{
			"alpha", "beta", "gamma", "delta", "epsilon",
			"zeta", "eta", "theta", "iota", "kappa"
		};

		for (var i = 0; i < greekLabels.Length; i++)
			if (string.Equals(normalized, greekLabels[i], StringComparison.OrdinalIgnoreCase))
				return i + 11;

		return 0;
	}

	public override string ToString()
	{
		return $"Level {Level} ({Confidence:P0} confidence)";
	}
}
