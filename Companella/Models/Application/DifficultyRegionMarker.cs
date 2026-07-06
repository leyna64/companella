namespace Companella.Models.Application;

/// <summary>
/// A named time marker on a beatmap. Two markers with the same name define one region (start/end by time).
/// </summary>
public class DifficultyRegionMarker
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Marker";

	public double TimeMs { get; set; }
}
