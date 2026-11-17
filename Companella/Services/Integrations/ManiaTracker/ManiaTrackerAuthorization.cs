using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Browser authorization with a bounded, single-attempt loopback callback.</summary>
internal static class ManiaTrackerAuthorization
{
	internal sealed record AuthorizationCode(string Code, string Verifier, string ClientId, string RedirectUri);

	internal static async Task<AuthorizationCode> AuthorizeAsync(ECDsa key, bool test, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromMinutes(10));
		var state = ManiaTrackerProof.Base64Url(RandomNumberGenerator.GetBytes(32));
		var verifier = ManiaTrackerProof.Base64Url(RandomNumberGenerator.GetBytes(32));
		var clientId = test ? "companella-test" : "companella";
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/companella/callback";
		var args = new Dictionary<string, string>
		{
			["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = redirect,
			["state"] = state,
			["code_challenge"] = ManiaTrackerProof.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
			["code_challenge_method"] = "S256", ["dpop_jkt"] = ManiaTrackerProof.Thumbprint(key),
			["scope"] = "companella:scores:submit companella:submissions:read companella:charts:upload companella:installation:read",
			["app_name"] = "Companella"
		};
		ManiaTrackerApiClient.OpenPage(ManiaTrackerApiClient.Origin + "/companella/authorize?" + string.Join("&", args.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
		var code = await ReceiveCodeAsync(listener, state, timeout.Token);
		return new AuthorizationCode(code, verifier, clientId, redirect);
	}

	internal static async Task<string> ReceiveCodeAsync(TcpListener listener, string state, CancellationToken token)
	{
		while (true)
		{
			using var socket = await listener.AcceptTcpClientAsync(token);
			using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
			requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
			using var stream = socket.GetStream();
			var bytes = new List<byte>();
			var next = new byte[1];
			try
			{
				while (bytes.Count < 8192 && await stream.ReadAsync(next, requestTimeout.Token) == 1)
				{
					bytes.Add(next[0]);
					if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) break;
				}
			}
			catch (OperationCanceledException) when (!token.IsCancellationRequested) { continue; }
			catch (IOException) { continue; }
			var first = Encoding.ASCII.GetString(bytes.ToArray()).Split('\n')[0].Split(' ');
			var query = new Dictionary<string, string>();
			var valid = first.Length == 3 && first[0] == "GET" && first[1].StartsWith("/companella/callback?", StringComparison.Ordinal);
			if (valid)
			{
				foreach (var part in first[1].Split('?', 2)[1].Split('&'))
				{
					var pair = part.Split('=', 2);
					if (pair.Length == 2 && !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1].Replace('+', ' ')))) valid = false;
				}
				valid &= query.GetValueOrDefault("state") == state;
			}
			var body = valid ? "You can return to Companella." : "Invalid callback. Return to the original sign-in window.";
			var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
			try { await stream.WriteAsync(response, requestTimeout.Token); } catch (IOException) { }
			if (!valid) continue;
			if (query.ContainsKey("error")) throw new OperationCanceledException("Connection was cancelled in the browser.");
			if (query.TryGetValue("code", out var code) && code.Length > 0) return code;
		}
	}
}
