using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Creates request-bound ES256 proofs for Mania Tracker.</summary>
public static class ManiaTrackerProof
{
	public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	public static string Thumbprint(ECDsa key)
	{
		var p = key.ExportParameters(false);
		var canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64Url(p.Q.X!)}\",\"y\":\"{Base64Url(p.Q.Y!)}\"}}";
		return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
	}

	public static string Create(ECDsa key, HttpMethod method, Uri uri, string? accessToken, string? nonce)
	{
		var p = key.ExportParameters(false);
		var header = new { typ = "dpop+jwt", alg = "ES256", jwk = new { kty = "EC", crv = "P-256", x = Base64Url(p.Q.X!), y = Base64Url(p.Q.Y!) } };
		var payload = new JsonObject
		{
			["jti"] = Guid.NewGuid().ToString("N"),
			["htm"] = method.Method,
			["htu"] = uri.GetLeftPart(UriPartial.Path),
			["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		};
		if (accessToken != null) payload["ath"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
		if (nonce != null) payload["nonce"] = nonce;
		var message = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(payload.ToJsonString()));
		return message + "." + Base64Url(key.SignData(Encoding.ASCII.GetBytes(message), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
	}
}

/// <summary>Credentials encrypted for the current Windows user.</summary>
public sealed class ManiaTrackerCredentials
{
	public string PrivateKey { get; set; } = "";
	public string AccessToken { get; set; } = "";
	public string RefreshToken { get; set; } = "";
	public string InstallationId { get; set; } = "";
	public long UserId { get; set; }
	public string Username { get; set; } = "";
	public string ClientId { get; set; } = "companella";
	public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Atomic, Windows-user-protected credential persistence.</summary>
public sealed class ManiaTrackerCredentialStore
{
	private readonly string _path;
	public ManiaTrackerCredentialStore(string directory) => _path = Path.Combine(directory, "connection.dat");

	public ManiaTrackerCredentials? Load()
	{
		if (!File.Exists(_path)) return null;
		var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
		try { return JsonSerializer.Deserialize<ManiaTrackerCredentials>(bytes); }
		finally { CryptographicOperations.ZeroMemory(bytes); }
	}

	public void Save(ManiaTrackerCredentials value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
		try
		{
			File.WriteAllBytes(_path + ".tmp", ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
			File.Move(_path + ".tmp", _path, true);
		}
		finally { CryptographicOperations.ZeroMemory(bytes); }
	}

	public void Delete() => File.Delete(_path);
}
