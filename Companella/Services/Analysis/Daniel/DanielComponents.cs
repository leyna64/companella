// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Daniel strain component calculators (anchor, jacks, cross, stream, unevenness).
/// </summary>
internal static class DanielComponents
{
	internal static double[] ComputeAnchor(int keyCount, double[][] keyUsage400, double[] baseCorners)
	{
		var rowCount = baseCorners.Length;
		var result = new double[rowCount];

		for (var row = 0; row < rowCount; row++)
		{
			var counts = new double[keyCount];
			for (var k = 0; k < keyCount; k++)
				counts[k] = keyUsage400[k][row];

			Array.Sort(counts);
			Array.Reverse(counts);

			var nNz = 0;
			for (var k = 0; k < keyCount; k++)
			{
				if (counts[k] > 0)
					nNz++;
			}

			double walk = 0;
			double maxWalk = 0;

			for (var k = 0; k < keyCount - 1; k++)
			{
				var c0 = counts[k];
				var c1 = counts[k + 1];
				var pairValid = c0 > 0 && c1 > 0;
				if (!pairValid)
					continue;

				var safeC0 = c0 > 0 ? c0 : 1.0;
				var ratio = c0 > 0 ? c1 / safeC0 : 0.0;
				var weight = 1 - 4 * Math.Pow(0.5 - ratio, 2);
				walk += c0 * weight;
				maxWalk += c0;
			}

			var rawAnchor = nNz > 1 ? walk / Math.Max(maxWalk, 1e-9) : 0.0;
			result[row] = 1 + Math.Min(rawAnchor - 0.18, 5 * Math.Pow(rawAnchor - 0.22, 3));
		}

		return result;
	}

	internal static (Dictionary<int, double[]> DeltaKs, double[] Jbar) ComputeJbar(
		int keyCount,
		double x,
		IReadOnlyList<List<DanielNote>> noteSeqByColumn,
		double[] baseCorners)
	{
		var jKs = new Dictionary<int, double[]>();
		var deltaKs = new Dictionary<int, double[]>();

		for (var k = 0; k < keyCount; k++)
		{
			jKs[k] = new double[baseCorners.Length];
			deltaKs[k] = Enumerable.Repeat(1e9, baseCorners.Length).ToArray();
		}

		for (var k = 0; k < keyCount; k++)
		{
			var notes = noteSeqByColumn[k];
			if (notes.Count < 2)
				continue;

			for (var i = 0; i < notes.Count - 1; i++)
			{
				var start = (double)notes[i].Time;
				var end = (double)notes[i + 1].Time;
				var delta = 0.001 * (end - start);
				var val = Math.Pow(delta, -1) * Math.Pow(delta + 0.11 * Math.Pow(x, 0.25), -1) * JackNerfer(delta);

				var li = DanielArrayMath.SearchSorted(baseCorners, start, DanielArrayMath.SearchSide.Left);
				var ri = DanielArrayMath.SearchSorted(baseCorners, end, DanielArrayMath.SearchSide.Left);

				if (ri > li)
				{
					for (var idx = li; idx < ri; idx++)
					{
						jKs[k][idx] = val;
						deltaKs[k][idx] = delta;
					}
				}
			}
		}

		var jbarKs = new Dictionary<int, double[]>();
		for (var k = 0; k < keyCount; k++)
			jbarKs[k] = DanielArrayMath.SmoothOnCorners(baseCorners, jKs[k], 500, 0.001, "sum");

		var jbar = new double[baseCorners.Length];
		for (var i = 0; i < baseCorners.Length; i++)
		{
			double num = 0;
			double den = 0;

			for (var k = 0; k < keyCount; k++)
			{
				var weight = 1.0 / deltaKs[k][i];
				num += Math.Pow(Math.Max(jbarKs[k][i], 0), 5.00) * weight;
				den += weight;
			}

			jbar[i] = Math.Pow(num / Math.Max(den, 1e-9), 0.2);
		}

		return (deltaKs, jbar);
	}

