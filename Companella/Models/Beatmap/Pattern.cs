namespace Companella.Models.Beatmap;

/// <summary>
/// Types of patterns that can be detected in osu!mania maps.
/// </summary>
public enum PatternType
{
	/// <summary>
	/// Alternating notes between 2 columns.
	/// </summary>
	Trill,

	/// <summary>
	/// 3+ consecutive notes in the same column.
	/// </summary>
	Jack,

	/// <summary>
	/// 2 consecutive notes in the same column (very fast).
	/// </summary>
	Minijack,

	/// <summary>
	/// Continuous notes with consistent intervals, no repeated columns.
	/// </summary>
	Stream,

	/// <summary>
	/// Two notes at the same time (2-note chord).
	/// </summary>
	Jump,

	/// <summary>
	/// Three notes at the same time (3-note chord).
	/// </summary>
	Hand,

	/// <summary>
	/// Four notes at the same time (4-note chord).
	/// </summary>
	Quad,

	/// <summary>
	/// Stream pattern with jumps interspersed.
	/// </summary>
	Jumpstream,

	/// <summary>
	/// Stream pattern with hands interspersed.
	/// </summary>
	Handstream,

	/// <summary>
	/// Consecutive chords in the same column positions (chord jacks).
	/// </summary>
	Chordjack,

	/// <summary>
	/// Sequential column movement (ascending/descending).
	/// </summary>
	Roll,

	/// <summary>
	/// Multiple trills using adjacent columns.
	/// </summary>
	Bracket,

	/// <summary>
	/// Alternating jumps between column pairs.
	/// </summary>
	Jumptrill
}

/// <summary>
/// Represents a detected pattern instance in a beatmap.
/// </summary>
public class Pattern
{
	/// <summary>
	/// The type of pattern detected.
	/// </summary>
	public PatternType Type { get; set; }

	/// <summary>
	/// Start time of the pattern in milliseconds.
	/// </summary>
	public double StartTime { get; set; }

	/// <summary>
	/// End time of the pattern in milliseconds.
	/// </summary>
	public double EndTime { get; set; }

	/// <summary>
	/// BPM at the start of the pattern.
	/// </summary>
	public double Bpm { get; set; }

	/// <summary>
	/// Columns involved in the pattern.
	/// </summary>
	public List<int> Columns { get; set; } = new();

	/// <summary>
	/// Number of notes in the pattern.
	/// </summary>
	public int NoteCount { get; set; }

	/// <summary>
	/// Interlude-specific pattern name when detected (e.g. "Jumpstream", "Minijacks").
	/// </summary>
	public string? SpecificName { get; set; }

	/// <summary>
	/// Whether this pattern instance has unstable BPM spacing (Interlude "Mixed").
	/// </summary>
	public bool Mixed { get; set; }

	/// <summary>
	/// Interlude core pattern category: Stream, Chordstream, or Jacks.
	/// </summary>
	public string? CorePattern { get; set; }

	/// <summary>
	/// Duration of the pattern in milliseconds.
	/// </summary>
	public double Duration => EndTime - StartTime;

	/// <summary>
	/// Notes per second in this pattern.
	/// </summary>
	public double NotesPerSecond => Duration > 0 ? NoteCount / (Duration / 1000.0) : 0;

	/// <summary>
	/// Gets a short display name for the pattern type.
	/// </summary>
	public string DisplayName => SpecificName ?? ShortName;

	public string ShortName => Type switch
	{
		PatternType.Trill => "Trill",
		PatternType.Jack => "Jack",
		PatternType.Minijack => "Minijack",
		PatternType.Stream => "Stream",
		PatternType.Jump => "Jump",
		PatternType.Hand => "Hand",
		PatternType.Quad => "Quad",
		PatternType.Jumpstream => "JS",
		PatternType.Handstream => "HS",
		PatternType.Chordjack => "CJ",
		PatternType.Roll => "Roll",
		PatternType.Bracket => "Bracket",
		PatternType.Jumptrill => "JT",
		_ => Type.ToString()
	};

	public override string ToString()
	{
		return $"{ShortName} [{StartTime:F0}-{EndTime:F0}ms] {NoteCount} notes @ {Bpm:F0} BPM";
	}
}
