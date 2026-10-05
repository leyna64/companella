using System.Globalization;
using System.Security.Cryptography;
using Companella.Extensions;
using Companella.Models.Session;
using Companella.Services.Analysis;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;

namespace Companella.Services.Session;

/// <summary>
/// Service that tracks player sessions by monitoring osu! gameplay.
/// Detects completed maps and records accuracy and MSD ratings.
/// </summary>
public class SessionTrackerService : IDisposable
{
	private readonly OsuProcessDetector _processDetector;
	private readonly SessionDatabaseService _databaseService;
	private readonly StructuredOsuMemoryReader _memoryReader;
	private readonly AptabaseService? _aptabaseService;
	private readonly object _lockObj = new();

	private Thread? _trackingThread;
	private CancellationTokenSource? _cancellation;
	private bool _isDisposed;

	// Session data
	private DateTime _sessionStartTime;
	private readonly List<SessionPlayResult> _plays = new();

	// State tracking
	private int _previousStatus = -1;
	private string? _currentPlayingBeatmap;
	private bool _wasPlaying;
	private bool _wasOnResultsScreen;
	private double _lastAccuracy;
	private float _currentPlayRate = 1.0f;
	private int _currentPlayMods;

	// Pause tracking
	private int _pauseCount;
	private int _lastAudioTime = int.MinValue;
	private int _audioTimeStuckCount;
	private bool _isPaused;
	private const int _pauseDetectionThreshold = 3; // Consecutive identical AudioTime readings to detect pause

	// Miss tracking
	private int _currentMissCount;
	private int _currentTotalHits;

	// Prevents double-recording when multiple completion paths fire in one poll
	private bool _playEndHandled;

	// Configuration
	private const int _pollIntervalMs = 150;

	/// <summary>
	/// Whether the session tracker is currently active.
	/// </summary>
	public bool IsTracking { get; private set; }

	/// <summary>
	/// The time when the current session started.
	/// </summary>
	public DateTime SessionStartTime => _sessionStartTime;

	/// <summary>
	/// Gets the duration of the current session.
	/// </summary>
	public TimeSpan SessionDuration => IsTracking ? DateTime.UtcNow - _sessionStartTime : TimeSpan.Zero;

	/// <summary>
	/// Gets a copy of all recorded plays in this session.
	/// </summary>
	public List<SessionPlayResult> Plays
	{
		get
		{
			lock (_lockObj)
			{
				return new List<SessionPlayResult>(_plays);
			}
		}
	}

	/// <summary>
	/// Gets the number of plays recorded in this session.
	/// </summary>
	public int PlayCount
	{
		get
		{
			lock (_lockObj)
			{
				return _plays.Count;
			}
		}
	}

	/// <summary>
	/// Event raised when a new play is recorded.
	/// </summary>
	public event EventHandler<SessionPlayResult>? PlayRecorded;

	/// <summary>Raised when a saved replay becomes available for an existing live play.</summary>
	public event EventHandler<SessionPlayResult>? PlayUpdated;

	/// <summary>Attaches a replay to exactly one matching live play, without creating a second score.</summary>
	internal void AttachReplay(Services.Integrations.ManiaTracker.ManiaTrackerReplay replay, string path, string hash)
	{
		SessionPlayResult play;
		lock (_lockObj)
		{
			var matches = _plays.Where(x => x.Status == PlayStatus.Completed &&
				SessionReplayMatcher.Matches(x.BeatmapHash, x.RecordedAt, replay)).ToArray();
			if (matches.Length != 1 || matches[0].HasReplay) return;
			play = matches[0];
			play.ReplayPath = path;
			play.ReplayHash = hash;
			var counts = replay.Judgements;
			play.Accuracy = SessionPlayMemoryHelper.ComputeManiaAccuracy(counts[3], counts[0], counts[4], counts[1], counts[2], counts[5]);
			play.Misses = counts[5];
			play.UpdateGrade();
		}
		PlayUpdated?.Invoke(this, play);
	}

	/// <summary>
	/// Event raised when a play ends and the player paused during it.
	/// The int parameter is the number of pauses.
	/// </summary>
	public event EventHandler<int>? PlayEndedWithPauses;

	/// <summary>
	/// Event raised when the session starts.
	/// </summary>
	public event EventHandler? SessionStarted;

	/// <summary>
	/// Event raised when the session stops.
	/// </summary>
	public event EventHandler? SessionStopped;

