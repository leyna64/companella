// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Debug snapshot for parity checks against Python Daniel.
/// Daniel SR/dan does not use MSD; this is algorithm-only diagnostics.
/// </summary>
public static class DanielParityDiagnostics
{
	public sealed class Snapshot
	{
		public int NoteCount { get; init; }
		public int BaseCornerCount { get; init; }
		public int AllCornerCount { get; init; }
		public double Percentile93 { get; init; }
		public double Percentile83 { get; init; }
		public double WeightedMean { get; init; }
		public double StarRating { get; init; }
		public string Indices { get; init; } = string.Empty;
	}

	public static Snapshot FromFile(string filePath, float rate = 1.0f)
	{
		var (keyCount, notes) = DanielOsuFileParser.ParseFile(filePath, rate);
		var preprocess = DanielPreprocessor.Preprocess(notes, keyCount);
		return FromPreprocess(preprocess);
	}

	internal static Snapshot FromPreprocess(DanielPreprocessResult preprocess)
	{
		var noteSeq = preprocess.NoteSeq;
		var keyCount = preprocess.KeyCount;
		var x = preprocess.X;
		var t = preprocess.T;

		var grids = DanielCornerBuilder.GetCorners(t, noteSeq);
		var baseCorners = grids.BaseCorners;
		var allCorners = grids.AllCorners;
		var aCorners = grids.ACorners;

		var keyUsage = DanielCornerBuilder.GetKeyUsage(keyCount, t, noteSeq, baseCorners);
		var activeColumns = new List<int>[baseCorners.Length];
		for (var i = 0; i < baseCorners.Length; i++)
		{
			var cols = new List<int>();
			for (var k = 0; k < keyCount; k++)
			{
				if (keyUsage[k][i])
					cols.Add(k);
			}

			activeColumns[i] = cols;
		}

		var keyUsage400 = DanielCornerBuilder.GetKeyUsage400(keyCount, noteSeq, baseCorners);
		var anchor = DanielComponents.ComputeAnchor(keyCount, keyUsage400, baseCorners);
		var (deltaKs, jbarBase) = DanielComponents.ComputeJbar(keyCount, x, preprocess.NoteSeqByColumn, baseCorners);
		var jbar = DanielArrayMath.InterpValues(allCorners, baseCorners, jbarBase);
		var xbarBase = DanielComponents.ComputeXbar(keyCount, x, preprocess.NoteSeqByColumn, activeColumns, baseCorners);
		var xbar = DanielArrayMath.InterpValues(allCorners, baseCorners, xbarBase);
		var pbarBase = DanielComponents.ComputePbar(x, noteSeq, anchor, baseCorners);
		var pbar = DanielArrayMath.InterpValues(allCorners, baseCorners, pbarBase);
		var abarBase = DanielComponents.ComputeAbar(keyCount, activeColumns, deltaKs, aCorners, baseCorners);
		var abar = DanielArrayMath.InterpValues(allCorners, aCorners, abarBase);
		var (cStep, ksStep) = DanielComponents.ComputeCAndKs(keyCount, keyUsage, noteSeq, baseCorners);
		var cArr = DanielArrayMath.StepInterp(allCorners, baseCorners, cStep);
		var ksArr = DanielArrayMath.StepInterp(allCorners, baseCorners, ksStep);

		var dAll = new double[allCorners.Length];
		for (var i = 0; i < allCorners.Length; i++)
		{
			var sAll = Math.Pow(
				0.4 * Math.Pow(Math.Pow(abar[i], 3.0 / ksArr[i]) * Math.Min(jbar[i], 8 + 0.85 * jbar[i]), 1.5) +
				0.6 * Math.Pow(Math.Pow(abar[i], 2.0 / 3.0) * (0.8 * pbar[i]), 1.5),
				2.0 / 3.0);

			var tAll = Math.Pow(abar[i], 3.0 / ksArr[i]) * xbar[i] / (xbar[i] + sAll + 1);
			dAll[i] = 2.7 * Math.Pow(sAll, 0.5) * Math.Pow(tAll, 1.5) + sAll * 0.27;
		}

		var gaps = new double[allCorners.Length];
		if (allCorners.Length > 1)
		{
			gaps[0] = (allCorners[1] - allCorners[0]) / 2.0;
			gaps[^1] = (allCorners[^1] - allCorners[^2]) / 2.0;
			for (var i = 1; i < allCorners.Length - 1; i++)
				gaps[i] = (allCorners[i + 1] - allCorners[i - 1]) / 2.0;
		}

		var effectiveWeights = new double[allCorners.Length];
		for (var i = 0; i < allCorners.Length; i++)
			effectiveWeights[i] = cArr[i] * gaps[i];

		var sortedIndices = DanielArrayMath.Argsort(dAll);
		var dSorted = sortedIndices.Select(idx => dAll[idx]).ToArray();
		var wSorted = sortedIndices.Select(idx => effectiveWeights[idx]).ToArray();
		var cumWeights = DanielArrayMath.Cumsum(wSorted);
		var normCumWeights = cumWeights.Select(w => w / cumWeights[^1]).ToArray();
		var targetPercentiles = new[] { 0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815 };
		var indices = targetPercentiles
			.Select(p => DanielArrayMath.SearchSorted(normCumWeights, p, DanielArrayMath.SearchSide.Left))
			.ToArray();

		var percentile93 = indices.Take(4).Select(idx => dSorted[idx]).Average();
		var percentile83 = indices.Skip(4).Take(4).Select(idx => dSorted[idx]).Average();

		var weightedSum = 0.0;
		var weightTotal = 0.0;
		for (var i = 0; i < dSorted.Length; i++)
		{
			weightedSum += Math.Pow(dSorted[i], 5) * wSorted[i];
			weightTotal += wSorted[i];
		}

		var weightedMean = Math.Pow(weightedSum / weightTotal, 0.2);
		var sr = 0.88 * percentile93 * 0.25 + 0.94 * percentile83 * 0.2 + weightedMean * 0.55;
		sr *= noteSeq.Count / (double)(noteSeq.Count + 60);
		sr = DanielArrayMath.RescaleHigh(sr) * 0.975;

		return new Snapshot
		{
			NoteCount = noteSeq.Count,
			BaseCornerCount = baseCorners.Length,
			AllCornerCount = allCorners.Length,
			Percentile93 = percentile93,
			Percentile83 = percentile83,
			WeightedMean = weightedMean,
			StarRating = sr,
			Indices = string.Join(' ', indices)
		};
	}
}
