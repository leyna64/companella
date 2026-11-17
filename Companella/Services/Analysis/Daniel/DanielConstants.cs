// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Constants for the Daniel rice difficulty algorithm.
/// </summary>
internal static class DanielConstants
{
	internal const double BreakZeroThresholdMs = 400;
	internal const double GraphResampleIntervalMs = 100;
	internal const double SmoothSigmaMs = 800;

	/// <summary>
	/// Daniel hardcodes OD to 9 regardless of beatmap OD.
	/// </summary>
	internal const double HardcodedOd = 9.0;

	internal const int DanOrderStart = 11;

	internal static readonly string[] DanOrder =
	[
		"Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta"
	];

	internal static readonly double[] DanMeans =
	[
		6.562, 6.957, 7.459, 7.939, 9.095, 9.473, 10.162, 10.782
	];

	/// <summary>
	/// Cross-column coefficients indexed by key count K.
	/// </summary>
	internal static readonly double[][] CrossMatrix =
	[
		[-1],
		[0.075, 0.075],
		[0.125, 0.05, 0.125],
		[0.125, 0.125, 0.125, 0.125],
		[0.175, 0.25, 0.05, 0.25, 0.175],
		[0.175, 0.25, 0.175, 0.175, 0.25, 0.175],
		[0.225, 0.35, 0.25, 0.05, 0.25, 0.35, 0.225],
		[0.225, 0.35, 0.25, 0.225, 0.225, 0.25, 0.35, 0.225],
		[0.275, 0.45, 0.35, 0.25, 0.05, 0.25, 0.35, 0.45, 0.275],
		[0.275, 0.45, 0.35, 0.25, 0.275, 0.275, 0.25, 0.35, 0.45, 0.275],
		[0.325, 0.55, 0.45, 0.35, 0.25, 0.05, 0.25, 0.35, 0.45, 0.55, 0.325]
	];
}
