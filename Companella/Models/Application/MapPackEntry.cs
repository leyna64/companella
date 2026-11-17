using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Companella.Extensions;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Services.Beatmap;

namespace Companella.Models.Application;

/// <summary>
/// Represents a single difficulty entry in a map pack.
/// </summary>
public class MapPackEntry
{
	/// <summary>
	/// Original source .osu path used when building the pack (for re-edit via sidecar).
	/// </summary>
	[JsonPropertyName("sourceFilePath")]
	public string SourceFilePath { get; set; } = string.Empty;

	/// <summary>
	/// Optional difficulty name override. When empty, the source map version is used.
	/// </summary>
	[JsonPropertyName("versionOverride")]
	public string VersionOverride { get; set; } = string.Empty;

	/// <summary>
	/// Playback rate multiplier (default 1.0).
	/// </summary>
	[JsonPropertyName("rate")]
	public double Rate { get; set; } = 1.0;

	/// <summary>
	/// When true, audio pitch follows the rate (DT/HT style).
	/// </summary>
	[JsonPropertyName("ratePitchAdjust")]
	public bool RatePitchAdjust { get; set; } = true;

	/// <summary>
	/// Custom HP Drain Rate. Null keeps the source (or rate-changed) value.
	/// </summary>
	[JsonPropertyName("hp")]
	public double? HP { get; set; }

	/// <summary>
	/// Custom Overall Difficulty. Null keeps the source (or rate-changed) value.
	/// </summary>
	[JsonPropertyName("od")]
	public double? OD { get; set; }

	/// <summary>
	/// Display order within the pack.
	/// </summary>
	[JsonPropertyName("sortOrder")]
	public int SortOrder { get; set; }

	/// <summary>
	/// Unique identifier for list tracking.
	/// </summary>
	[JsonPropertyName("id")]
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>
	/// Parsed source beatmap (not serialized to sidecar).
	/// </summary>
	[JsonIgnore]
	public OsuFile? OsuFile { get; set; }

	/// <summary>
	/// True when the sidecar source path no longer exists on disk.
	/// </summary>
	[JsonIgnore]
	public bool SourceMissing { get; set; }

	/// <summary>
	/// Dominant BPM of the original beatmap.
	/// </summary>
	[JsonIgnore]
	public double DominantBpm { get; set; }

	/// <summary>
	/// MSD skillset scores at the selected rate.
	/// </summary>
	[JsonIgnore]
	public SkillsetScores? MsdValues { get; set; }

	/// <summary>
	/// MD5 hash of the source beatmap file.
	/// </summary>
	[JsonIgnore]
	public string FileHash { get; set; } = string.Empty;

	[JsonIgnore]
	public string SourceTitle => OsuFile?.DisplayTitle ?? (SourceMissing ? "Missing source" : "Unknown");

	[JsonIgnore]
	public string SourceArtist => OsuFile?.DisplayArtist ?? "";

	[JsonIgnore]
	public string SourceVersion => OsuFile?.Version ?? "";

	[JsonIgnore]
	public string SourceCreator => OsuFile?.Creator ?? "";

	[JsonIgnore]
	public string EffectiveVersion =>
		!string.IsNullOrWhiteSpace(VersionOverride) ? VersionOverride.Trim() : SourceVersion;

	[JsonIgnore]
	public double Bpm => DominantBpm * Rate;

	[JsonIgnore]
	public string RateBpmDisplay => $"{Rate:0.0#}x / {Bpm:F0}bpm";

	/// <summary>
	/// Creates a MapPackEntry from an OsuFile.
	/// </summary>
	public static MapPackEntry FromOsuFile(OsuFile osuFile)
	{
		var entry = new MapPackEntry
		{
			SourceFilePath = osuFile.FilePath,
			OsuFile = osuFile,
			DominantBpm = CalculateDominantBpm(osuFile.TimingPoints)
		};
		entry.ComputeFileHash();
		return entry;
	}

	/// <summary>
	/// Re-parses the source file if it exists.
	/// </summary>
	public void RefreshFromSource()
	{
		SourceMissing = string.IsNullOrEmpty(SourceFilePath) || !File.Exists(SourceFilePath);
		if (SourceMissing)
		{
			OsuFile = null;
			FileHash = string.Empty;
			return;
		}

		OsuFile = OsuFileParser.Parse(SourceFilePath);
		DominantBpm = CalculateDominantBpm(OsuFile.TimingPoints);
		ComputeFileHash();
		SourceMissing = false;
	}

	public void ComputeFileHash()
	{
		if (OsuFile == null || string.IsNullOrEmpty(OsuFile.FilePath) || !File.Exists(OsuFile.FilePath))
		{
			FileHash = string.Empty;
			return;
		}

		try
		{
			FileHash = File.OpenRead(OsuFile.FilePath).Md5();
		}
		catch
		{
			FileHash = string.Empty;
		}
	}

	[SuppressMessage("Globalization", "CA1310")]
	private static double CalculateDominantBpm(List<TimingPoint> timingPoints)
	{
		var uninherited = timingPoints.Where(tp => tp.Uninherited && tp.BeatLength > 0).ToList();
		if (uninherited.Count == 0)
			return 120;
		if (uninherited.Count == 1)
			return uninherited[0].Bpm;

		var bpmDurations = new Dictionary<double, double>();
		for (var i = 0; i < uninherited.Count; i++)
		{
			var bpm = Math.Round(uninherited[i].Bpm, 1);
			var duration = i < uninherited.Count - 1
				? uninherited[i + 1].Time - uninherited[i].Time
				: 60000;
			if (!bpmDurations.ContainsKey(bpm))
				bpmDurations[bpm] = 0;
			bpmDurations[bpm] += duration;
		}

		return bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
	}
}
