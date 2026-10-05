using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companella.Services.Common;
using Companella.Services.Platform;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Owns optional capture, account lifecycle and a single resumable upload worker.</summary>
public sealed class ManiaTrackerService : IDisposable
{
	private sealed class Preferences
	{
		public bool Enabled { get; set; }
		public DateTimeOffset NextRequest { get; set; }
	}

	private readonly ManiaTrackerQueue _queue;
	private readonly ManiaTrackerApiClient _api;
	private readonly ManiaTrackerCapture _capture;
	private readonly string _preferencesPath;
	private readonly SemaphoreSlim _apiGate = new(1);
	private readonly object _captureGate = new();
	private readonly object _requestGate = new();
	private readonly object _preferencesGate = new();
	private readonly CancellationTokenSource _stop = new();
	private CancellationTokenSource? _activeRequest;
	private CancellationTokenSource? _authorization;
	private Task? _running;
	private volatile bool _enabled;
	private Preferences _preferences = new();
	private JsonObject? _capabilities;
	private DateTimeOffset _capabilitiesUpdated;
	private volatile string _status = "Not connected";
	private volatile string _summary = "No plays submitted.";
	private volatile string _account = "Not connected";
	private ManiaTrackerPlay[] _snapshot = Array.Empty<ManiaTrackerPlay>();
	public bool Enabled => _enabled;
	public bool Connected => _api.Credentials != null;
	public bool Connecting { get { lock (_requestGate) return _authorization != null; } }
	public string Status => _status;
	public string Summary => _summary;
	public string Account => _account;
	public string CaptureStatus => _enabled ? _capture.LastMessage ?? "Waiting for a new stable mania play." : "Capture paused.";

	public ManiaTrackerService(OsuProcessDetector detector)
	{
		var directory = Path.Combine(DataPaths.AppDataFolder, "mania-tracker");
		_queue = new ManiaTrackerQueue(directory);
		_api = new ManiaTrackerApiClient(new ManiaTrackerCredentialStore(directory));
		_capture = new ManiaTrackerCapture(detector, _queue);
		_preferencesPath = Path.Combine(directory, "preferences.json");
	}

	internal ManiaTrackerService(ManiaTrackerQueue queue, ManiaTrackerApiClient api, string preferencesPath)
	{
		_queue = queue;
		_api = api;
		_capture = new ManiaTrackerCapture(() => null, () => null, queue);
		_preferencesPath = preferencesPath;
	}

