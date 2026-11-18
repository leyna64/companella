using System.Reflection;
using Companella.Models.Application;
using System.Globalization;
using Squirrel;

// Alias to resolve conflict between Companella.Models.UpdateInfo and Squirrel.UpdateInfo
using AppUpdateInfo = Companella.Models.Application.UpdateInfo;

namespace Companella.Services.Common;

/// <summary>
/// Service for checking and applying automatic updates using Squirrel.Windows.
/// Updates are fetched from the update server.
/// </summary>
public class SquirrelUpdaterService : IDisposable
{
	internal const string UpdateUrl = "https://updates.erdbee.re/companella";
	private const string _gitHubRepoUrl = "https://github.com/Leinadix/companella"; // For release notes only
	private const string _versionFileName = "version.txt";
	private const string _failedUpdatesFileName = "failed_updates.txt";
	private const int _maxDeltaFailuresBeforeFull = 3;

	private readonly string _versionFilePath;
	private readonly string _failedUpdatesFilePath;
	private bool _isDisposed;
	private bool _updatePending;
	private ReleaseEntry? _pendingUpdate;
	private int _deltaFailureCount;

	/// <summary>
	/// Gets the current application version.
	/// </summary>
	public string CurrentVersion { get; private set; } = "unknown";

	/// <summary>
	/// Gets whether an update is pending (downloaded and ready to apply on restart).
	/// </summary>
	public bool IsUpdatePending => _updatePending;

	/// <summary>
	/// Event raised when download progress changes.
	/// </summary>
	public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

	/// <summary>
	/// Event raised when an update is available.
	/// </summary>
	public event EventHandler<AppUpdateInfo>? UpdateAvailable;

	/// <summary>
	/// Event raised when an error occurs during update check or download.
	/// </summary>
	public event EventHandler<string>? UpdateError;

	public SquirrelUpdaterService()
	{
		_versionFilePath = DataPaths.VersionFile;
		_failedUpdatesFilePath = Path.Combine(DataPaths.AppDataFolder, _failedUpdatesFileName);
		LoadCurrentVersion();
		LoadDeltaFailureCount();
	}

	/// <summary>
	/// Loads the current version from the version file.
	/// </summary>
	private void LoadCurrentVersion()
	{
		try
		{
			if (File.Exists(_versionFilePath))
			{
				CurrentVersion = File.ReadAllText(_versionFilePath).Trim();
			}
			else
			{
				// Try to get version from assembly
				var assembly = Assembly.GetExecutingAssembly();
				var version = assembly.GetName().Version;
				if (version != null) CurrentVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
			}
		}
		catch
		{
			CurrentVersion = "unknown";
		}

		Logger.Info($"[SquirrelUpdater] Current version: {CurrentVersion}");
	}

