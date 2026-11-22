using System.Text.Json;
using Companella.Models.Application;

namespace Companella.Services.Common;

/// <summary>
/// Service for managing user settings/preferences.
/// Settings are stored in %AppData%\Companella\settings.json to survive updates.
/// </summary>
public class UserSettingsService
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private UserSettings _settings;
	private readonly string _settingsPath;
	private string? _loadError;

	/// <summary>
	/// Gets the loaded settings.
	/// </summary>
	public UserSettings Settings => _settings;

	/// <summary>
	/// Gets the error message if loading failed.
	/// </summary>
	public string? LoadError => _loadError;

	public UserSettingsService()
	{
		// Use DataPaths for AppData-based storage
		_settingsPath = DataPaths.SettingsFile;

		// Initialize with default settings
		_settings = new UserSettings();
	}

	/// <summary>
	/// Initializes the service by loading settings.
	/// Call this on application startup.
	/// </summary>
	public async Task InitializeAsync()
	{
		await LoadAsync();
	}

	/// <summary>
	/// Loads settings from the file.
	/// Creates default settings if file doesn't exist.
	/// </summary>
	public async Task LoadAsync()
	{
		_loadError = null;

		if (!File.Exists(_settingsPath))
		{
			// No settings file yet - use defaults
			_settings = new UserSettings();
			return;
		}

		try
		{
			var json = await File.ReadAllTextAsync(_settingsPath);
			var loaded = JsonSerializer.Deserialize<UserSettings>(json, _jsonOptions);

			if (loaded != null)
				_settings = loaded;
			else
				_settings = new UserSettings();
		}
		catch (Exception ex)
		{
			_loadError = ex.Message;
			_settings = new UserSettings();
			// Keep the unreadable file: the next save writes defaults over settings.json
			var backup = _settingsPath + ".corrupt";
			try { File.Copy(_settingsPath, backup, true); }
			catch (Exception copyEx) { backup = $"not kept ({copyEx.Message})"; }
			Logger.Warn($"[Settings] Could not read {_settingsPath}, using defaults: {ex.Message}. Old file: {backup}");
		}
	}

	/// <summary>
	/// Saves settings to the file.
	/// </summary>
	public async Task SaveAsync()
	{
		try
		{
			var json = JsonSerializer.Serialize(_settings, _jsonOptions);
			await File.WriteAllTextAsync(_settingsPath, json);
		}
		catch (Exception ex)
		{
			_loadError = ex.Message;
			throw;
		}
	}
}
