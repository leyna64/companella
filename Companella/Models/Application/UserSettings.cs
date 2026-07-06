using System.Text.Json.Serialization;

namespace Companella.Models.Application;

/// <summary>
/// User settings/preferences for the application.
/// </summary>
public class UserSettings
{
	/// <summary>
	/// Schema version for future compatibility.
	/// </summary>
	[JsonPropertyName("version")]
	public int Version { get; set; } = 1;

	/// <summary>
	/// Window width in pixels.
	/// </summary>
	[JsonPropertyName("windowWidth")]
	public int WindowWidth { get; set; } = 480;

	/// <summary>
	/// Window height in pixels.
	/// </summary>
	[JsonPropertyName("windowHeight")]
	public int WindowHeight { get; set; } = 810;

	/// <summary>
	/// Window X position in pixels.
	/// </summary>
	[JsonPropertyName("windowX")]
	public int WindowX { get; set; } = 100;

	/// <summary>
	/// Window Y position in pixels.
	/// </summary>
	[JsonPropertyName("windowY")]
	public int WindowY { get; set; } = 100;

	/// <summary>
	/// Window state (Normal, Maximized, etc.).
	/// </summary>
	[JsonPropertyName("windowState")]
	public string WindowState { get; set; } = "Normal";

	/// <summary>
	/// Whether overlay mode is enabled (window follows osu!).
	/// </summary>
	[JsonPropertyName("overlayMode")]
	public bool OverlayMode { get; set; } = false;

	/// <summary>
	/// Keybind for toggling overlay visibility.
	/// Format: "Alt+Q" for Alt+Q key combination
	/// </summary>
	[JsonPropertyName("toggleVisibilityKeybind")]
	public string ToggleVisibilityKeybind { get; set; } = "Alt+Q";

	/// <summary>
	/// Last used rate changer name format.
	/// </summary>
	[JsonPropertyName("rateChangerFormat")]
	public string RateChangerFormat { get; set; } = "[[name]] [[rate]]";

	/// <summary>
	/// Whether to adjust pitch when rate changing (like DT/HT).
	/// When false, only speed changes while pitch is preserved.
	/// </summary>
	[JsonPropertyName("rateChangerPitchAdjust")]
	public bool RateChangerPitchAdjust { get; set; } = true;

	/// <summary>
	/// Overlay position X offset from default position.
	/// </summary>
	[JsonPropertyName("overlayOffsetX")]
	public int OverlayOffsetX { get; set; } = 0;

	/// <summary>
	/// Overlay position Y offset from default position.
	/// </summary>
	[JsonPropertyName("overlayOffsetY")]
	public int OverlayOffsetY { get; set; } = 0;

	/// <summary>
	/// Whether to automatically start a session when the application starts.
	/// </summary>
	[JsonPropertyName("autoStartSession")]
	public bool AutoStartSession { get; set; } = false;

	/// <summary>
	/// Whether to automatically end the session when the application exits.
	/// </summary>
	[JsonPropertyName("autoEndSession")]
	public bool AutoEndSession { get; set; } = false;

	/// <summary>
	/// Whether to send anonymous usage analytics data.
	/// This helps improve the application by understanding feature usage patterns.
	/// </summary>
	[JsonPropertyName("sendAnalytics")]
	public bool SendAnalytics { get; set; } = true;

	/// <summary>
	/// URL of the beatmap API server for uploading maps during indexing.
	/// Set to empty string to disable server uploads.
	/// </summary>
	[JsonPropertyName("beatmapApiUrl")]
	public string BeatmapApiUrl { get; set; } = "http://localhost:3000";

	/// <summary>
	/// Whether to upload beatmaps to the server during indexing.
	/// </summary>
	[JsonPropertyName("uploadBeatmapsToServer")]
	public bool UploadBeatmapsToServer { get; set; } = false;

	/// <summary>
	/// When true, osu! directory is detected from the running process, settings cache, and default paths.
	/// When false, <see cref="CachedOsuDirectory"/> is the only source (manual path).
	/// </summary>
	[JsonPropertyName("autoDetectOsuDirectory")]
	public bool AutoDetectOsuDirectory { get; set; } = true;

