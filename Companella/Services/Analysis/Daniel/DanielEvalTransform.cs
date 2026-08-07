// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Blends original Daniel SR and pass 1 SR based on map duration (short maps lean toward pass 1).
/// </summary>
internal static class DanielEvalTransform
{
	/// <summary>
	/// evalSr = baseSr - (max(0, 60s - duration) / 60s) * (baseSr - pass1Sr)
	/// 60s+: baseSr, 30s: average, shorter maps approach pass1Sr.
	/// </summary>
	internal static double Apply(double baseSr, double pass1Sr, int originalDurationMs)
	{
		var durationSec = originalDurationMs / 1000.0;
		var deficitSec = Math.Max(0.0, 60.0 - durationSec);
		var blendFromPass1 = deficitSec / 60.0;
		return baseSr - blendFromPass1 * (baseSr - pass1Sr);
	}
}
