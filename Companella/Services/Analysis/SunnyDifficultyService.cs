using System.Diagnostics.CodeAnalysis;
using Companella.Models.Beatmap;
using Companella.Services.Beatmap;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for calculating Sunny difficulty rating from chart data.
/// </summary>
[SuppressMessage("", "CA1310")]
public class SunnyDifficultyService
{
	/// <summary>
	/// Calculates Sunny difficulty from an OsuFile.
	/// </summary>
	public static double CalculateDifficulty(OsuFile osuFile, float rate = 1.0f)
	{
		if (osuFile == null)
			return -1.0;

		var hitObjects = ParseHitObjects(osuFile);
		if (hitObjects.Count == 0)
			return -1.0;

		return CalculateDifficulty(hitObjects, osuFile, rate);
	}

	/// <summary>
	/// Calculates Sunny difficulty from pre-parsed hit objects.
	/// </summary>
	public static double CalculateDifficulty(List<HitObject> hitObjects, OsuFile osuFile, float rate = 1.0f)
	{
		if (hitObjects == null || hitObjects.Count == 0 || osuFile == null)
			return -1.0;

		var mod = GetModFromRate(rate);
		return Calculate(hitObjects, (int)osuFile.CircleSize, osuFile.OverallDifficulty, mod);
	}

	private static string GetModFromRate(float rate)
	{
		if (Math.Abs(rate - 1.5f) < 0.01f)
			return "DT";
		if (Math.Abs(rate - 0.75f) < 0.01f)
			return "HT";
		return "NM";
	}

