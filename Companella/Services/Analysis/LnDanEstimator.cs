using Companella.Models.Difficulty;
using Companella.Models.Training;

namespace Companella.Services.Analysis;

/// <summary>
/// Estimates LN dan level by extrapolating LN difficulty against calibrated dan anchors.
/// Handles the non-monotonic dip between dan 3 and dan 4 by preferring the higher dan
/// when multiple segments match the same LN difficulty.
/// </summary>
public static class LnDanEstimator
{
	private const string _yamikaiDisplayName = "Yamikai";
	private const int _yamikaiIndex = 20;

	private readonly record struct LnDanAnchor(int Index, string DisplayName, double LnDifficulty);

	private static readonly LnDanAnchor[] _anchors =
	[
		new(1, "1", 3.128),
		new(2, "2", 4.005),
		new(3, "3", 4.86),
		new(4, "4", 4.801),
		new(5, "5", 4.964),
		new(6, "6", 5.471),
		new(7, "7", 6.193),
		new(8, "8", 6.302),
		new(9, "9", 6.739),
		new(10, "10", 7.282),
		new(11, "11-夜明け", 7.783),
		new(12, "12-夕暮れ", 8.092),
		new(13, "13-夜", 8.605),
		new(14, "14-闇", 9.243),
		new(15, "15-夢", 9.769),
		new(16, "16-夜風", 9.878),
		new(17, "17-Yeehee", 10.731),
		new(18, "18-Pongtai", 10.987),
		new(19, "19-絶縁", 11.136),
		new(20, _yamikaiDisplayName, 11.851)
	];

	/// <summary>
	/// Estimates LN dan from an LN difficulty value.
	/// </summary>
	public static LnDanEstimate Estimate(double lnDifficulty)
	{
		var candidates = new List<double>();

		for (var i = 0; i < _anchors.Length - 1; i++)
		{
			var a = _anchors[i];
			var b = _anchors[i + 1];
			if (!IsBetweenInclusive(lnDifficulty, a.LnDifficulty, b.LnDifficulty))
				continue;

			var span = b.LnDifficulty - a.LnDifficulty;
			var t = Math.Abs(span) < 1e-9 ? 0.0 : (lnDifficulty - a.LnDifficulty) / span;
			candidates.Add(a.Index + t * (b.Index - a.Index));
		}

		var rawDan = candidates.Count > 0
			? candidates.Max()
			: ExtrapolateOutsideRange(lnDifficulty);

		return new LnDanEstimate
		{
			DanName = GetDanName(rawDan),
			DisplayName = GetDisplayName(rawDan),
			RawDan = rawDan
		};
	}

	private static double ExtrapolateOutsideRange(double lnDifficulty)
	{
		var first = _anchors[0];
		var second = _anchors[1];
		var last = _anchors[^1];
		var penultimate = _anchors[^2];

		if (lnDifficulty < first.LnDifficulty)
		{
			var span = second.LnDifficulty - first.LnDifficulty;
			var t = Math.Abs(span) < 1e-9 ? 0.0 : (lnDifficulty - first.LnDifficulty) / span;
			return first.Index + t;
		}

		var topSpan = last.LnDifficulty - penultimate.LnDifficulty;
		var topT = Math.Abs(topSpan) < 1e-9 ? 0.0 : (lnDifficulty - last.LnDifficulty) / topSpan;
		return last.Index + topT;
	}

	private static string GetDisplayName(double rawDan)
	{
		if (rawDan >= _yamikaiIndex)
			return _yamikaiDisplayName;

		if (rawDan < 1.0)
			return "<1";

		var (danIndex, variant) = DanLabelFormatter.ParseRawDan(rawDan);
		var name = _anchors[danIndex].DisplayName;
		return DanLabelFormatter.AppendVariant(name, variant);
	}

	private static string GetDanName(double rawDan)
	{
		if (rawDan < 1.0)
			return "<1";

		if (rawDan >= _yamikaiIndex)
			return _yamikaiDisplayName;

		var index = Math.Clamp((int)Math.Floor(rawDan), 1, _anchors.Length);
		return _anchors[index - 1].DisplayName;
	}

	private static bool IsBetweenInclusive(double value, double a, double b)
	{
		var lo = Math.Min(a, b);
		var hi = Math.Max(a, b);
		return value >= lo - 1e-9 && value <= hi + 1e-9;
	}
}