	internal static double[] ComputeXbar(
		int keyCount,
		double x,
		IReadOnlyList<List<DanielNote>> noteSeqByColumn,
		List<int>[] activeColumns,
		double[] baseCorners)
	{
		var crossCoeff = DanielConstants.CrossMatrix[keyCount];
		var xKs = new Dictionary<int, double[]>();
		var fastCross = new Dictionary<int, double[]>();

		for (var k = 0; k <= keyCount; k++)
		{
			xKs[k] = new double[baseCorners.Length];
			fastCross[k] = new double[baseCorners.Length];
		}

		for (var k = 0; k <= keyCount; k++)
		{
			List<DanielNote> notesInPair;
			if (k == 0)
				notesInPair = noteSeqByColumn[0];
			else if (k == keyCount)
				notesInPair = noteSeqByColumn[keyCount - 1];
			else
				notesInPair = noteSeqByColumn[k - 1]
					.Concat(noteSeqByColumn[k])
					.OrderBy(n => n.Time)
					.ToList();

			for (var i = 1; i < notesInPair.Count; i++)
			{
				var start = notesInPair[i - 1].Time;
				var end = notesInPair[i].Time;
				var li = DanielArrayMath.SearchSorted(baseCorners, start, DanielArrayMath.SearchSide.Left);
				var ri = DanielArrayMath.SearchSorted(baseCorners, end, DanielArrayMath.SearchSide.Left);

				if (ri <= li)
					continue;

				var delta = 0.001 * (notesInPair[i].Time - notesInPair[i - 1].Time);
				var val = 0.16 * Math.Pow(Math.Max(x, delta), -2);

				var leftInactive = !activeColumns[li].Contains(k - 1) && !activeColumns[ri].Contains(k - 1);
				var rightInactive = !activeColumns[li].Contains(k) && !activeColumns[ri].Contains(k);

				if (leftInactive || rightInactive)
					val *= 1 - crossCoeff[k];

				var fastVal = Math.Max(0, 0.4 * Math.Pow(Math.Max(delta, Math.Max(0.06, 0.75 * x)), -2) - 80);

				for (var idx = li; idx < ri; idx++)
				{
					xKs[k][idx] = val;
					fastCross[k][idx] = fastVal;
				}
			}
		}

		var xBase = new double[baseCorners.Length];
		for (var i = 0; i < baseCorners.Length; i++)
		{
			double sum = 0;
			for (var k = 0; k <= keyCount; k++)
				sum += xKs[k][i] * crossCoeff[k];

			for (var k = 0; k < keyCount; k++)
				sum += Math.Sqrt(fastCross[k][i] * crossCoeff[k] * fastCross[k + 1][i] * crossCoeff[k + 1]);

			xBase[i] = sum;
		}

		return DanielArrayMath.SmoothOnCorners(baseCorners, xBase, 500, 0.001, "sum");
	}

	internal static double[] ComputePbar(
		double x,
		IReadOnlyList<DanielNote> noteSeq,
		double[] anchor,
		double[] baseCorners)
	{
		var pStep = new double[baseCorners.Length];

		for (var i = 0; i < noteSeq.Count - 1; i++)
		{
			var hL = noteSeq[i].Time;
			var hR = noteSeq[i + 1].Time;
			var deltaTime = hR - hL;

			if (deltaTime < 1e-9)
			{
				var spike = 1000 * Math.Pow(0.02 * (4 / x - 24), 0.25);
				var li = DanielArrayMath.SearchSorted(baseCorners, hL, DanielArrayMath.SearchSide.Left);
				var ri = DanielArrayMath.SearchSorted(baseCorners, hL, DanielArrayMath.SearchSide.Right);

				if (ri > li)
				{
					for (var idx = li; idx < ri; idx++)
						pStep[idx] += spike;
				}

				continue;
			}

			var liSeg = DanielArrayMath.SearchSorted(baseCorners, hL, DanielArrayMath.SearchSide.Left);
			var riSeg = DanielArrayMath.SearchSorted(baseCorners, hR, DanielArrayMath.SearchSide.Left);

			if (riSeg <= liSeg)
				continue;

			var delta = 0.001 * deltaTime;
			var bVal = StreamBooster(delta);
			var baseInc = Math.Pow(0.08 * Math.Pow(x, -1) * (1 - 24 * Math.Pow(x, -1) * Math.Pow(x / 6, 2)), 0.25);

			double inc;
			if (delta < 2 * x / 3)
				inc = Math.Pow(delta, -1) * Math.Pow(0.08 * Math.Pow(x, -1) * (1 - 24 * Math.Pow(x, -1) * Math.Pow(delta - x / 2, 2)), 0.25) * Math.Max(bVal, 1);
			else
				inc = Math.Pow(delta, -1) * baseInc * Math.Max(bVal, 1);

			for (var idx = liSeg; idx < riSeg; idx++)
				pStep[idx] += Math.Min(inc * anchor[idx], Math.Max(inc, inc * 2 - 10));
		}

		return DanielArrayMath.SmoothOnCorners(baseCorners, pStep, 500, 0.001, "sum");
	}

