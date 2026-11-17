using System.Diagnostics;
using System.Text.RegularExpressions;
using Companella.Services.Common;
using OsuMemoryDataProvider;

namespace Companella.Services.Platform;

/// <summary>
/// Detects running osu! process and monitors for beatmap changes.
/// Uses memory reading for song selection and window title for editor mode.
/// </summary>
public class OsuProcessDetector : IDisposable
{
	private Process? _osuProcess;
	private string? _songsFolder;
	private string? _osuDirectory;
	private FileSystemWatcher? _watcher;
	private string? _lastWindowTitle;
	private string? _lastMemoryBeatmapPath;
	private readonly object _lockObj = new();

	// Memory reader for song selection
	private StructuredOsuMemoryReader? _memoryReader;


	// Settings service for caching osu! directory
	private UserSettingsService? _settingsService;

	// Track recently modified files
	private readonly Dictionary<string, DateTime> _recentlyModified = new();
	private readonly TimeSpan _modificationWindow = TimeSpan.FromSeconds(5);

	public OsuProcessDetector()
	{
		_memoryReader = StructuredOsuMemoryReader.Instance;
	}

	/// <summary>
	/// Sets the settings service for caching the osu! directory path.
	/// </summary>
	public void SetSettingsService(UserSettingsService settingsService)
	{
		_settingsService = settingsService;

		// Load cached directory: manual path when auto-detect is off; last known path when auto-detect is on
		if (!string.IsNullOrEmpty(_settingsService.Settings.CachedOsuDirectory))
		{
			_osuDirectory = _settingsService.Settings.CachedOsuDirectory;
			_songsFolder = Path.Combine(_osuDirectory, "Songs");

			if (Directory.Exists(_songsFolder))
			{
				Logger.Info($"[Detect] Using cached osu! directory: {_osuDirectory}");
			}
			else
			{
				Logger.Info($"[Detect] Cached Songs folder not found: {_songsFolder}");
				_songsFolder = null;
			}
		}
	}

	/// <summary>
	/// Event raised when a beatmap file is modified.
	/// </summary>
	public event EventHandler<string>? BeatmapFileModified;

	/// <summary>
	/// Currently detected beatmap file path.
	/// </summary>
	public string? CurrentBeatmapPath { get; private set; }