	private static List<HitObject> ParseHitObjects(OsuFile osuFile)
	{
		var hitObjects = new List<HitObject>();

		if (osuFile.RawSections.TryGetValue("HitObjects", out var hitObjectLines))
		{
			var keyCount = (int)osuFile.CircleSize;
			foreach (var line in hitObjectLines)
			{
				if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("//"))
					continue;
				var hitObj = HitObject.Parse(line, keyCount);
				if (hitObj != null)
					hitObjects.Add(hitObj);
			}
		}
		else
		{
			var fullOsuFile = OsuFileParser.Parse(osuFile.FilePath);

			if (fullOsuFile.RawSections.TryGetValue("HitObjects", out hitObjectLines))
			{
				var keyCount = (int)osuFile.CircleSize;
				foreach (var line in hitObjectLines)
				{
					if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("//"))
						continue;
					var hitObj = HitObject.Parse(line, keyCount);
					if (hitObj != null)
						hitObjects.Add(hitObj);
				}
			}
		}

		return hitObjects;
	}

	private static double Calculate(List<HitObject> hitObjects, int keyCount, double od, string mod)
	{
		try
		{
			// Build note_seq as list of (column, head_time, tail_time)
			// tail_time is -1 for normal notes (not LN)
			var noteSeq = new List<(int k, int h, int t)>();

			foreach (var obj in hitObjects)
			{
				var k = obj.Column;
				var h = (int)obj.Time;
				// Only set tail when it's a hold note, otherwise -1
				var t = obj.Type == HitObjectType.Hold ? (int)obj.EndTime : -1;

				// Apply mod timing changes
				if (mod == "DT")
				{
					h = (int)Math.Floor(h * 2.0 / 3.0);
					if (t >= 0)
						t = (int)Math.Floor(t * 2.0 / 3.0);
				}
				else if (mod == "HT")
				{
					h = (int)Math.Floor(h * 4.0 / 3.0);
					if (t >= 0)
						t = (int)Math.Floor(t * 4.0 / 3.0);
				}

				noteSeq.Add((k, h, t));
			}

			// Hit leniency x - uses OD (Overall Difficulty), not key count!
			var x = 0.3 * Math.Pow((64.5 - Math.Ceiling(od * 3)) / 500.0, 0.5);
			x = Math.Min(x, 0.6 * (x - 0.09) + 0.09);

			// Sort by (head_time, column)
			noteSeq = noteSeq.OrderBy(n => n.h).ThenBy(n => n.k).ToList();

			if (noteSeq.Count == 0)
				return -1.0;

			var K = keyCount;
			var T = Math.Max(
				noteSeq.Max(n => n.h),
				noteSeq.Max(n => n.t) // includes -1 values but that's fine
			) + 1;

			// Group notes by column
			var noteDict = new Dictionary<int, List<(int k, int h, int t)>>();
			foreach (var n in noteSeq)
			{
				if (!noteDict.ContainsKey(n.k))
					noteDict[n.k] = new List<(int k, int h, int t)>();
				noteDict[n.k].Add(n);
			}

			var noteSeqByColumn = noteDict.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();

			// Long notes (LN) are those with tail >= 0
			var lnSeq = noteSeq.Where(n => n.t >= 0).ToList();
			var tailSeq = lnSeq.OrderBy(n => n.t).ToList();

			// Get corners
			var (allCorners, baseCorners, aCorners) = GetCorners(T, noteSeq);

			// Key usage
			var keyUsage = GetKeyUsage(K, T, noteSeq, baseCorners);

			// Active columns at each base corner
			var activeColumns = new List<List<int>>();
			for (var i = 0; i < baseCorners.Length; i++)
			{
				var cols = new List<int>();
				for (var col = 0; col < K; col++)
					if (keyUsage[col][i])
						cols.Add(col);

				activeColumns.Add(cols);
			}

			var keyUsage400 = GetKeyUsage400(K, T, noteSeq, baseCorners);
			var anchor = ComputeAnchor(K, keyUsage400, baseCorners);

			var (deltaKs, jbar) = ComputeJbar(K, x, noteSeqByColumn, baseCorners);
			jbar = InterpValues(allCorners, baseCorners, jbar);

			var xbar = ComputeXbar(K, x, noteSeqByColumn, activeColumns, baseCorners);
			xbar = InterpValues(allCorners, baseCorners, xbar);

			var lnRep = LnBodiesCountSparseRepresentation(lnSeq, T);

			var pbar = ComputePbar(x, noteSeq, lnRep, anchor, baseCorners);
			pbar = InterpValues(allCorners, baseCorners, pbar);

			var abar = ComputeAbar(K, activeColumns, deltaKs, aCorners, baseCorners);
			abar = InterpValues(allCorners, aCorners, abar);

			var rbar = ComputeRbar(x, noteSeqByColumn, tailSeq, baseCorners);
			rbar = InterpValues(allCorners, baseCorners, rbar);

			var (cStep, ksStep) = ComputeCAndKs(K, noteSeq, keyUsage, baseCorners);
			var cArr = StepInterp(allCorners, baseCorners, cStep);
			var ksArr = StepInterp(allCorners, baseCorners, ksStep);

			// Final computations - exactly as in Python
			var sAll = new double[allCorners.Length];
			var tAll = new double[allCorners.Length];
			var dAll = new double[allCorners.Length];

			for (var i = 0; i < allCorners.Length; i++)
			{
				var term1 = Math.Pow(abar[i], 3.0 / ksArr[i]) * Math.Min(jbar[i], 8 + 0.85 * jbar[i]);
				var term2 = Math.Pow(abar[i], 2.0 / 3.0) * (0.8 * pbar[i] + rbar[i] * 35.0 / (cArr[i] + 8));

				sAll[i] = Math.Pow(0.4 * Math.Pow(term1, 1.5) + (1 - 0.4) * Math.Pow(term2, 1.5), 2.0 / 3.0);
				tAll[i] = Math.Pow(abar[i], 3.0 / ksArr[i]) * xbar[i] / (xbar[i] + sAll[i] + 1);
				dAll[i] = 2.7 * Math.Pow(sAll[i], 0.5) * Math.Pow(tAll[i], 1.5) + sAll[i] * 0.27;
			}

			// Gaps
			var gaps = new double[allCorners.Length];
			gaps[0] = (allCorners[1] - allCorners[0]) / 2.0;
			gaps[^1] = (allCorners[^1] - allCorners[^2]) / 2.0;
			for (var i = 1; i < allCorners.Length - 1; i++) gaps[i] = (allCorners[i + 1] - allCorners[i - 1]) / 2.0;

			// Effective weights
			var effectiveWeights = new double[allCorners.Length];
			for (var i = 0; i < allCorners.Length; i++) effectiveWeights[i] = cArr[i] * gaps[i];

			// Sort by difficulty, track original indices
			var sortedIndices = Enumerable.Range(0, dAll.Length).OrderBy(i => dAll[i]).ToArray();
			var dSorted = sortedIndices.Select(i => dAll[i]).ToArray();
			var wSorted = sortedIndices.Select(i => effectiveWeights[i]).ToArray();

			// Cumulative weights using np.cumsum equivalent
			var cumWeights = new double[wSorted.Length];
			cumWeights[0] = wSorted[0];
			for (var i = 1; i < wSorted.Length; i++) cumWeights[i] = cumWeights[i - 1] + wSorted[i];

			var totalWeight = cumWeights[^1];
			var normCumWeights = cumWeights.Select(w => w / totalWeight).ToArray();

			// Target percentiles
			var targetPercentiles = new[] { 0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815 };
			var indices = new int[targetPercentiles.Length];

			for (var i = 0; i < targetPercentiles.Length; i++)
			{
				indices[i] = NpSearchSortedLeft(normCumWeights, targetPercentiles[i]);
				// Clip to valid range
				if (indices[i] >= dSorted.Length)
					indices[i] = dSorted.Length - 1;
			}

			// np.mean of first 4 and last 4
			var percentile93 =
				(dSorted[indices[0]] + dSorted[indices[1]] + dSorted[indices[2]] + dSorted[indices[3]]) / 4.0;
			var percentile83 =
				(dSorted[indices[4]] + dSorted[indices[5]] + dSorted[indices[6]] + dSorted[indices[7]]) / 4.0;

			// Weighted mean: (sum(D^5 * w) / sum(w))^(1/5)
			double sumD5W = 0, sumW = 0;
			for (var i = 0; i < dSorted.Length; i++)
			{
				sumD5W += Math.Pow(dSorted[i], 5) * wSorted[i];
				sumW += wSorted[i];
			}

			var weightedMean = Math.Pow(sumD5W / sumW, 0.2);

			// Final SR calculation
			var sr = 0.88 * percentile93 * 0.25 + 0.94 * percentile83 * 0.2 + weightedMean * 0.55;
			// SR = SR^1.0 / 8^1.0 * 8 simplifies to SR

			// Total notes calculation
			double totalNotes = noteSeq.Count;
			foreach (var (_, h, t) in lnSeq) totalNotes += 0.5 * Math.Min(t - h, 1000) / 200.0;

			sr *= totalNotes / (totalNotes + 60);

			sr = RescaleHigh(sr);
			sr *= 0.975;

			return sr;
		}
		catch
		{
			return -1.0;
		}
	}

	#region Helper Methods

	/// <summary>
	/// Equivalent to np.searchsorted(arr, value, side='left')
	/// </summary>
	private static int NpSearchSortedLeft(double[] arr, double value)
	{
		int lo = 0, hi = arr.Length;
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

	/// <summary>
	/// Equivalent to np.searchsorted(arr, value, side='right')
	/// </summary>
	private static int NpSearchSortedRight(double[] arr, double value)
	{
		int lo = 0, hi = arr.Length;
		while (lo < hi)
		{
			var mid = (lo + hi) / 2;
			if (arr[mid] <= value)
				lo = mid + 1;
			else
				hi = mid;
		}

		return lo;
	}

	private static double[] CumulativeSum(double[] x, double[] f)
	{
		var F = new double[x.Length];
		for (var i = 1; i < x.Length; i++) F[i] = F[i - 1] + f[i - 1] * (x[i] - x[i - 1]);

		return F;
	}

	private static double QueryCumsum(double q, double[] x, double[] F, double[] f)
	{
		if (q <= x[0])
			return 0.0;
		if (q >= x[^1])
			return F[^1];

		// np.searchsorted(x, q) - 1
		var i = NpSearchSortedLeft(x, q) - 1;
		if (i < 0)
			i = 0;
		return F[i] + f[i] * (q - x[i]);
	}

	private static double[] SmoothOnCorners(double[] x, double[] f, double window, double scale = 1.0,
		string mode = "sum")
	{
		var F = CumulativeSum(x, f);
		var g = new double[f.Length];

		for (var i = 0; i < x.Length; i++)
		{
			var s = x[i];
			var a = Math.Max(s - window, x[0]);
			var b = Math.Min(s + window, x[^1]);
			var val = QueryCumsum(b, x, F, f) - QueryCumsum(a, x, F, f);

			if (mode == "avg")
				g[i] = b - a > 0 ? val / (b - a) : 0.0;
			else
				g[i] = scale * val;
		}

		return g;
	}

	/// <summary>
	/// Linear interpolation equivalent to np.interp
	/// </summary>
	private static double[] InterpValues(double[] newX, double[] oldX, double[] oldVals)
	{
		var newVals = new double[newX.Length];

		for (var i = 0; i < newX.Length; i++)
		{
			var xVal = newX[i];

			if (xVal <= oldX[0])
			{
				newVals[i] = oldVals[0];
			}
			else if (xVal >= oldX[^1])
			{
				newVals[i] = oldVals[^1];
			}
			else
			{
				// Find position
				var pos = NpSearchSortedLeft(oldX, xVal);
				if (pos == 0)
				{
					newVals[i] = oldVals[0];
				}
				else if (pos >= oldX.Length)
				{
					newVals[i] = oldVals[^1];
				}
				else
				{
					// Linear interpolation
					var x0 = oldX[pos - 1];
					var x1 = oldX[pos];
					var y0 = oldVals[pos - 1];
					var y1 = oldVals[pos];
					var ratio = (xVal - x0) / (x1 - x0);
					newVals[i] = y0 + ratio * (y1 - y0);
				}
			}
		}

		return newVals;
	}

	/// <summary>
	/// Step interpolation (zero-order hold) - equivalent to Python step_interp
	/// indices = np.searchsorted(old_x, new_x, side='right') - 1
	/// indices = np.clip(indices, 0, len(old_vals)-1)
	/// </summary>
	private static double[] StepInterp(double[] newX, double[] oldX, double[] oldVals)
	{
		var result = new double[newX.Length];

		for (var i = 0; i < newX.Length; i++)
		{
			var idx = NpSearchSortedRight(oldX, newX[i]) - 1;
			idx = Math.Clamp(idx, 0, oldVals.Length - 1);
			result[i] = oldVals[idx];
		}

		return result;
	}

	private static double RescaleHigh(double sr)
	{
		return sr <= 9 ? sr : 9 + (sr - 9) * (1.0 / 1.2);
	}

	/// <summary>
	/// Equivalent to bisect.bisect_left
	/// </summary>
	private static int BisectLeft(List<int> list, int value)
	{
		int lo = 0, hi = list.Count;
		while (lo < hi)
		{
			var mid = (lo + hi) / 2;
			if (list[mid] < value)
				lo = mid + 1;
			else
				hi = mid;
		}

		return lo;
	}

	/// <summary>
	/// Equivalent to bisect.bisect_right for List of double
	/// </summary>
	private static int BisectRight(List<double> list, double value)
	{
		int lo = 0, hi = list.Count;
		while (lo < hi)
		{
			var mid = (lo + hi) / 2;
			if (list[mid] <= value)
				lo = mid + 1;
			else
				hi = mid;
		}

		return lo;
	}

	private static (int k, int h, int t) FindNextNoteInColumn(
		(int k, int h, int t) note,
		List<int> times,
		List<(int k, int h, int t)> columnNotes)
	{
		var idx = BisectLeft(times, note.h);
		return idx + 1 < columnNotes.Count ? columnNotes[idx + 1] : (0, 1000000000, 1000000000);
	}

	#endregion

	#region Component Calculations

	private static (double[] allCorners, double[] baseCorners, double[] aCorners) GetCorners(int T,
		List<(int k, int h, int t)> noteSeq)
	{
		var cornersBase = new HashSet<double>();
		foreach (var (_, h, t) in noteSeq)
		{
			cornersBase.Add(h);
			if (t >= 0)
				cornersBase.Add(t);
		}

		var toAdd = new List<double>();
		foreach (var s in cornersBase)
		{
			toAdd.Add(s + 501);
			toAdd.Add(s - 499);
			toAdd.Add(s + 1); // To resolve the Dirac-Delta additions exactly at notes
		}

		foreach (var s in toAdd)
			cornersBase.Add(s);
		cornersBase.Add(0);
		cornersBase.Add(T);

		var baseCorners = cornersBase.Where(s => s >= 0 && s <= T).OrderBy(s => s).ToArray();

		var cornersA = new HashSet<double>();
		foreach (var (_, h, t) in noteSeq)
		{
			cornersA.Add(h);
			if (t >= 0)
				cornersA.Add(t);
		}

		toAdd.Clear();
		foreach (var s in cornersA)
		{
			toAdd.Add(s + 1000);
			toAdd.Add(s - 1000);
		}

		foreach (var s in toAdd)
			cornersA.Add(s);
		cornersA.Add(0);
		cornersA.Add(T);

		var aCorners = cornersA.Where(s => s >= 0 && s <= T).OrderBy(s => s).ToArray();
		var allCorners = baseCorners.Union(aCorners).OrderBy(s => s).ToArray();

		return (allCorners, baseCorners, aCorners);
	}

	private static bool[][] GetKeyUsage(int K, int T, List<(int k, int h, int t)> noteSeq, double[] baseCorners)
	{
		var keyUsage = new bool[K][];
		for (var col = 0; col < K; col++)
			keyUsage[col] = new bool[baseCorners.Length];

		foreach (var (col, h, t) in noteSeq)
		{
			double startTime = Math.Max(h - 150, 0);
			double endTime = t < 0 ? h + 150 : Math.Min(t + 150, T - 1);

			var leftIdx = NpSearchSortedLeft(baseCorners, startTime);
			var rightIdx = NpSearchSortedLeft(baseCorners, endTime);

			for (var idx = leftIdx; idx < rightIdx; idx++)
				keyUsage[col][idx] = true;
		}

		return keyUsage;
	}

	private static double[][] GetKeyUsage400(int K, int T, List<(int k, int h, int t)> noteSeq, double[] baseCorners)
	{
		var keyUsage400 = new double[K][];
		for (var col = 0; col < K; col++)
			keyUsage400[col] = new double[baseCorners.Length];

		foreach (var (col, h, t) in noteSeq)
		{
			double startTime = Math.Max(h, 0);
			double endTime = t < 0 ? h : Math.Min(t, T - 1);

			var left400Idx = NpSearchSortedLeft(baseCorners, startTime - 400);
			var leftIdx = NpSearchSortedLeft(baseCorners, startTime);
			var rightIdx = NpSearchSortedLeft(baseCorners, endTime);
			var right400Idx = NpSearchSortedLeft(baseCorners, endTime + 400);

			// Constant value for notes/holds
			for (var idx = leftIdx; idx < rightIdx; idx++)
				keyUsage400[col][idx] += 3.75 + Math.Min(endTime - startTime, 1500) / 150.0;

			// Quadratic falloff before start
			for (var idx = left400Idx; idx < leftIdx; idx++)
			{
				var dist = baseCorners[idx] - startTime;
				keyUsage400[col][idx] += 3.75 - 3.75 / (400.0 * 400.0) * (dist * dist);
			}

			// Quadratic falloff after end
			for (var idx = rightIdx; idx < right400Idx; idx++)
			{
				var dist = Math.Abs(baseCorners[idx] - endTime);
				keyUsage400[col][idx] += 3.75 - 3.75 / (400.0 * 400.0) * (dist * dist);
			}
		}

		return keyUsage400;
	}

	private static double[] ComputeAnchor(int K, double[][] keyUsage400, double[] baseCorners)
	{
		var anchor = new double[baseCorners.Length];

		for (var idx = 0; idx < baseCorners.Length; idx++)
		{
			var counts = new double[K];
			for (var col = 0; col < K; col++)
				counts[col] = keyUsage400[col][idx];

			// Sort descending (counts[::-1].sort() in numpy sorts in place descending)
			Array.Sort(counts);
			Array.Reverse(counts);

			var nonzeroCounts = counts.Where(c => c != 0).ToArray();

			if (nonzeroCounts.Length > 1)
			{
				// walk = sum(nonzero_counts[:-1] * (1 - 4*(0.5 - nonzero_counts[1:]/nonzero_counts[:-1])^2))
				double walk = 0;
				double maxWalk = 0;
				for (var i = 0; i < nonzeroCounts.Length - 1; i++)
				{
					var ratio = nonzeroCounts[i + 1] / nonzeroCounts[i];
					walk += nonzeroCounts[i] * (1 - 4 * Math.Pow(0.5 - ratio, 2));
					maxWalk += nonzeroCounts[i];
				}

				anchor[idx] = walk / maxWalk;
			}
		}

		// anchor = 1 + min(anchor - 0.18, 5*(anchor - 0.22)^3)
		for (var i = 0; i < anchor.Length; i++)
			anchor[i] = 1 + Math.Min(anchor[i] - 0.18, 5 * Math.Pow(anchor[i] - 0.22, 3));

		return anchor;
	}

	private static (double[][] deltaKs, double[] jbar) ComputeJbar(int K, double x,
		List<List<(int k, int h, int t)>> noteSeqByColumn, double[] baseCorners)
	{
		var jKs = new double[K][];
		var deltaKs = new double[K][];

		for (var col = 0; col < K; col++)
		{
			jKs[col] = new double[baseCorners.Length];
			deltaKs[col] = new double[baseCorners.Length];
			Array.Fill(deltaKs[col], 1e9);
		}

		// jack_nerfer = lambda delta: 1 - 7e-5 * (0.15 + abs(delta - 0.08))^(-4)
		Func<double, double> jackNerfer = delta => 1 - 7e-5 * Math.Pow(0.15 + Math.Abs(delta - 0.08), -4);

		for (var col = 0; col < K; col++)
		{
			if (col >= noteSeqByColumn.Count)
				continue;
			var notes = noteSeqByColumn[col];

			for (var i = 0; i < notes.Count - 1; i++)
			{
				double start = notes[i].h;
				double end = notes[i + 1].h;

				var leftIdx = NpSearchSortedLeft(baseCorners, start);
				var rightIdx = NpSearchSortedLeft(baseCorners, end);

				if (leftIdx >= rightIdx)
					continue;

				var delta = 0.001 * (end - start);
				// val = delta^(-1) * (delta + 0.11 * x^(1/4))^(-1)
				var val = Math.Pow(delta, -1) * Math.Pow(delta + 0.11 * Math.Pow(x, 0.25), -1);
				var jVal = val * jackNerfer(delta);

				for (var idx = leftIdx; idx < rightIdx; idx++)
				{
					jKs[col][idx] = jVal;
					deltaKs[col][idx] = delta;
				}
			}
		}

		// Smooth each column
		var jbarKs = new double[K][];
		for (var col = 0; col < K; col++)
			jbarKs[col] = SmoothOnCorners(baseCorners, jKs[col], 500, 0.001, "sum");

		// Aggregate with weighted average
		var jbar = new double[baseCorners.Length];
		for (var i = 0; i < baseCorners.Length; i++)
		{
			double num = 0, den = 0;
			for (var col = 0; col < K; col++)
			{
				var v = jbarKs[col][i];
				var w = 1.0 / deltaKs[col][i];
				num += Math.Pow(Math.Max(v, 0), 5) * w;
				den += w;
			}

			jbar[i] = Math.Pow(num / Math.Max(1e-9, den), 0.2);
		}

		return (deltaKs, jbar);
	}

	private static double[] ComputeXbar(int K, double x, List<List<(int k, int h, int t)>> noteSeqByColumn,
		List<List<int>> activeColumns, double[] baseCorners)
	{
		var crossMatrix = new double[][]
		{
			new[] { -1.0 },
			new[] { 0.075, 0.075 },
			new[] { 0.125, 0.05, 0.125 },
			new[] { 0.125, 0.125, 0.125, 0.125 },
			new[] { 0.175, 0.25, 0.05, 0.25, 0.175 },
			new[] { 0.175, 0.25, 0.175, 0.175, 0.25, 0.175 },
			new[] { 0.225, 0.35, 0.25, 0.05, 0.25, 0.35, 0.225 },
			new[] { 0.225, 0.35, 0.25, 0.225, 0.225, 0.25, 0.35, 0.225 },
			new[] { 0.275, 0.45, 0.35, 0.25, 0.05, 0.25, 0.35, 0.45, 0.275 },
			new[] { 0.275, 0.45, 0.35, 0.25, 0.275, 0.275, 0.25, 0.35, 0.45, 0.275 },
			new[] { 0.325, 0.55, 0.45, 0.35, 0.25, 0.05, 0.25, 0.35, 0.45, 0.55, 0.325 }
		};

		var xKs = new double[K + 1][];
		var fastCross = new double[K + 1][];
		for (var col = 0; col <= K; col++)
		{
			xKs[col] = new double[baseCorners.Length];
			fastCross[col] = new double[baseCorners.Length];
		}

		var crossCoeff = crossMatrix[K];

		for (var col = 0; col <= K; col++)
		{
			List<(int k, int h, int t)> notesInPair;
			if (col == 0)
			{
				notesInPair = noteSeqByColumn.Count > 0 ? noteSeqByColumn[0] : new List<(int k, int h, int t)>();
			}
			else if (col == K)
			{
				notesInPair = noteSeqByColumn.Count > 0 ? noteSeqByColumn[K - 1] : new List<(int k, int h, int t)>();
			}
			else
			{
				// heapq.merge equivalent - merge two sorted lists by h
				var list1 = col - 1 < noteSeqByColumn.Count
					? noteSeqByColumn[col - 1]
					: new List<(int k, int h, int t)>();
				var list2 = col < noteSeqByColumn.Count ? noteSeqByColumn[col] : new List<(int k, int h, int t)>();
				notesInPair = list1.Concat(list2).OrderBy(n => n.h).ToList();
			}

			for (var i = 1; i < notesInPair.Count; i++)
			{
				double start = notesInPair[i - 1].h;
				double end = notesInPair[i].h;

				var idxStart = NpSearchSortedLeft(baseCorners, start);
				var idxEnd = NpSearchSortedLeft(baseCorners, end);

				if (idxStart >= idxEnd)
					continue;

				var delta = 0.001 * (notesInPair[i].h - notesInPair[i - 1].h);
				var val = 0.16 * Math.Pow(Math.Max(x, delta), -2);

				// Check active columns condition
				var prevNotInStart = !activeColumns[idxStart].Contains(col - 1);
				var prevNotInEnd = !activeColumns[idxEnd < activeColumns.Count ? idxEnd : activeColumns.Count - 1]
					.Contains(col - 1);
				var currNotInStart = !activeColumns[idxStart].Contains(col);
				var currNotInEnd = !activeColumns[idxEnd < activeColumns.Count ? idxEnd : activeColumns.Count - 1]
					.Contains(col);

				if ((prevNotInStart && prevNotInEnd) || (currNotInStart && currNotInEnd))
					val *= 1 - crossCoeff[col];

				for (var idx = idxStart; idx < idxEnd; idx++)
				{
					xKs[col][idx] = val;
					// max(0, 0.4 * max(delta, 0.06, 0.75*x)^(-2) - 80)
					fastCross[col][idx] =
						Math.Max(0, 0.4 * Math.Pow(Math.Max(Math.Max(delta, 0.06), 0.75 * x), -2) - 80);
				}
			}
		}

		var xBase = new double[baseCorners.Length];
		for (var i = 0; i < baseCorners.Length; i++)
		{
			// sum(X_ks[k][i] * cross_coeff[k] for k in range(K+1))
			double sum1 = 0;
			for (var col = 0; col <= K; col++)
				sum1 += xKs[col][i] * crossCoeff[col];

			// sum(sqrt(fast_cross[k][i]*cross_coeff[k]*fast_cross[k+1][i]*cross_coeff[k+1]) for k in range(K))
			double sum2 = 0;
			for (var col = 0; col < K; col++)
				sum2 += Math.Sqrt(fastCross[col][i] * crossCoeff[col] * fastCross[col + 1][i] * crossCoeff[col + 1]);

			xBase[i] = sum1 + sum2;
		}

		return SmoothOnCorners(baseCorners, xBase, 500, 0.001, "sum");
	}

	private static (List<double> points, List<double> cumsum, List<double> values) LnBodiesCountSparseRepresentation(
		List<(int k, int h, int t)> lnSeq, int T)
	{
		var diff = new Dictionary<double, double>();

		foreach (var (_, h, t) in lnSeq)
		{
			double t0 = Math.Min(h + 60, t);
			double t1 = Math.Min(h + 120, t);

			diff.TryAdd(t0, 0);
			diff[t0] += 1.3;

			diff.TryAdd(t1, 0);
			diff[t1] += -0.3; // (-1.3 + 1)

			diff.TryAdd(t, 0);
			diff[t] -= 1;
		}

		var pointsSet = new HashSet<double> { 0, T };
		pointsSet.UnionWith(diff.Keys);
		var points = pointsSet.OrderBy(p => p).ToList();

		var values = new List<double>();
		var cumsum = new List<double> { 0 };
		var curr = 0.0;

		for (var i = 0; i < points.Count - 1; i++)
		{
			var time = points[i];
			if (diff.TryGetValue(time, out var change))
				curr += change;

			var v = Math.Min(curr, 2.5 + 0.5 * curr);
			values.Add(v);
			var segLength = points[i + 1] - points[i];
			cumsum.Add(cumsum[^1] + segLength * v);
		}

		return (points, cumsum, values);
	}

	private static double LnSum(double a, double b,
		(List<double> points, List<double> cumsum, List<double> values) lnRep)
	{
		var (points, cumsum, values) = lnRep;

		// bisect.bisect_right(points, a) - 1
		var i = BisectRight(points, a) - 1;
		var j = BisectRight(points, b) - 1;
		i = Math.Max(0, i);
		j = Math.Max(0, j);

		// Bounds check for values
		if (values.Count == 0)
			return 0;
		i = Math.Min(i, values.Count - 1);
		j = Math.Min(j, values.Count - 1);

		if (i == j)
			return (b - a) * values[i];

		var total = (points[i + 1] - a) * values[i];
		total += cumsum[j] - cumsum[i + 1];
		total += (b - points[j]) * values[j];
		return total;
	}

	private static double[] ComputePbar(double x, List<(int k, int h, int t)> noteSeq,
		(List<double>, List<double>, List<double>) lnRep, double[] anchor, double[] baseCorners)
	{
		// stream_booster = lambda delta: 1 + 1.7e-7 * ((7.5/delta) - 160) * ((7.5/delta) - 360)^2 if 160 < (7.5/delta) < 360 else 1
		Func<double, double> streamBooster = delta =>
		{
			var nps = 7.5 / delta;
			return 160 < nps && nps < 360
				? 1 + 1.7e-7 * (nps - 160) * Math.Pow(nps - 360, 2)
				: 1;
		};

		var pStep = new double[baseCorners.Length];

		for (var i = 0; i < noteSeq.Count - 1; i++)
		{
			double hL = noteSeq[i].h;
			double hR = noteSeq[i + 1].h;
			var deltaTime = hR - hL;

			if (deltaTime < 1e-9)
			{
				// Dirac delta case
				var spike = 1000 * Math.Pow(0.02 * (4 / x - 24), 0.25);
				var leftIdx = NpSearchSortedLeft(baseCorners, hL);
				var rightIdx = NpSearchSortedRight(baseCorners, hL);
				for (var idx = leftIdx; idx < rightIdx; idx++)
					if (idx >= 0 && idx < pStep.Length)
						pStep[idx] += spike;
				continue;
			}

			var start = NpSearchSortedLeft(baseCorners, hL);
			var end = NpSearchSortedLeft(baseCorners, hR);

			if (start >= end)
				continue;

			var delta = 0.001 * deltaTime;
			var v = 1 + 6 * 0.001 * LnSum(hL, hR, lnRep);
			var bVal = streamBooster(delta);
			double inc;

			if (delta < 2 * x / 3)
				inc = Math.Pow(delta, -1) *
					  Math.Pow(0.08 * Math.Pow(x, -1) * (1 - 24 * Math.Pow(x, -1) * Math.Pow(delta - x / 2, 2)), 0.25) *
					  Math.Max(bVal, v);
			else
				inc = Math.Pow(delta, -1) *
					  Math.Pow(0.08 * Math.Pow(x, -1) * (1 - 24 * Math.Pow(x, -1) * Math.Pow(x / 6, 2)), 0.25) *
					  Math.Max(bVal, v);

			// P_step[idx] += min(inc * anchor[idx], max(inc, inc*2-10))
			for (var idx = start; idx < end; idx++)
				pStep[idx] += Math.Min(inc * anchor[idx], Math.Max(inc, inc * 2 - 10));
		}

		return SmoothOnCorners(baseCorners, pStep, 500, 0.001, "sum");
	}

	private static double[] ComputeAbar(int K, List<List<int>> activeColumns,
		double[][] deltaKs, double[] aCorners, double[] baseCorners)
	{
		var dks = new double[K - 1][];
		for (var col = 0; col < K - 1; col++)
			dks[col] = new double[baseCorners.Length];

		for (var i = 0; i < baseCorners.Length; i++)
		{
			var cols = activeColumns[i];
			for (var j = 0; j < cols.Count - 1; j++)
			{
				var k0 = cols[j];
				var k1 = cols[j + 1];
				if (k0 < K - 1 && k0 < deltaKs.Length && k1 < deltaKs.Length)
					dks[k0][i] = Math.Abs(deltaKs[k0][i] - deltaKs[k1][i]) +
								 0.4 * Math.Max(0, Math.Max(deltaKs[k0][i], deltaKs[k1][i]) - 0.11);
			}
		}

		var aStep = new double[aCorners.Length];
		Array.Fill(aStep, 1.0);

		for (var i = 0; i < aCorners.Length; i++)
		{
			var s = aCorners[i];
			// np.searchsorted(base_corners, s) - note this is side='left' by default in numpy
			var idx = NpSearchSortedLeft(baseCorners, s);
			if (idx >= baseCorners.Length)
				idx = baseCorners.Length - 1;

			var cols = activeColumns[idx];
			for (var j = 0; j < cols.Count - 1; j++)
			{
				var k0 = cols[j];
				var k1 = cols[j + 1];
				if (k0 >= K - 1 || k0 >= dks.Length)
					continue;

				var dVal = dks[k0][idx];
				if (dVal < 0.02)
					aStep[i] *= Math.Min(0.75 + 0.5 * Math.Max(deltaKs[k0][idx], deltaKs[k1][idx]), 1);
				else if (dVal < 0.07)
					aStep[i] *= Math.Min(0.65 + 5 * dVal + 0.5 * Math.Max(deltaKs[k0][idx], deltaKs[k1][idx]), 1);
			}
		}

		return SmoothOnCorners(aCorners, aStep, 250, 1.0, "avg");
	}

	private static double[] ComputeRbar(double x, List<List<(int k, int h, int t)>> noteSeqByColumn,
		List<(int k, int h, int t)> tailSeq, double[] baseCorners)
	{
		var rStep = new double[baseCorners.Length];

		if (tailSeq.Count == 0)
			return SmoothOnCorners(baseCorners, rStep, 500, 0.001, "sum");

		var timesByColumn = new Dictionary<int, List<int>>();
		for (var i = 0; i < noteSeqByColumn.Count; i++)
			timesByColumn[i] = noteSeqByColumn[i].Select(n => n.h).ToList();

		// Release Index
		var iList = new double[tailSeq.Count];
		for (var i = 0; i < tailSeq.Count; i++)
		{
			var (k, hI, tI) = tailSeq[i];
			if (!timesByColumn.ContainsKey(k) || k >= noteSeqByColumn.Count)
			{
				iList[i] = 0;
				continue;
			}

			var nextNote = FindNextNoteInColumn((k, hI, tI), timesByColumn[k], noteSeqByColumn[k]);
			double hJ = nextNote.Item2;

			var iH = 0.001 * Math.Abs(tI - hI - 80) / x;
			var iT = 0.001 * Math.Abs(hJ - tI - 80) / x;

			iList[i] = 2.0 / (2 + Math.Exp(-5 * (iH - 0.75)) + Math.Exp(-5 * (iT - 0.75)));
		}

		for (var i = 0; i < tailSeq.Count - 1; i++)
		{
			double tStart = tailSeq[i].t;
			double tEnd = tailSeq[i + 1].t;

			var leftIdx = NpSearchSortedLeft(baseCorners, tStart);
			var rightIdx = NpSearchSortedLeft(baseCorners, tEnd);

			if (leftIdx >= rightIdx)
				continue;

			var deltaR = 0.001 * (tailSeq[i + 1].t - tailSeq[i].t);
			var rValue = 0.08 * Math.Pow(deltaR, -0.5) * Math.Pow(x, -1) * (1 + 0.8 * (iList[i] + iList[i + 1]));

			for (var idx = leftIdx; idx < rightIdx; idx++)
				rStep[idx] = rValue;
		}

		return SmoothOnCorners(baseCorners, rStep, 500, 0.001, "sum");
	}

	private static (double[] cStep, double[] ksStep) ComputeCAndKs(int K, List<(int k, int h, int t)> noteSeq,
		bool[][] keyUsage, double[] baseCorners)
	{
		var noteHitTimes = noteSeq.Select(n => n.h).OrderBy(t => t).ToList();
		var cStep = new double[baseCorners.Length];

		for (var i = 0; i < baseCorners.Length; i++)
		{
			var s = baseCorners[i];
			var low = s - 500;
			var high = s + 500;

			// bisect.bisect_left(note_hit_times, high) - bisect.bisect_left(note_hit_times, low)
			var highIdx = BisectLeftDouble(noteHitTimes, high);
			var lowIdx = BisectLeftDouble(noteHitTimes, low);

			cStep[i] = highIdx - lowIdx;
		}

		var ksStep = new double[baseCorners.Length];
		for (var i = 0; i < baseCorners.Length; i++)
		{
			var keyCount = 0;
			for (var col = 0; col < K; col++)
				if (keyUsage[col][i])
					keyCount++;
			ksStep[i] = Math.Max(keyCount, 1);
		}

		return (cStep, ksStep);
	}

	private static int BisectLeftDouble(List<int> list, double value)
	{
		int lo = 0, hi = list.Count;
		while (lo < hi)
		{
			var mid = (lo + hi) / 2;
			if (list[mid] < value)
				lo = mid + 1;
			else
				hi = mid;
		}

		return lo;
	}

	#endregion
}
