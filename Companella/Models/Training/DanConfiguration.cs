using System.Text.Json.Serialization;

namespace Companella.Models.Training;

/// <summary>
/// Represents the 8 MSD skillset values from MinaCalc.
/// </summary>
public class MsdSkillsetValues
{
	[JsonPropertyName("overall")] public double Overall { get; set; }

	[JsonPropertyName("stream")] public double Stream { get; set; }

	[JsonPropertyName("jumpstream")] public double Jumpstream { get; set; }

	[JsonPropertyName("handstream")] public double Handstream { get; set; }

	[JsonPropertyName("stamina")] public double Stamina { get; set; }

	[JsonPropertyName("jackspeed")] public double Jackspeed { get; set; }

	[JsonPropertyName("chordjack")] public double Chordjack { get; set; }

	[JsonPropertyName("technical")] public double Technical { get; set; }

	/// <summary>
	/// Creates a new MsdSkillsetValues with all values set to 0.
	/// </summary>
	public MsdSkillsetValues()
	{
	}

	/// <summary>
	/// Creates a new MsdSkillsetValues with all values set to the same base value.
	/// </summary>
	public MsdSkillsetValues(double baseValue)
	{
		Overall = baseValue;
		Stream = baseValue;
		Jumpstream = baseValue;
		Handstream = baseValue;
		Stamina = baseValue;
		Jackspeed = baseValue;
		Chordjack = baseValue;
		Technical = baseValue;
	}

	/// <summary>
	/// Creates MsdSkillsetValues from a SkillsetScores object.
	/// </summary>
	public static MsdSkillsetValues FromSkillsetScores(Difficulty.SkillsetScores scores)
	{
		return new MsdSkillsetValues
		{
			Overall = scores.Overall,
			Stream = scores.Stream,
			Jumpstream = scores.Jumpstream,
			Handstream = scores.Handstream,
			Stamina = scores.Stamina,
			Jackspeed = scores.Jackspeed,
			Chordjack = scores.Chordjack,
			Technical = scores.Technical
		};
	}

	/// <summary>
	/// Calculates Euclidean distance to another MsdSkillsetValues.
	/// </summary>
	public double DistanceTo(MsdSkillsetValues other)
	{
		var dOverall = Overall - other.Overall;
		var dStream = Stream - other.Stream;
		var dJumpstream = Jumpstream - other.Jumpstream;
		var dHandstream = Handstream - other.Handstream;
		var dStamina = Stamina - other.Stamina;
		var dJackspeed = Jackspeed - other.Jackspeed;
		var dChordjack = Chordjack - other.Chordjack;
		var dTechnical = Technical - other.Technical;

		return Math.Sqrt(
			dOverall * dOverall +
			dStream * dStream +
			dJumpstream * dJumpstream +
			dHandstream * dHandstream +
			dStamina * dStamina +
			dJackspeed * dJackspeed +
			dChordjack * dChordjack +
			dTechnical * dTechnical
		);
	}

	/// <summary>
	/// Returns an array of all 8 MSD values.
	/// </summary>
	public double[] ToArray()
	{
		return new[] { Overall, Stream, Jumpstream, Handstream, Stamina, Jackspeed, Chordjack, Technical };
	}

	/// <summary>
	/// Gets the dominant skillset name (highest non-overall value).
	/// </summary>
	[JsonIgnore]
	public string DominantSkillset
	{
		get
		{
			var skillsets = new (string Name, double Value)[]
			{
				("Stream", Stream),
				("Jumpstream", Jumpstream),
				("Handstream", Handstream),
				("Stamina", Stamina),
				("Jackspeed", Jackspeed),
				("Chordjack", Chordjack),
				("Technical", Technical)
			};

			return skillsets.MaxBy(s => s.Value).Name;
		}
	}
}

/// <summary>
/// Root configuration for dans (difficulty levels).
/// Uses MSD skillset values + Interlude rating for classification.
/// </summary>
public class DanConfiguration
{
	/// <summary>
	/// List of all dan definitions, ordered from lowest to highest.
	/// </summary>
	[JsonPropertyName("dans")]
	public List<DanDefinition> Dans { get; set; } = new();

	/// <summary>
	/// Schema version for future compatibility.
	/// Version 3 = MSD skillsets + Interlude rating structure.
	/// </summary>
	[JsonPropertyName("version")]
	public int Version { get; set; } = 3;

