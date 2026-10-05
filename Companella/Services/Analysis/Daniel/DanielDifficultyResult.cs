// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Result of Daniel rice difficulty calculation.
/// </summary>
public sealed class DanielDifficultyResult
{
	/// <summary>
	/// Whether calculation succeeded (4K map with at least one note).
	/// </summary>
	public bool IsValid { get; init; }

	/// <summary>
	/// Daniel star rating used for dan mapping (duration-weighted blend of pass 0 and pass 1).
	/// </summary>
	public double StarRating { get; init; }

	/// <summary>
	/// Single-pass Daniel SR on the original map (pass 0).
	/// </summary>
	public double BaseStarRating { get; init; }

	/// <summary>
	/// Daniel SR on pass 1 (60s min-duration reflection crop) before eval averaging.
	/// </summary>
	public double Pass1StarRating { get; init; }

	/// <summary>
	/// Greek-letter dan label (e.g. "Gamma Mid", "&lt;Alpha Low").
	/// </summary>
	public string DanLabel { get; init; } = string.Empty;

	/// <summary>
	/// Numeric dan string (e.g. "13.42") or "N/A".
	/// </summary>
	public string DanNumeric { get; init; } = string.Empty;

	/// <summary>
	/// Parsed numeric dan when available.
	/// </summary>
	public double? DanNumericValue { get; init; }

	/// <summary>
	/// True when SR is below Alpha and Companella ONNX should be used instead.
	/// </summary>
	public bool IsBelowAlphaThreshold { get; init; }

	/// <summary>
	/// Time-averaged factor values for display/debug.
	/// </summary>
	public IReadOnlyDictionary<string, double> FactorAverages { get; init; } =
		new Dictionary<string, double>();

	/// <summary>
	/// Error message when <see cref="IsValid"/> is false.
	/// </summary>
	public string? ErrorMessage { get; init; }

	internal static DanielDifficultyResult Invalid(string message) => new()
	{
		IsValid = false,
		ErrorMessage = message
	};
}
