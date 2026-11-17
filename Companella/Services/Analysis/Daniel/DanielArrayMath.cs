// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Numpy-equivalent array helpers for the Daniel algorithm.
/// </summary>
internal static class DanielArrayMath
{
	internal enum SearchSide
	{
		Left,
		Right
	}

	internal static double[] CumulativeSum(double[] x, double[] f)
	{
		var result = new double[x.Length];
		for (var i = 1; i < x.Length; i++)
			result[i] = result[i - 1] + f[i - 1] * (x[i] - x[i - 1]);
		return result;
	}

	internal static double[] SmoothOnCorners(
		double[] x,
		double[] f,
		double window,
		double scale = 1.0,
		string mode = "sum")
	{
		var fIntegral = CumulativeSum(x, f);
		var result = new double[x.Length];
		var x0 = x[0];
		var xLast = x[^1];

		for (var i = 0; i < x.Length; i++)
		{
			var a = Clip(x[i] - window, x0, xLast);
			var b = Clip(x[i] + window, x0, xLast);
			var val = QueryIntegral(x, f, fIntegral, b) - QueryIntegral(x, f, fIntegral, a);

			if (mode == "avg")
			{
				var span = b - a;
				result[i] = span > 0 ? val / span : 0.0;
			}
			else
			{
				result[i] = scale * val;
			}
		}

		return result;
	}

	private static double QueryIntegral(double[] x, double[] f, double[] fIntegral, double q)
	{
		var idx = SearchSorted(x, q, SearchSide.Left) - 1;
		idx = ClipInt(idx, 0, x.Length - 2);
		return fIntegral[idx] + f[idx] * (q - x[idx]);
	}

	internal static double[] InterpValues(double[] newX, double[] oldX, double[] oldVals)
	{
		var result = new double[newX.Length];
		for (var i = 0; i < newX.Length; i++)
			result[i] = InterpScalar(newX[i], oldX, oldVals);
		return result;
	}

	internal static double[] StepInterp(double[] newX, double[] oldX, double[] oldVals)
	{
		var result = new double[newX.Length];
		for (var i = 0; i < newX.Length; i++)
		{
			var idx = SearchSorted(oldX, newX[i], SearchSide.Right) - 1;
			idx = ClipInt(idx, 0, oldVals.Length - 1);
			result[i] = oldVals[idx];
		}

		return result;
	}

	internal static double InterpScalar(double q, double[] oldX, double[] oldVals)
	{
		if (oldX.Length == 0)
			return 0;

		if (q <= oldX[0])
			return oldVals[0];

		if (q >= oldX[^1])
			return oldVals[^1];

		var idx = SearchSorted(oldX, q, SearchSide.Right) - 1;
		idx = Math.Max(0, Math.Min(idx, oldX.Length - 2));
		var x0 = oldX[idx];
		var x1 = oldX[idx + 1];
		var y0 = oldVals[idx];
		var y1 = oldVals[idx + 1];
		var t = (q - x0) / (x1 - x0);
		return y0 + t * (y1 - y0);
	}

	internal static int SearchSorted(double[] arr, double value, SearchSide side = SearchSide.Left)
	{
		if (side == SearchSide.Left)
		{
			var lo = 0;
			var hi = arr.Length;
			while (lo < hi)
			{
				var mid = (lo + hi) / 2;
				if (arr[mid] < value)
					lo = mid + 1;
				else
					hi = mid;
			}

			return lo;
		}

		var loR = 0;
		var hiR = arr.Length;
		while (loR < hiR)
		{
			var mid = (loR + hiR) / 2;
			if (arr[mid] <= value)
				loR = mid + 1;
			else
				hiR = mid;
		}

		return loR;
	}

	internal static int[] Argsort(double[] values)
	{
		var indices = Enumerable.Range(0, values.Length).ToArray();
		Array.Sort(indices, (a, b) => values[a].CompareTo(values[b]));
		return indices;
	}

	internal static double[] Cumsum(double[] values)
	{
		var result = new double[values.Length];
		for (var i = 0; i < values.Length; i++)
			result[i] = (i == 0 ? 0 : result[i - 1]) + values[i];
		return result;
	}

	internal static double Clip(double value, double min, double max)
	{
		if (value < min)
			return min;
		if (value > max)
			return max;
		return value;
	}

	internal static int ClipInt(int value, int min, int max)
	{
		if (value < min)
			return min;
		if (value > max)
			return max;
		return value;
	}

	internal static double RescaleHigh(double sr)
	{
		if (sr <= 9)
			return sr;
		return 9 + (sr - 9) / 1.2;
	}
}