	/// <summary>
	/// Creates a default configuration with placeholder MSD + Interlude values.
	/// </summary>
	public static DanConfiguration CreateDefault()
	{
		var config = new DanConfiguration { Version = 3 };

		// Create all 20 levels (1-10 numeric, then Greek letter names)
		var labels = new[]
		{
			"1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
			"alpha", "beta", "gamma", "delta", "epsilon",
			"zeta", "eta", "theta", "iota", "kappa"
		};

		for (var i = 0; i < labels.Length; i++)
		{
			// Base MSD scales with dan level (roughly 3.0 to 15.0+ range)
			var baseMsd = 3.0 + i * 0.6;

			var dan = new DanDefinition
			{
				Label = labels[i],
				MsdValues = new MsdSkillsetValues
				{
					Overall = Math.Round(baseMsd, 2),
					Stream = Math.Round(baseMsd - 0.1, 2),
					Jumpstream = Math.Round(baseMsd - 0.2, 2),
					Handstream = Math.Round(baseMsd - 0.3, 2),
					Stamina = Math.Round(baseMsd - 0.15, 2),
					Jackspeed = Math.Round(baseMsd + 0.1, 2),
					Chordjack = Math.Round(baseMsd + 0.2, 2),
					Technical = Math.Round(baseMsd - 0.25, 2)
				},
				InterludeRating = Math.Round(baseMsd * 0.9, 2) // Interlude scales slightly lower
			};

			config.Dans.Add(dan);
		}

		return config;
	}
}

/// <summary>
/// Definition of a single dan (difficulty level).
/// </summary>
public class DanDefinition
{
	/// <summary>
	/// The label for this dan (1-10 or Greek letter).
	/// </summary>
	[JsonPropertyName("label")]
	public string Label { get; set; } = string.Empty;

	/// <summary>
	/// MSD skillset values for this dan level.
	/// Contains all 8 MinaCalc skillsets.
	/// </summary>
	[JsonPropertyName("msdValues")]
	public MsdSkillsetValues MsdValues { get; set; } = new();

	/// <summary>
	/// Interlude (YAVSRG) difficulty rating for this dan level.
	/// </summary>
	[JsonPropertyName("interludeRating")]
	public double InterludeRating { get; set; }

	/// <summary>
	/// Gets the display name for this dan.
	/// </summary>
	[JsonIgnore]
	public string DisplayName => DanLabelFormatter.ToDisplayLabel(Label);

	/// <summary>
	/// Gets the low variant display name.
	/// </summary>
	[JsonIgnore]
	public string LowDisplayName => $"{DanLabelFormatter.ToDisplayLabel(Label)}-";

	/// <summary>
	/// Gets the high variant display name.
	/// </summary>
	[JsonIgnore]
	public string HighDisplayName => $"{DanLabelFormatter.ToDisplayLabel(Label)}+";

	/// <summary>
	/// Calculates combined distance to a given MSD + Interlude input.
	/// Uses Euclidean distance across all 9 dimensions.
	/// </summary>
	public double DistanceTo(MsdSkillsetValues msd, double interludeRating)
	{
		var msdDistance = MsdValues.DistanceTo(msd);
		var interludeDistance = InterludeRating - interludeRating;

		// Combine MSD distance with Interlude distance
		return Math.Sqrt(msdDistance * msdDistance + interludeDistance * interludeDistance);
	}
}

/// <summary>
/// Result of classifying a map against dans using MSD + Interlude.
/// </summary>
public class DanClassificationResult
{
	/// <summary>
	/// The matched dan label.
	/// </summary>
	public string Label { get; set; } = "?";

	/// <summary>
	/// Variant: null for mid-range, "--", "-", "+", or "++" for edge cases.
	/// Determined by comparing to adjacent dan thresholds.
	/// </summary>
	public string? Variant { get; set; }

	/// <summary>
	/// Gets the full display name including variant.
	/// Daniel mode uses only -/+/none; ONNX uses --/-/+/++ from raw output.
	/// </summary>
	public string DisplayName => UsedDanielCalculator || !RawModelOutput.HasValue
		? DanLabelFormatter.FormatWithVariant(Label, Variant)
		: DanLabelFormatter.FormatWithRawVariant(Label, RawModelOutput.Value);

	/// <summary>
	/// Confidence score (0.0 - 1.0).
	/// Higher when the distance to the dan is smaller.
	/// </summary>
	public double Confidence { get; set; }

	/// <summary>
	/// The dominant MSD skillset for this map.
	/// </summary>
	public string? DominantSkillset { get; set; }

	/// <summary>
	/// The MSD values used for classification.
	/// </summary>
	public MsdSkillsetValues? MsdValues { get; set; }

	/// <summary>
	/// The Interlude rating used for classification.
	/// </summary>
	public double InterludeRating { get; set; }

	/// <summary>
	/// The distance to the matched dan (lower is better match).
	/// </summary>
	public double Distance { get; set; }

	/// <summary>
	/// Index of the matched dan (0-19, or -1 if unknown).
	/// </summary>
	public int DanIndex { get; set; } = -1;

	/// <summary>
	/// Raw model output value (continuous 1.0-20.0).
	/// Only set when using ONNX model inference.
	/// </summary>
	public float? RawModelOutput { get; set; }

	/// <summary>
	/// True when the result came from the Daniel rice calculator.
	/// </summary>
	public bool UsedDanielCalculator { get; set; }

	/// <summary>
	/// Daniel star rating when <see cref="UsedDanielCalculator"/> is true.
	/// </summary>
	public double? DanielStarRating { get; set; }
}
