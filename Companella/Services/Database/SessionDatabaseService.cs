using Companella.Models.Session;
using Companella.Services.Common;
using Microsoft.Data.Sqlite;

namespace Companella.Services.Database;

#pragma warning disable CA1305

/// <summary>
/// Service for persisting session data to a SQLite database.
/// Database is stored in %AppData%\Companella to preserve data across updates.
/// </summary>
public class SessionDatabaseService : IDisposable
{
	private readonly string _databasePath;
	private readonly string _connectionString;
	private bool _isDisposed;

	/// <summary>
	/// Creates a new SessionDatabaseService.
	/// </summary>
	public SessionDatabaseService() : this(DataPaths.SessionsDatabase)
	{
	}

	internal SessionDatabaseService(string databasePath)
	{
		// Use DataPaths for AppData-based storage
		_databasePath = databasePath;
		_connectionString = $"Data Source={_databasePath}";

		InitializeDatabase();
	}

	/// <summary>
	/// Initializes the database and creates tables if they don't exist.
	/// </summary>
	private void InitializeDatabase()
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var createSessionsTable = @"
                CREATE TABLE IF NOT EXISTS Sessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    TotalPlays INTEGER NOT NULL,
                    AverageAccuracy REAL NOT NULL,
                    BestAccuracy REAL NOT NULL,
                    WorstAccuracy REAL NOT NULL,
                    AverageMsd REAL NOT NULL,
                    TotalTimePlayed TEXT NOT NULL
                )";

			var createPlaysTable = @"
                CREATE TABLE IF NOT EXISTS SessionPlays (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    BeatmapPath TEXT NOT NULL,
                    BeatmapHash TEXT NOT NULL DEFAULT '',
                    Accuracy REAL NOT NULL,
                    Misses INTEGER NOT NULL DEFAULT 0,
                    PauseCount INTEGER NOT NULL DEFAULT 0,
                    Grade TEXT NOT NULL DEFAULT 'D',
                    Status TEXT NOT NULL DEFAULT 'Completed',
                    SessionTime TEXT NOT NULL,
                    RecordedAt TEXT NOT NULL,
                    HighestMsdValue REAL NOT NULL,
                    DominantSkillset TEXT NOT NULL,
                    ReplayHash TEXT,
                    ReplayPath TEXT,
                    FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
                )";

			var createDanRatingsTable = @"
                CREATE TABLE IF NOT EXISTS DanRatings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BeatmapHash TEXT NOT NULL UNIQUE,
                    DanLabel TEXT NOT NULL,
                    RatedAt TEXT NOT NULL
                )";

			using (var cmd = new SqliteCommand(createSessionsTable, connection))
			{
				cmd.ExecuteNonQuery();
			}

			using (var cmd = new SqliteCommand(createPlaysTable, connection))
			{
				cmd.ExecuteNonQuery();
			}

			using (var cmd = new SqliteCommand(createDanRatingsTable, connection))
			{
				cmd.ExecuteNonQuery();
			}

			// Create index for faster lookups
			var createIndex = @"
                CREATE INDEX IF NOT EXISTS idx_sessionplays_sessionid 
                ON SessionPlays(SessionId)";

			using (var cmd = new SqliteCommand(createIndex, connection))
			{
				cmd.ExecuteNonQuery();
			}

			// Migrate existing database to add new columns if they don't exist
			// This must run BEFORE creating indexes on new columns
			MigrateDatabase(connection);

			// Create index for beatmap hash lookups (for replay matching)
			// This runs AFTER migration so the column exists
			try
			{
				var createBeatmapHashIndex = @"
                    CREATE INDEX IF NOT EXISTS idx_sessionplays_beatmaphash 
                    ON SessionPlays(BeatmapHash)";

				using var cmd = new SqliteCommand(createBeatmapHashIndex, connection);
				cmd.ExecuteNonQuery();
			}
			catch (SqliteException ex)
			{
				Logger.Info($"[SessionDB] Could not create BeatmapHash index: {ex.Message}");
			}

			Logger.Info($"[SessionDB] Database initialized at: {_databasePath}");
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error initializing database: {ex.Message}");
			throw;
		}
	}

	/// <summary>
	/// Migrates the database to add new columns if they don't exist.
	/// </summary>
	private static void MigrateDatabase(SqliteConnection connection)
	{
		Logger.Info("[SessionDB] Starting database migration check...");

		// Check which columns already exist
		var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using var pragmaCmd = new SqliteCommand("PRAGMA table_info(SessionPlays)", connection);
			using var reader = pragmaCmd.ExecuteReader();
			while (reader.Read())
			{
				var columnName = reader.GetString(1); // Column name is at index 1
				existingColumns.Add(columnName);
				Logger.Info($"[SessionDB] Found existing column: {columnName}");
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error reading table schema: {ex.Message}");
			return;
		}

		Logger.Info($"[SessionDB] Found {existingColumns.Count} existing columns");

		// Define new columns to add (using NULL-able columns for SQLite compatibility)
		var newColumns = new[]
		{
			("BeatmapHash", "TEXT DEFAULT ''"),
			("Misses", "INTEGER DEFAULT 0"),
			("PauseCount", "INTEGER DEFAULT 0"),
			("Grade", "TEXT DEFAULT 'D'"),
			("Status", "TEXT DEFAULT 'Completed'"),
			("ReplayHash", "TEXT"),
			("ReplayPath", "TEXT"),
			("Rate", "REAL DEFAULT 1.0")
		};

		var addedCount = 0;
		foreach (var (columnName, columnType) in newColumns)
		{
			if (existingColumns.Contains(columnName))
			{
				Logger.Info($"[SessionDB] Column {columnName} already exists, skipping");
				continue;
			}

			try
			{
				Logger.Info($"[SessionDB] Adding column {columnName}...");
				var alterTable = $"ALTER TABLE SessionPlays ADD COLUMN {columnName} {columnType}";
				using var cmd = new SqliteCommand(alterTable, connection);
				cmd.ExecuteNonQuery();
				addedCount++;
				Logger.Info($"[SessionDB] Successfully added column {columnName}");
			}
			catch (SqliteException ex)
			{
				Logger.Info($"[SessionDB] Error adding column {columnName}: {ex.Message}");
			}
		}

		Logger.Info($"[SessionDB] Migration complete. Added {addedCount} new columns.");
	}

	// Cache for column existence checks
	private bool? _hasNewColumns;

	/// <summary>
	/// Checks if new columns exist in the SessionPlays table.
	/// Result is cached after first check.
	/// </summary>
	private bool HasNewColumns(SqliteConnection connection)
	{
		if (_hasNewColumns.HasValue)
			return _hasNewColumns.Value;

		try
		{
			using var cmd = new SqliteCommand("PRAGMA table_info(SessionPlays)", connection);
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				var columnName = reader.GetString(1);
				if (columnName.Equals("BeatmapHash", StringComparison.OrdinalIgnoreCase))
				{
					_hasNewColumns = true;
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error checking columns: {ex.Message}");
		}

		_hasNewColumns = false;
		return false;
	}

	/// <summary>
	/// Saves a session and its plays to the database.
	/// </summary>
	/// <param name="startTime">Session start time (UTC)</param>
	/// <param name="endTime">Session end time (UTC)</param>
	/// <param name="plays">List of plays in the session</param>
	/// <returns>The ID of the saved session</returns>
	public long SaveSession(DateTime startTime, DateTime endTime, List<SessionPlayResult> plays)
	{
		if (plays.Count == 0)
		{
			Logger.Info("[SessionDB] No plays to save, skipping session");
			return -1;
		}

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			using var transaction = connection.BeginTransaction();

			try
			{
				// Calculate session statistics
				var totalPlays = plays.Count;
				var avgAccuracy = plays.Average(p => p.Accuracy);
				var bestAccuracy = plays.Max(p => p.Accuracy);
				var worstAccuracy = plays.Min(p => p.Accuracy);
				var avgMsd = plays.Average(p => p.HighestMsdValue);
				var totalTime = plays.Max(p => p.SessionTime);

				// Insert session
				var insertSession = @"
                    INSERT INTO Sessions (StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed)
                    VALUES (@StartTime, @EndTime, @TotalPlays, @AverageAccuracy, @BestAccuracy, @WorstAccuracy, @AverageMsd, @TotalTimePlayed);
                    SELECT last_insert_rowid();";

				long sessionId;
				using (var cmd = new SqliteCommand(insertSession, connection, transaction))
				{
					cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("o"));
					cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("o"));
					cmd.Parameters.AddWithValue("@TotalPlays", totalPlays);
					cmd.Parameters.AddWithValue("@AverageAccuracy", avgAccuracy);
					cmd.Parameters.AddWithValue("@BestAccuracy", bestAccuracy);
					cmd.Parameters.AddWithValue("@WorstAccuracy", worstAccuracy);
					cmd.Parameters.AddWithValue("@AverageMsd", avgMsd);
					cmd.Parameters.AddWithValue("@TotalTimePlayed", totalTime.ToString());

					sessionId = (long)cmd.ExecuteScalar()!;
				}

				// Insert plays
				var insertPlay = @"
                    INSERT INTO SessionPlays (SessionId, BeatmapPath, BeatmapHash, Accuracy, Misses, PauseCount, Grade, Status, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset, ReplayHash, ReplayPath, Rate)
                    VALUES (@SessionId, @BeatmapPath, @BeatmapHash, @Accuracy, @Misses, @PauseCount, @Grade, @Status, @SessionTime, @RecordedAt, @HighestMsdValue, @DominantSkillset, @ReplayHash, @ReplayPath, @Rate)";

				foreach (var play in plays)
				{
					using var cmd = new SqliteCommand(insertPlay, connection, transaction);
					cmd.Parameters.AddWithValue("@SessionId", sessionId);
					cmd.Parameters.AddWithValue("@BeatmapPath", play.BeatmapPath);
					cmd.Parameters.AddWithValue("@BeatmapHash", play.BeatmapHash ?? "");
					cmd.Parameters.AddWithValue("@Accuracy", play.Accuracy);
					cmd.Parameters.AddWithValue("@Misses", play.Misses);
					cmd.Parameters.AddWithValue("@PauseCount", play.PauseCount);
					cmd.Parameters.AddWithValue("@Grade", play.Grade ?? "D");
					cmd.Parameters.AddWithValue("@Status", play.Status.ToString());
					cmd.Parameters.AddWithValue("@SessionTime", play.SessionTime.ToString());
					cmd.Parameters.AddWithValue("@RecordedAt", play.RecordedAt.ToString("o"));
					cmd.Parameters.AddWithValue("@HighestMsdValue", play.HighestMsdValue);
					cmd.Parameters.AddWithValue("@DominantSkillset", play.DominantSkillset);
					cmd.Parameters.AddWithValue("@ReplayHash", (object?)play.ReplayHash ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@ReplayPath", (object?)play.ReplayPath ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@Rate", play.Rate);
					cmd.ExecuteNonQuery();
				}

				transaction.Commit();
				Logger.Info($"[SessionDB] Saved session {sessionId} with {totalPlays} plays");
				return sessionId;
			}
			catch
			{
				transaction.Rollback();
				throw;
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error saving session: {ex.Message}");
			throw;
		}
	}

	/// <summary>
	/// Gets all sessions (without plays) ordered by start time descending.
	/// </summary>
	public List<StoredSession> GetSessions()
	{
		var sessions = new List<StoredSession>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                SELECT Id, StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed
                FROM Sessions
                ORDER BY StartTime DESC";

			using var cmd = new SqliteCommand(query, connection);
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) sessions.Add(ReadSession(reader));
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting sessions: {ex.Message}");
		}

		return sessions;
	}

	/// <summary>
	/// Gets a session by ID with all its plays.
	/// </summary>
	public StoredSession? GetSessionById(long sessionId)
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			// Get session
			var sessionQuery = @"
                SELECT Id, StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed
                FROM Sessions
                WHERE Id = @Id";

			StoredSession? session = null;
			using (var cmd = new SqliteCommand(sessionQuery, connection))
			{
				cmd.Parameters.AddWithValue("@Id", sessionId);
				using var reader = cmd.ExecuteReader();
				if (reader.Read()) session = ReadSession(reader);
			}

			if (session == null)
				return null;

			// Get plays
			session.Plays = GetSessionPlays(sessionId, connection);

			return session;
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting session {sessionId}: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Gets the plays for a session.
	/// </summary>
	public List<StoredSessionPlay> GetSessionPlays(long sessionId)
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();
			return GetSessionPlays(sessionId, connection);
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting plays for session {sessionId}: {ex.Message}");
			return new List<StoredSessionPlay>();
		}
	}

	private List<StoredSessionPlay> GetSessionPlays(long sessionId, SqliteConnection connection)
	{
		var plays = new List<StoredSessionPlay>();

		// Check if new columns exist (for backward compatibility with old databases)
		var hasNewColumns = HasNewColumns(connection);

		string query;
		if (hasNewColumns)
			query = @"
                SELECT Id, SessionId, BeatmapPath, BeatmapHash, Accuracy, Misses, PauseCount, Grade, Status, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset, ReplayHash, ReplayPath, Rate
                FROM SessionPlays
                WHERE SessionId = @SessionId
                ORDER BY SessionTime ASC";
		else
			// Old schema without new columns
			query = @"
                SELECT Id, SessionId, BeatmapPath, Accuracy, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset
                FROM SessionPlays
                WHERE SessionId = @SessionId
                ORDER BY SessionTime ASC";

		using var cmd = new SqliteCommand(query, connection);
		cmd.Parameters.AddWithValue("@SessionId", sessionId);
		using var reader = cmd.ExecuteReader();

		while (reader.Read()) plays.Add(ReadPlay(reader, hasNewColumns));

		return plays;
	}

	/// <summary>
	/// Deletes a session and all its plays.
	/// </summary>
	public bool DeleteSession(long sessionId)
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			// Delete plays first (cascade should handle this, but be explicit)
			using (var cmd = new SqliteCommand("DELETE FROM SessionPlays WHERE SessionId = @Id", connection))
			{
				cmd.Parameters.AddWithValue("@Id", sessionId);
				cmd.ExecuteNonQuery();
			}

			// Delete session
			using (var cmd = new SqliteCommand("DELETE FROM Sessions WHERE Id = @Id", connection))
			{
				cmd.Parameters.AddWithValue("@Id", sessionId);
				var affected = cmd.ExecuteNonQuery();
				Logger.Info($"[SessionDB] Deleted session {sessionId}");
				return affected > 0;
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error deleting session {sessionId}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Gets the total number of sessions.
	/// </summary>
	public int GetSessionCount()
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Sessions", connection);
			return Convert.ToInt32(cmd.ExecuteScalar());
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting session count: {ex.Message}");
			return 0;
		}
	}

	/// <summary>
	/// Gets sessions within a date range.
	/// </summary>
	public List<StoredSession> GetSessionsInRange(DateTime startDate, DateTime endDate)
	{
		var sessions = new List<StoredSession>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                SELECT Id, StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed
                FROM Sessions
                WHERE StartTime >= @StartDate AND StartTime <= @EndDate
                ORDER BY StartTime DESC";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("o"));
			cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("o"));
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) sessions.Add(ReadSession(reader));
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting sessions in range: {ex.Message}");
		}

		return sessions;
	}

	/// <summary>
	/// Gets all plays within a date range across all sessions.
	/// Used for trend analysis.
	/// </summary>
	public List<StoredSessionPlay> GetPlaysInTimeRange(DateTime startDate, DateTime endDate)
	{
		var plays = new List<StoredSessionPlay>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			// Check if new columns exist (for backward compatibility with old databases)
			var hasNewColumns = HasNewColumns(connection);

			string query;
			if (hasNewColumns)
				query = @"
                    SELECT sp.Id, sp.SessionId, sp.BeatmapPath, sp.BeatmapHash, sp.Accuracy, sp.Misses, sp.PauseCount, sp.Grade, sp.Status, sp.SessionTime, sp.RecordedAt, sp.HighestMsdValue, sp.DominantSkillset, sp.ReplayHash, sp.ReplayPath, sp.Rate
                    FROM SessionPlays sp
                    INNER JOIN Sessions s ON sp.SessionId = s.Id
                    WHERE sp.RecordedAt >= @StartDate AND sp.RecordedAt <= @EndDate
                    ORDER BY sp.RecordedAt ASC";
			else
				query = @"
                    SELECT sp.Id, sp.SessionId, sp.BeatmapPath, sp.Accuracy, sp.SessionTime, sp.RecordedAt, sp.HighestMsdValue, sp.DominantSkillset
                    FROM SessionPlays sp
                    INNER JOIN Sessions s ON sp.SessionId = s.Id
                    WHERE sp.RecordedAt >= @StartDate AND sp.RecordedAt <= @EndDate
                    ORDER BY sp.RecordedAt ASC";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("o"));
			cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("o"));
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) plays.Add(ReadPlay(reader, hasNewColumns));

			Logger.Info($"[SessionDB] Found {plays.Count} plays in time range");
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting plays in time range: {ex.Message}");
		}

		return plays;
	}

	/// <summary>
	/// Gets total play count across all sessions.
	/// </summary>
	public int GetTotalPlayCount()
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			using var cmd = new SqliteCommand("SELECT COUNT(*) FROM SessionPlays", connection);
			return Convert.ToInt32(cmd.ExecuteScalar());
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting total play count: {ex.Message}");
			return 0;
		}
	}

	private static StoredSession ReadSession(SqliteDataReader reader)
	{
		return new StoredSession
		{
			Id = reader.GetInt64(0),
			StartTime = DateTime.Parse(reader.GetString(1)),
			EndTime = DateTime.Parse(reader.GetString(2)),
			TotalPlays = reader.GetInt32(3),
			AverageAccuracy = reader.GetDouble(4),
			BestAccuracy = reader.GetDouble(5),
			WorstAccuracy = reader.GetDouble(6),
			AverageMsd = reader.GetDouble(7),
			TotalTimePlayed = TimeSpan.Parse(reader.GetString(8))
		};
	}

	private static StoredSessionPlay ReadPlay(SqliteDataReader reader, bool hasNewColumns = true)
	{
		if (hasNewColumns)
		{
			// New schema: Id, SessionId, BeatmapPath, BeatmapHash, Accuracy, Misses, PauseCount, Grade, Status, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset, ReplayHash, ReplayPath, Rate
			var play = new StoredSessionPlay
			{
				Id = reader.GetInt64(0),
				SessionId = reader.GetInt64(1),
				BeatmapPath = reader.GetString(2),
				BeatmapHash = reader.IsDBNull(3) ? "" : reader.GetString(3),
				Accuracy = reader.GetDouble(4),
				Misses = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
				PauseCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
				Grade = reader.IsDBNull(7) ? "D" : reader.GetString(7),
				SessionTime = TimeSpan.Parse(reader.GetString(9)),
				RecordedAt = DateTime.Parse(reader.GetString(10)),
				HighestMsdValue = (float)reader.GetDouble(11),
				DominantSkillset = reader.GetString(12),
				ReplayHash = reader.IsDBNull(13) ? null : reader.GetString(13),
				ReplayPath = reader.IsDBNull(14) ? null : reader.GetString(14),
				Rate = reader.IsDBNull(15) ? 1.0f : (float)reader.GetDouble(15)
			};

			// Parse status
			var statusStr = reader.IsDBNull(8) ? "Completed" : reader.GetString(8);
			play.Status = Enum.TryParse<PlayStatus>(statusStr, out var status) ? status : PlayStatus.Completed;

			return play;
		}
		else
		{
			// Old schema: Id, SessionId, BeatmapPath, Accuracy, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset
			return new StoredSessionPlay
			{
				Id = reader.GetInt64(0),
				SessionId = reader.GetInt64(1),
				BeatmapPath = reader.GetString(2),
				BeatmapHash = "",
				Accuracy = reader.GetDouble(3),
				Misses = 0,
				PauseCount = 0,
				Grade = "D",
				Status = PlayStatus.Completed,
				SessionTime = TimeSpan.Parse(reader.GetString(4)),
				RecordedAt = DateTime.Parse(reader.GetString(5)),
				HighestMsdValue = (float)reader.GetDouble(6),
				DominantSkillset = reader.GetString(7),
				ReplayHash = null,
				ReplayPath = null,
				Rate = 1.0f
			};
		}
	}

	/// <summary>
	/// Gets all dates that have sessions.
	/// </summary>
	public List<DateTime> GetDatesWithSessions()
	{
		var dates = new List<DateTime>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                SELECT DISTINCT date(StartTime) as SessionDate
                FROM Sessions
                ORDER BY SessionDate DESC";

			using var cmd = new SqliteCommand(query, connection);
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
				if (DateTime.TryParse(reader.GetString(0), out var date))
					dates.Add(date);
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting dates with sessions: {ex.Message}");
		}

		return dates;
	}

	/// <summary>
	/// Gets session counts per day for heatmap display.
	/// </summary>
	public Dictionary<DateTime, int> GetSessionCountsPerDay()
	{
		var counts = new Dictionary<DateTime, int>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                SELECT date(StartTime) as SessionDate, COUNT(*) as SessionCount
                FROM Sessions
                GROUP BY date(StartTime)
                ORDER BY SessionDate DESC";

			using var cmd = new SqliteCommand(query, connection);
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
				if (DateTime.TryParse(reader.GetString(0), out var date))
					counts[date] = reader.GetInt32(1);
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting session counts: {ex.Message}");
		}

		return counts;
	}

	/// <summary>
	/// Gets sessions for a specific date.
	/// </summary>
	public List<StoredSession> GetSessionsForDate(DateTime date)
	{
		var startOfDay = date.Date;
		var endOfDay = startOfDay.AddDays(1).AddTicks(-1);
		return GetSessionsInRange(startOfDay, endOfDay);
	}

	/// <summary>
	/// Updates the replay information for a play.
	/// </summary>
	public bool UpdateReplayInfo(long playId, string replayHash, string replayPath, double? accuracy = null, int? misses = null)
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                UPDATE SessionPlays 
                SET ReplayHash = @ReplayHash, ReplayPath = @ReplayPath,
                    Accuracy = COALESCE(@Accuracy, Accuracy), Misses = COALESCE(@Misses, Misses),
                    Grade = COALESCE(@Grade, Grade)
                WHERE Id = @Id";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@Id", playId);
			cmd.Parameters.AddWithValue("@ReplayHash", replayHash);
			cmd.Parameters.AddWithValue("@ReplayPath", replayPath);
			cmd.Parameters.AddWithValue("@Accuracy", (object?)accuracy ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@Misses", (object?)misses ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@Grade", accuracy.HasValue && misses.HasValue ? SessionPlayResult.CalculateGrade(accuracy.Value, misses.Value, PlayStatus.Completed) : DBNull.Value);

			var affected = cmd.ExecuteNonQuery();
			if (affected > 0 && accuracy.HasValue)
			{
				using var totals = connection.CreateCommand();
				totals.CommandText = @"UPDATE Sessions SET
                    AverageAccuracy = (SELECT AVG(Accuracy) FROM SessionPlays WHERE SessionId = Sessions.Id),
                    BestAccuracy = (SELECT MAX(Accuracy) FROM SessionPlays WHERE SessionId = Sessions.Id),
                    WorstAccuracy = (SELECT MIN(Accuracy) FROM SessionPlays WHERE SessionId = Sessions.Id)
                    WHERE Id = (SELECT SessionId FROM SessionPlays WHERE Id = @PlayId)";
				totals.Parameters.AddWithValue("@PlayId", playId);
				totals.ExecuteNonQuery();
			}
			if (affected > 0) Logger.Info($"[SessionDB] Updated replay info for play {playId}");

			return affected > 0;
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error updating replay info: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Updates the beatmap hash for a play.
	/// </summary>
	public bool UpdateBeatmapHash(long playId, string beatmapHash)
	{
		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                UPDATE SessionPlays 
                SET BeatmapHash = @BeatmapHash
                WHERE Id = @Id";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@Id", playId);
			cmd.Parameters.AddWithValue("@BeatmapHash", beatmapHash);

			var affected = cmd.ExecuteNonQuery();
			if (affected > 0) Logger.Info($"[SessionDB] Updated beatmap hash for play {playId}");

			return affected > 0;
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error updating beatmap hash: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Finds plays that match the given beatmap hash and don't have a replay yet.
	/// Used for matching newly found replays to plays.
	/// </summary>
	public List<StoredSessionPlay> GetPlaysWithoutReplayByBeatmapHash(string beatmapHash)
	{
		var plays = new List<StoredSessionPlay>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			// Check if new columns exist - this feature requires the new schema
			if (!HasNewColumns(connection)) return plays;

			var query = @"
                SELECT Id, SessionId, BeatmapPath, BeatmapHash, Accuracy, Misses, PauseCount, Grade, Status, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset, ReplayHash, ReplayPath, Rate
                FROM SessionPlays
                WHERE BeatmapHash = @BeatmapHash COLLATE NOCASE AND (ReplayPath IS NULL OR ReplayPath = '')
                ORDER BY RecordedAt DESC";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@BeatmapHash", beatmapHash);
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) plays.Add(ReadPlay(reader, true));
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting plays without replay: {ex.Message}");
		}

		return plays;
	}

	/// <summary>
	/// Gets recent plays without replay files (for replay file watcher to check).
	/// </summary>
	public List<StoredSessionPlay> GetRecentPlaysWithoutReplay(int maxAgeMinutes = 60)
	{
		var plays = new List<StoredSessionPlay>();

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			// Check if new columns exist - this feature requires the new schema
			if (!HasNewColumns(connection)) return plays;

			var cutoffTime = DateTime.UtcNow.AddMinutes(-maxAgeMinutes);

			var query = @"
                SELECT Id, SessionId, BeatmapPath, BeatmapHash, Accuracy, Misses, PauseCount, Grade, Status, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset, ReplayHash, ReplayPath, Rate
                FROM SessionPlays
                WHERE (ReplayPath IS NULL OR ReplayPath = '') AND RecordedAt >= @CutoffTime
                ORDER BY RecordedAt DESC";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@CutoffTime", cutoffTime.ToString("o"));
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) plays.Add(ReadPlay(reader, true));
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting recent plays without replay: {ex.Message}");
		}

		return plays;
	}

	/// <summary>
	/// Checks if a beatmap has already been rated with a dan label.
	/// </summary>
	public bool HasDanRating(string beatmapHash)
	{
		if (string.IsNullOrEmpty(beatmapHash))
			return false;

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = "SELECT COUNT(*) FROM DanRatings WHERE BeatmapHash = @Hash";
			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@Hash", beatmapHash);

			var count = Convert.ToInt32(cmd.ExecuteScalar());
			return count > 0;
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error checking dan rating: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Gets the dan rating for a beatmap if it exists.
	/// </summary>
	public string? GetDanRating(string beatmapHash)
	{
		if (string.IsNullOrEmpty(beatmapHash))
			return null;

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = "SELECT DanLabel FROM DanRatings WHERE BeatmapHash = @Hash";
			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@Hash", beatmapHash);

			var result = cmd.ExecuteScalar();
			return result as string;
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error getting dan rating: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Saves a dan rating for a beatmap.
	/// </summary>
	public bool SaveDanRating(string beatmapHash, string danLabel)
	{
		if (string.IsNullOrEmpty(beatmapHash) || string.IsNullOrEmpty(danLabel))
			return false;

		try
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();

			var query = @"
                INSERT OR REPLACE INTO DanRatings (BeatmapHash, DanLabel, RatedAt)
                VALUES (@Hash, @DanLabel, @RatedAt)";

			using var cmd = new SqliteCommand(query, connection);
			cmd.Parameters.AddWithValue("@Hash", beatmapHash);
			cmd.Parameters.AddWithValue("@DanLabel", danLabel);
			cmd.Parameters.AddWithValue("@RatedAt", DateTime.UtcNow.ToString("o"));

			cmd.ExecuteNonQuery();
			Logger.Info($"[SessionDB] Saved dan rating: {danLabel} for {beatmapHash}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Info($"[SessionDB] Error saving dan rating: {ex.Message}");
			return false;
		}
	}

#pragma warning restore CA1305

	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		GC.SuppressFinalize(this);
	}
}
