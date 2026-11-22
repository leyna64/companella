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
			Logger.Warn($"[Settings] Could not read {_settingsPath}, using defaults: {ex.Message}");
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
			// Under osu-winello, closing osu! kills Companella! mid-save: write a temp file, then swap it in so settings.json is never truncated
			string tempPath = _settingsPath + ".tmp";
			await File.WriteAllTextAsync(tempPath, json);
			File.Move(tempPath, _settingsPath, true);
		}
		catch (Exception ex)
		{
			_loadError = ex.Message;
			throw;
		}
	}
}
