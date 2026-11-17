using System.Security.Cryptography;
using System.Text;
using Companella.Services.Common;
using Companella.Services.Platform;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Observes stable gameplay without starting a practice session or running MSD analysis.</summary>
public sealed class ManiaTrackerCapture
{
	private readonly Func<string?> _beatmapPath;
	private readonly Func<string?> _osuDirectory;
	private readonly ManiaTrackerQueue _queue;
	private int _lastStatus = -1;
	private int _lastAudioTime;
	private int _lastRetries;
	private ManiaTrackerPlay? _playing;
	private byte[]? _chart;
	private int _notes;
	private int _progress;
	private DateTimeOffset? _resultsReadyAt;
	public string? LastMessage { get; private set; }

	public ManiaTrackerCapture(OsuProcessDetector detector, ManiaTrackerQueue queue) : this(() => detector.ResolveBeatmapPath(), detector.GetOsuDirectory, queue)
	{
	}

	internal ManiaTrackerCapture(Func<string?> beatmapPath, Func<string?> osuDirectory, ManiaTrackerQueue queue)
	{
		_beatmapPath = beatmapPath;
		_osuDirectory = osuDirectory;
		_queue = queue;
	}

	public void Reset()
	{
		_playing = null;
		_chart = null;
		_lastStatus = -1;
		_progress = 0;
		_resultsReadyAt = null;
	}

	public void Poll(ManiaTrackerCredentials? connection)
	{
		if (connection == null) { Reset(); return; }
		var memory = StructuredOsuMemoryReader.Instance;
		var general = new GeneralData();
		var player = new Player();
		var map = new CurrentBeatmap();
		lock (HitErrorReaderService.MemoryReaderLock)
		{
			if (!memory.CanRead || !memory.TryRead(general))
			{
				Reset();
				SetMessage("Waiting for osu!stable game state.");
				return;
			}
			// Player pointers do not exist in menus. Preserve the menu status so the next
			// successful gameplay read can establish a fresh play transition.
			if (general.RawStatus is not (2 or 7 or 14))
			{
				Observe(connection, general, player, map);
				return;
			}
			if (general.RawStatus is 7 or 14)
			{
				if (_playing == null) { _lastStatus = general.RawStatus; return; }
				_resultsReadyAt ??= DateTimeOffset.UtcNow.AddMilliseconds(300);
				if (DateTimeOffset.UtcNow < _resultsReadyAt) return;
				var results = new ResultsScreen();
				if (memory.TryRead(results) && results.Mode == 3 && results.Score > 0)
				{
					player.Mode = results.Mode;
					player.Username = results.Username;
					player.Score = results.Score;
					player.Hit300 = results.Hit300;
					player.Hit100 = results.Hit100;
					player.Hit50 = results.Hit50;
					player.HitGeki = results.HitGeki;
					player.HitKatu = results.HitKatu;
					player.HitMiss = results.HitMiss;
				}
				else if (!memory.TryRead(player))
				{
					SetMessage("Waiting for valid final score data; no upload queued yet.");
					return;
				}
			}
			else if (!memory.TryRead(player) || !memory.TryRead(map))
			{
				// Do not accept a later result when replay identity became unreadable mid-play.
				_playing = null;
				_chart = null;
				SetMessage("Gameplay memory could not be read. This play will not be uploaded.");
				return;
			}
		}
		Observe(connection, general, player, map);
	}

