// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using System.Globalization;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Parses .osu files using the same rules as Daniel's osu_file_parser.py.
/// </summary>
internal static class DanielOsuFileParser
{
	internal static (int KeyCount, List<DanielNote> Notes) ParseFile(string filePath, float rate = 1.0f)
	{
		var keyCount = -1;
		var notes = new List<DanielNote>();
		var inHitObjects = false;

		foreach (var rawLine in File.ReadLines(filePath))
		{
			var line = rawLine;

			if (!inHitObjects)
			{
				var parsedKeyCount = TryReadColumnCount(line);
				if (parsedKeyCount >= 0)
					keyCount = parsedKeyCount;

				if (line.Trim() == "[HitObjects]")
					inHitObjects = true;

				continue;
			}

			if (string.IsNullOrWhiteSpace(line))
				continue;

			var parts = line.Split(',');
			if (parts.Length < 5)
				continue;

			var columnWidth = 512 / keyCount;
			var column = (int)double.Parse(parts[0], CultureInfo.InvariantCulture) / columnWidth;
			var time = ApplyRate(int.Parse(parts[2], CultureInfo.InvariantCulture), rate);
			notes.Add(new DanielNote(column, time));
		}

		if (keyCount < 0)
			throw new InvalidOperationException("CircleSize not found in beatmap.");

		notes.Sort((a, b) =>
		{
			var timeCompare = a.Time.CompareTo(b.Time);
			return timeCompare != 0 ? timeCompare : a.Column.CompareTo(b.Column);
		});

		return (keyCount, notes);
	}

	private static int TryReadColumnCount(string line)
	{
		if (!line.Contains("CircleSize:", StringComparison.Ordinal))
			return -1;

		var trimmed = line.Trim();
		var val = trimmed[^1];
		if (val == '0')
			return 10;

		return int.Parse(val.ToString(), CultureInfo.InvariantCulture);
	}

	private static int ApplyRate(int timeMs, float rate)
	{
		if (Math.Abs(rate - 1.5f) < 0.01f)
			return (int)Math.Floor(timeMs * 2.0 / 3.0);

		if (Math.Abs(rate - 0.75f) < 0.01f)
			return (int)Math.Floor(timeMs * 4.0 / 3.0);

		if (Math.Abs(rate - 1.0f) < 0.0001f)
			return timeMs;

		return (int)Math.Floor(timeMs / rate);
	}
}
