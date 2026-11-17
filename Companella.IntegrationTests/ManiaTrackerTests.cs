using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Companella.Services.Integrations.ManiaTracker;
using Microsoft.Data.Sqlite;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;
using Xunit;

namespace Companella.IntegrationTests;

public sealed class ManiaTrackerTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "companella-tracker-tests", Guid.NewGuid().ToString("N"));
	private static readonly byte[] _chart = Encoding.UTF8.GetBytes("osu file format v14\r\n[General]\r\nMode:3\r\n[HitObjects]\r\n64,192,1000,1,0\r\n192,192,2000,1,0\r\n");

	[Fact]
	public void ProofIsRequestBoundAndUsesP1363Signature()
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var uri = new Uri("https://mania-tracker.com/test?ignored=yes#fragment");
		var proof = ManiaTrackerProof.Create(key, HttpMethod.Put, uri, "access", "nonce");
		var parts = proof.Split('.');
		var header = DecodeJson(parts[0]);
		var payload = DecodeJson(parts[1]);
		Assert.Equal("dpop+jwt", (string?)header["typ"]);
		Assert.Null(header["jwk"]?["d"]);
		Assert.Equal("PUT", (string?)payload["htm"]);
		Assert.Equal("https://mania-tracker.com/test", (string?)payload["htu"]);
		Assert.Equal("nonce", (string?)payload["nonce"]);
		Assert.Equal(ManiaTrackerProof.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes("access"))), (string?)payload["ath"]);
		Assert.Equal(64, Decode(parts[2]).Length);
		Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
		var second = DecodeJson(ManiaTrackerProof.Create(key, HttpMethod.Put, uri, null, null).Split('.')[1]);
		Assert.NotEqual((string?)payload["jti"], (string?)second["jti"]);
		Assert.Null(second["ath"]);
	}

	[Fact]
	public async Task NonceChallengeUsesFreshProofAndReopensUpload()
	{
		var proofs = new List<JsonNode>();
		var bodies = new List<byte[]>();
		using var api = CreateApi(async request =>
		{
			proofs.Add(DecodeJson(request.Headers.GetValues("DPoP").Single().Split('.')[1]));
			bodies.Add(await request.Content!.ReadAsByteArrayAsync());
			Assert.Equal("DPoP", request.Headers.Authorization!.Scheme);
			if (proofs.Count == 1)
			{
				var response = Response(401, "{\"error\":\"use_dpop_nonce\"}");
				response.Headers.Add("DPoP-Nonce", "challenge");
				return response;
			}
			return Response(200, "{}");
		});
		var path = Path.Combine(_directory, "upload.osr");
		File.WriteAllBytes(path, new byte[] { 0, 11, 13, 255 });
		await api.SendAsync(HttpMethod.Put, ManiaTrackerApiClient.Prefix + "/submissions/test/replay", null, path, null, default);
		Assert.Equal(2, proofs.Count);
		Assert.Equal(bodies[0], bodies[1]);
		Assert.NotEqual((string?)proofs[0]["jti"], (string?)proofs[1]["jti"]);
		Assert.Equal("challenge", (string?)proofs[1]["nonce"]);
	}

	[Fact]
	public async Task ExpiredTokenRefreshesOnceAndTokenProofHasNoAth()
	{
		var calls = 0;
		using var api = CreateApi(request =>
		{
			calls++;
			if (request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal))
			{
				Assert.Null(request.Headers.Authorization);
				Assert.Null(DecodeJson(request.Headers.GetValues("DPoP").Single().Split('.')[1])["ath"]);
				return Task.FromResult(Response(200, "{\"access_token\":\"new-access\",\"refresh_token\":\"refresh\",\"token_type\":\"DPoP\",\"expires_in\":300}"));
			}
			return Task.FromResult(calls == 1 ? Response(401, "{\"error\":\"token_expired\"}") : Response(200, "{}"));
		});
		await api.SendAsync(HttpMethod.Get, ManiaTrackerApiClient.Prefix + "/me", null, null, null, default);
		Assert.Equal(3, calls);
		Assert.Equal("new-access", api.Credentials!.AccessToken);
	}

	[Fact]
	public async Task InvalidProofDoesNotRefreshOrFallBack()
	{
		var calls = 0;
		using var api = CreateApi(_ => { calls++; return Task.FromResult(Response(401, "{\"error\":\"key_mismatch\"}")); });
		await Assert.ThrowsAsync<ManiaTrackerApiException>(() => api.SendAsync(HttpMethod.Get, ManiaTrackerApiClient.Prefix + "/me", null, null, null, default));
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task RevokeUsesNoAccessTokenAndErasesCredentialsOnlyOnSuccess()
	{
		var success = false;
		using var api = CreateApi(request =>
		{
			Assert.Null(request.Headers.Authorization);
			Assert.Null(DecodeJson(request.Headers.GetValues("DPoP").Single().Split('.')[1])["ath"]);
			return Task.FromResult(Response(success ? 200 : 503, success ? "{\"revoked\":true}" : "{\"error\":\"unavailable\"}"));
		});
		await Assert.ThrowsAsync<ManiaTrackerApiException>(() => api.RevokeAsync(default));
		Assert.NotNull(api.Credentials);
		Assert.True(File.Exists(Path.Combine(_directory, "connection.dat")));
		success = true;
		await api.RevokeAsync(default);
		Assert.Null(api.Credentials);
		Assert.False(File.Exists(Path.Combine(_directory, "connection.dat")));
	}

	[Fact]
	public void QueueSurvivesRestartAndKeepsOriginalBytes()
	{
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		queue.Capture(play, _chart);
		Assert.True(queue.AttachReplay(play, Replay(play)));
		var restored = new ManiaTrackerQueue(_directory).ReadAll().Single();
		Assert.Equal(play.Id, restored.Id);
		Assert.Equal(play.InstallationId, restored.InstallationId);
		queue.VerifyAssets(restored);
		Assert.Equal(_chart, File.ReadAllBytes(queue.AssetPath(restored, false)));
		File.AppendAllText(queue.AssetPath(restored, false), "changed");
		Assert.Throws<InvalidDataException>(() => queue.VerifyAssets(restored));
	}

	[Fact]
	public void DuplicateReplayCannotBecomeAnotherSubmission()
	{
		var queue = new ManiaTrackerQueue(_directory);
		var first = Play();
		queue.Capture(first, _chart);
		var bytes = Replay(first);
		Assert.True(queue.AttachReplay(first, bytes));
		var second = Play();
		queue.Capture(second, _chart);
		Assert.False(queue.AttachReplay(second, bytes));
		Assert.Equal("waiting_replay", queue.ReadAll().Single(x => x.Id == second.Id).State);
	}

	[Theory]
	[InlineData(30000000, 3)]
	[InlineData(30000019, 3)]
	[InlineData(20260101, 0)]
	public void RejectsLazerAndOtherRulesets(int version, byte mode)
	{
		Assert.Throws<InvalidDataException>(() => ManiaTrackerReplay.Read(Replay(Play(), version, mode)));
	}

	[Fact]
	public void ReplayMustMatchEveryObservedScoreField()
	{
		var play = Play();
		var replay = ManiaTrackerReplay.Read(Replay(play));
		Assert.True(replay.Matches(play));
		Assert.False((replay with { PlayerName = "someone-else" }).Matches(play));
		Assert.False((replay with { Mods = 64 }).Matches(play));
		Assert.False((replay with { Score = 99 }).Matches(play));
		Assert.False((replay with { Timestamp = play.StartedAt.AddMinutes(-1) }).Matches(play));
		Assert.Throws<InvalidDataException>(() => ManiaTrackerReplay.Read(Replay(play)[..^10]));
	}

	[Theory]
	[InlineData(false, 7, 2, 0, true)]
	[InlineData(false, 14, 2, 0, true)]
	[InlineData(true, 7, 2, 0, false)]
	[InlineData(false, 5, 2, 0, false)]
	[InlineData(false, 7, 1, 0, false)]
	[InlineData(false, 7, 2, 2048, false)]
	public void CaptureRequiresFreshRealCompletedGameplay(bool replay, int endStatus, ushort hits, int mods, bool expected)
	{
		Directory.CreateDirectory(_directory);
		var path = Path.Combine(_directory, "map.osu");
		File.WriteAllBytes(path, _chart);
		var queue = new ManiaTrackerQueue(_directory);
		var capture = new ManiaTrackerCapture(() => path, () => _directory, queue);
		var connection = Credentials();
		var general = new GeneralData { RawStatus = 5, GameMode = 3, Mods = mods };
		var player = new Player { Mode = 3, Username = "player", IsReplay = replay, Score = 900000 };
		var map = new CurrentBeatmap { Md5 = ManiaTrackerQueue.Hex(MD5.HashData(_chart)) };
		capture.Observe(connection, general, player, map);
		general.RawStatus = 2;
		capture.Observe(connection, general, player, map);
		player.Hit300 = 1;
		general.AudioTime = 1000;
		capture.Observe(connection, general, player, map);
		general.RawStatus = endStatus;
		player.Hit300 = hits;
		capture.Observe(connection, general, player, map);
		capture.Observe(connection, general, player, map);
		Assert.Equal(expected ? 1 : 0, queue.ReadAll().Count);
	}

	[Fact]
	public async Task WorkerReservesUploadsCompletesAndPollsWithoutSendingScoreClaims()
	{
		var calls = new List<string>();
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		queue.Capture(play, _chart);
		queue.AttachReplay(play, Replay(play));
		using var api = CreateApi(async request =>
		{
			var path = request.RequestUri!.AbsolutePath;
			calls.Add(request.Method + " " + path);
			if (path.EndsWith("/capabilities", StringComparison.Ordinal)) return Response(200, "{\"enabled\":true,\"protocol_version\":1}");
			if (path.EndsWith("/submissions", StringComparison.Ordinal))
			{
				Assert.Equal(play.Id, request.Headers.GetValues("Idempotency-Key").Single());
				var manifest = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
				Assert.Equal("stable", (string?)manifest["game_client"]);
				foreach (var forbidden in new[] { "username", "accuracy", "mods", "score", "rate", "msd", "user_id" }) Assert.Null(manifest[forbidden]);
				return Response(201, "{\"submission_id\":\"receipt\",\"state\":\"awaiting_assets\",\"needs_replay\":true,\"needs_beatmap\":false,\"replay_upload_path\":\"/api/integrations/companella/v1/submissions/receipt/replay\"}");
			}
			if (path.EndsWith("/replay", StringComparison.Ordinal))
			{
				Assert.Equal(File.ReadAllBytes(queue.AssetPath(play, true)), await request.Content!.ReadAsByteArrayAsync());
				Assert.Equal("application/octet-stream", request.Content.Headers.ContentType!.MediaType);
				return Response(200, "{}");
			}
			if (path.EndsWith("/complete", StringComparison.Ordinal)) return Response(202, "{\"state\":\"queued\"}");
			return Response(200, "{\"state\":\"accepted\",\"rating\":{\"msd\":12.3,\"chart_msd\":13.4}}");
		});
		using var service = new ManiaTrackerService(queue, api, Path.Combine(_directory, "preferences.json"));
		await service.ProcessNextAsync(default);
		var queued = queue.ReadAll().Single();
		Assert.Equal("queued", queued.State);
		Assert.Equal("receipt", queued.SubmissionId);
		queued.NextAttempt = DateTimeOffset.MinValue;
		queue.Save(queued);
		await service.ProcessNextAsync(default);
		Assert.Equal("accepted", queue.ReadAll().Single().State);
		Assert.False(File.Exists(queue.AssetPath(play, true)));
		Assert.Equal(5, calls.Count);
	}

	[Fact]
	public async Task WorkerNeverMovesPendingPlaysToAnotherConnection()
	{
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		play.InstallationId = "old-connection";
		queue.Capture(play, _chart);
		queue.AttachReplay(play, Replay(play));
		using var api = CreateApi(_ => throw new Exception("No request should be made."));
		using var service = new ManiaTrackerService(queue, api, Path.Combine(_directory, "preferences.json"));
		await service.ProcessNextAsync(default);
		Assert.Equal("pending", queue.ReadAll().Single().State);
	}

	[Fact]
	public async Task RateLimitPreservesQueueAndHonoursRetryAfter()
	{
		var calls = 0;
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		queue.Capture(play, _chart);
		queue.AttachReplay(play, Replay(play));
		using var api = CreateApi(request =>
		{
			calls++;
			if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities", StringComparison.Ordinal)) return Task.FromResult(Response(200, "{\"enabled\":true,\"protocol_version\":1}"));
			var response = Response(429, "{\"error\":\"rate_limited\"}");
			response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
			return Task.FromResult(response);
		});
		using var service = new ManiaTrackerService(queue, api, Path.Combine(_directory, "preferences.json"));
		await service.ProcessNextAsync(default);
		await service.ProcessNextAsync(default);
		Assert.Equal(2, calls);
		var restored = queue.ReadAll().Single();
		Assert.Equal(play.Id, restored.Id);
		Assert.True(restored.NextAttempt > DateTimeOffset.UtcNow.AddMinutes(4));
		queue.VerifyAssets(restored);
	}

	[Fact]
	public async Task CallbackIgnoresWrongStateAndAcceptsOnlyTheOriginalAttempt()
	{
		using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var callback = ManiaTrackerAuthorization.ReceiveCodeAsync(listener, "expected", timeout.Token);
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		using var http = new HttpClient();
		using var invalid = await http.GetAsync($"http://127.0.0.1:{port}/companella/callback?state=wrong&code=forged", timeout.Token);
		Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
		Assert.False(callback.IsCompleted);
		using var valid = await http.GetAsync($"http://127.0.0.1:{port}/companella/callback?state=expected&code=original", timeout.Token);
		Assert.Equal("original", await callback);
	}

	[Fact]
	public void AmbiguousReplayMatchesRemainUnsent()
	{
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		play.ReplayDirectory = _directory;
		queue.Capture(play, _chart);
		var folder = Path.Combine(_directory, "Replays");
		Directory.CreateDirectory(folder);
		var bytes = Replay(play);
		File.WriteAllBytes(Path.Combine(folder, "one.osr"), bytes);
		bytes[^1] = 1;
		File.WriteAllBytes(Path.Combine(folder, "two.osr"), bytes);
		Assert.False(ManiaTrackerCapture.FindReplay(queue, play));
		Assert.Equal("waiting_replay", queue.ReadAll().Single().State);
	}

	[Theory]
	[InlineData(503, "{\"error\":\"unavailable\"}", false)]
	[InlineData(200, "not json", false)]
	[InlineData(413, "{\"error\":\"file_too_large\"}", true)]
	public async Task WorkerRetainsRetryableFailuresAndStopsPermanentOnes(int status, string body, bool terminal)
	{
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		queue.Capture(play, _chart);
		queue.AttachReplay(play, Replay(play));
		using var api = CreateApi(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/capabilities", StringComparison.Ordinal)
			? Response(200, "{\"enabled\":true,\"protocol_version\":1}") : Response(status, body)));
		using var service = new ManiaTrackerService(queue, api, Path.Combine(_directory, "preferences.json"));
		await service.ProcessNextAsync(default);
		Assert.Equal(terminal, queue.ReadAll().Single().IsTerminal);
		Assert.Equal(!terminal, File.Exists(queue.AssetPath(play, true)));
	}

	[Fact]
	public async Task LostReservationResponseReusesTheSameIdempotencyKey()
	{
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		queue.Capture(play, _chart);
		queue.AttachReplay(play, Replay(play));
		var keys = new List<string>();
		using var api = CreateApi(request =>
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities", StringComparison.Ordinal)) return Task.FromResult(Response(200, "{\"enabled\":true,\"protocol_version\":1}"));
			keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
			if (keys.Count == 1) throw new HttpRequestException("Response lost after server stored the reservation.");
			return Task.FromResult(Response(200, "{\"submission_id\":\"receipt\",\"state\":\"queued\"}"));
		});
		using var service = new ManiaTrackerService(queue, api, Path.Combine(_directory, "preferences.json"));
		await service.ProcessNextAsync(default);
		var pending = queue.ReadAll().Single();
		pending.NextAttempt = DateTimeOffset.MinValue;
		queue.Save(pending);
		await service.ProcessNextAsync(default);
		Assert.Equal(new[] { play.Id, play.Id }, keys);
		Assert.Equal("receipt", queue.ReadAll().Single().SubmissionId);
	}

	[Fact]
	public async Task MissingAssetsAreRecoveredFromTheReceiptBeforeCompletingAgain()
	{
		var queue = new ManiaTrackerQueue(_directory);
		var play = Play();
		queue.Capture(play, _chart);
		queue.AttachReplay(play, Replay(play));
		play.SubmissionId = "receipt";
		queue.Save(play);
		var completions = 0;
		var uploads = 0;
		using var api = CreateApi(request =>
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("/capabilities", StringComparison.Ordinal)) return Task.FromResult(Response(200, "{\"enabled\":true,\"protocol_version\":1}"));
			if (path.EndsWith("/beatmap", StringComparison.Ordinal)) { uploads++; return Task.FromResult(Response(200, "{}")); }
			if (path.EndsWith("/complete", StringComparison.Ordinal))
			{
				completions++;
				return Task.FromResult(completions == 1 ? Response(409, "{\"error\":\"assets_missing\"}") : Response(202, "{\"state\":\"queued\"}"));
			}
			return Task.FromResult(Response(200, "{\"state\":\"awaiting_assets\",\"needs_beatmap\":true,\"beatmap_upload_path\":\"/api/integrations/companella/v1/submissions/receipt/beatmap\"}"));
		});
		using var service = new ManiaTrackerService(queue, api, Path.Combine(_directory, "preferences.json"));
		await service.ProcessNextAsync(default);
		var pending = queue.ReadAll().Single();
		pending.NextAttempt = DateTimeOffset.MinValue;
		queue.Save(pending);
		await service.ProcessNextAsync(default);
		Assert.Equal(2, uploads);
		Assert.Equal(2, completions);
		Assert.Equal("queued", queue.ReadAll().Single().State);
	}

	[Fact]
	public void EmptyPlayerCannotTurnIntoAPerfectScore()
	{
		var player = new Player { Mode = 3, Accuracy = 100 };
		var stats = Companella.Services.Session.SessionPlayMemoryHelper.BuildStats(player);
		Assert.Equal(0, stats.Accuracy);
		Assert.Equal(0, stats.TotalHits);
	}

	[Fact]
	public void SessionAccuracyComesFromJudgementsRatherThanResetAccuracy()
	{
		var player = new Player { Mode = 3, Accuracy = 100, HitGeki = 8, Hit300 = 1, HitMiss = 1 };
		var stats = Companella.Services.Session.SessionPlayMemoryHelper.BuildStats(player);
		Assert.Equal(90, stats.Accuracy);
		Assert.Equal(1, stats.Misses);
		Assert.Equal(10, stats.TotalHits);
	}

	[Fact]
	public void InvalidResultsPointerIsNotAcceptedAsManiaScore()
	{
		var results = new ResultsScreen { Mode = 1640929060, Hit300 = 49024, Score = 417770 };
		Assert.Equal(0, Companella.Services.Session.SessionPlayMemoryHelper.BuildStats(results).TotalHits);
	}

	[Fact]
	public void AnalysisMatcherRejectsThePreviousPlayEvenWhenItIsTheNewestFile()
	{
		var play = Play();
		var folder = Path.Combine(_directory, "Replays");
		Directory.CreateDirectory(folder);
		File.WriteAllBytes(Path.Combine(folder, "previous.osr"), Replay(play));
		var finishedAt = play.FinishedAt.UtcDateTime.AddSeconds(43);
		Assert.Null(Companella.Services.Session.SessionReplayMatcher.Find(_directory, play.ChartMd5, finishedAt));
		play.FinishedAt = new DateTimeOffset(finishedAt);
		var path = Path.Combine(folder, "current.osr");
		File.WriteAllBytes(path, Replay(play));
		Assert.Equal(path, Companella.Services.Session.SessionReplayMatcher.Find(_directory, play.ChartMd5.ToUpperInvariant(), finishedAt));
	}

	[Fact]
	public void MatchingReplayUpdatesStoredScoreAndSessionTotals()
	{
		Directory.CreateDirectory(_directory);
		using var db = new Companella.Services.Database.SessionDatabaseService(Path.Combine(_directory, "session.db"));
		var play = new Companella.Models.Session.SessionPlayResult
		{
			BeatmapHash = "ABCDEF", Accuracy = 100, RecordedAt = DateTime.UtcNow,
			SessionTime = TimeSpan.FromMinutes(1), Grade = "SS"
		};
		var sessionId = db.SaveSession(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, new() { play });
		var stored = db.GetPlaysWithoutReplayByBeatmapHash("abcdef").Single();
		Assert.True(db.UpdateReplayInfo(stored.Id, "digest", "saved.osr", 93.5, 12));
		var session = db.GetSessionById(sessionId)!;
		Assert.Equal(93.5, session.AverageAccuracy);
		Assert.Equal(93.5, session.BestAccuracy);
		Assert.Equal(93.5, session.Plays.Single().Accuracy);
		Assert.Equal(12, session.Plays.Single().Misses);
		Assert.Equal("saved.osr", session.Plays.Single().ReplayPath);
		Assert.NotEqual("SS", session.Plays.Single().Grade);
	}

	private ManiaTrackerApiClient CreateApi(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
	{
		var store = new ManiaTrackerCredentialStore(_directory);
		store.Save(Credentials());
		var api = new ManiaTrackerApiClient(store, new Handler(responder));
		api.Load();
		return api;
	}

	private static ManiaTrackerCredentials Credentials()
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		return new ManiaTrackerCredentials { PrivateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey()), AccessToken = "access", RefreshToken = "refresh", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), UserId = 1, InstallationId = "installation", ClientId = "companella-test", Username = "player" };
	}

	private static ManiaTrackerPlay Play() => new()
	{
		UserId = 1, InstallationId = "installation", ClientId = "companella-test", PlayerName = "player",
		StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1), FinishedAt = DateTimeOffset.UtcNow,
		ChartMd5 = ManiaTrackerQueue.Hex(MD5.HashData(_chart)), Score = 900000, Judgements = new[] { 2, 0, 0, 0, 0, 0 }
	};

	private static byte[] Replay(ManiaTrackerPlay play, int version = 20260101, byte mode = 3)
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
		writer.Write(mode);
		writer.Write(version);
		foreach (var value in new[] { play.ChartMd5, play.PlayerName, "replay-hash" }) { writer.Write((byte)11); writer.Write(value); }
		foreach (var count in play.Judgements) writer.Write((ushort)count);
		writer.Write(play.Score);
		writer.Write((ushort)2);
		writer.Write(false);
		writer.Write(play.Mods);
		writer.Write((byte)0);
		writer.Write(play.FinishedAt.UtcTicks);
		writer.Write(32);
		writer.Write(new byte[32]);
		writer.Write(0L);
		writer.Flush();
		return stream.ToArray();
	}

	private static HttpResponseMessage Response(int status, string json) => new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
	private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
	private static JsonNode DecodeJson(string value) => JsonNode.Parse(Decode(value))!;
	private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
	}
}
