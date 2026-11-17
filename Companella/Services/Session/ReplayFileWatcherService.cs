using Companella.Extensions;
using Companella.Models.Session;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Tools;

namespace Companella.Services.Session;

/// <summary>
/// Service that watches osu! replay folders for new replay files and matches them to session plays.
/// Uses both FileSystemWatcher and polling as fallback since FSW can be unreliable on Windows.
/// </summary>
public class ReplayFileWatcherService : IDisposable
{
	private readonly OsuProcessDetector _processDetector;
	private readonly SessionDatabaseService _databaseService;
	private readonly ScoreImportService _scoreImportService;
	private readonly SessionTrackerService? _tracker;
	private readonly object _matchingLock = new();
	private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _pendingFiles = new();
	private readonly List<FileSystemWatcher> _watchers = new();
	private readonly object _lockObj = new();
	private bool _isDisposed;
	private bool _isWatching;

	// Debounce dictionary to avoid processing the same file multiple times
	private readonly Dictionary<string, DateTime> _recentlyProcessed = new();
	private readonly TimeSpan _debounceWindow = TimeSpan.FromSeconds(2);

	// Polling fallback - tracks known files and their write times
	private readonly Dictionary<string, DateTime> _knownFiles = new();
	private System.Threading.Timer? _pollingTimer;
	private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(3);
	private List<string> _watchedFolders = new();

	/// <summary>
	/// Event raised when a replay is matched to a play.
	/// </summary>
	public event EventHandler<ReplayMatchedEventArgs>? ReplayMatched;

	/// <summary>
	/// Whether the service is currently watching for replays.
	/// </summary>
	public bool IsWatching => _isWatching;

	/// <summary>
	/// Creates a new ReplayFileWatcherService.
	/// </summary>
	public ReplayFileWatcherService(OsuProcessDetector processDetector, SessionDatabaseService databaseService,
		ScoreImportService scoreImportService, SessionTrackerService? tracker = null)
	{
		_processDetector = processDetector;
		_databaseService = databaseService;
		_scoreImportService = scoreImportService;
		_tracker = tracker;
		if (_tracker != null)
		{
			_tracker.PlayRecorded += OnPlayRecorded;
			_tracker.SessionStopped += OnSessionStopped;
		}
	}

	private void OnPlayRecorded(object? sender, SessionPlayResult play) => Task.Run(CheckForMissingReplays);
	private void OnSessionStopped(object? sender, EventArgs e) => Task.Run(CheckForMissingReplays);

