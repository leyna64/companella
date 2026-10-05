using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Durable receipt, capture evidence and immutable asset identities for one play.</summary>
public sealed class ManiaTrackerPlay
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public long UserId { get; set; }
	public string InstallationId { get; set; } = "";
	public string ClientId { get; set; } = "";
	public string BeatmapPath { get; set; } = "";
	public string ReplayDirectory { get; set; } = "";
	public DateTimeOffset StartedAt { get; set; }
	public DateTimeOffset FinishedAt { get; set; }
	public DateTimeOffset? SyncedAt { get; set; }
	public string PlayerName { get; set; } = "";
	public int Mods { get; set; }
	public int Score { get; set; }
	public int[] Judgements { get; set; } = Array.Empty<int>();
	public string ChartMd5 { get; set; } = "";
	public string ChartSha256 { get; set; } = "";
	public long ChartLength { get; set; }
	public string? ReplaySha256 { get; set; }
	public long ReplayLength { get; set; }
	public string? SubmissionId { get; set; }
	public string State { get; set; } = "waiting_replay";
	public string? Message { get; set; }
	public string? RatingJson { get; set; }
	public int Attempts { get; set; }
	public DateTimeOffset NextAttempt { get; set; }
	public bool IsTerminal => State is "accepted" or "rejected" or "expired" or "deleted" or "skipped";
}

/// <summary>SQLite receipts and bounded local staging, independent of session lifetime.</summary>
public sealed class ManiaTrackerQueue
{
	private readonly string _directory;
	private readonly string _connectionString;
	private readonly object _gate = new();
	public const long MaxBytes = 512L * 1024 * 1024;

	public ManiaTrackerQueue(string directory)
	{
		_directory = directory;
		Directory.CreateDirectory(directory);
		_connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "queue.db") }.ToString();
		using var connection = Open();
		using var command = connection.CreateCommand();
		command.CommandText = "CREATE TABLE IF NOT EXISTS plays (id TEXT PRIMARY KEY, replay_hash TEXT UNIQUE, data TEXT NOT NULL);";
		command.ExecuteNonQuery();
	}

	private SqliteConnection Open()
	{
		var connection = new SqliteConnection(_connectionString);
		connection.Open();
		return connection;
	}

	public string AssetPath(ManiaTrackerPlay play, bool replay)
	{
		if (!Guid.TryParseExact(play.Id, "N", out _)) throw new InvalidDataException("Invalid local queue identity.");
		return Path.Combine(_directory, play.Id + (replay ? ".osr" : ".osu"));
	}

	public List<ManiaTrackerPlay> ReadAll()
	{
		lock (_gate)
		{
			using var connection = Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT data FROM plays ORDER BY rowid DESC";
			using var reader = command.ExecuteReader();
			var plays = new List<ManiaTrackerPlay>();
			while (reader.Read()) plays.Add(JsonSerializer.Deserialize<ManiaTrackerPlay>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid queue receipt."));
			return plays;
		}
	}

	public void Save(ManiaTrackerPlay play)
	{
		lock (_gate)
		{
			using var connection = Open();
			using var command = connection.CreateCommand();
			command.CommandText = "INSERT INTO plays(id,replay_hash,data) VALUES($id,$hash,$data) ON CONFLICT(id) DO UPDATE SET replay_hash=$hash,data=$data";
			command.Parameters.AddWithValue("$id", play.Id);
			command.Parameters.AddWithValue("$hash", (object?)play.ReplaySha256 ?? DBNull.Value);
			command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(play));
			command.ExecuteNonQuery();
		}
	}

	public void Capture(ManiaTrackerPlay play, byte[] chart)
	{
		lock (_gate)
		{
			if (ReadAll().Count(x => !x.IsTerminal) >= 500) throw new IOException("Mania Tracker queue is full (500 pending plays). Resume uploads or clear pending plays.");
			WriteAsset(play, false, chart);
			play.ChartMd5 = Hex(MD5.HashData(chart));
			play.ChartSha256 = Hex(SHA256.HashData(chart));
			play.ChartLength = chart.Length;
			Save(play);
		}
	}

	public bool AttachReplay(ManiaTrackerPlay play, byte[] replay)
	{
		lock (_gate)
		{
			var hash = Hex(SHA256.HashData(replay));
			if (ReadAll().Any(x => x.Id != play.Id && x.ReplaySha256 == hash)) return false;
			WriteAsset(play, true, replay);
			play.ReplaySha256 = hash;
			play.ReplayLength = replay.Length;
			play.State = "pending";
			play.Message = null;
			Save(play);
			return true;
		}
	}

	private void WriteAsset(ManiaTrackerPlay play, bool replay, byte[] bytes)
	{
		var size = new DirectoryInfo(_directory).EnumerateFiles().Sum(x => x.Length);
		if (size + bytes.Length > MaxBytes) throw new IOException("Mania Tracker queue storage is full (512 MiB). Resume uploads or clear pending plays.");
		var path = AssetPath(play, replay);
		using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
		{
			stream.Write(bytes);
			stream.Flush(true);
		}
		File.Move(path + ".tmp", path, true);
	}

	public void ReleaseAssets(ManiaTrackerPlay play)
	{
		lock (_gate)
		{
			File.Delete(AssetPath(play, false));
			File.Delete(AssetPath(play, true));
		}
	}

	public void VerifyAssets(ManiaTrackerPlay play)
	{
		foreach (var replay in new[] { false, true })
		{
			using var stream = File.OpenRead(AssetPath(play, replay));
			if (stream.Length != (replay ? play.ReplayLength : play.ChartLength) || Hex(SHA256.HashData(stream)) != (replay ? play.ReplaySha256 : play.ChartSha256))
				throw new InvalidDataException("Staged play files changed. This play will not be uploaded.");
		}
	}

	public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
