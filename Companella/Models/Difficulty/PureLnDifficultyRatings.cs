namespace Companella.Models.Difficulty;

/// <summary>
/// Sunny-based pure LN difficulty estimates.
/// </summary>
public class PureLnDifficultyRatings
{
	public double? PureLnSunny { get; set; }
	public double? NoLnSunny { get; set; }
	public double? LnDifference { get; set; }
	public double? LnDifficulty { get; set; }
	public string? EstimatedLnDanName { get; set; }
	public string? EstimatedLnDanDisplayName { get; set; }
	public double? EstimatedLnDanRaw { get; set; }

	public bool HasLnDanEstimate => EstimatedLnDanRaw.HasValue;
}
