namespace Companella.Services.Analysis.InterludePatterns;

/// <summary>
/// Core pattern categories from YAVSRG/Interlude Prelude.Calculator.Patterns.
/// </summary>
public enum InterludeCorePattern
{
	Stream,
	Chordstream,
	Jacks
}

public enum InterludeDirection
{
	None,
	Left,
	Right,
	Outwards,
	Inwards
}

/// <summary>
/// Per-row chart data used by Interlude pattern recognisers.
/// Port of Prelude RowInfo.
/// </summary>
public sealed record InterludeRowInfo
{
	public int Index { get; init; }
	public float Time { get; init; }
	public float MsPerBeat { get; init; }
	public int Notes { get; init; }
	public int Jacks { get; init; }
	public InterludeDirection Direction { get; init; }
	public bool Roll { get; init; }
	public float Density { get; init; }
	public int[] RawNotes { get; init; } = Array.Empty<int>();
}

/// <summary>
/// A single detected pattern instance before clustering.
/// Port of Prelude FoundPattern.
/// </summary>
public sealed class InterludeFoundPattern
{
	public InterludeCorePattern Pattern { get; init; }
	public string? SpecificType { get; init; }
	public bool Mixed { get; init; }
	public float Start { get; init; }
	public float End { get; init; }
	public float MsPerBeat { get; init; }
	public float Density { get; init; }
}

/// <summary>
/// Clustered pattern summary for display, matching Interlude's PatternReport clusters.
/// </summary>
public sealed class InterludePatternCluster
{
	public InterludeCorePattern Pattern { get; init; }
	public string DisplayName { get; init; } = string.Empty;
	public IReadOnlyList<(string Name, float Fraction)> SpecificTypes { get; init; } = Array.Empty<(string, float)>();
	public int Bpm { get; init; }
	public bool Mixed { get; init; }
	public float AmountMs { get; init; }
	public float Importance { get; init; }

	public string FormatLabel(float rate = 1.0f)
	{
		var bpm = Bpm * rate;
		return Mixed
			? $"~{bpm:F0} Mixed {DisplayName}"
			: $"{bpm:F0} {DisplayName}";
	}
}

/// <summary>
/// Full Interlude-style pattern report for a chart.
/// </summary>
public sealed class InterludePatternReport
{
	public IReadOnlyList<InterludeFoundPattern> FoundPatterns { get; init; } = Array.Empty<InterludeFoundPattern>();
	public IReadOnlyList<InterludePatternCluster> Clusters { get; init; } = Array.Empty<InterludePatternCluster>();
	public string Category { get; init; } = "Unknown";
	public float DurationMs { get; init; }
	public float FirstNoteTimeMs { get; init; }
	public int TotalRows { get; init; }
}

public delegate int InterludePatternRecogniser(List<InterludeRowInfo> rows);

public sealed class InterludeSpecificPatterns
{
	public required List<(string Name, InterludePatternRecogniser Recogniser)> Stream { get; init; }
	public required List<(string Name, InterludePatternRecogniser Recogniser)> Chordstream { get; init; }
	public required List<(string Name, InterludePatternRecogniser Recogniser)> Jack { get; init; }
}