	/// <summary>
	/// Loads the delta failure count from persistent storage.
	/// </summary>
	private void LoadDeltaFailureCount()
	{
		try
		{
			if (File.Exists(_failedUpdatesFilePath))
			{
				var content = File.ReadAllText(_failedUpdatesFilePath).Trim();
				if (int.TryParse(content, out var count))
				{
					_deltaFailureCount = count;
					Logger.Info($"[SquirrelUpdater] Loaded delta failure count: {_deltaFailureCount}");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"[SquirrelUpdater] Failed to load delta failure count: {ex.Message}");
			_deltaFailureCount = 0;
		}
	}

	/// <summary>
	/// Saves the delta failure count to persistent storage.
	/// </summary>
	private void SaveDeltaFailureCount()
	{
		try
		{
			File.WriteAllText(_failedUpdatesFilePath, _deltaFailureCount.ToString(CultureInfo.InvariantCulture));
			Logger.Info($"[SquirrelUpdater] Saved delta failure count: {_deltaFailureCount}");
		}
		catch (Exception ex)
		{
			Logger.Warn($"[SquirrelUpdater] Failed to save delta failure count: {ex.Message}");
		}
	}

	/// <summary>
	/// Resets the delta failure count (called after a successful update).
	/// </summary>
	private void ResetDeltaFailureCount()
	{
		_deltaFailureCount = 0;
		try
		{
			if (File.Exists(_failedUpdatesFilePath)) File.Delete(_failedUpdatesFilePath);
		}
		catch (Exception ex)
		{
			Logger.Warn($"[SquirrelUpdater] Failed to delete failure count file: {ex.Message}");
		}
	}

	/// <summary>
	/// Determines whether to use full updates instead of delta updates.
	/// </summary>
	private bool ShouldUseFull => _deltaFailureCount >= _maxDeltaFailuresBeforeFull;

	/// <summary>
	/// Checks for updates from GitHub.
	/// </summary>
	/// <returns>Update info if an update is available, null otherwise.</returns>
	public async Task<AppUpdateInfo?> CheckForUpdatesAsync()
	{
		// If not a Squirrel installation, skip update check
		if (!DataPaths.IsSquirrelInstallation())
		{
			Logger.Info("[SquirrelUpdater] Not a Squirrel installation, skipping update check");
			Logger.Info("[SquirrelUpdater] To receive automatic updates, please install using Setup.exe");
			return null;
		}

		try
		{
			Logger.Info($"[SquirrelUpdater] Checking for updates from: {UpdateUrl}");

			using var updateManager = new UpdateManager(UpdateUrl);

			var updateInfo = await updateManager.CheckForUpdate();

			if (updateInfo == null || updateInfo.ReleasesToApply.Count == 0)
			{
				Logger.Info("[SquirrelUpdater] No updates available");
				return null;
			}

			var latestRelease = updateInfo.ReleasesToApply.Last();
			var newVersion = latestRelease.Version.ToString();

			Logger.Info($"[SquirrelUpdater] Update available: {newVersion} (current: {CurrentVersion})");

			// Fetch release notes from GitHub API
			var releaseNotes = await FetchReleaseNotesAsync(newVersion);

			var result = new AppUpdateInfo
			{
				TagName = $"v{newVersion}",
				Name = $"Companella v{newVersion}",
				Body = releaseNotes,
				Prerelease = false,
				PublishedAt = DateTime.UtcNow,
				DownloadUrl = "", // Not used with Squirrel
				DownloadSize = latestRelease.Filesize,
				HtmlUrl = $"{_gitHubRepoUrl}/releases/tag/v{newVersion}"
			};

			UpdateAvailable?.Invoke(this, result);
			return result;
		}
		catch (Exception ex)
		{
			var error = $"Failed to check for updates: {ex.Message}";
			Logger.Error($"[SquirrelUpdater] {error}");
			UpdateError?.Invoke(this, error);
			return null;
		}
	}

	/// <summary>
	/// Downloads and prepares an update for installation on restart.
	/// </summary>
	/// <param name="updateInfo">The update information.</param>
	/// <param name="progress">Optional progress reporter.</param>
	/// <returns>True if the update was successfully downloaded and prepared.</returns>
	public async Task<bool> DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo,
		IProgress<DownloadProgressEventArgs>? progress = null)
	{
		if (!DataPaths.IsSquirrelInstallation())
		{
			UpdateError?.Invoke(this, "Cannot update: not a Squirrel installation");
			return false;
		}

		// Try delta update first, then full update if delta has failed too many times
		var useFull = ShouldUseFull;

		if (useFull)
			Logger.Info($"[SquirrelUpdater] Delta updates have failed {_deltaFailureCount} times, using full update");

		var result = await TryDownloadAndApplyUpdateAsync(updateInfo, progress, useFull);

		if (result)
		{
			// Success - reset the failure counter
			ResetDeltaFailureCount();
			return true;
		}

		// If we were trying delta and it failed, increment counter and potentially retry with full
		if (!useFull)
		{
			_deltaFailureCount++;
			SaveDeltaFailureCount();

			Logger.Warn(
				$"[SquirrelUpdater] Delta update failed (attempt {_deltaFailureCount}/{_maxDeltaFailuresBeforeFull})");

			// If we've now reached the threshold, immediately retry with full update
			if (_deltaFailureCount >= _maxDeltaFailuresBeforeFull)
			{
				Logger.Info("[SquirrelUpdater] Delta failure threshold reached, retrying with full update...");
				ReportProgress(progress, 0, "Delta update failed, trying full update...");

				result = await TryDownloadAndApplyUpdateAsync(updateInfo, progress, true);

				if (result)
				{
					ResetDeltaFailureCount();
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Attempts to download and apply an update with the specified delta setting.
	/// </summary>
	/// <param name="updateInfo">The update information.</param>
	/// <param name="progress">Optional progress reporter.</param>
	/// <param name="ignoreDelta">If true, forces full package download instead of delta.</param>
	/// <returns>True if the update was successfully downloaded and prepared.</returns>
	private async Task<bool> TryDownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo,
		IProgress<DownloadProgressEventArgs>? progress, bool ignoreDelta)
	{
		try
		{
			var updateType = ignoreDelta ? "full" : "delta";
			Logger.Info($"[SquirrelUpdater] Downloading {updateType} update: {updateInfo.TagName}");

			ReportProgress(progress, 0, $"Preparing {updateType} update...");

			using var updateManager = new UpdateManager(UpdateUrl);

			// Check for updates again to get the release entries
			// ignoreDeltaUpdates forces full package downloads when true
			var updateResult = await updateManager.CheckForUpdate(ignoreDelta);

			if (updateResult == null || updateResult.ReleasesToApply.Count == 0)
			{
				UpdateError?.Invoke(this, "No update available");
				return false;
			}

			ReportProgress(progress, 10, $"Downloading {updateType} update...");

			// Download the updates
			await updateManager.DownloadReleases(updateResult.ReleasesToApply, p =>
			{
				// p is 0-100
				var adjustedProgress = 10 + (int)(p * 0.7); // 10-80%
				ReportProgress(progress, adjustedProgress, $"Downloading {updateType}... {p}%");
			});

			ReportProgress(progress, 80, "Applying update...");

			// Apply the updates (stages them for next restart)
			await updateManager.ApplyReleases(updateResult, p =>
			{
				var adjustedProgress = 80 + (int)(p * 0.2); // 80-100%
				ReportProgress(progress, adjustedProgress, $"Applying... {p}%");
			});

			_updatePending = true;
			_pendingUpdate = updateResult.ReleasesToApply.Last();

			ReportProgress(progress, 100, "Update ready! Restart to apply.");

			Logger.Info(
				$"[SquirrelUpdater] {updateType[..1].ToUpper(CultureInfo.InvariantCulture) + updateType[1..]} update downloaded and staged, will apply on restart");
			return true;
		}
		catch (Exception ex)
		{
			var updateType = ignoreDelta ? "full" : "delta";
			var error = $"Failed to download/apply {updateType} update: {ex.Message}";
			Logger.Error($"[SquirrelUpdater] {error}");
			UpdateError?.Invoke(this, error);
			return false;
		}
	}

	/// <summary>
	/// Restarts the application to apply a pending update.
	/// </summary>
	public void RestartToApplyUpdate()
	{
		if (!_updatePending)
		{
			Logger.Info("[SquirrelUpdater] No pending update to apply");
			return;
		}

		try
		{
			Logger.Info("[SquirrelUpdater] Restarting to apply update...");

			// UpdateManager.RestartApp() handles the restart properly for Squirrel
			UpdateManager.RestartApp();
		}
		catch (Exception ex)
		{
			Logger.Error($"[SquirrelUpdater] Failed to restart: {ex.Message}");
			UpdateError?.Invoke(this, $"Failed to restart: {ex.Message}");
		}
	}

	/// <summary>
	/// Starts the update process and restarts the application.
	/// This is called when the user confirms they want to restart and apply the update.
	/// </summary>
	/// <param name="updateInfo">The update information (used for compatibility with existing UI).</param>
	public void StartUpdateAndRestart(AppUpdateInfo updateInfo)
	{
		RestartToApplyUpdate();
	}

	/// <summary>
	/// Cancels any ongoing download (not directly supported by Squirrel, but included for API compatibility).
	/// </summary>
	public static void CancelDownload()
	{
		// Squirrel doesn't support cancellation directly
		// This is here for API compatibility with the existing UpdateDialog
		Logger.Info("[SquirrelUpdater] Download cancellation requested (not fully supported by Squirrel)");
	}

	/// <summary>
	/// Fetches release notes from GitHub API.
	/// </summary>
	private static async Task<string> FetchReleaseNotesAsync(string version)
	{
		try
		{
			using var httpClient = new HttpClient();
			httpClient.DefaultRequestHeaders.Add("User-Agent", "Companella-Updater");
			httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

			// Squirrel version is 3-part (5.67.0), but GitHub tags are 2-part (v5.67)
			// Try to convert and try both formats
			var versionParts = version.Split('.');
			var twoPartVersion = versionParts.Length >= 2
				? $"{versionParts[0]}.{versionParts[1]}"
				: version;

			// Try two-part version first (v5.67)
			var apiUrl = $"https://api.github.com/repos/Leinadix/companella/releases/tags/v{twoPartVersion}";
			Logger.Info($"[SquirrelUpdater] Fetching release notes from: {apiUrl}");
			var response = await httpClient.GetAsync(apiUrl);

			// If not found, try three-part version (v5.67.0)
			if (!response.IsSuccessStatusCode && twoPartVersion != version)
			{
				apiUrl = $"https://api.github.com/repos/Leinadix/companella/releases/tags/v{version}";
				Logger.Info($"[SquirrelUpdater] Retrying with: {apiUrl}");
				response = await httpClient.GetAsync(apiUrl);
			}

			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				var release = System.Text.Json.JsonSerializer.Deserialize<GitHubRelease>(json);
				return release?.Body ?? "No release notes available.";
			}
			else
			{
				Logger.Warn($"[SquirrelUpdater] GitHub API returned {response.StatusCode} for {apiUrl}");
			}
		}
		catch (Exception ex)
		{
			Logger.Warn($"[SquirrelUpdater] Failed to fetch release notes: {ex.Message}");
		}

		return "No release notes available.";
	}

	/// <summary>
	/// Reports download progress to subscribers.
	/// </summary>
	private void ReportProgress(IProgress<DownloadProgressEventArgs>? progress, int percentage, string status)
	{
		var args = new DownloadProgressEventArgs
		{
			BytesDownloaded = percentage,
			TotalBytes = 100,
			ProgressPercentage = percentage,
			Status = status
		};

		progress?.Report(args);
		DownloadProgressChanged?.Invoke(this, args);
	}

	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		GC.SuppressFinalize(this);
	}
}

/// <summary>
/// Event arguments for download progress updates.
/// </summary>
public class DownloadProgressEventArgs : EventArgs
{
	/// <summary>
	/// The number of bytes downloaded so far.
	/// </summary>
	public long BytesDownloaded { get; set; }

	/// <summary>
	/// The total number of bytes to download.
	/// </summary>
	public long TotalBytes { get; set; }

	/// <summary>
	/// The download progress percentage (0-100).
	/// </summary>
	public int ProgressPercentage { get; set; }

	/// <summary>
	/// A status message describing the current operation.
	/// </summary>
	public string Status { get; set; } = string.Empty;
}