	/// <summary>
	/// Starts watching the osu! replay folders.
	/// </summary>
	public void StartWatching()
	{
		if (_isWatching || _isDisposed)
			return;

		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir))
		{
			Logger.Info("[ReplayWatcher] osu! directory not found, cannot start watching");
			return;
		}

		_watchedFolders.Clear();

		// Watch Data/r folder (temporary replays)
		var dataR = Path.Combine(osuDir, "Data", "r");
		if (Directory.Exists(dataR))
		{
			StartWatchingFolder(dataR);
			_watchedFolders.Add(dataR);
			Logger.Info($"[ReplayWatcher] Watching Data/r folder: {dataR}");
		}

		// Watch Replays folder (saved replays)
		var replays = Path.Combine(osuDir, "Replays");
		if (Directory.Exists(replays))
		{
			StartWatchingFolder(replays);
			_watchedFolders.Add(replays);
			Logger.Info($"[ReplayWatcher] Watching Replays folder: {replays}");
		}

		_isWatching = _watchers.Count > 0 || _watchedFolders.Count > 0;

		if (_isWatching)
		{
			Logger.Info($"[ReplayWatcher] Started watching {_watchers.Count} folder(s) via FSW");

			// Initialize known files from watched folders
			InitializeKnownFiles();

			// Start polling timer as fallback for unreliable FileSystemWatcher
			_pollingTimer?.Dispose();
			_pollingTimer = new System.Threading.Timer(PollForNewReplays, null, _pollingInterval, _pollingInterval);
			Logger.Info($"[ReplayWatcher] Polling fallback started (every {_pollingInterval.TotalSeconds}s)");
		}
		else
		{
			Logger.Info("[ReplayWatcher] No replay folders found to watch");
		}
	}

	/// <summary>
	/// Initializes the known files dictionary with current files in watched folders.
	/// </summary>
	private void InitializeKnownFiles()
	{
		lock (_lockObj)
		{
			_knownFiles.Clear();

			foreach (var folder in _watchedFolders)
				try
				{
					foreach (var file in Directory.GetFiles(folder, "*.osr"))
					{
						var info = new FileInfo(file);
						_knownFiles[file] = info.LastWriteTimeUtc;
					}
				}
				catch (Exception ex)
				{
					Logger.Info($"[ReplayWatcher] Error scanning folder {folder}: {ex.Message}");
				}

			Logger.Info($"[ReplayWatcher] Initialized with {_knownFiles.Count} known replay files");
		}
	}

	/// <summary>
	/// Polls watched folders for new replay files (fallback for unreliable FSW).
	/// </summary>
	private void PollForNewReplays(object? state)
	{
		if (_isDisposed || !_isWatching)
			return;
		foreach (var pending in _pendingFiles.ToArray())
		{
			if (pending.Value >= 10) _pendingFiles.TryRemove(pending.Key, out _);
			else ProcessReplayFile(pending.Key);
		}

		foreach (var folder in _watchedFolders)
			try
			{
				if (!Directory.Exists(folder))
					continue;

				foreach (var file in Directory.GetFiles(folder, "*.osr"))
				{
					var info = new FileInfo(file);
					var lastWrite = info.LastWriteTimeUtc;

					var isNew = false;
					lock (_lockObj)
					{
						if (!_knownFiles.TryGetValue(file, out var knownTime))
						{
							// New file
							_knownFiles[file] = lastWrite;
							isNew = true;
							Logger.Info($"[ReplayWatcher] Poll detected new file: {Path.GetFileName(file)}");
						}
						else if (lastWrite > knownTime)
						{
							// File was modified
							_knownFiles[file] = lastWrite;
							isNew = true;
							Logger.Info($"[ReplayWatcher] Poll detected modified file: {Path.GetFileName(file)}");
						}
					}

					if (isNew)
					{
						// Check debounce
						lock (_lockObj)
						{
							if (_recentlyProcessed.TryGetValue(file, out var lastProcessed))
								if (DateTime.Now - lastProcessed < _debounceWindow)
								{
									Logger.Info(
										$"[ReplayWatcher] Poll skipped (already processed): {Path.GetFileName(file)}");
									continue;
								}

							_recentlyProcessed[file] = DateTime.Now;
						}

						// Process the file
						Task.Run(() => ProcessReplayFile(file));
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Info($"[ReplayWatcher] Poll error in {folder}: {ex.Message}");
			}
	}

	/// <summary>
	/// Stops watching the osu! replay folders.
	/// </summary>
	public void StopWatching()
	{
		if (!_isWatching)
			return;

		// Stop polling timer
		_pollingTimer?.Dispose();
		_pollingTimer = null;

		lock (_lockObj)
		{
			foreach (var watcher in _watchers)
			{
				watcher.EnableRaisingEvents = false;
				watcher.Dispose();
			}

			_watchers.Clear();
			_watchedFolders.Clear();
			_knownFiles.Clear();
		}

		_isWatching = false;
		Logger.Info("[ReplayWatcher] Stopped watching");
	}

	/// <summary>
	/// Starts watching a specific folder for .osr files.
	/// </summary>
	private void StartWatchingFolder(string folderPath)
	{
		try
		{
			var watcher = new FileSystemWatcher(folderPath)
			{
				Filter = "*.osr",
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime |
							   NotifyFilters.Size,
				EnableRaisingEvents = true,
				IncludeSubdirectories = false,
				InternalBufferSize = 65536 // 64KB buffer for high-volume scenarios
			};

			watcher.Created += OnReplayFileCreated;
			watcher.Changed += OnReplayFileCreated;
			watcher.Renamed += OnReplayFileRenamed;
			watcher.Error += OnWatcherError;

			lock (_lockObj)
			{
				_watchers.Add(watcher);
			}

			Logger.Info(
				$"[ReplayWatcher] Watcher created for {folderPath}, EnableRaisingEvents={watcher.EnableRaisingEvents}");
		}
		catch (Exception ex)
		{
			Logger.Info($"[ReplayWatcher] Error watching folder {folderPath}: {ex.Message}");
		}
	}

	/// <summary>
	/// Handles when a replay file is renamed (F2 save moves files).
	/// </summary>
	private void OnReplayFileRenamed(object sender, RenamedEventArgs e)
	{
		Logger.Info($"[ReplayWatcher] File renamed: {e.OldName} -> {e.Name}");

		// Only process if the new name is an .osr file
		if (e.FullPath.EndsWith(".osr", StringComparison.OrdinalIgnoreCase))
			// Process on a background thread
			Task.Run(() => ProcessReplayFile(e.FullPath));
	}

	/// <summary>
	/// Handles watcher errors (e.g., buffer overflow).
	/// </summary>
	private void OnWatcherError(object sender, ErrorEventArgs e)
	{
		Logger.Info($"[ReplayWatcher] Watcher error: {e.GetException().Message}");

		// Try to restart the watcher
		Schedule(() =>
		{
			Logger.Info("[ReplayWatcher] Attempting to restart watchers after error");
			StopWatching();
			StartWatching();
		});
	}

	private static void Schedule(Action action)
	{
		Task.Run(action);
	}

	/// <summary>
	/// Handles when a new replay file is created or changed.
	/// </summary>
	private void OnReplayFileCreated(object sender, FileSystemEventArgs e)
	{
		Logger.Info($"[ReplayWatcher] File event: {e.ChangeType} - {e.Name}");

		// Debounce - ignore if we just processed this file
		lock (_lockObj)
		{
			if (_recentlyProcessed.TryGetValue(e.FullPath, out var lastTime))
				if (DateTime.Now - lastTime < _debounceWindow)
				{
					Logger.Info($"[ReplayWatcher] Debounced: {e.Name}");
					return;
				}

			_recentlyProcessed[e.FullPath] = DateTime.Now;

			// Clean up old entries
			var oldKeys = _recentlyProcessed
				.Where(kvp => DateTime.Now - kvp.Value > _debounceWindow * 5)
				.Select(kvp => kvp.Key)
				.ToList();
			foreach (var key in oldKeys)
				_recentlyProcessed.Remove(key);
		}

		// Process on a background thread to avoid blocking the watcher
		Task.Run(() => ProcessReplayFile(e.FullPath));
	}

	/// <summary>
	/// Processes a new replay file and tries to match it to a play.
	/// </summary>
	private void ProcessReplayFile(string replayPath)
	{
		lock (_matchingLock)
		{
			try
			{
				using var stream = new FileStream(replayPath, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (stream.Length is <= 0 or > 26214400) return;
				var bytes = new byte[(int)stream.Length];
				stream.ReadExactly(bytes);
				var replay = Companella.Services.Integrations.ManiaTracker.ManiaTrackerReplay.Read(bytes);
				var replayHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes));
				_tracker?.AttachReplay(replay, replayPath, replayHash);
				var matches = _databaseService.GetPlaysWithoutReplayByBeatmapHash(replay.BeatmapHash)
					.Where(p => p.Status == PlayStatus.Completed && SessionReplayMatcher.Matches(p.BeatmapHash, p.RecordedAt, replay)).ToArray();
				if (matches.Length == 1)
				{
					var counts = replay.Judgements;
					var accuracy = SessionPlayMemoryHelper.ComputeManiaAccuracy(counts[3], counts[0], counts[4], counts[1], counts[2], counts[5]);
					if (_databaseService.UpdateReplayInfo(matches[0].Id, replayHash, replayPath, accuracy, counts[5]))
						ReplayMatched?.Invoke(this, new ReplayMatchedEventArgs(matches[0].Id, replayPath, replayHash));
				}
				_pendingFiles.TryRemove(replayPath, out _);
			}
			catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException)
			{
				_pendingFiles.AddOrUpdate(replayPath, 1, (_, attempts) => attempts + 1);
			}
		}
	}

	/// <summary>
	/// Manually checks for replays for recent plays.
	/// Useful when the watcher might have missed files.
	/// </summary>
	public void CheckForMissingReplays()
	{
		if (_isDisposed) return;
		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir))
			return;
		foreach (var play in _tracker?.Plays.Where(p => !p.HasReplay && p.Status == PlayStatus.Completed) ?? Enumerable.Empty<SessionPlayResult>())
		{
			var path = SessionReplayMatcher.Find(osuDir, play.BeatmapHash, play.RecordedAt);
			if (path != null) ProcessReplayFile(path);
		}

		var recentPlays = _databaseService.GetRecentPlaysWithoutReplay(60);
		if (recentPlays.Count == 0)
			return;

		Logger.Info($"[ReplayWatcher] Checking for missing replays for {recentPlays.Count} recent plays");

		var folders = new List<string>();
		var dataR = Path.Combine(osuDir, "Data", "r");
		if (Directory.Exists(dataR))
			folders.Add(dataR);
		var replays = Path.Combine(osuDir, "Replays");
		if (Directory.Exists(replays))
			folders.Add(replays);

		foreach (var play in recentPlays)
		{
			if (string.IsNullOrEmpty(play.BeatmapHash))
				continue;

			// Look for a replay file with matching beatmap hash
			foreach (var folder in folders)
				try
				{
					var pattern = $"{play.BeatmapHash}*.osr";
					var files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);

					foreach (var file in files)
					{
						// Check if this replay was created around the same time as the play
						var fileInfo = new FileInfo(file);
						var timeDiff = Math.Abs((fileInfo.CreationTime - play.RecordedAt).TotalMinutes);

						if (timeDiff < 5) // Within 5 minutes
						{
							ProcessReplayFile(file);
							break;
						}
					}
				}
				catch (Exception ex)
				{
					Logger.Info($"[ReplayWatcher] Error checking folder {folder}: {ex.Message}");
				}
		}
	}

	/// <summary>
	/// Finds replays for plays in a specific session.
	/// </summary>
	/// <param name="sessionId">The session ID to find replays for.</param>
	/// <param name="progress">Optional progress callback (matched, total).</param>
	/// <returns>Number of replays found.</returns>
	public int FindReplaysForSession(long sessionId, Action<int, int>? progress = null)
	{
		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir))
			return 0;

		var session = _databaseService.GetSessionById(sessionId);
		if (session == null)
			return 0;

		var playsWithoutReplay =
			session.Plays.Where(p => !p.HasReplay && !string.IsNullOrEmpty(p.BeatmapHash)).ToList();
		if (playsWithoutReplay.Count == 0)
			return 0;

		Logger.Info($"[ReplayWatcher] Finding replays for {playsWithoutReplay.Count} plays in session {sessionId}");

		return FindReplaysForPlays(playsWithoutReplay, progress);
	}

	/// <summary>
	/// Finds replays for all plays that don't have replays yet.
	/// </summary>
	/// <param name="progress">Optional progress callback (matched, total).</param>
	/// <returns>Number of replays found.</returns>
	public int FindAllMissingReplays(Action<int, int>? progress = null)
	{
		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir))
			return 0;

		// Get all sessions and find plays without replays
		var allSessions = _databaseService.GetSessions();
		var playsWithoutReplay = new List<StoredSessionPlay>();
		var totalPlays = 0;
		var playsWithHash = 0;
		var playsWithoutReplayFile = 0;

		foreach (var session in allSessions)
		{
			var fullSession = _databaseService.GetSessionById(session.Id);
			if (fullSession != null)
				foreach (var play in fullSession.Plays)
				{
					totalPlays++;
					if (!string.IsNullOrEmpty(play.BeatmapHash))
						playsWithHash++;
					if (!play.HasReplay)
						playsWithoutReplayFile++;

					// Include plays without hash if they have a valid beatmap path (we can calculate hash)
					if (!play.HasReplay && (!string.IsNullOrEmpty(play.BeatmapHash) ||
											!string.IsNullOrEmpty(play.BeatmapPath)))
						playsWithoutReplay.Add(play);
				}
		}

		Logger.Info(
			$"[ReplayWatcher] Stats: {totalPlays} total plays, {playsWithHash} with beatmap hash, {playsWithoutReplayFile} without replay file");

		if (playsWithoutReplay.Count == 0)
		{
			Logger.Info("[ReplayWatcher] No plays without replays found (that have beatmap hash)");
			return 0;
		}

		Logger.Info($"[ReplayWatcher] Finding replays for {playsWithoutReplay.Count} plays across all sessions");

		return FindReplaysForPlays(playsWithoutReplay, progress);
	}

	/// <summary>
	/// Finds replays for a list of plays by querying scores.db for exact matches.
	/// </summary>
	private int FindReplaysForPlays(List<StoredSessionPlay> plays, Action<int, int>? progress = null)
	{
		Logger.Info($"[ReplayWatcher] Querying scores.db for {plays.Count} plays");

		var matchedCount = 0;
		var processed = 0;

		foreach (var play in plays)
		{
			processed++;
			progress?.Invoke(matchedCount, plays.Count);

			// Get or calculate beatmap hash
			var beatmapHash = play.BeatmapHash;

			// If no hash stored, try to calculate from beatmap file
			if (string.IsNullOrEmpty(beatmapHash) && !string.IsNullOrEmpty(play.BeatmapPath) &&
				File.Exists(play.BeatmapPath))
				try
				{
					beatmapHash = File.OpenRead(play.BeatmapPath).Md5();
					Logger.Info($"[ReplayWatcher] Calculated hash for play ID {play.Id}: {beatmapHash}");

					// Update the database with the calculated hash
					_databaseService.UpdateBeatmapHash(play.Id, beatmapHash);
				}
				catch (Exception ex)
				{
					Logger.Info($"[ReplayWatcher] Error calculating hash for play ID {play.Id}: {ex.Message}");
				}

			if (string.IsNullOrEmpty(beatmapHash))
			{
				Logger.Info($"[ReplayWatcher] Play ID {play.Id} has no beatmap hash and file not found, skipping");
				continue;
			}

			Logger.Info($"[ReplayWatcher] Looking up play ID {play.Id}: hash={beatmapHash}, acc={play.Accuracy:F4}%");

			// Query scores.db for matching score by beatmap hash and accuracy
			var replayResults = _scoreImportService.FindReplayInScoresDb(beatmapHash, play.Accuracy);

			Logger.Info($"[ReplayWatcher] Found {replayResults.Count} results from scores.db");

			if (replayResults.Count > 0)
			{
				// Take the first match (most recent)
				var result = replayResults[0];

				if (!string.IsNullOrEmpty(result.ReplayHash))
					if (_databaseService.UpdateReplayInfo(play.Id, result.ReplayHash,
							result.ReplayPath ?? string.Empty))
					{
						matchedCount++;
						Logger.Info(
							$"[ReplayWatcher] Matched replay to play ID {play.Id} via scores.db (hash: {result.ReplayHash})");
						ReplayMatched?.Invoke(this,
							new ReplayMatchedEventArgs(play.Id, result.ReplayPath ?? string.Empty, result.ReplayHash));
					}
			}
		}

		progress?.Invoke(matchedCount, plays.Count);
		Logger.Info($"[ReplayWatcher] Found {matchedCount} replays out of {plays.Count} plays via scores.db");

		return matchedCount;
	}

	private void Dispose(bool isDisposing)
	{
		if (_isDisposed || !isDisposing)
			return;

		if (_tracker != null)
		{
			_tracker.PlayRecorded -= OnPlayRecorded;
			_tracker.SessionStopped -= OnSessionStopped;
		}
		StopWatching();
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~ReplayFileWatcherService()
	{
		Dispose(false);
	}
}

/// <summary>
/// Event arguments for when a replay is matched to a play.
/// </summary>
public class ReplayMatchedEventArgs : EventArgs
{
	/// <summary>
	/// The ID of the play that was matched.
	/// </summary>
	public long PlayId { get; }

	/// <summary>
	/// The path to the replay file.
	/// </summary>
	public string ReplayPath { get; }

	/// <summary>
	/// The MD5 hash of the replay file.
	/// </summary>
	public string ReplayHash { get; }

	public ReplayMatchedEventArgs(long playId, string replayPath, string replayHash)
	{
		PlayId = playId;
		ReplayPath = replayPath;
		ReplayHash = replayHash;
	}
}
