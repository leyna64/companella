namespace Companella.Models.Application;

/// <summary>
/// A region derived from two same-named markers (start/end by time). Version name matches marker name.
/// </summary>
public class DifficultyRegionPair
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public Guid StartMarkerId { get; set; }

	public Guid EndMarkerId { get; set; }

	/// <summary>
	/// Used as the Version: field in the exported .osu file.
	/// </summary>
	public string VersionName { get; set; } = string.Empty;
}
