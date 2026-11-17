// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using Companella.Services.Common;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Assembles strain curve and computes Daniel star rating (SR).
/// </summary>
internal static class DanielStarRatingCalculator
{
	/// <summary>
	/// Minimum gameplay duration for pass 1 extension (maps shorter than this are reflected then cropped).
	/// </summary>
	internal const int Pass1TargetDurationMs = DanielPass1NoteBuilder.TargetDurationMs;

	internal sealed class RatingResult
	{
		public required double BaseStarRating { get; init; }
		public required double Pass1StarRating { get; init; }
		public required int OriginalDurationMs { get; init; }
		public required double[] AllCorners { get; init; }
		public required Dictionary<string, double[]> Factors { get; init; }
	}

	private readonly record struct PassComputationResult(
		double[] AllCorners,
		double[] DAll,
		Dictionary<string, double[]> Factors);

	/// <summary>
	/// Pass 0 Daniel SR (original map) and pass 1 min-duration reflected SR for duration-weighted eval.
	/// </summary>
	internal static RatingResult Calculate(DanielPreprocessResult preprocess)
	{
		var pass1Build = DanielPass1NoteBuilder.Build(preprocess.NoteSeq);
		var (baseSr, pass0) = ComputeFullMapPassStarRating(preprocess);
		LogPass(0, preprocess.NoteSeq.Count, preprocess.T, baseSr, pass1Build.OriginalDurationMs);
		double pass1Sr;
		if (!pass1Build.Changed)
		{
			pass1Sr = baseSr;
			LogPass(
				1,
				preprocess.NoteSeq.Count,
				preprocess.T,
				pass1Sr,
				changed: false,
				originalDurationMs: pass1Build.OriginalDurationMs,
				extendedDurationMs: pass1Build.ExtendedDurationMs,
				trimEachSideMs: 0);
		}
		else
		{
			var pass1Preprocess = DanielPreprocessor.PreprocessPass(pass1Build.Notes, preprocess, pass1Build.MapEndTime);
			pass1Sr = ComputeFullMapPassStarRating(pass1Preprocess).Sr;
			LogPass(
				1,
				pass1Build.Notes.Length,
				pass1Build.MapEndTime,
				pass1Sr,
				changed: true,
				originalDurationMs: pass1Build.OriginalDurationMs,
				extendedDurationMs: pass1Build.ExtendedDurationMs,
				trimEachSideMs: pass1Build.TrimEachSideMs);
		}

		Logger.Debug(
			$"[DanielPass] summary baseSR={baseSr:F4} pass1SR={pass1Sr:F4} evalSR={DanielEvalTransform.Apply(baseSr, pass1Sr, pass1Build.OriginalDurationMs):F4}");

		return new RatingResult
		{
			BaseStarRating = baseSr,
			Pass1StarRating = pass1Sr,
			OriginalDurationMs = pass1Build.OriginalDurationMs,
			AllCorners = pass0.AllCorners,
			Factors = pass0.Factors
		};
	}

	private static (double Sr, PassComputationResult Pass) ComputeFullMapPassStarRating(DanielPreprocessResult preprocess)
	{
		var pass = ComputePass(preprocess);
		var cArr = BuildOriginalCornerWeights(preprocess, pass.AllCorners);
		var sr = AggregateStarRating(
			pass.DAll,
			pass.AllCorners,
			cArr,
			preprocess.NoteSeq.Count);
		return (sr, pass);
	}

	private static void LogPass(int pass, int noteCount, int mapEndTime, double sr, int originalDurationMs)
	{
		Logger.Debug(
			$"[DanielPass] pass=0 durationMs={originalDurationMs} notes={noteCount} T={mapEndTime} SR={sr:F4}");
	}

	private static void LogPass(
		int pass,
		int noteCount,
		int mapEndTime,
		double sr,
		bool changed,
		int originalDurationMs,
		int extendedDurationMs,
		int trimEachSideMs)
	{
		Logger.Debug(
			$"[DanielPass] pass=1 changed={changed} origDurationMs={originalDurationMs} extendedDurationMs={extendedDurationMs} trimEachSideMs={trimEachSideMs} notes={noteCount} T={mapEndTime} SR={sr:F4}");
	}

	private static double[] BuildOriginalCornerWeights(DanielPreprocessResult preprocess, double[] originalAllCorners)
	{
		var grids = DanielCornerBuilder.GetCorners(preprocess.T, preprocess.NoteSeq);
		var keyUsage = DanielCornerBuilder.GetKeyUsage(
			preprocess.KeyCount,
			preprocess.T,
			preprocess.NoteSeq,
			grids.BaseCorners);
		var (cStep, _) = DanielComponents.ComputeCAndKs(
			preprocess.KeyCount,
			keyUsage,
			preprocess.NoteSeq,
			grids.BaseCorners);
		return DanielArrayMath.StepInterp(originalAllCorners, grids.BaseCorners, cStep);
	}

