using Companella.Models.Beatmap;

namespace Companella.Models.Application;

/// <summary>
/// Request payload for splitting a difficulty into region-based copies.
/// </summary>
public class DifficultySplitterRequest
{
	public required OsuFile Source { get; init; }

	public List<DifficultyRegionMarker> Markers { get; init; } = new();

	public List<DifficultyRegionPair> Pairs { get; init; } = new();
}
