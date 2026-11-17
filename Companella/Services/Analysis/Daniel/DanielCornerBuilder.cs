// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Corner grid construction for Daniel strain calculation.
/// </summary>
internal static class DanielCornerBuilder
{
	internal sealed class CornerGrids
	{
		public required double[] AllCorners { get; init; }
		public required double[] BaseCorners { get; init; }
		public required double[] ACorners { get; init; }
	}

	internal static CornerGrids GetCorners(int t, IReadOnlyList<DanielNote> noteSeq)
	{
		var cornersBase = new HashSet<int>();
		foreach (var note in noteSeq)
		{
			var h = note.Time;
			cornersBase.Add(h);
			cornersBase.Add(h + 501);
			cornersBase.Add(h - 499);
			cornersBase.Add(h + 1);
		}

		cornersBase.Add(0);
		cornersBase.Add(t);

		var cornersA = new HashSet<int>();
		foreach (var note in noteSeq)
		{
			var h = note.Time;
			cornersA.Add(h);
			cornersA.Add(h + 1000);
			cornersA.Add(h - 1000);
		}

		cornersA.Add(0);
		cornersA.Add(t);

		var baseCorners = cornersBase.Where(s => s >= 0 && s <= t).OrderBy(s => s).Select(s => (double)s).ToArray();
		var aCorners = cornersA.Where(s => s >= 0 && s <= t).OrderBy(s => s).Select(s => (double)s).ToArray();
		var allCorners = baseCorners.Union(aCorners).OrderBy(s => s).ToArray();

		return new CornerGrids
		{
			AllCorners = allCorners,
			BaseCorners = baseCorners,
			ACorners = aCorners
		};
	}

	internal static bool[][] GetKeyUsage(
		int keyCount,
		int t,
		IReadOnlyList<DanielNote> noteSeq,
		double[] baseCorners)
	{
		var keyUsage = new bool[keyCount][];
		for (var k = 0; k < keyCount; k++)
			keyUsage[k] = new bool[baseCorners.Length];

		foreach (var note in noteSeq)
		{
			var start = Math.Max(note.Time - 150, 0);
			var end = Math.Min(note.Time + 150, t - 1);
			var li = DanielArrayMath.SearchSorted(baseCorners, start, DanielArrayMath.SearchSide.Left);
			var ri = DanielArrayMath.SearchSorted(baseCorners, end, DanielArrayMath.SearchSide.Left);

			for (var i = li; i < ri; i++)
				keyUsage[note.Column][i] = true;
		}

		return keyUsage;
	}

	internal static double[][] GetKeyUsage400(
		int keyCount,
		IReadOnlyList<DanielNote> noteSeq,
		double[] baseCorners)
	{
		var keyUsage400 = new double[keyCount][];
		for (var k = 0; k < keyCount; k++)
			keyUsage400[k] = new double[baseCorners.Length];

		foreach (var note in noteSeq)
		{
			var start = Math.Max(note.Time, 0);
			var li = DanielArrayMath.SearchSorted(baseCorners, start - 400, DanielArrayMath.SearchSide.Left);
			var ri = DanielArrayMath.SearchSorted(baseCorners, start + 400, DanielArrayMath.SearchSide.Left);
			var mid = DanielArrayMath.SearchSorted(baseCorners, start, DanielArrayMath.SearchSide.Left);

			keyUsage400[note.Column][mid] += 3.75;

			for (var i = li; i < mid; i++)
				keyUsage400[note.Column][i] += 3.75 - 3.75 / 400.0 / 400.0 * Math.Pow(baseCorners[i] - start, 2);

			for (var i = mid + 1; i < ri; i++)
				keyUsage400[note.Column][i] += 3.75 - 3.75 / 400.0 / 400.0 * Math.Pow(baseCorners[i] - start, 2);
		}

		return keyUsage400;
	}
}
