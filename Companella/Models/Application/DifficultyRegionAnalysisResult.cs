using Companella.Models.Beatmap;
using Companella.Models.Training;

namespace Companella.Models.Application;

/// <summary>
/// Live analysis result for a single region pair.
/// </summary>
public class DifficultyRegionAnalysisResult
{
	public Guid PairId { get; init; }

	public bool IsValid { get; init; }

	public string? ErrorMessage { get; init; }

	public int NoteCount { get; init; }

	public PatternAnalysisResult? Patterns { get; init; }

	public DanClassificationResult? Dan { get; init; }

	public static DifficultyRegionAnalysisResult Invalid(Guid pairId, string message) =>
		new()
		{
			PairId = pairId,
			IsValid = false,
			ErrorMessage = message,
			NoteCount = 0
		};

	public static DifficultyRegionAnalysisResult Success(
		Guid pairId,
		int noteCount,
		PatternAnalysisResult patterns,
		DanClassificationResult? dan) =>
		new()
		{
			PairId = pairId,
			IsValid = true,
			NoteCount = noteCount,
			Patterns = patterns,
			Dan = dan
		};
}
