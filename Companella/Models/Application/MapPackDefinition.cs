using System.Text.Json.Serialization;

namespace Companella.Models.Application;

/// <summary>
/// Defines a map pack: unified metadata plus an ordered list of cross-map difficulties.
/// </summary>
public class MapPackDefinition
{
	public const string SidecarFileName = ".companella.mappack.json";
	public const string DefaultArtist = "Various Artists";
	public const string AutoExportTag = "companella";

	[JsonPropertyName("version")]
	public int Version { get; set; } = 1;

	[JsonPropertyName("title")]
	public string Title { get; set; } = "Map Pack";

	[JsonPropertyName("artist")]
	public string Artist { get; set; } = DefaultArtist;

	[JsonPropertyName("creator")]
	public string Creator { get; set; } = "Companella";

	[JsonPropertyName("tags")]
	public string Tags { get; set; } = string.Empty;

	[JsonPropertyName("source")]
	public string Source { get; set; } = string.Empty;

	[JsonPropertyName("outputFolderName")]
	public string OutputFolderName { get; set; } = string.Empty;

	[JsonPropertyName("entries")]
	public List<MapPackEntry> Entries { get; set; } = new();

	/// <summary>
	/// Folder path when loaded from an existing pack (not serialized).
	/// </summary>
	[JsonIgnore]
	public string? LoadedFolderPath { get; set; }

	/// <summary>
	/// When true, exported difficulties get BeatmapID 0 and BeatmapSetID -1 (new packs only).
	/// </summary>
	[JsonIgnore]
	public bool ResetOnlineIdsOnExport { get; set; } = true;

	public string GetEffectiveOutputFolderName()
	{
		if (!string.IsNullOrWhiteSpace(OutputFolderName))
			return SanitizeFolderName(OutputFolderName.Trim());

		var artist = string.IsNullOrWhiteSpace(Artist) ? DefaultArtist : Artist.Trim();
		var title = string.IsNullOrWhiteSpace(Title) ? "Map Pack" : Title.Trim();
		return SanitizeFolderName($"{artist} - {title}");
	}

	public static string SanitizeFolderName(string name)
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
	}

	/// <summary>
	/// Tags written to exported .osu files, always including <see cref="AutoExportTag"/>.
	/// </summary>
	public string GetExportTags()
	{
		var userTags = Tags.Trim();
		if (string.IsNullOrEmpty(userTags))
			return AutoExportTag;

		var parts = userTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Any(p => p.Equals(AutoExportTag, StringComparison.OrdinalIgnoreCase)))
			return userTags;

		return $"{userTags} {AutoExportTag}";
	}

	public MapPackDefinition Clone()
	{
		return new MapPackDefinition
		{
			Version = Version,
			Title = Title,
			Artist = Artist,
			Creator = Creator,
			Tags = Tags,
			Source = Source,
			OutputFolderName = OutputFolderName,
			LoadedFolderPath = LoadedFolderPath,
			ResetOnlineIdsOnExport = ResetOnlineIdsOnExport,
			Entries = Entries.Select(e => new MapPackEntry
			{
				Id = e.Id,
				SourceFilePath = e.SourceFilePath,
				VersionOverride = e.VersionOverride,
				Rate = e.Rate,
				RatePitchAdjust = e.RatePitchAdjust,
				HP = e.HP,
				OD = e.OD,
				SortOrder = e.SortOrder,
				OsuFile = e.OsuFile,
				DominantBpm = e.DominantBpm,
				MsdValues = e.MsdValues,
				FileHash = e.FileHash,
				SourceMissing = e.SourceMissing
			}).ToList()
		};
	}
}