	internal void Observe(ManiaTrackerCredentials connection, GeneralData general, Player player, CurrentBeatmap map)
	{
		var status = general.RawStatus;
		var isResults = status is 7 or 14;
		// Fail closed: only real mania gameplay, never replay/spectator interfaces or automated mods.
		var eligible = general.GameMode == 3 && player.Mode == 3 && !player.IsReplay &&
			(general.Mods & ((1 << 7) | (1 << 11) | (1 << 12) | (1 << 13))) == 0;
		if ((status == 2 && !eligible) || (_playing != null && (_playing.InstallationId != connection.InstallationId || _playing.UserId != connection.UserId)))
		{
			_playing = null;
			_chart = null;
		}
		var restarted = status == 2 && (_lastRetries != general.Retries || general.AudioTime < _lastAudioTime - 1000);
		if (status == 2 && eligible && _lastStatus >= 0 && (_lastStatus != 2 || restarted))
		{
			Begin(connection, player, map, general);
		}
		if (status == 2 && _playing != null)
		{
			if (!map.Md5.Equals(_playing.ChartMd5, StringComparison.OrdinalIgnoreCase) || general.Mods != _playing.Mods || player.Username != _playing.PlayerName)
			{
				_playing = null;
				_chart = null;
			}
			else _progress = Math.Max(_progress, Counts(player).Sum());
		}
		if (isResults && _lastStatus == 2 && _playing != null && _chart != null)
		{
			var counts = Counts(player);
			if (_progress > 0 && counts.Sum() == _notes && player.Mode == 3 && player.Score > 0 && player.Username == _playing.PlayerName)
			{
				_playing.FinishedAt = DateTimeOffset.UtcNow;
				_playing.Score = player.Score;
				_playing.Judgements = counts;
				_queue.Capture(_playing, _chart);
				SetMessage("Completed play captured; waiting for its original replay.");
			}
			else SetMessage($"Play not submitted: completion could not be verified (observed {_progress}, final {counts.Sum()}, chart {_notes}).");
		}
		if (status != 2) { _playing = null; _chart = null; }
		_lastStatus = status;
		_lastAudioTime = general.AudioTime;
		_lastRetries = general.Retries;
	}

	private void Begin(ManiaTrackerCredentials connection, Player player, CurrentBeatmap map, GeneralData general)
	{
		_playing = null;
		_chart = null;
		_progress = 0;
		_resultsReadyAt = null;
		var path = _beatmapPath();
		var directory = _osuDirectory();
		if (path == null || directory == null || string.IsNullOrEmpty(player.Username)) return;
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length is <= 0 or > 8388608) return;
		var bytes = new byte[(int)stream.Length];
		stream.ReadExactly(bytes);
		var hash = ManiaTrackerQueue.Hex(MD5.HashData(bytes));
		if (!hash.Equals(map.Md5, StringComparison.OrdinalIgnoreCase)) return;
		var lines = Encoding.UTF8.GetString(bytes).Split('\n').Select(x => x.Trim()).ToArray();
		var noteStart = Array.IndexOf(lines, "[HitObjects]");
		if (noteStart < 0 || !lines.Any(x => x.Replace(" ", "", StringComparison.Ordinal) == "Mode:3")) return;
		_notes = lines.Skip(noteStart + 1).TakeWhile(x => !x.StartsWith('[')).Count(x => x.Length > 0 && !x.StartsWith("//", StringComparison.Ordinal));
		if (_notes == 0) return;
		_chart = bytes;
		_playing = new ManiaTrackerPlay
		{
			UserId = connection.UserId, InstallationId = connection.InstallationId, ClientId = connection.ClientId,
			StartedAt = DateTimeOffset.UtcNow, BeatmapPath = path, ChartMd5 = hash,
			ReplayDirectory = directory, PlayerName = player.Username, Mods = general.Mods
		};
		SetMessage("Tracking a new stable mania play; waiting for completion.");
	}

	private void SetMessage(string message)
	{
		if (LastMessage == message) return;
		LastMessage = message;
		Logger.Info("[ManiaTrackerCapture] " + message);
	}

	private static int[] Counts(Player player) => new[] { (int)player.Hit300, player.Hit100, player.Hit50, player.HitGeki, player.HitKatu, player.HitMiss };

	public static bool FindReplay(ManiaTrackerQueue queue, ManiaTrackerPlay play)
	{
		var matches = new Dictionary<string, byte[]>();
		foreach (var folder in new[] { Path.Combine(play.ReplayDirectory, "Data", "r"), Path.Combine(play.ReplayDirectory, "Replays") })
		{
			if (!Directory.Exists(folder)) continue;
			foreach (var path in Directory.EnumerateFiles(folder, "*.osr"))
			{
				try
				{
					var info = new FileInfo(path);
					if (info.LastWriteTimeUtc < play.StartedAt.UtcDateTime.AddMinutes(-1) || info.Length is <= 0 or > 26214400) continue;
					using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
					if (stream.Length is <= 0 or > 26214400) continue;
					var bytes = new byte[(int)stream.Length];
					stream.ReadExactly(bytes);
					if (!ManiaTrackerReplay.Read(bytes).Matches(play)) continue;
					matches.TryAdd(ManiaTrackerQueue.Hex(SHA256.HashData(bytes)), bytes);
					if (matches.Count > 1) { play.Message = "Multiple matching replays; automatic upload withheld."; return false; }
				}
				catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { }
			}
		}
		return matches.Count == 1 && queue.AttachReplay(play, matches.Values.Single());
	}
}