	/// <summary>
	/// Event raised when there's a status update (for UI feedback).
	/// </summary>
	public event EventHandler<string>? StatusUpdated;

	/// <summary>
	/// Event raised when the player enters the results screen after completing a map.
	/// Contains the beatmap path and rate for timing deviation analysis.
	/// </summary>
	public event EventHandler<ResultsScreenEventArgs>? ResultsScreenEntered;

	/// <summary>
	/// Event raised when the player leaves the results screen.
	/// </summary>
	public event EventHandler? ResultsScreenExited;

	/// <summary>
	/// Creates a new SessionTrackerService.
	/// </summary>
	public SessionTrackerService(OsuProcessDetector processDetector, SessionDatabaseService databaseService,
		AptabaseService? aptabaseService = null)
	{
		_processDetector = processDetector;
		_databaseService = databaseService;
		_memoryReader = StructuredOsuMemoryReader.Instance;
		_aptabaseService = aptabaseService;
	}

	/// <summary>
	/// Starts tracking the session.
	/// </summary>
	public void StartSession()
	{
		if (IsTracking)
			return;

		lock (_lockObj)
		{
			_plays.Clear();
		}

		_sessionStartTime = DateTime.UtcNow;
		_previousStatus = -1;
		_currentPlayingBeatmap = null;
		_wasPlaying = false;
		_lastAccuracy = 0;
		_currentPlayMods = 0;
		_playEndHandled = false;

		_processDetector.TryAttachToOsu();

		_cancellation = new CancellationTokenSource();
		_trackingThread = new Thread(TrackingLoop)
		{
			Name = "SessionTrackerThread",
			IsBackground = true
		};

		IsTracking = true;
		_trackingThread.Start();

		// Track analytics
		_aptabaseService?.TrackSessionStart();

		SessionStarted?.Invoke(this, EventArgs.Empty);
		StatusUpdated?.Invoke(this, "Session started");
		Logger.Info("[Session] Session tracking started");
	}

	/// <summary>
	/// Stops tracking the session.
	/// </summary>
	public void StopSession()
	{
		if (!IsTracking)
			return;

		_cancellation?.Cancel();

		// Wait for thread to finish
		if (_trackingThread != null && _trackingThread.IsAlive) _trackingThread.Join(1000);

		_cancellation?.Dispose();
		_cancellation = null;
		_trackingThread = null;

		IsTracking = false;

		// Track analytics
		var durationMinutes = (DateTime.UtcNow - _sessionStartTime).TotalMinutes;
		int playCount;
		lock (_lockObj)
		{
			playCount = _plays.Count;
		}

		_aptabaseService?.TrackSessionStop(durationMinutes, playCount);

		// Save session to database
		SaveSessionToDatabase();

		SessionStopped?.Invoke(this, EventArgs.Empty);
		StatusUpdated?.Invoke(this, "Session stopped");
		Logger.Info("[Session] Session tracking stopped");
	}

	/// <summary>
	/// Saves the current session to the database.
	/// </summary>
	private void SaveSessionToDatabase()
	{
		List<SessionPlayResult> playsToSave;
		lock (_lockObj)
		{
			if (_plays.Count == 0)
			{
				Logger.Info("[Session] No plays to save");
				return;
			}

			playsToSave = new List<SessionPlayResult>(_plays);
		}

		try
		{
			var endTime = DateTime.UtcNow;
			var sessionId = _databaseService.SaveSession(_sessionStartTime, endTime, playsToSave);

			if (sessionId > 0)
			{
				Logger.Info($"[Session] Session saved to database with ID: {sessionId}");
				StatusUpdated?.Invoke(this, $"Session saved ({playsToSave.Count} plays)");
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Session] Error saving session to database: {ex.Message}");
			StatusUpdated?.Invoke(this, "Failed to save session");
		}
	}

	/// <summary>
	/// The main tracking loop that runs on a background thread.
	/// </summary>
	private void TrackingLoop()
	{
		var token = _cancellation?.Token ?? CancellationToken.None;

		while (!token.IsCancellationRequested)
		{
			try
			{
				PollGameState();
			}
			catch (Exception ex)
			{
				Logger.Info($"[Session] Error in tracking loop: {ex.Message}");
			}

			try
			{
				Thread.Sleep(_pollIntervalMs);
			}
			catch (ThreadInterruptedException)
			{
				break;
			}
		}
	}

