// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Maps Daniel star rating to Greek-letter dan labels.
/// </summary>
public static class DanielDanMapper
{
	private static readonly (double Lower, double Upper)[] _danBoundaries = PrecomputeDanBoundaries();

	/// <summary>
	/// Lower SR boundary for Alpha dan. Below this, Companella ONNX fallback applies.
	/// </summary>
	public static double AlphaLowerBound => _danBoundaries[0].Lower;

	/// <summary>
	/// Returns true when SR is below the Alpha threshold.
	/// </summary>
	public static bool IsBelowAlphaThreshold(double starRating)
	{
		return starRating < AlphaLowerBound;
	}

	/// <summary>
	/// Maps a Daniel star rating to a dan label and numeric value.
	/// </summary>
	public static (string Label, string Numeric) GetDanFromDiff(double diff)
	{
		if (diff < _danBoundaries[0].Lower)
			return ("<Alpha Low", "N/A");

		if (diff >= _danBoundaries[^1].Upper)
			return ("? ? ? ? ?", "N/A");

		for (var i = 0; i < DanielConstants.DanOrder.Length; i++)
		{
			var (lower, upper) = _danBoundaries[i];
			if (diff >= lower && diff < upper)
			{
				var span = upper - lower;
				var t = span > 0 ? Math.Clamp((diff - lower) / span, 0.0, 1.0) : 0.0;
				var numeric = Math.Round(DanielConstants.DanOrderStart + i + t, 2);
				var dan = DanielConstants.DanOrder[i];

				string label;
				if (t < 1.0 / 3.0)
					label = $"{dan} Low";
				else if (t < 2.0 / 3.0)
					label = $"{dan} Mid";
				else
					label = $"{dan} High";

				return (label, FormatDanNumeric(numeric));
			}
		}

		return ("? ? ? ? ?", "N/A");
	}

	/// <summary>
	/// Confidence within the current dan band (0 at edges, 1 at center third).
	/// </summary>
	public static double GetBandConfidence(double diff)
	{
		if (diff < _danBoundaries[0].Lower || diff >= _danBoundaries[^1].Upper)
			return 0;

		for (var i = 0; i < _danBoundaries.Length; i++)
		{
			var (lower, upper) = _danBoundaries[i];
			if (diff >= lower && diff < upper)
			{
				var span = upper - lower;
				var t = span > 0 ? (diff - lower) / span : 0.5;
				var distFromCenter = Math.Abs(t - 0.5);
				return Math.Max(0, 1.0 - distFromCenter * 2);
			}
		}

		return 0;
	}

	private static (double Lower, double Upper)[] PrecomputeDanBoundaries()
	{
		var means = DanielConstants.DanMeans;
		var boundaries = new (double Lower, double Upper)[means.Length];

		for (var i = 0; i < means.Length; i++)
		{
			var mean = means[i];
			var lower = i > 0
				? (means[i - 1] + mean) / 2.0
				: mean - ((means[1] + mean) / 2.0 - mean);
			var upper = i < means.Length - 1
				? (mean + means[i + 1]) / 2.0
				: mean + (mean - means[i - 1]) / 2.0;
			boundaries[i] = (lower, upper);
		}

		return boundaries;
	}

	/// <summary>
	/// Matches Python <c>str(round(value, 2))</c> (e.g. 14.5 not 14.50).
	/// </summary>
	private static string FormatDanNumeric(double value)
	{
		var rounded = Math.Round(value, 2);
		var text = rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
		if (!text.Contains('.'))
			text += ".0";
		return text;
	}
}