	public void Start()
	{
		if (_running != null) return;
		try
		{
			_api.Load();
			if (File.Exists(_preferencesPath)) _preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_preferencesPath)) ?? new Preferences();
			_enabled = Connected && _preferences.Enabled;
			_account = _api.Credentials == null ? "Not connected" : $"{_api.Credentials.Username}{(_api.Credentials.ClientId == "companella-test" ? " (test connection)" : "")}";
			_status = !Connected ? "Connect an account to get started." : _enabled ? "Automatic submission enabled" : "Uploads paused";
			RefreshSummary();
		}
		catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException or JsonException)
		{
			_enabled = false;
			_status = "Could not restore the Mania Tracker connection or queue. Reconnect before enabling uploads.";
		}
		_running = Task.WhenAll(Task.Run(CaptureLoopAsync), Task.Run(WorkerLoopAsync));
	}

	public async Task ConnectAsync(bool test)
	{
		using var authorization = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
		lock (_requestGate)
		{
			if (_authorization != null) return;
			_authorization = authorization;
		}
		try
		{
			await _apiGate.WaitAsync(authorization.Token);
			try
			{
				if (Connected) { _status = "Disconnect the current account before connecting another."; return; }
				_status = "Finish connecting in your browser (expires after 10 minutes).";
				await _api.ConnectAsync(test, authorization.Token);
				_account = _api.Credentials!.Username + (test ? " (test connection)" : "");
				_status = "Connected. Enable automatic submission to send new completed stable plays.";
				_capabilities = null;
			}
			finally { _apiGate.Release(); }
		}
		catch (OperationCanceledException) { _status = "Connection cancelled or timed out."; }
		catch (Exception ex) { _status = SafeError(ex); }
		finally { lock (_requestGate) _authorization = null; }
	}

	public void CancelConnection()
	{
		lock (_requestGate) _authorization?.Cancel();
	}

	public void SetEnabled(bool enabled)
	{
		_enabled = enabled && Connected;
		if (!_enabled)
		{
			lock (_requestGate) _activeRequest?.Cancel();
			lock (_captureGate) _capture.Reset();
		}
		lock (_preferencesGate)
		{
			_preferences.Enabled = _enabled;
			SavePreferences();
		}
		_status = _enabled ? "Automatic submission enabled for new completed osu!stable plays." : "Uploads paused. Pending plays are retained.";
	}

	public async Task DisconnectAsync()
	{
		SetEnabled(false);
		CancelConnection();
		await _apiGate.WaitAsync(_stop.Token);
		try
		{
			await _api.RevokeAsync(_stop.Token);
			_account = "Not connected";
			_status = "Disconnected. Old pending plays will not be sent through a new connection.";
		}
		catch (Exception) { _status = "Uploads stopped, but remote disconnect is unconfirmed. Retry Disconnect or revoke access on Mania Tracker."; }
		finally { _apiGate.Release(); }
	}

	public async Task RetryPendingAsync()
	{
		await _apiGate.WaitAsync(_stop.Token);
		try
		{
			foreach (var play in _queue.ReadAll().Where(x => x.State == "retry_paused" && BelongsToConnection(x)))
			{
				play.State = play.SubmissionId == null ? "pending" : "queued";
				play.Attempts = 0;
				play.NextAttempt = DateTimeOffset.UtcNow;
				_queue.Save(play);
			}
			_status = "Retryable plays resumed. Server cooldowns still apply.";
			RefreshSummary();
		}
		finally { _apiGate.Release(); }
	}

	public async Task ClearPendingAsync()
	{
		SetEnabled(false);
		await _apiGate.WaitAsync(_stop.Token);
		try
		{
			foreach (var play in _queue.ReadAll().Where(x => !x.IsTerminal))
			{
				play.State = "skipped";
				play.Message = "Removed from this device's queue. Already submitted plays may remain on Mania Tracker.";
				_queue.Save(play);
				_queue.ReleaseAssets(play);
			}
			RefreshSummary();
			_status = "Pending local files cleared; uploads paused.";
		}
		finally { _apiGate.Release(); }
	}

	private async Task CaptureLoopAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			try
			{
				lock (_captureGate) _capture.Poll(_enabled ? _api.Credentials : null);
			}
			catch (Exception ex) { _status = SafeError(ex); lock (_captureGate) _capture.Reset(); }
			try { await Task.Delay(250, _stop.Token); } catch (OperationCanceledException) { break; }
		}
	}

	private async Task WorkerLoopAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			try
			{
				if (_enabled)
				{
					await _apiGate.WaitAsync(_stop.Token);
					try
					{
						if (_enabled)
						{
							using var request = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
							lock (_requestGate)
							{
								_activeRequest = request;
								if (!_enabled) request.Cancel();
							}
							try { await ProcessNextAsync(request.Token); }
							finally { lock (_requestGate) _activeRequest = null; }
						}
					}
					finally { _apiGate.Release(); }
				}
				RefreshSummary();
			}
			catch (OperationCanceledException) { }
			catch (Exception ex) { _status = SafeError(ex); }
			try { await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token); } catch (OperationCanceledException) { break; }
		}
	}

	private bool BelongsToConnection(ManiaTrackerPlay play) => _api.Credentials is { } credentials &&
		play.UserId == credentials.UserId && play.InstallationId == credentials.InstallationId && play.ClientId == credentials.ClientId;

	internal async Task ProcessNextAsync(CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		var plays = _queue.ReadAll();
		foreach (var waiting in plays.Where(x => x.State == "waiting_replay" && BelongsToConnection(x)))
		{
			token.ThrowIfCancellationRequested();
			if (!ManiaTrackerCapture.FindReplay(_queue, waiting))
			{
				if (DateTimeOffset.UtcNow - waiting.FinishedAt > TimeSpan.FromMinutes(10))
				{
					waiting.State = "skipped";
					waiting.Message ??= "Original replay not found within 10 minutes. Save the replay in osu! after future plays.";
					_queue.Save(waiting);
					_queue.ReleaseAssets(waiting);
				}
				else _queue.Save(waiting);
			}
		}
		if (DateTimeOffset.UtcNow < _preferences.NextRequest) return;
		var play = _queue.ReadAll().Where(x => !x.IsTerminal && x.State is not ("waiting_replay" or "retry_paused") && BelongsToConnection(x) && x.NextAttempt <= DateTimeOffset.UtcNow).OrderBy(x => x.NextAttempt).FirstOrDefault();
		if (play == null) return;
		try
		{
			if (_capabilities == null || DateTimeOffset.UtcNow - _capabilitiesUpdated > TimeSpan.FromMinutes(15))
			{
				_capabilities = await _api.CapabilitiesAsync(token);
				_capabilitiesUpdated = DateTimeOffset.UtcNow;
			}
			if (_capabilities["enabled"]?.GetValue<bool>() != true || _capabilities["protocol_version"]?.GetValue<int>() != 1)
				throw new ManiaTrackerApiException(503, "integration_unavailable", "Mania Tracker submission is temporarily unavailable.");
			var limits = _capabilities["limits"];
			if (play.ReplayLength > (limits?["max_replay_bytes"]?.GetValue<long>() ?? 26214400) || play.ChartLength > (limits?["max_beatmap_bytes"]?.GetValue<long>() ?? 8388608))
				throw new ManiaTrackerApiException(413, "file_too_large", "Replay or chart exceeds the server's size limit.");
			_queue.VerifyAssets(play);
			_status = "Syncing " + Path.GetFileNameWithoutExtension(play.BeatmapPath);
			JsonObject receipt;
			if (play.SubmissionId == null)
			{
				var manifest = new JsonObject
				{
					["protocol_version"] = 1, ["client_version"] = "companella/" + ReadVersion(),
					["game_client"] = "stable", ["capture_kind"] = "automatic_completed_play",
					["replay"] = new JsonObject { ["sha256"] = play.ReplaySha256, ["byte_length"] = play.ReplayLength },
					["chart"] = new JsonObject { ["md5"] = play.ChartMd5, ["sha256"] = play.ChartSha256, ["byte_length"] = play.ChartLength }
				};
				receipt = await _api.SendAsync(HttpMethod.Post, ManiaTrackerApiClient.Prefix + "/submissions", manifest, null, play.Id, token);
				play.SubmissionId = receipt["submission_id"]?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(play.SubmissionId)) throw new HttpRequestException("Reservation receipt was incomplete.");
				_queue.Save(play);
			}
			else receipt = await _api.SendAsync(HttpMethod.Get, SubmissionPath(play), null, null, null, token);
			ApplyReceipt(play, receipt);
			if (play.State == "accepted" && play.RatingJson == null)
			{
				receipt = await _api.SendAsync(HttpMethod.Get, SubmissionPath(play), null, null, null, token);
				ApplyReceipt(play, receipt);
			}
			if (play.State == "awaiting_assets")
			{
				await UploadIfNeededAsync(play, receipt, true, token);
				await UploadIfNeededAsync(play, receipt, false, token);
				receipt = await _api.SendAsync(HttpMethod.Post, SubmissionPath(play) + "/complete", null, null, null, token);
				ApplyReceipt(play, receipt);
			}
			play.Attempts = 0;
			if (play.State == "accepted") play.SyncedAt = DateTimeOffset.UtcNow;
			play.NextAttempt = DateTimeOffset.UtcNow.AddSeconds(play.State == "deferred" ? 60 : 10);
			_status = play.IsTerminal ? Describe(play) : "Play uploaded; waiting for Mania Tracker processing.";
		}
		catch (ManiaTrackerApiException ex)
		{
			play.Message = ex.Message;
			if (ex.Status == 404 && play.SubmissionId != null) play.State = "deleted";
			else if (ex.Status is 401 or 403 || ex.Code is "invalid_grant" or "installation_revoked")
			{
				SetEnabled(false);
				_status = ex.Message + " Uploads paused; check account access or reconnect.";
			}
			else if (ex.Status == 409 && ex.Code == "assets_missing")
			{
				play.State = "awaiting_assets";
				ScheduleRetry(play, ex.RetryAfter);
			}
			else if (ex.Status is 429 or >= 500)
			{
				ScheduleRetry(play, ex.RetryAfter);
				lock (_preferencesGate)
				{
					_preferences.NextRequest = play.NextAttempt;
					SavePreferences();
				}
			}
			else play.State = "rejected";
		}
		catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
		{
			play.State = "skipped";
			play.Message = "Staged assets are missing, changed, or invalid. This play will not be sent.";
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !token.IsCancellationRequested))
		{
			play.Message = "Connection interrupted. The same play will be retried.";
			ScheduleRetry(play, null);
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
		{
			play.State = "retry_paused";
			play.Message = "Unexpected server response. Original files retained; retry after checking the integration.";
		}
		finally
		{
			_queue.Save(play);
			if (play.IsTerminal) _queue.ReleaseAssets(play);
		}
	}

	private async Task UploadIfNeededAsync(ManiaTrackerPlay play, JsonObject receipt, bool replay, CancellationToken token)
	{
		if (receipt[replay ? "needs_replay" : "needs_beatmap"]?.GetValue<bool>() != true) return;
		var expected = SubmissionPath(play) + (replay ? "/replay" : "/beatmap");
		if (receipt[replay ? "replay_upload_path" : "beatmap_upload_path"]?.GetValue<string>() != expected)
			throw new InvalidDataException("Unexpected upload destination.");
		await _api.SendAsync(HttpMethod.Put, expected, null, _queue.AssetPath(play, replay), null, token);
	}

	private static string SubmissionPath(ManiaTrackerPlay play) => ManiaTrackerApiClient.Prefix + "/submissions/" + Uri.EscapeDataString(play.SubmissionId!);

	private static void ApplyReceipt(ManiaTrackerPlay play, JsonObject receipt)
	{
		var state = receipt["state"]?.GetValue<string>();
		if (state is not ("awaiting_assets" or "queued" or "validating" or "analyzing" or "accepted" or "deferred" or "rejected" or "expired" or "deleted"))
			throw new HttpRequestException("Unknown or missing submission state.");
		play.State = state;
		play.Message = receipt["error"]?["message"]?.GetValue<string>() ?? receipt["error"]?["code"]?.GetValue<string>();
		play.RatingJson = receipt["rating"]?.ToJsonString();
	}

	private static void ScheduleRetry(ManiaTrackerPlay play, TimeSpan? retryAfter)
	{
		play.Attempts++;
		var delay = TimeSpan.FromSeconds(Math.Min(1800, 5 * Math.Pow(2, play.Attempts)) + Random.Shared.Next(5));
		if (retryAfter > delay) delay = retryAfter.Value;
		play.NextAttempt = DateTimeOffset.UtcNow + delay;
		if (play.Attempts >= 8) play.State = "retry_paused";
	}

	private void SavePreferences()
	{
		File.WriteAllText(_preferencesPath + ".tmp", JsonSerializer.Serialize(_preferences));
		File.Move(_preferencesPath + ".tmp", _preferencesPath, true);
	}

	private void RefreshSummary()
	{
		var plays = _queue.ReadAll();
		var pending = plays.Count(x => !x.IsTerminal);
		Volatile.Write(ref _snapshot, plays.ToArray());
		var held = plays.Count(x => !x.IsTerminal && !BelongsToConnection(x));
		var accepted = plays.FirstOrDefault(x => x.State == "accepted");
		_summary = $"{pending} pending ({held} held for another connection). Last successful sync: {(accepted == null ? "none" : (accepted.SyncedAt ?? accepted.FinishedAt).ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}.";
		_summary += string.Concat(plays.Take(6).Select(x => "\n" + Path.GetFileNameWithoutExtension(x.BeatmapPath) + ": " + Describe(x)));
	}

	public string? DescribeSessionPlay(string beatmapHash, DateTime recordedAt)
	{
		// Display association only; upload eligibility always uses the original capture evidence.
		var matches = Volatile.Read(ref _snapshot).Where(x => x.ChartMd5 == beatmapHash && Math.Abs((x.FinishedAt.UtcDateTime - recordedAt.ToUniversalTime()).TotalSeconds) < 35).ToArray();
		return matches.Length == 1 ? "Mania Tracker: " + Describe(matches[0]) : null;
	}

	private static string Describe(ManiaTrackerPlay play)
	{
		if (play.State == "accepted" && play.RatingJson != null)
		{
			var rating = JsonNode.Parse(play.RatingJson);
			var ssr = rating?["msd"]?.GetValue<double>();
			var chart = rating?["chart_msd"]?.GetValue<double>();
			var dan = rating?["dan"]?["label"]?.GetValue<string>();
			var reason = rating?["unrated_reason"]?.GetValue<string>();
			var family = rating?["dan"]?["family"]?.GetValue<string>() == "ln" ? "LN dan" : "Dan";
			return "Accepted · " + (ssr.HasValue ? $"Play SSR {ssr.Value:F2}" : "Unrated" + (reason == null ? "" : " (" + reason + ")")) + (chart.HasValue ? $" · Chart MSD {chart.Value:F2}" : "") + (dan == null ? "" : $" · {family} {dan}");
		}
		return play.State.Replace('_', ' ') + (play.Message == null ? "" : ": " + play.Message);
	}

	private static string ReadVersion() => File.Exists(DataPaths.VersionFile) ? File.ReadAllText(DataPaths.VersionFile).Trim().TrimStart('v') : "dev";
	private static string SafeError(Exception ex) => ex is ManiaTrackerApiException ? ex.Message : "Mania Tracker could not finish this operation (" + ex.GetType().Name + "). Pending plays are retained.";

	public void Dispose()
	{
		_stop.Cancel();
		_enabled = false;
		lock (_captureGate) _capture.Reset();
		CancelConnection();
		// Dispose transport only once all owners have observed cancellation.
		if (_running != null) _ = _running.ContinueWith(_ => _api.Dispose(), TaskScheduler.Default);
		else _api.Dispose();
	}
}
