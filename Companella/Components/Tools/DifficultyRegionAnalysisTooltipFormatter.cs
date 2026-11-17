using Companella.Models.Application;
using Companella.Models.Beatmap;
using System.Globalization;
using System.Text;

namespace Companella.Components.Tools;

/// <summary>
/// Formats region-scoped DAN and Interlude pattern analysis for tooltips.
/// </summary>
public static class DifficultyRegionAnalysisTooltipFormatter
{
	private static readonly PatternType[] _hiddenPatternTypes =
	{
		PatternType.Jump,
		PatternType.Quad,
		PatternType.Hand
	};

	public static string Format(DifficultyRegionPair pair, DifficultyRegionAnalysisResult? result)
	{
		var builder = new StringBuilder();
		builder.AppendLine(pair.VersionName);

		if (result == null)
		{
			builder.Append("Analyzing...");
			return builder.ToString().TrimEnd();
		}

		if (!result.IsValid)
		{
			builder.Append(result.ErrorMessage ?? "Invalid region");
			return builder.ToString().TrimEnd();
		}

		builder.AppendLine(CultureInfo.InvariantCulture, $"{result.NoteCount} notes");

		if (result.Dan != null)
		{
			builder.Append(CultureInfo.InvariantCulture, $"Dan: {result.Dan.DisplayName}");
			if (result.Dan.UsedDanielCalculator && result.Dan.DanielStarRating.HasValue)
				builder.Append(CultureInfo.InvariantCulture, $" (SR {result.Dan.DanielStarRating.Value:F2})");
			else if (result.Dan.RawModelOutput.HasValue)
				builder.Append(CultureInfo.InvariantCulture, $" (raw {result.Dan.RawModelOutput.Value:F2})");
			builder.AppendLine();
		}
		else
		{
			builder.AppendLine("Dan: unavailable");
		}

		if (result.Patterns != null)
		{
			var category = result.Patterns.GetInterludeCategoryDisplay()?.Trim();
			if (!string.IsNullOrWhiteSpace(category))
				builder.AppendLine(category);

			var topPatterns = result.Patterns.GetTopPatterns()
				.Where(p => !_hiddenPatternTypes.Contains(p.Type))
				.Take(5)
				.ToList();

			var visibleTotal = topPatterns.Sum(p => p.Percentage);
			if (visibleTotal > 0)
			{
				foreach (var pattern in topPatterns)
				{
					var share = pattern.Percentage / visibleTotal * 100.0;
					builder.AppendLine(CultureInfo.InvariantCulture, $"{pattern.CompactDisplay} {share:F0}%");
				}
			}
		}

		return builder.ToString().TrimEnd();
	}
}
