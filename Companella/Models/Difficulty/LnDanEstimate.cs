namespace Companella.Models.Difficulty;

/// <summary>
/// Extrapolated LN dan estimate from LN difficulty calibration anchors.
/// </summary>
public class LnDanEstimate
{
	public string DanName { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public double RawDan { get; set; }
}