	/// <summary>
	/// Polls the current game state and detects map completion.
	/// </summary>
	private void PollGameState()
	{
		if (!_memoryReader.CanRead)
		{
			_processDetector.TryAttachToOsu();
			if (!_memoryReader.CanRead)
				return;
		}

		if (!_processDetector.IsOsuRunning)
			_processDetector.TryAttachToOsu();

		int currentStatus;
		var currentAudioTime = 0;
		double playerAccuracy = 0;
		var missCount = 0;
		var totalHits = 0;
		var showPlayingInterface = false;

		// Use lock to prevent concurrent access with HitErrorReaderService
		lock (HitErrorReaderService.MemoryReaderLock)
		{
			try
			{
				// Read general data which contains game status
				var generalData = new GeneralData();
				if (!_memoryReader.TryRead(generalData)) return;

				currentStatus = generalData.RawStatus;
				currentAudioTime = generalData.AudioTime;
				showPlayingInterface = generalData.ShowPlayingInterface;

				// Also read player data to track accuracy and misses during gameplay
				var stats = SessionPlayMemoryHelper.ReadPlayStats(_memoryReader, currentStatus is 7 or 14);
				playerAccuracy = stats.Accuracy;
				missCount = stats.Misses;
				totalHits = stats.TotalHits;
			}
			catch (Exception ex)
			{
				Logger.Info($"[Session] Memory read error: {ex.Message}");
				return;
			}
		}

		try
		{
			// OsuMemoryStatus values from OsuMemoryDataProvider:
			// 0 = MainMenu, 1 = EditingMap, 2 = Playing, 3 = GameShutdownAnimation
			// 4 = SongSelectEdit, 5 = SongSelect, 6 = WIP_NoIdeaWhatThisIs
			// 7 = ResultsScreen, 11 = MultiplayerRooms, 12 = MultiplayerRoom
			// 13 = MultiplayerSongSelect, 14 = MultiplayerResultsScreen
			// 15 = OsuDirect, 19 = RankingTagCoop, 20 = RankingTeam
			// 22 = ProcessingBeatmaps, 23 = Tourney

			const int STATUS_PLAYING = 2;
			const int STATUS_RESULTS = 7;
			const int STATUS_SONGSELECT = 5;
			const int STATUS_MULTIPLAYER_RESULTS = 14;

			var isResultsScreen = currentStatus is STATUS_RESULTS or STATUS_MULTIPLAYER_RESULTS;
			// Results still reports ShowPlayingInterface + AudioTime; exclude it from active gameplay.
			var isInGameplay = currentStatus == STATUS_PLAYING
							   || (!isResultsScreen && showPlayingInterface && currentAudioTime > 0
								   && currentStatus != STATUS_SONGSELECT);

			// Track pauses during gameplay by detecting when AudioTime stops advancing
			if (_wasPlaying && isInGameplay)
				// Only track pauses after the song has actually started (AudioTime > 0)
				if (currentAudioTime > 0)
				{
					if (currentAudioTime == _lastAudioTime)
					{
						_audioTimeStuckCount++;

						// Detect pause when audio time is stuck for multiple polls
						if (_audioTimeStuckCount >= _pauseDetectionThreshold && !_isPaused)
						{
							_isPaused = true;
							_pauseCount++;
							Logger.Info($"[Session] Pause detected! Total pauses: {_pauseCount}");
						}
					}
					else
					{
						// Audio time is advancing - not paused
						_audioTimeStuckCount = 0;
						_isPaused = false;
					}

					_lastAudioTime = currentAudioTime;
				}

			// Log status changes for debugging
			if (currentStatus != _previousStatus)
				Logger.Info($"[Session] Status changed: {_previousStatus} -> {currentStatus}");

			// Track accuracy and misses while a play is in progress (including the results screen)
			if (_wasPlaying && !_playEndHandled && totalHits >= _currentTotalHits)
			{
				if (playerAccuracy > 0) _lastAccuracy = playerAccuracy;

				if (missCount > 0) _currentMissCount = missCount;

				if (totalHits > 0) _currentTotalHits = totalHits;
			}

			// Complete immediately when entering results after gameplay
			if (_wasPlaying && isResultsScreen && !_playEndHandled)
			{
				Logger.Info(
					$"[Session] Play completed on results screen - accuracy: {_lastAccuracy:F2}%, misses: {_currentMissCount}, hits: {_currentTotalHits}");

				var beatmapPath = _processDetector.ResolveBeatmapPath(_currentPlayingBeatmap);
				var playRate = _currentPlayRate;
				var finishedAt = DateTime.UtcNow;
				CompleteCurrentPlay(PlayStatus.Completed);

				if (!string.IsNullOrEmpty(beatmapPath))
					ResultsScreenEntered?.Invoke(this, new ResultsScreenEventArgs(beatmapPath, playRate, false, finishedAt));
			}
			// Detect transition from playing to song select or other non-results exit
			else if (_wasPlaying && !isInGameplay && !isResultsScreen)
			{
				Logger.Info(
					$"[Session] Detected end of play, status: {currentStatus}, last accuracy: {_lastAccuracy:F2}%, misses: {_currentMissCount}, hits: {_currentTotalHits}");

				// Player quit or failed before reaching results
				if (currentStatus == STATUS_SONGSELECT)
				{
					// Player quit or failed - record if they made any progress
					if (_lastAccuracy > 0 || _currentMissCount > 0 || _currentTotalHits > 0)
					{
						Logger.Info("[Session] Play quit - recording with current stats");
						CompleteCurrentPlay(PlayStatus.Quit);
					}
					else
					{
						Logger.Info("[Session] Play quit before any progress - not recording");
						ResetPlayState();
					}
				}
				else
				{
					Logger.Info($"[Session] Play ended with unhandled status {currentStatus}");
					if (_lastAccuracy > 0 || _currentMissCount > 0 || _currentTotalHits > 0)
						CompleteCurrentPlay(PlayStatus.Quit);
					else
						ResetPlayState();
				}

				// Fire pause event if player paused during this play
				if (_pauseCount > 0 && !_playEndHandled)
				{
					Logger.Info($"[Session] Play ended with {_pauseCount} pause(s)");
					PlayEndedWithPauses?.Invoke(this, _pauseCount);
				}
			}
			else if (isInGameplay && !_wasPlaying)
			{
				// Just started playing - capture beatmap path, rate, and mods
				_wasPlaying = true;
				_playEndHandled = false;
				_currentPlayingBeatmap = _processDetector.ResolveBeatmapPath();
				_currentPlayRate = _processDetector.GetCurrentRateFromMods();
				_currentPlayMods = _processDetector.GetCurrentMods();
				_lastAccuracy = 0;
				_currentMissCount = 0;
				_currentTotalHits = 0;

				// Reset pause tracking for new play
				_pauseCount = 0;
				_lastAudioTime = int.MinValue;
				_audioTimeStuckCount = 0;
				_isPaused = false;

				var rateInfo = Math.Abs(_currentPlayRate - 1.0f) > 0.01f ? $" @ {_currentPlayRate:F2}x" : "";
				Logger.Info(
					$"[Session] Started playing: {Path.GetFileName(_currentPlayingBeatmap ?? "Unknown")}{rateInfo} (status={currentStatus}, interface={showPlayingInterface})");
			}
			else if (isResultsScreen && !_wasOnResultsScreen && _previousStatus == STATUS_PLAYING && !_wasPlaying)
			{
				// Status transition caught results even though the playing flag was missed
				Logger.Info("[Session] Results detected via status transition (missed playing flag)");
				_currentPlayingBeatmap ??= _processDetector.ResolveBeatmapPath();
				var beatmapPath = _processDetector.ResolveBeatmapPath(_currentPlayingBeatmap);
				var playRate = _currentPlayRate;
				CompleteCurrentPlay(PlayStatus.Completed);

				if (!string.IsNullOrEmpty(beatmapPath))
					ResultsScreenEntered?.Invoke(this, new ResultsScreenEventArgs(beatmapPath, playRate));
			}

			// Detect leaving results screen
			if (_wasOnResultsScreen && !isResultsScreen)
			{
				Logger.Info("[Session] Left results screen");
				ResultsScreenExited?.Invoke(this, EventArgs.Empty);
				_wasOnResultsScreen = false;
			}

			// Track if we're on results screen and fire event for non-gameplay entries
			// (e.g., viewing replays or previous scores from song select)
			if (isResultsScreen && !_wasOnResultsScreen)
			{
				_wasOnResultsScreen = true;

				// If we didn't come from playing (event already fired above), fire the event now
				// This handles cases like viewing replays or clicking on previous scores
				if (!_wasPlaying && _previousStatus != STATUS_PLAYING)
				{
					var beatmapPath = _processDetector.ResolveBeatmapPath();
					var rate = _processDetector.GetCurrentRateFromMods();

					if (!string.IsNullOrEmpty(beatmapPath))
					{
						Logger.Info(
							$"[Session] Entered results screen from song select (viewing replay/score): {Path.GetFileName(beatmapPath)}");
						// Mark as replay view - hit errors in memory are for the replay being viewed
						ResultsScreenEntered?.Invoke(this,
							new ResultsScreenEventArgs(beatmapPath, rate, true));
					}
				}
			}

			_previousStatus = currentStatus;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Session] Error polling game state: {ex.Message}");
		}
	}

	/// <summary>
	/// Records a completed play once and resets transient play state.
	/// </summary>
	private void CompleteCurrentPlay(PlayStatus status)
	{
		if (_playEndHandled)
			return;

		_playEndHandled = true;
		OnMapCompleted(status);
		ResetPlayState();
	}

	/// <summary>
	/// Clears transient state for the current play without recording it.
	/// </summary>
	private void ResetPlayState()
	{
		_wasPlaying = false;
		_currentPlayingBeatmap = null;
		_lastAccuracy = 0;
		_currentPlayRate = 1.0f;
		_currentPlayMods = 0;
		_currentMissCount = 0;
		_currentTotalHits = 0;
		_pauseCount = 0;
		_lastAudioTime = int.MinValue;
		_audioTimeStuckCount = 0;
		_isPaused = false;
	}

	/// <summary>
	/// Called when a map is completed, quit, or failed. Reads accuracy and triggers MSD analysis.
	/// </summary>
	/// <param name="status">The play status (Completed, Failed, Quit).</param>
	private void OnMapCompleted(PlayStatus status)
	{
		try
		{
			// Check mods captured at play start (results screen mods can differ)
			if (OsuProcessDetector.HasAutoMod(_currentPlayMods))
			{
				Logger.Info($"[Session] Autoplay mod detected (mods={_currentPlayMods}) - skipping play recording");
				return;
			}

			SessionPlayMemoryHelper.PlayStats stats;
			lock (HitErrorReaderService.MemoryReaderLock)
			{
				stats = SessionPlayMemoryHelper.ReadPlayStats(_memoryReader, status == PlayStatus.Completed);
			}

			var accuracy = _lastAccuracy;
			var misses = _currentMissCount;

			if (stats.TotalHits > 0 && stats.TotalHits >= _currentTotalHits)
			{
				accuracy = stats.Accuracy;
				Logger.Info($"[Session] Read accuracy from memory: {accuracy:F2}%");
			}
			else if (accuracy > 0)
			{
				Logger.Info($"[Session] Using last recorded accuracy: {accuracy:F2}%");
			}

			if (stats.Misses >= 0 && stats.Misses > misses)
				misses = stats.Misses;

			// Skip if we have no evidence of gameplay progress
			if (accuracy <= 0 && stats.TotalHits <= 0 && stats.Score <= 0)
			{
				Logger.Info(
					$"[Session] No valid play stats (accuracy={accuracy:F2}, hits={stats.TotalHits}, score={stats.Score}) - skipping play");
				return;
			}


			var beatmapPath = _processDetector.ResolveBeatmapPath(_currentPlayingBeatmap);

			if (string.IsNullOrEmpty(beatmapPath))
			{
				Logger.Info("[Session] Could not determine beatmap path");
				return;
			}

			var rateInfo = Math.Abs(_currentPlayRate - 1.0f) > 0.01f ? $" @ {_currentPlayRate:F2}x" : "";
			var statusStr = status == PlayStatus.Completed ? "completed" :
				status == PlayStatus.Failed ? "failed" : "quit";
			Logger.Info(
				$"[Session] Map {statusStr}: {Path.GetFileName(beatmapPath)} - {accuracy:F2}%, {misses} misses{rateInfo}");
			StatusUpdated?.Invoke(this,
				$"{char.ToUpper(statusStr[0], CultureInfo.InvariantCulture)}{statusStr[1..]}: {Path.GetFileName(beatmapPath)} ({accuracy:F2}%){rateInfo}");

			// Analyze MSD with the rate used during play (this happens synchronously on the tracking thread)
			AnalyzeAndRecordPlay(beatmapPath, accuracy, misses, _pauseCount, status, _currentPlayRate);
		}
		catch (Exception ex)
		{
			Logger.Info($"[Session] Error recording completed map: {ex.Message}");
		}
	}

	/// <summary>
	/// Analyzes the MSD for the beatmap and records the play.
	/// </summary>
	/// <param name="beatmapPath">Path to the beatmap file.</param>
	/// <param name="accuracy">Accuracy achieved on the play.</param>
	/// <param name="misses">Number of misses during the play.</param>
	/// <param name="pauseCount">Number of pauses during the play.</param>
	/// <param name="status">Play status (Completed, Failed, Quit).</param>
	/// <param name="rate">Rate the map was played at (1.5 for DT, 0.75 for HT, 1.0 for normal).</param>
	private void AnalyzeAndRecordPlay(string beatmapPath, double accuracy, int misses, int pauseCount,
		PlayStatus status, float rate = 1.0f)
	{
		var recordedAt = DateTime.UtcNow;
		float highestMsd = 0;
		var dominantSkillset = "unknown";
		var beatmapHash = "";

		// Calculate beatmap hash
		try
		{
			if (File.Exists(beatmapPath))
			{
				beatmapHash = File.OpenRead(beatmapPath).Md5();
				Logger.Info($"[Session] Beatmap hash: {beatmapHash}");
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Session] Error calculating beatmap hash: {ex.Message}");
		}

		try
		{
			if (ToolPaths.MsdCalculatorExists)
			{
				var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
				var result = analyzer.AnalyzeSingleRate(beatmapPath, rate, 30000);

				if (result?.Scores != null)
				{
					var dominant = result.Scores.GetDominantSkillset();
					highestMsd = dominant.Value;
					dominantSkillset = dominant.Name;
					var rateInfo = Math.Abs(rate - 1.0f) > 0.01f ? $" @ {rate:F2}x" : "";
					Logger.Info($"[Session] MSD: {highestMsd:F2} ({dominantSkillset}){rateInfo}");
				}
			}
			else
			{
				Logger.Info("[Session] MSD calculator not found - using 0 for MSD");
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Session] MSD analysis failed: {ex.Message}");
		}

		// Create the play result with all new fields
		var sessionTime = DateTime.UtcNow - _sessionStartTime;
		var playResult = new SessionPlayResult(
			beatmapPath,
			beatmapHash,
			accuracy,
			misses,
			pauseCount,
			status,
			sessionTime,
			recordedAt,
			highestMsd,
			dominantSkillset,
			rate
		);

		// Add to list
		lock (_lockObj)
		{
			_plays.Add(playResult);
		}

		// Track analytics
		_aptabaseService?.TrackPlayRecorded(accuracy, highestMsd, dominantSkillset);

		// Raise event
		PlayRecorded?.Invoke(this, playResult);
		Logger.Info($"[Session] Play #{PlayCount} recorded - Grade: {playResult.Grade}");
	}

	/// <summary>
	/// Clears all recorded plays without stopping the session.
	/// </summary>
	public void ClearPlays()
	{
		lock (_lockObj)
		{
			_plays.Clear();
		}

		Logger.Info("[Session] Plays cleared");
	}

	public void Dispose()
	{
		if (_isDisposed)
			return;

		StopSession();
		_isDisposed = true;
		GC.SuppressFinalize(this);
	}
}

/// <summary>
/// Event arguments for when the results screen is entered.
/// </summary>
public class ResultsScreenEventArgs : EventArgs
{
	/// <summary>
	/// The path to the beatmap that was just played.
	/// </summary>
	public string BeatmapPath { get; }

	/// <summary>
	/// The rate multiplier used (1.5 for DT, 0.75 for HT, 1.0 for normal).
	/// </summary>
	public float Rate { get; }

	/// <summary>
	/// Whether this is from viewing a replay/previous score (true) or from completing a play (false).
	/// When true, hit errors should be read from memory as they represent the replay being viewed.
	/// </summary>
	public bool IsReplayView { get; }

	/// <summary>The UTC result transition time used to reject older replay files.</summary>
	public DateTime EnteredAtUtc { get; }

	public ResultsScreenEventArgs(string beatmapPath, float rate, bool isReplayView = false, DateTime? enteredAtUtc = null)
	{
		BeatmapPath = beatmapPath;
		Rate = rate;
		IsReplayView = isReplayView;
		EnteredAtUtc = enteredAtUtc ?? DateTime.UtcNow;
	}
}