	internal static double[] ComputeAbar(
		int keyCount,
		List<int>[] activeColumns,
		Dictionary<int, double[]> deltaKs,
		double[] aCorners,
		double[] baseCorners)
	{
		var dks = new Dictionary<int, double[]>();
		for (var k = 0; k < keyCount - 1; k++)
			dks[k] = new double[baseCorners.Length];

		for (var i = 0; i < baseCorners.Length; i++)
		{
			var cols = activeColumns[i];
			for (var j = 0; j < cols.Count - 1; j++)
			{
				var k0 = cols[j];
				var k1 = cols[j + 1];
				dks[k0][i] = Math.Abs(deltaKs[k0][i] - deltaKs[k1][i]) +
							 0.4 * Math.Max(0, Math.Max(deltaKs[k0][i], deltaKs[k1][i]) - 0.11);
			}
		}

		var aStep = Enumerable.Repeat(1.0, aCorners.Length).ToArray();

		for (var i = 0; i < aCorners.Length; i++)
		{
			var idx = DanielArrayMath.ClipInt(
				DanielArrayMath.SearchSorted(baseCorners, aCorners[i]),
				0,
				baseCorners.Length - 1);

			var cols = activeColumns[idx];
			for (var j = 0; j < cols.Count - 1; j++)
			{
				var k0 = cols[j];
				var k1 = cols[j + 1];
				var dVal = dks[k0][idx];
				var dk0 = deltaKs[k0][idx];
				var dk1 = deltaKs[k1][idx];

				if (dVal < 0.02)
					aStep[i] *= Math.Min(0.75 + 0.5 * Math.Max(dk0, dk1), 1);
				else if (dVal < 0.07)
					aStep[i] *= Math.Min(0.65 + 5 * dVal + 0.5 * Math.Max(dk0, dk1), 1);
			}
		}

		return DanielArrayMath.SmoothOnCorners(aCorners, aStep, 250, mode: "avg");
	}

	internal static (double[] CStep, double[] KsStep) ComputeCAndKs(
		int keyCount,
		bool[][] keyUsage,
		IReadOnlyList<DanielNote> noteSeq,
		double[] baseCorners)
	{
		var noteHitTimes = noteSeq.Select(n => (double)n.Time).OrderBy(t => t).ToArray();
		var cStep = new double[baseCorners.Length];
		var ksStep = new double[baseCorners.Length];

		for (var i = 0; i < baseCorners.Length; i++)
		{
			var lo = DanielArrayMath.SearchSorted(noteHitTimes, baseCorners[i] - 500, DanielArrayMath.SearchSide.Left);
			var hi = DanielArrayMath.SearchSorted(noteHitTimes, baseCorners[i] + 500, DanielArrayMath.SearchSide.Left);
			cStep[i] = hi - lo;

			var keySum = 0;
			for (var k = 0; k < keyCount; k++)
			{
				if (keyUsage[k][i])
					keySum++;
			}

			ksStep[i] = Math.Max(keySum, 1);
		}

		return (cStep, ksStep);
	}

	internal static Dictionary<string, double> FactorAverages(double[] times, Dictionary<string, double[]> factors)
	{
		var names = factors.Keys.ToList();
		var result = new Dictionary<string, double>();
		var duration = times[^1] - times[0];

		if (duration <= 0)
		{
			foreach (var name in names)
				result[name] = 0;
			return result;
		}

		for (var f = 0; f < names.Count; f++)
		{
			var values = factors[names[f]];
			double integral = 0;
			for (var i = 1; i < times.Length; i++)
				integral += 0.5 * (values[i] + values[i - 1]) * (times[i] - times[i - 1]);

			result[names[f]] = integral / duration;
		}

		return result;
	}

	private static double JackNerfer(double delta)
	{
		return 1 - 7e-5 * Math.Pow(0.15 + Math.Abs(delta - 0.08), -4);
	}

	private static double StreamBooster(double delta)
	{
		var bpm = DanielArrayMath.Clip(7.5 / delta, 0, 420);
		var primary = 0.10 / (1 + Math.Exp(-0.06 * (bpm - 175)));
		var secondary = bpm is >= 200 and <= 350
			? 0.30 * (1 - Math.Exp(-0.02 * (bpm - 200)))
			: 0.0;
		return 1 + primary + secondary;
	}
}