	/// <summary>
	/// Cached osu! installation directory path.
	/// Used when osu! is not running to still access files.
	/// When <see cref="AutoDetectOsuDirectory"/> is false, this is the user-defined manual path.
	/// </summary>
	[JsonPropertyName("cachedOsuDirectory")]
	public string? CachedOsuDirectory { get; set; }

	/// <summary>
	/// Global UI scale factor (0.5 to 2.0).
	/// Default is 1.0 (100% scale).
	/// </summary>
	[JsonPropertyName("uiScale")]
	public float UIScale { get; set; } = 1.0f;

	/// <summary>
	/// MinaCalc version to use for MSD calculations.
	/// "515" = MinaCalc 5.15 (latest), "505" = MinaCalc 5.05 (legacy).
	/// Default is "515".
	/// </summary>
	[JsonPropertyName("minacalcVersion")]
	public string MinaCalcVersion { get; set; } = "515";

	/// <summary>
	/// Replay analysis window width in pixels.
	/// Default is 800 for 8:4 aspect ratio.
	/// </summary>
	[JsonPropertyName("replayAnalysisWidth")]
	public int ReplayAnalysisWidth { get; set; } = 800;

	/// <summary>
	/// Replay analysis window height in pixels.
	/// Default is 400 for 8:4 aspect ratio.
	/// </summary>
	[JsonPropertyName("replayAnalysisHeight")]
	public int ReplayAnalysisHeight { get; set; } = 400;

	/// <summary>
	/// Replay analysis window X position in pixels.
	/// </summary>
	[JsonPropertyName("replayAnalysisX")]
	public int ReplayAnalysisX { get; set; } = 100;

	/// <summary>
	/// Replay analysis window Y position in pixels.
	/// </summary>
	[JsonPropertyName("replayAnalysisY")]
	public int ReplayAnalysisY { get; set; } = 100;

	/// <summary>
	/// Whether the replay analysis window is enabled.
	/// </summary>
	[JsonPropertyName("replayAnalysisEnabled")]
	public bool ReplayAnalysisEnabled { get; set; } = true;

	/// <summary>
	/// Whether to prefer romanized metadata (title/artist) over original unicode.
	/// When true, shows "Artist - Title". When false, shows unicode versions if available.
	/// </summary>
	[JsonPropertyName("preferRomanizedMetadata")]
	public bool PreferRomanizedMetadata { get; set; } = false;

	/// <summary>
	/// Custom presets for the bulk rate changer.
	/// If null or empty, default presets are used.
	/// </summary>
	[JsonPropertyName("bulkRatePresets")]
	public List<BulkRatePreset>? BulkRatePresets { get; set; }

	/// <summary>
	/// Custom presets for osu! restart with command line arguments.
	/// If null or empty, default presets are used.
	/// </summary>
	[JsonPropertyName("osuRestartPresets")]
	public List<OsuRestartPreset>? OsuRestartPresets { get; set; }

	/// <summary>
	/// Whether to participate in dan training data collection.
	/// When enabled, shows the dan rating dialog after completing maps
	/// and sends rating data to help improve dan classification.
	/// </summary>
	[JsonPropertyName("participateDanTraining")]
	public bool ParticipateDanTraining { get; set; } = true;

	/// <summary>
	/// Whether the user has completed the first launch tutorial.
	/// When false, the tutorial overlay will be shown on startup.
	/// </summary>
	[JsonPropertyName("hasCompletedFirstLaunch")]
	public bool HasCompletedFirstLaunch { get; set; } = false;

	/// <summary>
	/// Rice dan calculator for 4K maps. Daniel mode falls back to ONNX below Alpha.
	/// </summary>
	[JsonPropertyName("riceDanCalculator")]
	public RiceDanCalculatorMode RiceDanCalculator { get; set; } = RiceDanCalculatorMode.CompanellaOnnx;

	/// <summary>
	/// Whether the difficulty splitter intro overlay has been dismissed.
	/// </summary>
	[JsonPropertyName("hasSeenDifficultySplitterIntro")]
	public bool HasSeenDifficultySplitterIntro { get; set; }
}
