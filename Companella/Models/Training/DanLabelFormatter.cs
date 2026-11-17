using System.Globalization;

namespace Companella.Models.Training;

/// <summary>
/// Formats dan labels for display, converting latin greek names to unicode greek letters.
/// </summary>
public static class DanLabelFormatter
{
	private static readonly (string Latin, string Greek)[] _greekLetters =
	[
		("alpha", "\u03B1"),
		("beta", "\u03B2"),
		("gamma", "\u03B3"),
		("delta", "\u03B4"),
		("epsilon", "\u03B5"),
		("zeta", "\u03B6"),
		("eta", "\u03B7"),
		("theta", "\u03B8"),
		("iota", "\u03B9"),
		("kappa", "\u03BA")
	];

	/// <summary>
	/// Converts a dan label to its display form (numeric unchanged, latin greek to unicode).
	/// </summary>
	public static string ToDisplayLabel(string label)
	{
		if (string.IsNullOrEmpty(label))
			return "?";

		foreach (var (latin, greek) in _greekLetters)
		{
			if (string.Equals(label, latin, StringComparison.OrdinalIgnoreCase))
				return greek;
			if (label == greek)
				return greek;
		}

		return label;
	}

	/// <summary>
	/// Formats a dan label with a variant suffix (--, -, +, ++).
	/// </summary>
	public static string FormatWithVariant(string label, string? variant)
	{
		var display = ToDisplayLabel(label);
		return AppendVariant(display, variant);
	}

	/// <summary>
	/// Appends a variant suffix to an already formatted dan display label.
	/// </summary>
	public static string AppendVariant(string display, string? variant)
	{
		return variant switch
		{
			"--" => $"{display}--",
			"-" => $"{display}-",
			"+" => $"{display}+",
			"++" => $"{display}++",
			_ => display
		};
	}

	/// <summary>
	/// Parses a continuous dan value into a 0-based dan index and variant.
	/// </summary>
	public static (int DanIndex, string? Variant) ParseRawDan(double rawValue)
	{
		if (rawValue < 1.0)
			return (0, "--");

		if (rawValue >= 20.0)
			return (19, "++");

		var danLevel = (int)Math.Round(rawValue);
		danLevel = Math.Clamp(danLevel, 1, 20);
		var danIndex = danLevel - 1;
		var offset = rawValue - danLevel;

		var variant = offset switch
		{
			<= -0.3 => "--",
			<= -0.1 => "-",
			< 0.1 => null,
			< 0.3 => "+",
			_ => "++"
		};

		return (danIndex, variant);
	}

	/// <summary>
	/// Formats a latin dan label using variant derived from a continuous dan value.
	/// </summary>
	public static string FormatWithRawVariant(string label, double rawValue, bool suppressVariantAtMax = false)
	{
		var display = ToDisplayLabel(label);
		if (suppressVariantAtMax && rawValue >= 20.0)
			return display;

		var (_, variant) = ParseRawDan(rawValue);
		return AppendVariant(display, variant);
	}

	/// <summary>
	/// Gets the display label for a dan level index (1-20).
	/// </summary>
	public static string GetLevelLabel(int levelIndex)
	{
		if (levelIndex < 1)
			return "?";
		if (levelIndex <= 10)
			return levelIndex.ToString(CultureInfo.InvariantCulture);
		if (levelIndex <= 20)
			return _greekLetters[levelIndex - 11].Greek;

		return _greekLetters[^1].Greek;
	}

	/// <summary>
	/// Parses a display or storage label back to its latin greek name or numeric string.
	/// </summary>
	public static string NormalizeLabel(string label)
	{
		if (string.IsNullOrEmpty(label))
			return string.Empty;

		if (int.TryParse(label, out var numericLevel) && numericLevel is >= 1 and <= 10)
			return numericLevel.ToString(CultureInfo.InvariantCulture);

		foreach (var (latin, greek) in _greekLetters)
		{
			if (string.Equals(label, latin, StringComparison.OrdinalIgnoreCase) || label == greek)
				return latin;
		}

		return label;
	}
}
