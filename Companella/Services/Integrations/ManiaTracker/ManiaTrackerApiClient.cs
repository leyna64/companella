using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Protocol error with a server-directed retry delay.</summary>
public sealed class ManiaTrackerApiException : Exception
{
	public int Status { get; }
	public string Code { get; }
	public TimeSpan? RetryAfter { get; }
	public ManiaTrackerApiException(int status, string code, string message, TimeSpan? retryAfter = null) : base(message)
	{
		Status = status;
		Code = code;
		RetryAfter = retryAfter;
	}
}

/// <summary>Protocol client. Its owner serializes requests, token refresh and connection changes.</summary>
public sealed class ManiaTrackerApiClient : IDisposable
{
	public const string Origin = "https://mania-tracker.com";
	public const string Prefix = "/api/integrations/companella/v1";
	private readonly HttpClient _http;
	private readonly ManiaTrackerCredentialStore _store;
	private string? _nonce;
	public ManiaTrackerCredentials? Credentials { get; private set; }

	public ManiaTrackerApiClient(ManiaTrackerCredentialStore store, HttpMessageHandler? handler = null)
	{
		_store = store;
		_http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(4) };
	}

	public void Load() => Credentials = _store.Load();

	public async Task<JsonObject> CapabilitiesAsync(CancellationToken token)
	{
		using var response = await _http.GetAsync(Origin + Prefix + "/capabilities", token);
		return await ReadResponseAsync(response, token);
	}

	public async Task ConnectAsync(bool test, CancellationToken token)
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var authorization = await ManiaTrackerAuthorization.AuthorizeAsync(key, test, token);
		var pending = new ManiaTrackerCredentials { PrivateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey()), ClientId = authorization.ClientId };
		var payload = new JsonObject
		{
			["grant_type"] = "authorization_code", ["code"] = authorization.Code,
			["code_verifier"] = authorization.Verifier, ["client_id"] = authorization.ClientId,
			["redirect_uri"] = authorization.RedirectUri
		};
		_nonce = null;
		var result = await SendSignedAsync(pending, HttpMethod.Post, Prefix + "/oauth/token", payload, null, null, false, token);
		SetTokens(pending, result);
		var identity = await SendSignedAsync(pending, HttpMethod.Get, Prefix + "/me", null, null, null, true, token);
		pending.UserId = identity["user_id"]!.GetValue<long>();
		pending.InstallationId = identity["installation_id"]!.GetValue<string>();
		pending.Username = identity["username"]!.GetValue<string>();
		_store.Save(pending);
		Credentials = pending;
	}

	public async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? payload, string? file, string? idempotencyKey, CancellationToken token)
	{
		var credentials = Credentials ?? throw new InvalidOperationException("Connect to Mania Tracker first.");
		if (credentials.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30)) await RefreshAsync(credentials, token);
		try { return await SendSignedAsync(credentials, method, path, payload, file, idempotencyKey, true, token); }
		catch (ManiaTrackerApiException ex) when (ex.Status == 401 && ex.Code == "token_expired")
		{
			await RefreshAsync(credentials, token);
			return await SendSignedAsync(credentials, method, path, payload, file, idempotencyKey, true, token);
		}
	}

	private async Task RefreshAsync(ManiaTrackerCredentials credentials, CancellationToken token)
	{
		var result = await SendSignedAsync(credentials, HttpMethod.Post, Prefix + "/oauth/token", new JsonObject { ["grant_type"] = "refresh_token", ["refresh_token"] = credentials.RefreshToken }, null, null, false, token);
		SetTokens(credentials, result);
		_store.Save(credentials);
	}

	private static void SetTokens(ManiaTrackerCredentials credentials, JsonObject value)
	{
		if (value["token_type"]?.GetValue<string>() is not string type || !type.Equals("DPoP", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Mania Tracker returned an unsupported token type.");
		credentials.AccessToken = value["access_token"]!.GetValue<string>();
		credentials.RefreshToken = value["refresh_token"]!.GetValue<string>();
		credentials.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(value["expires_in"]?.GetValue<int>() ?? 300);
	}

	public async Task RevokeAsync(CancellationToken token)
	{
		if (Credentials == null) return;
		await SendSignedAsync(Credentials, HttpMethod.Post, Prefix + "/oauth/revoke", new JsonObject { ["token"] = Credentials.RefreshToken }, null, null, false, token);
		_store.Delete();
		Credentials = null;
		_nonce = null;
	}

	private async Task<JsonObject> SendSignedAsync(ManiaTrackerCredentials credentials, HttpMethod method, string path, JsonObject? payload, string? file, string? idempotencyKey, bool authenticated, CancellationToken token)
	{
		if (!path.StartsWith(Prefix + "/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
			throw new InvalidDataException("Unexpected Mania Tracker API path.");
		var uri = new Uri(Origin + path);
		using var key = ECDsa.Create();
		key.ImportPkcs8PrivateKey(Convert.FromBase64String(credentials.PrivateKey), out _);
		for (var attempt = 0; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, uri);
			request.Headers.Add("DPoP", ManiaTrackerProof.Create(key, method, uri, authenticated ? credentials.AccessToken : null, _nonce));
			if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credentials.AccessToken);
			if (idempotencyKey != null) request.Headers.Add("Idempotency-Key", idempotencyKey);
			if (file != null)
			{
				request.Content = new StreamContent(File.OpenRead(file));
				request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			}
			else if (payload != null) request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync(request, token);
			var hasNonce = response.Headers.TryGetValues("DPoP-Nonce", out var nonces);
			if (hasNonce) _nonce = nonces!.FirstOrDefault();
			try { return await ReadResponseAsync(response, token); }
			catch (ManiaTrackerApiException ex) when (attempt == 0 && hasNonce && (ex.Status == 401 || (ex.Status == 400 && ex.Code == "use_dpop_nonce"))) { }
		}
	}

	private static async Task<JsonObject> ReadResponseAsync(HttpResponseMessage response, CancellationToken token)
	{
		var text = await response.Content.ReadAsStringAsync(token);
		JsonObject? value = null;
		try { value = JsonNode.Parse(text) as JsonObject; } catch (System.Text.Json.JsonException) { }
		if (!response.IsSuccessStatusCode)
		{
			var code = value?["error"] is JsonObject obj ? obj["code"]?.GetValue<string>() : value?["error"]?.GetValue<string>();
			var message = value?["error"] is JsonObject error ? error["message"]?.GetValue<string>() : value?["error_description"]?.GetValue<string>();
			message ??= value?["message"]?.GetValue<string>();
			var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
			throw new ManiaTrackerApiException((int)response.StatusCode, code ?? "http_error", message ?? $"Mania Tracker: {code ?? response.StatusCode.ToString()}", retry);
		}
		if (value == null && response.StatusCode != System.Net.HttpStatusCode.NoContent)
			throw new HttpRequestException("Mania Tracker returned an invalid response.");
		return value ?? new JsonObject();
	}

	public static void OpenPage(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
	public void Dispose() => _http.Dispose();
}