	/// <summary>
	/// Whether osu! process is currently detected.
	/// </summary>
	public bool IsOsuRunning
	{
		get
		{
			if (_osuProcess == null)
				return false;
			try
			{
				return !_osuProcess.HasExited;
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>
	/// Attempts to find and attach to the osu! process.
	/// </summary>
	public bool TryAttachToOsu()
	{
		// Try to find osu! stable
		var processes = Process.GetProcessesByName("osu!");

		if (processes.Length == 0) processes = Process.GetProcessesByName("osu");

		if (processes.Length == 0)
		{
			_osuProcess = null;
			return false;
		}

		_osuProcess = processes[0];
		Logger.Info($"[Detect] Attached to osu! process: PID {_osuProcess.Id}");

		// Get osu! directory and Songs folder
		_osuDirectory = GetOsuDirectory();
		_songsFolder = GetSongsFolderFromDirectory(_osuDirectory);

		// Cache the directory for when osu! is not running (only when auto-detect is enabled so manual paths are not overwritten)
		if (_osuDirectory != null && _settingsService != null && _settingsService.Settings.AutoDetectOsuDirectory)
			if (_settingsService.Settings.CachedOsuDirectory != _osuDirectory)
			{
				_settingsService.Settings.CachedOsuDirectory = _osuDirectory;
				Task.Run(async () => await _settingsService.SaveAsync());
				Logger.Info($"[Detect] Cached osu! directory: {_osuDirectory}");
			}

		if (_songsFolder != null)
		{
			StartFileWatcher();
			Logger.Info($"[Detect] Watching Songs folder: {_songsFolder}");
		}
		else
		{
			Logger.Info("[Detect] Could not find Songs folder");
		}

		return true;
	}

	/// <summary>
	/// Gets the current mods from osu! memory.
	/// Returns the raw mod flags.
	/// </summary>
	public int GetCurrentMods()
	{
		if (_memoryReader == null || !_memoryReader.CanRead) return 0;

		try
		{
			lock (HitErrorReaderService.MemoryReaderLock)
			{
				var generalData = new OsuMemoryDataProvider.OsuMemoryModels.Direct.GeneralData();
				if (_memoryReader.TryRead(generalData)) return generalData.Mods;
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Memory] Error reading mods: {ex.Message}");
		}

		return 0;
	}

	/// <summary>
	/// Gets the rate multiplier based on current mods.
	/// Returns 1.5 for DT/NC, 0.75 for HT, or 1.0 for no speed mods.
	/// </summary>
	public float GetCurrentRateFromMods()
	{
		var mods = GetCurrentMods();

		// Mod flags:
		// DT (Double Time) = 64 (bit 6)
		// NC (Nightcore) = 512 (bit 9) - implies DT
		// HT (Half Time) = 256 (bit 8)

		const int MOD_DT = 64;
		const int MOD_NC = 512;
		const int MOD_HT = 256;

		if ((mods & MOD_NC) != 0 || (mods & MOD_DT) != 0) return 1.5f;

		if ((mods & MOD_HT) != 0) return 0.75f;

		return 1.0f;
	}

	/// <summary>
	/// Checks whether autoplay-related mods are active in the given mod flag set.
	/// </summary>
	/// <param name="mods">osu! mod flags.</param>
	/// <returns>True when Autoplay (AT) or Autopilot (AP) is active.</returns>
	public static bool HasAutoMod(int mods)
	{
		const int MOD_AUTOPLAY = 2048;
		const int MOD_AUTOPILOT = 8192;

		return (mods & (MOD_AUTOPLAY | MOD_AUTOPILOT)) != 0;
	}

	/// <summary>
	/// Checks if AUTO mod is currently active.
	/// </summary>
	/// <returns>True if AUTO mod is active, false otherwise.</returns>
	public bool HasAutoMod()
	{
		return HasAutoMod(GetCurrentMods());
	}

	/// <summary>
	/// Resolves the best available beatmap path using captured, memory, cached, and window-title fallbacks.
	/// </summary>
	public string? ResolveBeatmapPath(string? capturedPath = null)
	{
		if (!string.IsNullOrEmpty(capturedPath) && File.Exists(capturedPath))
			return capturedPath;

		var fromMemory = GetBeatmapFromMemory();
		if (!string.IsNullOrEmpty(fromMemory))
			return fromMemory;

		if (!string.IsNullOrEmpty(CurrentBeatmapPath) && File.Exists(CurrentBeatmapPath))
			return CurrentBeatmapPath;

		return GetBeatmapFromWindowTitle();
	}

	/// <summary>
	/// Gets the current beatmap path from osu! memory.
	/// Works in song selection mode.
	/// </summary>
	public string? GetBeatmapFromMemory()
	{
		if (_memoryReader == null || _songsFolder == null) return null;

		string? beatmapFolder;
		string? beatmapFile;

		try
		{
			lock (HitErrorReaderService.MemoryReaderLock)
			{
				if (!_memoryReader.CanRead) return null;

				// Read CurrentBeatmap directly - this contains the selected beatmap info
				var currentBeatmap = new OsuMemoryDataProvider.OsuMemoryModels.Direct.CurrentBeatmap();
				if (!_memoryReader.TryRead(currentBeatmap)) return null;

				beatmapFolder = currentBeatmap.FolderName;
				beatmapFile = currentBeatmap.OsuFileName;
			}

			if (string.IsNullOrEmpty(beatmapFolder) || string.IsNullOrEmpty(beatmapFile)) return null;

			var fullPath = Path.Combine(_songsFolder, beatmapFolder, beatmapFile);

			if (File.Exists(fullPath))
			{
				if (fullPath != _lastMemoryBeatmapPath)
				{
					_lastMemoryBeatmapPath = fullPath;
					Logger.Info($"[Memory] {beatmapFolder}/{beatmapFile}");
				}

				CurrentBeatmapPath = fullPath;
				return fullPath;
			}
			else
			{
				// Try fuzzy folder match
				try
				{
					var possibleDirs = Directory.GetDirectories(_songsFolder, $"*{beatmapFolder}*",
						SearchOption.TopDirectoryOnly);
					foreach (var dir in possibleDirs)
					{
						var possiblePath = Path.Combine(dir, beatmapFile);
						if (File.Exists(possiblePath))
						{
							if (possiblePath != _lastMemoryBeatmapPath)
							{
								_lastMemoryBeatmapPath = possiblePath;
								Logger.Info($"[Memory] {Path.GetFileName(dir)}/{beatmapFile}");
							}

							CurrentBeatmapPath = possiblePath;
							return possiblePath;
						}
					}
				}
				catch
				{
					/* Ignore search errors */
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Memory] Error: {ex.Message}");
		}

		return null;
	}

	/// <summary>
	/// Starts the file system watcher on the Songs folder.
	/// </summary>
	private void StartFileWatcher()
	{
		if (_songsFolder == null || _watcher != null)
			return;

		try
		{
			_watcher = new FileSystemWatcher(_songsFolder)
			{
				Filter = "*.osu",
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
			};

			_watcher.Changed += OnFileChanged;
			_watcher.Created += OnFileChanged;
			_watcher.EnableRaisingEvents = true;

			Logger.Info("[Detect] FileSystemWatcher started");
		}
		catch (Exception ex)
		{
			Logger.Info($"[Detect] Failed to start FileSystemWatcher: {ex.Message}");
		}
	}

	/// <summary>
	/// Handles file change events.
	/// </summary>
	private void OnFileChanged(object sender, FileSystemEventArgs e)
	{
		if (!e.FullPath.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
			return;

		lock (_lockObj)
		{
			var now = DateTime.Now;

			// Debounce - ignore if we just saw this file
			if (_recentlyModified.TryGetValue(e.FullPath, out var lastTime))
				if (now - lastTime < TimeSpan.FromMilliseconds(500))
					return;

			_recentlyModified[e.FullPath] = now;

			// Clean up old entries
			var oldKeys = _recentlyModified.Where(kvp => now - kvp.Value > _modificationWindow)
				.Select(kvp => kvp.Key).ToList();
			foreach (var key in oldKeys)
				_recentlyModified.Remove(key);
		}

		Logger.Info($"[Detect] File modified: {Path.GetFileName(e.FullPath)}");
		CurrentBeatmapPath = e.FullPath;
		BeatmapFileModified?.Invoke(this, e.FullPath);
	}

	/// <summary>
	/// Gets the beatmap path from the osu! window title.
	/// Works in both editor mode and song selection mode.
	/// </summary>
	public string? GetBeatmapFromWindowTitle()
	{
		if (_osuProcess == null || _songsFolder == null)
			return null;

		try
		{
			_osuProcess.Refresh();
			var title = _osuProcess.MainWindowTitle;

			if (string.IsNullOrEmpty(title) || title == "osu!")
				return null;

			// Check if title changed
			if (title == _lastWindowTitle)
				return CurrentBeatmapPath;

			_lastWindowTitle = title;
			Logger.Info($"[Detect] Window title: {title}");

			// Remove .osu suffix if present (editor sometimes adds this)
			var cleanTitle = Regex.Replace(title, @"\.osu$", "");

			// Pattern 1: Editor mode - "osu! - Artist - Title (Creator) [Difficulty]"
			// Handle cases like "osu!  - azazal - azazal - meow or never!! (Leyna) [(?3?)?]"
			var match = Regex.Match(cleanTitle, @"osu!\s+-\s+(.+?)\s+-\s+(.+?)\s+\(([^)]+)\)\s+\[([^\]]+)\]$");
			if (match.Success)
			{
				var artist = match.Groups[1].Value.Trim();
				var songTitle = match.Groups[2].Value.Trim();
				var creator = match.Groups[3].Value.Trim();
				var difficulty = match.Groups[4].Value.Trim();
				Logger.Info($"[Detect] Editor mode (full): {artist} - {songTitle} ({creator}) [{difficulty}]");
				return FindBeatmapFile(artist, songTitle, difficulty);
			}

			// Pattern 2: Editor mode simpler - "osu! - Artist - Title [Difficulty]"
			match = Regex.Match(cleanTitle, @"osu!\s+-\s+(.+?)\s+-\s+(.+?)\s+\[([^\]]+)\]$");
			if (match.Success)
			{
				var artist = match.Groups[1].Value.Trim();
				var songTitle = match.Groups[2].Value.Trim();
				var difficulty = match.Groups[3].Value.Trim();
				Logger.Info($"[Detect] Editor mode: {artist} - {songTitle} [{difficulty}]");
				return FindBeatmapFile(artist, songTitle, difficulty);
			}

			// Pattern 3: Song select - "osu! - Artist - Title"
			match = Regex.Match(cleanTitle, @"osu!\s+-\s+(.+?)\s+-\s+(.+)$");
			if (match.Success)
			{
				var artist = match.Groups[1].Value.Trim();
				var songTitle = match.Groups[2].Value.Trim();
				Logger.Info($"[Detect] Song select: {artist} - {songTitle}");
				return FindBeatmapFile(artist, songTitle, null);
			}

			// Pattern 4: Simple format - "osu! - Title"
			match = Regex.Match(cleanTitle, @"osu!\s+-\s+(.+)$");
			if (match.Success)
			{
				var songName = match.Groups[1].Value.Trim();
				Logger.Info($"[Detect] Simple title: {songName}");
				return FindBeatmapFile(null, songName, null);
			}

			Logger.Info($"[Detect] Could not parse window title");
			return null;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Detect] Error reading window title: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Finds a beatmap file matching the given metadata.
	/// Searches recursively through all subdirectories.
	/// </summary>
	/// <param name="artist">Artist name (optional)</param>
	/// <param name="title">Song title (may include difficulty in brackets)</param>
	/// <param name="difficulty">Difficulty name (optional - if null, returns first matching file)</param>
	private string? FindBeatmapFile(string? artist, string title, string? difficulty)
	{
		if (_songsFolder == null)
			return null;

		try
		{
			// Extract clean title without difficulty brackets for folder matching
			// "Gde moj dom [0.9x [31.38]]" -> "Gde moj dom"
			var cleanTitle = Regex.Replace(title, @"\s*\[.*$", "").Trim();

			// Also extract any difficulty from the title if not provided
			if (difficulty == null)
			{
				var diffMatch = Regex.Match(title, @"\[([^\[\]]+(?:\[[^\]]*\])?[^\[\]]*)\]$");
				if (diffMatch.Success) difficulty = diffMatch.Groups[1].Value;
			}

			Logger.Info($"[Search] Artist='{artist}', CleanTitle='{cleanTitle}', Diff='{difficulty}'");

			var candidates = new List<string>();

			// First try: find folders matching artist-title pattern
			var searchPatterns = new List<string>();
			if (!string.IsNullOrEmpty(artist))
			{
				searchPatterns.Add($"*{artist}*{cleanTitle}*");
				searchPatterns.Add($"*{cleanTitle}*");
			}
			else
			{
				searchPatterns.Add($"*{cleanTitle}*");
			}

			foreach (var pattern in searchPatterns)
			{
				try
				{
					var matchingDirs = Directory.GetDirectories(_songsFolder, pattern, SearchOption.TopDirectoryOnly);
					foreach (var dir in matchingDirs)
					{
						var osuFiles = Directory.GetFiles(dir, "*.osu");
						candidates.AddRange(osuFiles);
					}
				}
				catch
				{
					/* Ignore pattern matching errors */
				}

				if (candidates.Count > 0)
					break;
			}

			// Fallback: search all .osu files if no folder match
			if (candidates.Count == 0)
			{
				var allOsuFiles = Directory.EnumerateFiles(_songsFolder, "*.osu", SearchOption.AllDirectories);

				foreach (var osuFile in allOsuFiles)
				{
					var fileName = Path.GetFileName(osuFile);
					var dirName = Path.GetFileName(Path.GetDirectoryName(osuFile) ?? "");

					var matchesTitle = fileName.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase) ||
									   dirName.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase);

					var matchesArtist = string.IsNullOrEmpty(artist) ||
										fileName.Contains(artist, StringComparison.OrdinalIgnoreCase) ||
										dirName.Contains(artist, StringComparison.OrdinalIgnoreCase);

					if (matchesTitle && matchesArtist) candidates.Add(osuFile);
				}
			}

			Logger.Info($"[Search] Found {candidates.Count} candidates");

			if (candidates.Count == 0) return null;

			// If difficulty specified, try to find exact match
			if (!string.IsNullOrEmpty(difficulty))
			{
				// Exact bracket match
				foreach (var candidate in candidates)
				{
					var fileName = Path.GetFileName(candidate);
					if (fileName.Contains($"[{difficulty}]", StringComparison.OrdinalIgnoreCase))
					{
						Logger.Info($"[Search] Exact diff match: {fileName}");
						return candidate;
					}
				}

				// Partial difficulty match
				foreach (var candidate in candidates)
				{
					var fileName = Path.GetFileName(candidate);
					if (fileName.Contains(difficulty, StringComparison.OrdinalIgnoreCase))
					{
						Logger.Info($"[Search] Partial diff match: {fileName}");
						return candidate;
					}
				}
			}

			// Return first candidate
			Logger.Info($"[Search] Using first: {Path.GetFileName(candidates[0])}");
			return candidates[0];
		}
		catch (Exception ex)
		{
			Logger.Info($"[Search] Error: {ex.Message}");
		}

		return null;
	}

	/// <summary>
	/// Finds recently modified .osu files in the Songs folder.
	/// </summary>
	public string? FindRecentlyModifiedBeatmap(TimeSpan? maxAge = null)
	{
		if (_songsFolder == null)
		{
			_songsFolder = GetSongsFolder();
			if (_songsFolder == null)
				return null;
		}

		maxAge ??= TimeSpan.FromMinutes(5);

		try
		{
			// First check our tracked modifications
			lock (_lockObj)
			{
				var recent = _recentlyModified
					.Where(kvp => DateTime.Now - kvp.Value < maxAge)
					.OrderByDescending(kvp => kvp.Value)
					.FirstOrDefault();

				if (recent.Key != null)
					return recent.Key;
			}

			// Fall back to file system scan
			var recentFile = Directory.EnumerateFiles(_songsFolder, "*.osu", SearchOption.AllDirectories)
				.Select(f => new FileInfo(f))
				.Where(fi => DateTime.Now - fi.LastWriteTime < maxAge)
				.OrderByDescending(fi => fi.LastWriteTime)
				.FirstOrDefault();

			return recentFile?.FullName;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Detect] Error scanning for recent files: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Gets the osu! installation directory.
	/// Returns the cached directory if osu! is not running.
	/// </summary>
	public string? GetOsuDirectory()
	{
		// Manual path only: no process or default-path fallback
		if (_settingsService != null && !_settingsService.Settings.AutoDetectOsuDirectory)
		{
			var manual = _settingsService.Settings.CachedOsuDirectory?.Trim();
			if (!string.IsNullOrEmpty(manual) && Directory.Exists(manual))
			{
				_osuDirectory = manual;
				GetSongsFolderFromDirectory(_osuDirectory);
				return _osuDirectory;
			}

			return null;
		}

		// If we have a cached directory, return it
		if (!string.IsNullOrEmpty(_osuDirectory) && Directory.Exists(_osuDirectory)) return _osuDirectory;

		// Try to get from running process
		var fromProcess = GetOsuDirectoryFromProcess();
		if (fromProcess != null)
		{
			_osuDirectory = fromProcess;
			return fromProcess;
		}

		// Fallback 1: check cached directory from settings (specified manual path)
		if (_settingsService != null)
		{
			var cachedDir = _settingsService.Settings.CachedOsuDirectory;
			if (!string.IsNullOrEmpty(cachedDir) && Directory.Exists(cachedDir))
			{
				_osuDirectory = cachedDir;
				return cachedDir;
			}
		}

		// Fallback 2: check common locations
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		var defaultPath = Path.Combine(appData, "osu!");
		if (Directory.Exists(defaultPath))
		{
			_osuDirectory = defaultPath;
			return defaultPath;
		}

		return null;
	}

	/// <summary>
	/// Gets the osu! installation directory directly from the running process.
	/// </summary>
	private string? GetOsuDirectoryFromProcess()
	{
		if (_osuProcess == null)
			return null;

		try
		{
			var mainModule = _osuProcess.MainModule;
			if (mainModule != null) return Path.GetDirectoryName(mainModule.FileName);
		}
		catch (Exception ex)
		{
			Logger.Info($"[Detect] Error getting osu! directory from process: {ex.Message}");
		}

		return null;
	}

	/// <summary>
	/// Gets the Songs folder path for osu!.
	/// Returns the cached Songs folder if osu! is not running.
	/// </summary>
	public string? GetSongsFolder()
	{
		// If we have a cached songs folder, return it
		if (!string.IsNullOrEmpty(_songsFolder) && Directory.Exists(_songsFolder)) return _songsFolder;

		// Try to get from osu! directory
		var osuDir = GetOsuDirectory();
		return GetSongsFolderFromDirectory(osuDir);
	}

	/// <summary>
	/// Gets the Songs folder path from a given osu! directory.
	/// </summary>
	private string? GetSongsFolderFromDirectory(string? osuDir)
	{
		if (osuDir == null)
			return null;

		var songsFolder = Path.Combine(osuDir, "Songs");
		if (Directory.Exists(songsFolder))
		{
			_songsFolder = songsFolder;
			return songsFolder;
		}

		Logger.Info($"[Detect] Songs folder not found at: {songsFolder}");
		return null;
	}

	/// <summary>
	/// Gets osu! process information.
	/// </summary>
	public OsuProcessInfo? GetProcessInfo()
	{
		if (_osuProcess == null)
			return null;

		try
		{
			if (_osuProcess.HasExited)
				return null;
		}
		catch
		{
			return null;
		}

		return new OsuProcessInfo
		{
			ProcessId = _osuProcess.Id,
			ProcessName = _osuProcess.ProcessName,
			MainWindowTitle = _osuProcess.MainWindowTitle,
			OsuDirectory = GetOsuDirectory(),
			SongsFolder = _songsFolder
		};
	}

	/// <summary>
	/// Gets the username from osu! configuration file.
	/// Reads from osu!.*.cfg file in the osu! directory.
	/// </summary>
	public string? GetUsername()
	{
		var osuDir = GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir)) return null;

		try
		{
			// Find osu!.*.cfg files
			var configFiles = Directory.GetFiles(osuDir, "osu!.*.cfg");

			foreach (var configFile in configFiles)
			{
				// Skip osu!.cfg (that's the main config, not user config)
				if (Path.GetFileName(configFile).Equals("osu!.cfg", StringComparison.OrdinalIgnoreCase))
					continue;

				// Extract username from filename: osu!.username.cfg
				var fileName = Path.GetFileNameWithoutExtension(configFile);
				if (fileName.StartsWith("osu!.", StringComparison.OrdinalIgnoreCase))
				{
					var username = fileName.Substring(5); // Remove "osu!." prefix
					if (!string.IsNullOrEmpty(username))
					{
						Logger.Info($"[Detect] Found username from config file: {username}");
						return username;
					}
				}
			}

			// Fallback: Read Username line from osu!.cfg
			var mainConfig = Path.Combine(osuDir, "osu!.cfg");
			if (File.Exists(mainConfig))
			{
				var lines = File.ReadAllLines(mainConfig);
				foreach (var line in lines)
					if (line.StartsWith("Username", StringComparison.OrdinalIgnoreCase))
					{
						var parts = line.Split('=', 2);
						if (parts.Length == 2)
						{
							var username = parts[1].Trim();
							if (!string.IsNullOrEmpty(username))
							{
								Logger.Info($"[Detect] Found username from osu!.cfg: {username}");
								return username;
							}
						}
					}
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Detect] Error reading username from config: {ex.Message}");
		}

		return null;
	}

	/// <summary>
	/// Detaches from the osu! process and stops monitoring.
	/// </summary>
	public void Detach()
	{
		if (_watcher != null)
		{
			_watcher.EnableRaisingEvents = false;
			_watcher.Dispose();
			_watcher = null;
		}

		_osuProcess = null;
		CurrentBeatmapPath = null;
		_lastWindowTitle = null;
		_lastMemoryBeatmapPath = null;

		lock (_lockObj)
		{
			_recentlyModified.Clear();
		}
	}

	private void Dispose(bool isDisposing)
	{
		Detach();
		_memoryReader = null;
		if (isDisposing)
		{
			_watcher?.Dispose();
			_watcher = null;
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~OsuProcessDetector()
	{
		Dispose(false);
	}
}

/// <summary>
/// Information about the detected osu! process.
/// </summary>
public class OsuProcessInfo
{
	public int ProcessId { get; set; }
	public string ProcessName { get; set; } = string.Empty;
	public string MainWindowTitle { get; set; } = string.Empty;
	public string? OsuDirectory { get; set; }
	public string? SongsFolder { get; set; }
}