	private static PassComputationResult ComputePass(DanielPreprocessResult preprocess)
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

		var factors = new Dictionary<string, double[]>
		{
			["Pressing Intensity"] = pbar,
			["Unevenness"] = abar,
			["Same-Column Pressure"] = jbar,
			["Cross-Column Pressure"] = xbar
		};

		return new PassComputationResult(allCorners, dAll, factors);
	}

	private static double AggregateStarRating(
		double[] dAll,
		double[] allCorners,
		double[] cArr,
		int totalNotes)
	{
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
		var weightTotal = wSorted.Sum();

		var chunks = DetectDifficultyChunks(dAll);

		var weightedSumPass2 = 0.0;
		for (var i = 0; i < dSorted.Length; i++)
		{
			var cornerIdx = sortedIndices[i];
			weightedSumPass2 += Math.Pow(dSorted[i], 5.0) * wSorted[i];
		}

		var weightedMeanPass2 = Math.Pow(weightedSumPass2 / weightTotal, 0.2);

		var starRating = 0.88 * percentile93 * 0.25 + 0.94 * percentile83 * 0.2 + weightedMeanPass2 * 0.55;
		starRating *= totalNotes / (double)(totalNotes + 60);
		return DanielArrayMath.RescaleHigh(starRating) * 0.975;
	}

	private static List<(int Start, int End)> DetectDifficultyChunks(double[] strain)
	{
		if (strain.Length == 0)
			return [];

		if (strain.Length == 1)
			return [(0, 0)];

		const double shiftThreshold = 0.2;
		const int minChunkLength = 2;

		var smoothed = SmoothForSegmentation(strain, 2);
		var chunks = new List<(int Start, int End)>();
		var chunkStart = 0;
		var priorMean = smoothed[0];

		for (var i = 1; i < smoothed.Length; i++)
		{
			var chunkLength = i - chunkStart + 1;
			var chunkMean = 0.0;
			for (var j = chunkStart; j <= i; j++)
				chunkMean += smoothed[j];
			chunkMean /= chunkLength;

			var relativeShift = Math.Abs(chunkMean - priorMean) / Math.Max(priorMean, 1e-9);
			if (relativeShift >= shiftThreshold && chunkLength >= minChunkLength)
			{
				chunks.Add((chunkStart, i - 1));

				var closedMean = 0.0;
				for (var j = chunkStart; j < i; j++)
					closedMean += smoothed[j];
				closedMean /= i - chunkStart;

				chunkStart = i;
				priorMean = closedMean;
			}
		}

		chunks.Add((chunkStart, smoothed.Length - 1));
		return chunks;
	}

	private static double[] BuildChunkExponents(
		List<(int Start, int End)> chunks,
		double[] times)
	{
		var exponents = new double[times.Length];
		if (chunks.Count == 0)
			return exponents;

		var mapDuration = Math.Max(times[^1] - times[0], 1e-9);
		var minRelativeLength = 1.0 / Math.Max(times.Length - 1, 1);

		var chunkLengths = chunks
			.Select(chunk => Math.Max(
				(times[chunk.End] - times[chunk.Start]) / mapDuration,
				minRelativeLength))
			.ToArray();
		var referenceLength = Median(chunkLengths);

		foreach (var chunk in chunks)
		{
			var length = Math.Max(
				(times[chunk.End] - times[chunk.Start]) / mapDuration,
				minRelativeLength);
			var exponent = ChunkExponentFromRelativeLength(length, referenceLength, minRelativeLength);
			for (var i = chunk.Start; i <= chunk.End; i++)
				exponents[i] = exponent;
		}

		return exponents;
	}

	private static double ChunkExponentFromRelativeLength(
		double lengthFraction,
		double referenceFraction,
		double minScale)
	{
		const double lowExponent = 5.0;
		const double highExponent = 5.0;
		var scale = Math.Max(referenceFraction * 0.25, minScale);
		var sigmoid = 1.0 / (1.0 + Math.Exp(-(lengthFraction - referenceFraction) / scale));
		return highExponent - (highExponent - lowExponent) * sigmoid;
	}

	private static double[] SmoothForSegmentation(double[] values, int radius)
	{
		var result = new double[values.Length];
		for (var i = 0; i < values.Length; i++)
		{
			var lo = Math.Max(0, i - radius);
			var hi = Math.Min(values.Length - 1, i + radius);
			var sum = 0.0;
			for (var j = lo; j <= hi; j++)
				sum += values[j];
			result[i] = sum / (hi - lo + 1);
		}

		return result;
	}

	private static double Median(double[] values)
	{
		if (values.Length == 0)
			return 0.0;

		var sorted = values.OrderBy(v => v).ToArray();
		var mid = sorted.Length / 2;
		return sorted.Length % 2 == 0
			? (sorted[mid - 1] + sorted[mid]) / 2.0
			: sorted[mid];
	}
}
