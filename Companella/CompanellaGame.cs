using System.Diagnostics;
using Companella.Analyzers.Attributes;
using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Mods;
using Companella.Screens;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Session;
using Companella.Services.Tools;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osuTK.Input;

namespace Companella;

/// <summary>
/// Main game class for the Companella! application.
/// </summary>
public partial class CompanellaGame : Game
{
	private ScreenStack _screenStack = null!;
	private ScaledContentContainer _scaledContainer = null!;
	private DependencyContainer _dependencies = null!;
	private MainScreen? _mainScreen;
	// OLD private TrainingScreen? _trainingScreen;

	/// <summary>
	/// Whether the application is running in training mode.
	/// </summary>
	private readonly bool _trainingMode;

	// Settings saving
	private double _settingsSaveTimer;
	private const double _settingsSaveInterval = 2000; // Save every 2 seconds
	private Size _lastWindowSize;
	private Point _lastWindowPosition;
	private Size _targetWindowSize = new(480, 810);
	private bool _isRestoringWindowSettings;

	// Services
	private OsuProcessDetector _processDetector = null!;
	private OsuFileParser _fileParser = null!;
	private OsuFileWriter _fileWriter = null!;
	private AudioExtractor _audioExtractor = null!;
	private TimingPointConverter _timingConverter = null!;
	private DanConfigurationService _danConfigService = null!;
	private TrainingDataService _trainingDataService = null!;
	private UserSettingsService _userSettingsService = null!;
	private OsuWindowOverlayService _overlayService = null!;
	private GlobalHotkeyService _hotkeyService = null!;
	private SquirrelUpdaterService _autoUpdaterService = null!;
	private SessionDatabaseService _sessionDatabaseService = null!;
	private SessionTrackerService _sessionTrackerService = null!;
	private Services.Integrations.ManiaTracker.ManiaTrackerService _maniaTrackerService = null!;

	// Skills analysis services
	private MapsDatabaseService _mapsDatabaseService = null!;
	private SkillsTrendAnalyzer _skillsTrendAnalyzer = null!;
	private MapMmrCalculator _mapMmrCalculator = null!;
	private MapRecommendationService _mapRecommendationService = null!;
	private OsuCollectionService _collectionService = null!;
	private BeatmapApiService _beatmapApiService = null!;
	private ScoreMigrationService _scoreMigrationService = null!;
	private ScoreImportService _scoreImportService = null!;

	// Replay file watcher
	private ReplayFileWatcherService _replayFileWatcherService = null!;

	// Tray icon
	private TrayIconService _trayIconService = null!;

	// Analytics
	private AptabaseService _aptabaseService = null!;

	// Timing deviation analysis services
	private ReplayParserService _replayParserService = null!;
	private TimingDeviationCalculator _timingDeviationCalculator = null!;
	private HitErrorReaderService _hitErrorReaderService = null!;

	// Mod system
	private ModService _modService = null!;

	// Results overlay for timing deviation display
	private ResultsOverlayWindow? _resultsOverlay;

	// Confirmation dialog for quit
	private ConfirmationDialog? _quitConfirmationDialog;

	// Restart dialog for startup restart with command line args
	private OsuRestartDialog? _startupRestartDialog;

	// Replay analysis window state
	private bool _isInReplayAnalysisMode;
	private Size _savedWindowSizeBeforeAnalysis;
	private Point _savedWindowPositionBeforeAnalysis;

	// Overlay state
	private bool _isWindowVisible = true;
	private bool _wasOsuRunning;
	private Point _savedWindowPosition;
	private DateTimeOffset _lastOverlayToggleTime = DateTimeOffset.MinValue;
	private const double _overlayToggleCooldownMs = 500; // 0.5 second cooldown

	// Window subclassing for preventing unwanted position changes
	private IntPtr _originalWndProc = IntPtr.Zero;
	private WndProcDelegate? _wndProcDelegate;
	private bool _isDragging;
	private Point _lastKnownPosition;

	// Flag to track if we've performed the startup restart after first connection
	private bool _hasPerformedStartupRestart;

	// Callback to close the native splash screen
	private readonly Action? _closeSplashScreen;

	/// <summary>
	/// Creates a new instance of the game.
	/// </summary>
	/// <param name="trainingMode">Whether to start in training mode.</param>
	/// <param name="closeSplashScreen">Optional callback to close the native splash screen.</param>
	public CompanellaGame(bool trainingMode = false, Action? closeSplashScreen = null)
	{
		_trainingMode = trainingMode;
		_closeSplashScreen = closeSplashScreen;
	}

	protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
	{
		_dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

		// Create and register services
		// Analytics (created early so it can be injected into other services)
		_aptabaseService = new AptabaseService();

		_processDetector = new OsuProcessDetector();
		_fileParser = new OsuFileParser();
		_fileWriter = new OsuFileWriter();
		_audioExtractor = new AudioExtractor();
		_timingConverter = new TimingPointConverter();
		_danConfigService = new DanConfigurationService();
		_trainingDataService = new TrainingDataService();
		_userSettingsService = new UserSettingsService();

		// Connect settings service to process detector for caching osu! directory
		_processDetector.SetSettingsService(_userSettingsService);
		_overlayService = new OsuWindowOverlayService();
		_hotkeyService = new GlobalHotkeyService();
		_autoUpdaterService = new SquirrelUpdaterService();
		_sessionDatabaseService = new SessionDatabaseService();
		_sessionTrackerService = new SessionTrackerService(_processDetector, _sessionDatabaseService, _aptabaseService);
		_maniaTrackerService = new Services.Integrations.ManiaTracker.ManiaTrackerService(_processDetector);
		_maniaTrackerService.Start();
		_sessionTrackerService.PlayEndedWithPauses += OnPlayEndedWithPauses;
		_sessionTrackerService.ResultsScreenEntered += OnResultsScreenEntered;
		_sessionTrackerService.ResultsScreenExited += OnResultsScreenExited;

		// Timing deviation analysis services
		_replayParserService = new ReplayParserService(_processDetector);
		_timingDeviationCalculator = new TimingDeviationCalculator(_fileParser);
		_hitErrorReaderService = new HitErrorReaderService();

		// Mod system
		_modService = new ModService();
		RegisterMods();

		// Skills analysis services
		_mapsDatabaseService = new MapsDatabaseService();
		_beatmapApiService = new BeatmapApiService(_userSettingsService);
		_mapsDatabaseService.SetBeatmapApiService(_beatmapApiService);
		_scoreMigrationService = new ScoreMigrationService(_processDetector, _fileParser);
		_scoreImportService = new ScoreImportService(_processDetector, _replayParserService, _sessionDatabaseService,
			_mapsDatabaseService);

		// Replay file watcher for matching replays to session plays
		_replayFileWatcherService =
			new ReplayFileWatcherService(_processDetector, _sessionDatabaseService, _scoreImportService, _sessionTrackerService);
		_skillsTrendAnalyzer = new SkillsTrendAnalyzer(_sessionDatabaseService);
		_mapMmrCalculator = new MapMmrCalculator(_mapsDatabaseService);
		_mapRecommendationService =
			new MapRecommendationService(_mapsDatabaseService, _mapMmrCalculator, _skillsTrendAnalyzer);
		_collectionService = new OsuCollectionService(_processDetector);

		// Tray icon
		_trayIconService = new TrayIconService();

		_dependencies.CacheAs(_processDetector);
		_dependencies.CacheAs(_fileParser);
		_dependencies.CacheAs(_fileWriter);
		_dependencies.CacheAs(_audioExtractor);
		_dependencies.CacheAs(_timingConverter);
		_dependencies.CacheAs(_danConfigService);
		_dependencies.CacheAs(_trainingDataService);
		_dependencies.CacheAs(_userSettingsService);
		_dependencies.CacheAs(_overlayService);
		_dependencies.CacheAs(_hotkeyService);
		_dependencies.CacheAs(_autoUpdaterService);
		_dependencies.CacheAs(_sessionDatabaseService);
		_dependencies.CacheAs(_sessionTrackerService);
		_dependencies.CacheAs(_maniaTrackerService);
		_dependencies.CacheAs(_replayFileWatcherService);
		_dependencies.CacheAs(_mapsDatabaseService);
		_dependencies.CacheAs(_skillsTrendAnalyzer);
		_dependencies.CacheAs(_mapMmrCalculator);
		_dependencies.CacheAs(_mapRecommendationService);
		_dependencies.CacheAs(_collectionService);
		_dependencies.CacheAs(_beatmapApiService);
		_dependencies.CacheAs(_scoreMigrationService);
		_dependencies.CacheAs(_scoreImportService);
		_dependencies.CacheAs(_trayIconService);
		_dependencies.CacheAs(_aptabaseService);
		_dependencies.CacheAs(_replayParserService);
		_dependencies.CacheAs(_timingDeviationCalculator);
		_dependencies.CacheAs(_modService);

		// Note: ScaledContentContainer will be cached after creation in load()
		return _dependencies;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		// Add embedded resources using AssemblyResourceStore which properly handles manifest names
		var assembly = typeof(CompanellaGame).Assembly;
		var assemblyStore = new AssemblyResourceStore(assembly, "Companella");
		Resources.AddStore(assemblyStore);

		// Load custom fonts from embedded resources
		AddFont(Resources, @"Resources/Fonts/Noto/Noto-Basic");
		AddFont(Resources, @"Resources/Fonts/Noto/Noto-JP");
		AddFont(Resources, @"Resources/Fonts/Noto/Noto-Hangul");
		AddFont(Resources, @"Resources/Fonts/Noto/Noto-CJK-Basic");
		AddFont(Resources, @"Resources/Fonts/Noto/Noto-Bopomofo");

		// Create scaled content container for global UI scaling
		_scaledContainer = new ScaledContentContainer
		{
			ReferenceWidth = _baseWidth,
			ReferenceHeight = _baseHeight
		};

		// Add screen stack to scaled container
		_screenStack = new ScreenStack
		{
			RelativeSizeAxes = Axes.Both
		};

		_scaledContainer.Add(_screenStack);

		// Wrap scaled container in TooltipContainer to enable tooltips throughout the app
		var tooltipContainer = new TooltipContainer { RelativeSizeAxes = Axes.Both };
		tooltipContainer.Add(_scaledContainer);
		Add(tooltipContainer);

		// Add results overlay for timing deviation display OUTSIDE the scaled container
		// This allows it to properly fill the replay analysis window (800x400)
		_resultsOverlay = new ResultsOverlayWindow
		{
			RelativeSizeAxes = Axes.Both,
			Depth = float.MinValue, // Ensure it's on top
			Alpha = 0 // Start hidden
		};
		_resultsOverlay.CloseRequested += (_, _) => ExitReplayAnalysisMode();
		_resultsOverlay.ReanalysisRequested += HandleReanalysisRequest;
		Add(_resultsOverlay);

		// Add quit confirmation dialog
		_quitConfirmationDialog = new ConfirmationDialog();
		Add(_quitConfirmationDialog);

		// Add startup restart dialog with command line args support
		_startupRestartDialog = new OsuRestartDialog();
		Add(_startupRestartDialog);

		// Cache the scaled container so other components can access it
		_dependencies.CacheAs(_scaledContainer);

		// Push appropriate screen based on mode
		// Native splash screen handles the startup animation
		// if (_trainingMode)
		// {
		// 	_trainingScreen = new TrainingScreen();
		// 	_screenStack.Push(_trainingScreen);
		// }
		// else
		// {
		_mainScreen = new MainScreen();
		_mainScreen.ReplayAnalysisRequested += OnReplayAnalysisRequested;
		_screenStack.Push(_mainScreen);
		// }
	}

	/// <summary>
	/// Handles replay analysis requests from the session panel.
	/// </summary>
	private void OnReplayAnalysisRequested(string replayPath)
	{
		Logger.Info($"[CompanellaGame] Replay analysis requested: {replayPath}");
		HandleReplayFileDrop(replayPath);
	}

	protected override void LoadComplete()
	{
		base.LoadComplete();
		Window.Resizable = false;

		// Initialize services
		Task.Run(async () =>
		{
			await _userSettingsService.InitializeAsync();
			_processDetector.SetSettingsService(_userSettingsService);
			_replayFileWatcherService.StartWatching();
			_replayFileWatcherService.CheckForMissingReplays();
			await _danConfigService.InitializeAsync();

			// Apply analytics setting from user preferences (GDPR compliance)
			_aptabaseService.IsEnabled = _userSettingsService.Settings.SendAnalytics;

			// Apply MinaCalc version setting
			ToolPaths.SelectedMinaCalcVersion = _userSettingsService.Settings.MinaCalcVersion;

			// Track app startup (only if analytics is enabled)
			_aptabaseService.TrackAppStarted(_trainingMode);

			// Restore window settings on UI thread
			Schedule(() =>
			{
				RestoreWindowSettings();
				RestoreUIScale();
				InitializeOverlayAndHotkeys();
				MakeWindowBorderless();
				InstallWindowHook();
				InitializeTrayIcon();

				// Auto-start session if enabled in settings
				if (_userSettingsService.Settings.AutoStartSession && !_sessionTrackerService.IsTracking)
				{
					_sessionTrackerService.StartSession();
					Logger.Info("[Session] Auto-started session on startup");
				}

				// Close the native splash screen now that the game is ready
				_closeSplashScreen?.Invoke();

				// Bring game window to foreground after splash closes
				BringWindowToFocus();
			});
		});

		// Set window properties after load is complete
		if (Window != null)
		{
			Window.Title = _trainingMode ? "Companella! - Training Mode" : "Companella!";

			// Configure osu!-style borderless window
			Window.CursorState = CursorState.Default;

			// Subscribe to file drop events
			Window.DragDrop += OnWindowFileDrop;

			// Subscribe to window state changes to save settings
			Window.WindowStateChanged += OnWindowStateChanged;

			// Try to restore window settings immediately if available
			if (_userSettingsService != null) RestoreWindowSettings();

			// Enforce window size immediately without changing position
			Schedule(() =>
			{
				var windowTitle = Window.Title;
				var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
				if (handle != IntPtr.Zero)
					SetWindowPos(handle, IntPtr.Zero, 0, 0, _currentTargetWidth, _currentTargetHeight,
						SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
			});
		}
	}

	private const int _baseWidth = 620;
	private const int _baseHeight = 810;

	// Current scaled window dimensions
	private int _currentTargetWidth = _baseWidth;
	private int _currentTargetHeight = _baseHeight;

	private void ForceWindowResolution(int width, int height)
	{
		if (Window == null)
			return;

		// Use only osu!framework API to set window properties
		// Note: Window.Size is read-only, so we monitor and correct size changes
		Window.WindowState = WindowState.Normal;

		// Store target size for monitoring
		_targetWindowSize = new Size(width, height);
	}

	/// <summary>
	/// Performs a one-time restart of osu! after first connection to ensure proper process attachment.
	/// This only happens once per app session.
	/// </summary>
	private void PerformFirstConnectionRestart()
	{
		if (_hasPerformedStartupRestart)
			return;

		// Mark as performed immediately to prevent multiple dialogs
		_hasPerformedStartupRestart = true;

		// Track if user has made a choice
		var userChoiceMade = false;

		// Show restart dialog with command line args support
		Schedule(() =>
		{
			if (_startupRestartDialog != null)
			{
				void OnConfirmed(string arguments)
				{
					if (userChoiceMade)
						return;
					userChoiceMade = true;
					DoStartupRestart(arguments);
					CleanupHandlers();
				}

				void OnSkipped()
				{
					if (userChoiceMade)
						return;
					userChoiceMade = true;
					Logger.Info("[Startup] User skipped osu! restart");
					CleanupHandlers();
				}

				void OnClosed()
				{
					if (userChoiceMade)
						return;
					userChoiceMade = true;
					Logger.Info("[Startup] User cancelled osu! restart");
					CleanupHandlers();
				}

				void CleanupHandlers()
				{
					if (_startupRestartDialog != null)
					{
						_startupRestartDialog.Confirmed -= OnConfirmed;
						_startupRestartDialog.Skipped -= OnSkipped;
						_startupRestartDialog.Closed -= OnClosed;
					}
				}

				_startupRestartDialog.Confirmed += OnConfirmed;
				_startupRestartDialog.Skipped += OnSkipped;
				_startupRestartDialog.Closed += OnClosed;

				_startupRestartDialog.Show(
					"Restart osu!?",
					"osu! needs to be restarted for proper attachment. Select your preferred server and command line arguments. You can skip this, but some features may not work correctly.",
					true
				);
			}
			else
			{
				// Fallback if dialog not initialized - perform restart directly
				DoStartupRestart("");
			}
		});
	}

	private void DoStartupRestart(string arguments)
	{
		try
		{
			var hasArgs = !string.IsNullOrWhiteSpace(arguments);
			Logger.Info(
				$"[Startup] First connection detected, performing quick restart for proper attachment{(hasArgs ? $" with args: {arguments}" : "")}...");
			_collectionService.RestartOsu(arguments);
		}
		catch (Exception ex)
		{
			Logger.Info($"[Startup] Error during osu! restart: {ex.Message}");
		}
	}

	private void RestoreUIScale()
	{
		if (_userSettingsService == null || _scaledContainer == null)
			return;

		var savedScale = _userSettingsService.Settings.UIScale;

		// Clamp to valid range
		savedScale = Math.Clamp(savedScale, 0.5f, 2.0f);

		_scaledContainer.UIScale = savedScale;

		// Apply window size based on scale
		ApplyWindowScale(savedScale);

		// Subscribe to future scale changes
		_scaledContainer.UIScaleBindable.BindValueChanged(e =>
		{
			// Must schedule to ensure we're on the correct thread for Windows API calls
			Schedule(() => ApplyWindowScale(e.NewValue));
		});

		Logger.Info($"[UIScale] Restored UI scale: {savedScale:P0}");
	}

	/// <summary>
	/// Applies window size based on the UI scale factor.
	/// </summary>
	private void ApplyWindowScale(float scale)
	{
		if (Window == null)
			return;

		// Calculate new window dimensions
		_currentTargetWidth = (int)(_baseWidth * scale);
		_currentTargetHeight = (int)(_baseHeight * scale);

		// Apply via Windows API
		var windowTitle = Window.Title;
		var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

		if (handle != IntPtr.Zero)
		{
			// Ensure borderless style is set
			var borderlessStyle = WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
			var result1 = SetWindowLong(handle, GWL_STYLE, borderlessStyle);
			if (result1 == 0)
			{
				Logger.Info($"[UIScale] Failed to set borderless style: {result1}");
				return;
			}

			// Apply size change without moving the window
			// SWP_NOMOVE prevents interfering with user's window placement
			var result2 = SetWindowPos(handle, IntPtr.Zero, 0, 0, _currentTargetWidth, _currentTargetHeight,
				SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

			if (!result2)
			{
				Logger.Info($"[UIScale] Failed to set window position: {result2}");
				return;
			}

			Logger.Info(
				$"[UIScale] Window resized to {_currentTargetWidth}x{_currentTargetHeight} (scale: {scale:P0})");
		}
		else
		{
			Logger.Info($"[UIScale] Failed to get window handle for resize");
		}
	}

	/// <summary>
	/// Block Alt+Enter to prevent fullscreen toggle.
	/// </summary>
	protected override bool OnKeyDown(KeyDownEvent e)
	{
		// Block Alt+Enter (fullscreen toggle)
		if (e.Key == Key.Enter && e.AltPressed) return true; // Consume the event

		return base.OnKeyDown(e);
	}

	private void RestoreWindowSettings()
	{
		if (Window == null || _userSettingsService == null)
			return;

		var settings = _userSettingsService.Settings;

		// Restore window state first (this might affect size/position)
		if (Enum.TryParse<WindowState>(settings.WindowState, out var windowState))
			Window.WindowState = windowState;
		else
			Window.WindowState = WindowState.Normal;

		// Restore window position and size using Windows API
		// Schedule to ensure window handle is available
		_isRestoringWindowSettings = true;
		Schedule(() =>
		{
			if (Window == null)
			{
				_isRestoringWindowSettings = false;
				return;
			}

			var windowTitle = Window.Title;
			var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

			if (handle != IntPtr.Zero)
			{
				// Use saved position and size, or defaults if not set
				var x = settings.WindowX > 0 ? settings.WindowX : 100;
				var y = settings.WindowY > 0 ? settings.WindowY : 100;
				var width = settings.WindowWidth > 0 ? settings.WindowWidth : _currentTargetWidth;
				var height = settings.WindowHeight > 0 ? settings.WindowHeight : _currentTargetHeight;

				// Restore window position and size
				SetWindowPos(handle, IntPtr.Zero, x, y, width, height,
					SWP_NOZORDER | SWP_FRAMECHANGED);

				// Wait a frame for the position to be applied, then update tracking variables
				Schedule(() =>
				{
					_lastWindowSize = Window.Size;
					_lastWindowPosition = new Point(x, y);
					_isRestoringWindowSettings = false;
				});
			}
			else
			{
				// Fallback: initialize tracking variables
				_lastWindowSize = Window.Size;
				_lastWindowPosition = Window.Position;
				_isRestoringWindowSettings = false;
			}
		});
	}

	private void OnWindowStateChanged(WindowState newState)
	{
		SaveWindowSettings();
	}

	private void SaveWindowSettings()
	{
		if (Window == null || _userSettingsService == null)
			return;

		var settings = _userSettingsService.Settings;

		// Save window size and position
		settings.WindowWidth = Window.Size.Width;
		settings.WindowHeight = Window.Size.Height;
		settings.WindowX = Window.Position.X;
		settings.WindowY = Window.Position.Y;
		settings.WindowState = Window.WindowState.ToString();

		// Update tracking variables
		_lastWindowSize = Window.Size;
		_lastWindowPosition = Window.Position;

		// Save asynchronously
		Task.Run(async () => await _userSettingsService.SaveAsync());
	}

	/// <summary>
	/// Initializes overlay and hotkey services.
	/// </summary>
	private void InitializeOverlayAndHotkeys()
	{
		if (_userSettingsService == null || _overlayService == null || _hotkeyService == null)
			return;

		var settings = _userSettingsService.Settings;

		// Initialize overlay mode (will be auto-enabled when osu! is detected)
		_overlayService.OsuWindowChanged += OnOsuWindowChanged;
		_overlayService.OverlayModeChangeRequested += OnOverlayModeChangeRequested;

		// Apply saved overlay offset
		_overlayService.OverlayOffset = new Point(
			settings.OverlayOffsetX,
			settings.OverlayOffsetY
		);

		// Store initial window position
		if (Window != null) _savedWindowPosition = Window.Position;

		// Initialize hotkey (will need window handle - see note below)
		// Note: osu!Framework doesn't expose window handle directly
		// We'll need to use a different approach or get handle via reflection/Windows API
		InitializeHotkey(settings.ToggleVisibilityKeybind);

		// Subscribe to hotkey press
		_hotkeyService.HotkeyPressed += OnToggleVisibilityHotkeyPressed;
	}

	/// <summary>
	/// Initializes the global hotkey using a message-only window.
	/// </summary>
	private void InitializeHotkey(string keybind)
	{
		if (_hotkeyService == null)
			return;

		try
		{
			// Initialize with IntPtr.Zero to create a message-only window
			// This is more reliable than trying to find the osu!Framework window handle
			_hotkeyService.Initialize(IntPtr.Zero);

			if (_hotkeyService.RegisterHotkey(keybind))
				Logger.Info($"[Hotkey] Registered hotkey: {keybind}");
			else
				Logger.Info("[Hotkey] Failed to register hotkey - it may already be in use");
		}
		catch (Exception ex)
		{
			Logger.Info($"[Hotkey] Error initializing hotkey: {ex.Message}");
		}
	}

	/// <summary>
	/// Initializes the system tray icon.
	/// </summary>
	private void InitializeTrayIcon()
	{
		if (_trayIconService == null)
			return;

		try
		{
			_trayIconService.Initialize();
			_trayIconService.CheckForUpdatesRequested += OnTrayCheckForUpdatesRequested;
			_trayIconService.ExitRequested += OnTrayExitRequested;
		}
		catch (Exception ex)
		{
			Logger.Info($"[TrayIcon] Error initializing tray icon: {ex.Message}");
		}
	}

	/// <summary>
	/// Handles the tray icon "Check for Updates" menu item click.
	/// </summary>
	private async void OnTrayCheckForUpdatesRequested(object? sender, EventArgs e)
	{
		if (_autoUpdaterService == null)
			return;

		try
		{
			_trayIconService?.ShowNotification("Companella!", "Checking for updates...",
				ToolTipIcon.Info, 2000);

			var updateInfo = await _autoUpdaterService.CheckForUpdatesAsync();

			if (updateInfo != null)
				_trayIconService?.ShowNotification(
					"Update Available",
					$"Version {updateInfo.TagName} is available. Open the app to update.",
					ToolTipIcon.Info,
					5000);
			else
				_trayIconService?.ShowNotification(
					"Companella!",
					$"You are running the latest version ({_autoUpdaterService.CurrentVersion}).",
					ToolTipIcon.Info,
					3000);
		}
		catch (Exception ex)
		{
			Logger.Info($"[TrayIcon] Error checking for updates: {ex.Message}");
			_trayIconService?.ShowNotification(
				"Update Check Failed",
				"Could not check for updates. Please try again later.",
				ToolTipIcon.Warning,
				3000);
		}
	}

	/// <summary>
	/// Handles the tray icon "Exit" menu item click.
	/// </summary>
	private void OnTrayExitRequested(object? sender, EventArgs e)
	{
		// Show confirmation dialog before exiting
		Schedule(() =>
		{
			if (_quitConfirmationDialog != null)
			{
				_quitConfirmationDialog.Confirmed -= OnQuitConfirmed;
				_quitConfirmationDialog.Confirmed += OnQuitConfirmed;
				_quitConfirmationDialog.Show(
					"Quit Companella!?",
					"Are you sure you want to quit?",
					false
				);
			}
			else
			{
				// Fallback if dialog not initialized
				Host.Exit();
			}
		});
	}

	private void OnQuitConfirmed()
	{
		Host.Exit();
	}

	/// <summary>
	/// Handles the play ended with pauses event.
	/// Shows a notification with the pause count.
	/// </summary>
	private void OnPlayEndedWithPauses(object? sender, int pauseCount)
	{
		var message = pauseCount == 1
			? "You paused 1 time >:c"
			: $"You paused {pauseCount} times >:c";

		_trayIconService?.ShowNotification("Pause Counter", message, ToolTipIcon.Warning, 3000);
	}

	/// <summary>
	/// Handles entering the results screen after completing a map or viewing a replay.
	/// Triggers timing deviation analysis by reading hit errors from memory.
	/// </summary>
	private void OnResultsScreenEntered(object? sender, ResultsScreenEventArgs e)
	{
		// Check if replay analysis is enabled
		if (!_userSettingsService.Settings.ReplayAnalysisEnabled)
		{
			Logger.Info("[TimingDeviation] Replay analysis is disabled in settings");
			return;
		}

		var source = e.IsReplayView ? "viewing replay" : "completed play";
		Logger.Info($"[TimingDeviation] Results screen entered ({source}): {Path.GetFileName(e.BeatmapPath)}");

		// Schedule on UI thread
		Schedule(() =>
		{
			if (_resultsOverlay == null)
				return;

			// Switch to replay analysis window mode
			EnterReplayAnalysisMode();

			_resultsOverlay.ShowLoading();

			// Run analysis in background
			Task.Run(() =>
			{
				try
				{
					// Small delay to ensure osu! has updated memory with results
					Thread.Sleep(300);

					// Read hit errors directly from memory
					// This works for both fresh plays and replay viewing - osu! loads the data into memory
					Logger.Info("[TimingDeviation] Reading hit errors from osu! memory...");
					var result = _hitErrorReaderService.ReadHitErrorsWithBeatmap(e.BeatmapPath, _fileParser, e.Rate);

					if (result != null && result.Success && result.Deviations.Count > 0)
					{
						Logger.Info(
							$"[TimingDeviation] Memory read successful: UR={result.UnstableRate:F2}, Mean={result.MeanDeviation:F2}ms, Hits={result.Deviations.Count}");
						Schedule(() => _resultsOverlay?.ShowData(result));
						return;
					}

					// If viewing a replay/score, try to find the exact replay using scores.db
					if (e.IsReplayView)
					{
						Logger.Info(
							"[TimingDeviation] Memory read failed for replay view - trying to identify specific replay...");

						// Step 1: Read score data from results screen
						var resultsData = _hitErrorReaderService.TryReadResultsScreenData();
						if (resultsData != null)
						{
							Logger.Info($"[TimingDeviation] Results screen data: {resultsData}");

							// Step 2: Get beatmap hash
							var beatmapHash = ReplayParserService.GetBeatmapHash(e.BeatmapPath);
							if (!string.IsNullOrEmpty(beatmapHash))
							{
								Logger.Info($"[TimingDeviation] Beatmap hash: {beatmapHash}");

								// Step 3: Find matching replay using scores.db
								var matchingReplay =
									_replayParserService.FindReplayByScoreData(resultsData, beatmapHash);

								if (matchingReplay != null)
								{
									// Get key count from beatmap
									var replayOsuFile = OsuFileParser.Parse(e.BeatmapPath);
									var replayKeyCount = (int)replayOsuFile.CircleSize;

									// Extract key events from replay
									var replayKeyEvents =
										ReplayParserService.ExtractManiaKeyEvents(matchingReplay, replayKeyCount);

									if (replayKeyEvents.Count > 0)
									{
										// Calculate timing deviations
										var replayRate = ReplayParserService.GetRateFromMods(matchingReplay);
										var hasMirror = ReplayParserService.HasMirrorMod(matchingReplay);
										Logger.Info(
											$"[TimingDeviation] Replay mods: {matchingReplay.Mods}, rate: {replayRate}x, mirror: {hasMirror}");
										var matchedAnalysis =
											TimingDeviationCalculator.CalculateDeviations(e.BeatmapPath,
												replayKeyEvents, replayRate, hasMirror);

										if (matchedAnalysis.Success)
										{
											Logger.Info(
												$"[TimingDeviation] Exact replay analysis complete: UR={matchedAnalysis.UnstableRate:F2}, Mean={matchedAnalysis.MeanDeviation:F2}ms");
											Schedule(() => _resultsOverlay?.ShowData(matchedAnalysis));
											return;
										}
									}
								}
							}
						}

						Logger.Info("[TimingDeviation] Could not identify the specific replay for this score");
						Schedule(() => ExitReplayAnalysisMode());
						return;
					}

					// For fresh plays only: Try to find replay file as fallback
					Logger.Info("[TimingDeviation] Memory read failed, trying replay file fallback...");
					Thread.Sleep(1000);
					var directory = _processDetector.GetOsuDirectory();
					var currentHash = ReplayParserService.GetBeatmapHash(e.BeatmapPath);
					var exactReplayPath = directory == null || currentHash == null ? null :
						SessionReplayMatcher.Find(directory, currentHash, e.EnteredAtUtc);
					var replayResult = exactReplayPath == null ? null : ReplayParserService.ParseReplay(exactReplayPath);

					if (replayResult is not { } replay)
					{
						Logger.Info("[TimingDeviation] No replay file found");
						Schedule(() => ExitReplayAnalysisMode());
						return;
					}

					// Get key count from beatmap
					var osuFile = OsuFileParser.Parse(e.BeatmapPath);
					var keyCount = (int)osuFile.CircleSize;

					// Extract key events from replay
					var keyEvents = ReplayParserService.ExtractManiaKeyEvents(replay, keyCount);

					if (keyEvents.Count == 0)
					{
						Logger.Info("[TimingDeviation] No key events in replay");
						Schedule(() => ExitReplayAnalysisMode());
						return;
					}

					// Calculate timing deviations
					var rate = ReplayParserService.GetRateFromMods(replay);
					var mirror = ReplayParserService.HasMirrorMod(replay);
					Logger.Info($"[TimingDeviation] Replay rate: {rate}x, mirror: {mirror}");
					var replayAnalysis =
						TimingDeviationCalculator.CalculateDeviations(e.BeatmapPath, keyEvents, rate, mirror);

					if (replayAnalysis.Success)
					{
						Logger.Info(
							$"[TimingDeviation] Replay file analysis complete: UR={replayAnalysis.UnstableRate:F2}, Mean={replayAnalysis.MeanDeviation:F2}ms");
						Schedule(() => _resultsOverlay?.ShowData(replayAnalysis));
					}
					else
					{
						Logger.Info($"[TimingDeviation] Analysis failed: {replayAnalysis.ErrorMessage}");
						Schedule(() => ExitReplayAnalysisMode());
					}
				}
				catch (Exception ex)
				{
					Logger.Info($"[TimingDeviation] Error during analysis: {ex.Message}");
					Schedule(() => ExitReplayAnalysisMode());
				}
			});
		});
	}

	/// <summary>
	/// Handles leaving the results screen.
	/// Hides the timing deviation overlay and restores window state.
	/// </summary>
	private void OnResultsScreenExited(object? sender, EventArgs e)
	{
		Logger.Info("[TimingDeviation] Results screen exited");
		Schedule(() =>
		{
			_resultsOverlay?.Hide();
			ExitReplayAnalysisMode();
		});
	}

	/// <summary>
	/// Handles a request to re-analyze with a different OD.
	/// Runs a full re-analysis with the new OD value.
	/// </summary>
	private void HandleReanalysisRequest(double newOD)
	{
		if (_resultsOverlay?.CurrentData == null)
		{
			Logger.Info("[TimingDeviation] Re-analysis requested but no current data");
			return;
		}

		var currentData = _resultsOverlay.CurrentData;

		// Check if we have the original key events for re-analysis
		if (currentData.OriginalKeyEvents == null || currentData.OriginalKeyEvents.Count == 0)
		{
			Logger.Info("[TimingDeviation] Re-analysis requested but no original key events stored");
			return;
		}

		Logger.Info($"[TimingDeviation] Re-analysis requested with OD={newOD}");

		// Run full re-analysis in background
		Task.Run(() =>
		{
			try
			{
				var newAnalysis = TimingDeviationCalculator.CalculateDeviations(
					currentData.BeatmapPath,
					currentData.OriginalKeyEvents,
					currentData.Rate,
					currentData.HasMirror,
					newOD // Custom OD
				);

				if (newAnalysis.Success)
				{
					Logger.Info(
						$"[TimingDeviation] Re-analysis complete: UR={newAnalysis.UnstableRate:F2}, Mean={newAnalysis.MeanDeviation:F2}ms");
					Schedule(() => _resultsOverlay?.ShowData(newAnalysis));
				}
				else
				{
					Logger.Info($"[TimingDeviation] Re-analysis failed: {newAnalysis.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				Logger.Info($"[TimingDeviation] Re-analysis error: {ex.Message}");
			}
		});
	}

	/// <summary>
	/// Enters replay analysis mode - resizes window to 8:4 aspect ratio.
	/// </summary>
	private void EnterReplayAnalysisMode()
	{
		if (_isInReplayAnalysisMode)
			return;

		Logger.Info("[ReplayAnalysis] Entering replay analysis mode");

		// Save current window state
		_savedWindowSizeBeforeAnalysis = Window.Size;
		_savedWindowPositionBeforeAnalysis = Window.Position;

		// Hide main app content, show only replay analysis
		_scaledContainer.FadeTo(0, 100);

		// Get settings for replay analysis window
		var settings = _userSettingsService.Settings;
		var scale = settings.UIScale;
		var width = (int)(settings.ReplayAnalysisWidth * scale);
		var height = (int)(settings.ReplayAnalysisHeight * scale);

		// In overlay mode, use saved position; otherwise keep current position (window is draggable)
		var isOverlayMode = _overlayService?.IsOverlayMode == true;
		var x = isOverlayMode ? settings.ReplayAnalysisX : _savedWindowPositionBeforeAnalysis.X;
		var y = isOverlayMode ? settings.ReplayAnalysisY : _savedWindowPositionBeforeAnalysis.Y;

		Logger.Info(
			$"[ReplayAnalysis] Resizing window to {width}x{height} at ({x}, {y}), overlay mode: {isOverlayMode}");

		// Tell the results overlay whether it should be draggable
		_resultsOverlay?.SetDraggable(!isOverlayMode);

		// Use SetWindowPos to resize and reposition window
		var windowTitle = Window.Title;
		var handle = Process.GetCurrentProcess().MainWindowHandle;
		if (handle == IntPtr.Zero) handle = FindWindow(null, windowTitle);

		if (handle != IntPtr.Zero)
			SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

		_isInReplayAnalysisMode = true;
	}

	/// <summary>
	/// Exits replay analysis mode - restores original window size and position.
	/// </summary>
	private void ExitReplayAnalysisMode()
	{
		if (!_isInReplayAnalysisMode)
			return;

		Logger.Info("[ReplayAnalysis] Exiting replay analysis mode");

		// Restore original window state
		Logger.Info(
			$"[ReplayAnalysis] Restoring window to {_savedWindowSizeBeforeAnalysis.Width}x{_savedWindowSizeBeforeAnalysis.Height} at ({_savedWindowPositionBeforeAnalysis.X}, {_savedWindowPositionBeforeAnalysis.Y})");

		var windowTitle = Window.Title;
		var handle = Process.GetCurrentProcess().MainWindowHandle;
		if (handle == IntPtr.Zero) handle = FindWindow(null, windowTitle);

		if (handle != IntPtr.Zero)
			SetWindowPos(handle, IntPtr.Zero,
				_savedWindowPositionBeforeAnalysis.X, _savedWindowPositionBeforeAnalysis.Y,
				_savedWindowSizeBeforeAnalysis.Width, _savedWindowSizeBeforeAnalysis.Height,
				SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

		// Show main app content again
		_scaledContainer.FadeTo(1, 100);

		_isInReplayAnalysisMode = false;
	}

	/// <summary>
	/// Handles osu! window position changes for overlay mode.
	/// </summary>
	private void OnOsuWindowChanged(object? sender, Rectangle osuRect)
	{
		if (!_overlayService.IsOverlayMode || Window == null)
			return;

		UpdateOverlayPosition();
	}

	/// <summary>
	/// Handles overlay mode change requests from UI (settings panel).
	/// </summary>
	private void OnOverlayModeChangeRequested(object? sender, bool enabled)
	{
		Schedule(() =>
		{
			if (enabled)
			{
				// Only enable if osu! is running
				if (_processDetector.IsOsuRunning)
				{
					EnableOverlayMode();
					Logger.Info("[Overlay] Overlay mode enabled via settings");
				}
				else
				{
					Logger.Info("[Overlay] Overlay mode setting enabled - will activate when osu! starts");
				}
			}
			else
			{
				DisableOverlayMode();
				Logger.Info("[Overlay] Overlay mode disabled via settings");
			}
		});
	}

	/// <summary>
	/// Updates the overlay window position relative to osu! window.
	/// Only shows overlay if osu! or the overlay itself is in focus and user hasn't hidden it.
	/// </summary>
	private void UpdateOverlayPosition()
	{
		// Skip if in replay analysis mode - don't override its window position
		if (_isInReplayAnalysisMode)
			return;

		if (!_overlayService.IsOverlayMode || Window == null)
			return;

		// Respect user's manual hide via hotkey
		if (!_isWindowVisible)
			return;

		var windowTitle = Window.Title;
		var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

		if (handle == IntPtr.Zero)
			return;

		// Only show overlay if osu! or the overlay window is in focus
		// This prevents the overlay from hiding when clicked
		if (!_overlayService.IsOsuOrOverlayInFocus(handle))
		{
			// Hide overlay window when neither osu! nor overlay is in focus
			HideOverlayWindow();
			return;
		}

		var overlayPos = _overlayService.CalculateOverlayPosition(_currentTargetWidth, _currentTargetHeight);
		if (overlayPos.HasValue)
			// Set window to topmost and position it, enforcing size
			// Include SWP_FRAMECHANGED to ensure transparency is applied
			SetWindowPos(handle, HWND_TOPMOST, overlayPos.Value.X, overlayPos.Value.Y, _currentTargetWidth,
				_currentTargetHeight,
				SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_FRAMECHANGED);
	}

	/// <summary>
	/// Hides the overlay window when osu! is not in focus.
	/// </summary>
	private void HideOverlayWindow()
	{
		if (Window == null)
			return;

		try
		{
			var windowTitle = Window.Title;
			var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

			if (handle != IntPtr.Zero)
			{
				// Hide window but keep it topmost (so it appears instantly when osu! regains focus)
				const int SW_HIDE = 0;
				ShowWindow(handle, SW_HIDE);
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Overlay] Error hiding overlay window: {ex.Message}");
		}
	}

	/// <summary>
	/// Makes the window borderless (removes title bar) and enforces size - always applied.
	/// </summary>
	private void MakeWindowBorderless()
	{
		if (Window == null)
			return;

		var windowTitle = Window.Title;
		var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

		if (handle != IntPtr.Zero)
		{
			// Set borderless window style (no title bar, no border)
			var borderlessStyle = WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
			var result1 = SetWindowLong(handle, GWL_STYLE, borderlessStyle);
			if (result1 == 0)
			{
				Logger.Info($"[Window] Failed to set borderless style: {result1}");
				return;
			}

			// Apply style changes and enforce window size without changing position
			// SWP_NOMOVE prevents interfering with user's window placement
			var result2 = SetWindowPos(handle, IntPtr.Zero, 0, 0, _currentTargetWidth, _currentTargetHeight,
				SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
			if (!result2)
			{
				Logger.Info($"[Window] Failed to set window position: {result2}");
				return;
			}
		}
	}

	/// <summary>
	/// Brings the game window to the foreground and gives it focus.
	/// </summary>
	private void BringWindowToFocus()
	{
		if (Window == null)
			return;

		try
		{
			var windowTitle = Window.Title;
			var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

			if (handle != IntPtr.Zero)
			{
				// Show window and bring to foreground
				const int SW_SHOW = 5;
				ShowWindow(handle, SW_SHOW);
				SetForegroundWindow(handle);
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Focus] Error bringing window to focus: {ex.Message}");
		}
	}

	/// <summary>
	/// Enables overlay mode and positions window relative to osu!.
	/// </summary>
	private void EnableOverlayMode()
	{
		if (_overlayService.IsOverlayMode || Window == null)
			return;

		// Save current window position
		_savedWindowPosition = Window.Position;

		// Enable window transparency FIRST, before making borderless
		// This ensures the layered window style is set correctly
		var windowTitle = Window.Title;
		var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

		// Ensure window is borderless (should already be, but make sure)
		MakeWindowBorderless();

		// Force a final refresh to ensure everything is applied
		if (handle != IntPtr.Zero)
			SetWindowPos(handle, IntPtr.Zero, Window.Position.X, Window.Position.Y,
				Window.Size.Width, Window.Size.Height,
				SWP_NOZORDER | SWP_FRAMECHANGED);

		// Enable overlay mode
		_overlayService.IsOverlayMode = true;

		// Update position immediately
		UpdateOverlayPosition();

		Logger.Info("[Overlay] Overlay mode enabled - window following osu! (transparent)");
	}

	/// <summary>
	/// Disables overlay mode and restores window to saved position.
	/// Window remains borderless (no title bar).
	/// </summary>
	private void DisableOverlayMode()
	{
		if (!_overlayService.IsOverlayMode)
			return;

		// Disable overlay mode
		_overlayService.IsOverlayMode = false;

		// Restore saved window position and remove topmost flag
		// Window remains borderless (no title bar)
		if (Window != null)
		{
			var windowTitle = Window.Title;
			var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

			if (handle != IntPtr.Zero)
			{
				// Ensure window is still borderless
				MakeWindowBorderless();

				// Remove topmost flag and restore position, enforcing size
				SetWindowPos(handle, HWND_NOTOPMOST, _savedWindowPosition.X, _savedWindowPosition.Y,
					_currentTargetWidth, _currentTargetHeight,
					SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_FRAMECHANGED);
			}
		}

		Logger.Info("[Overlay] Overlay mode disabled - window restored to saved position (borderless)");
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy,
		uint uFlags);

	[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
	private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern int GetWindowLongPtr(IntPtr hWnd, int nIndex);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern int SetWindowLongPtr(IntPtr hWnd, int nIndex, int dwNewLong);

	[WinApiContext] private static readonly IntPtr HWND_TOPMOST = new(-1);
	[WinApiContext] private static readonly IntPtr HWND_NOTOPMOST = new(-2);

	[WinApiContext] private const int GWL_STYLE = -16;
	[WinApiContext] private const int WS_OVERLAPPEDWINDOW = unchecked((int)0x00CF0000);
	[WinApiContext] private const int WS_POPUP = unchecked((int)0x80000000);
	[WinApiContext] private const int WS_VISIBLE = unchecked((int)0x10000000);
	[WinApiContext] private const int WS_CLIPSIBLINGS = unchecked((int)0x04000000);
	[WinApiContext] private const int WS_CLIPCHILDREN = unchecked((int)0x02000000);

	[WinApiContext] private const uint SWP_NOMOVE = 0x0002;
	[WinApiContext] private const uint SWP_NOSIZE = 0x0001;
	[WinApiContext] private const uint SWP_NOZORDER = 0x0004;
	[WinApiContext] private const uint SWP_NOACTIVATE = 0x0010;
	[WinApiContext] private const uint SWP_SHOWWINDOW = 0x0040;
	[WinApiContext] private const uint SWP_FRAMECHANGED = 0x0020;

	// Window procedure subclassing
	[WinApiContext] private const int GWLP_WNDPROC = -4;
	[WinApiContext] private const int WM_ENTERSIZEMOVE = 0x0231;
	[WinApiContext] private const int WM_EXITSIZEMOVE = 0x0232;
	[WinApiContext] private const int WM_MOVING = 0x0216;
	[WinApiContext] private const int WM_WINDOWPOSCHANGING = 0x0046;

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam,
		IntPtr lParam);

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	[WinApiContext]
	private struct WINDOWPOS
	{
		public IntPtr Hwnd;
		public IntPtr HwndInsertAfter;
		public int X;
		public int Y;
		public int Cx;
		public int Cy;
		public uint Flags;
	}


	/// <summary>
	/// Installs a window procedure hook to track dragging and prevent unwanted position changes.
	/// </summary>
	private void InstallWindowHook()
	{
		if (Window == null)
			return;

		var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(Window.Title);
		if (handle == IntPtr.Zero)
			return;

		// Store the original window procedure
		_originalWndProc = GetWindowLongPtrW(handle, GWLP_WNDPROC);
		if (_originalWndProc == IntPtr.Zero)
			return;

		// Create delegate and prevent garbage collection
		_wndProcDelegate = CustomWndProc;
		var newWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

		// Install our custom window procedure
		SetWindowLongPtrW(handle, GWLP_WNDPROC, newWndProc);

		// Initialize last known position
		if (GetWindowRect(handle, out var rect)) _lastKnownPosition = new Point(rect.Left, rect.Top);

		Logger.Info("[WindowHook] Window procedure hook installed");
	}

	/// <summary>
	/// Custom window procedure that tracks drag operations and prevents unwanted position changes.
	/// </summary>
	private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		switch (msg)
		{
			case WM_ENTERSIZEMOVE:
				// User started dragging or resizing
				_isDragging = true;
				// Record position at start of drag
				if (GetWindowRect(hWnd, out var startRect))
					_lastKnownPosition = new Point(startRect.Left, startRect.Top);

				break;

			case WM_EXITSIZEMOVE:
				// User finished dragging or resizing
				_isDragging = false;
				// Record the final position after drag completes
				if (GetWindowRect(hWnd, out var endRect)) _lastKnownPosition = new Point(endRect.Left, endRect.Top);

				break;

			case WM_WINDOWPOSCHANGING:
				// Window position is about to change
				// Only intervene when NOT dragging and NOT in overlay mode
				if (!_isDragging && _overlayService?.IsOverlayMode != true && !_isInReplayAnalysisMode)
					unsafe
					{
						var pos = (WINDOWPOS*)lParam;

						// Check if this is an external position change (not initiated by us)
						// If the position is different from last known and SWP_NOMOVE is not set
						if ((pos->Flags & 0x0002) == 0) // SWP_NOMOVE not set, so position is being changed
														// Get current actual position
							if (GetWindowRect(hWnd, out var currentRect))
							{
								// If current position matches what we expect, but new position is different,
								// this might be an unwanted correction - prevent it
								var deltaX = Math.Abs(pos->X - currentRect.Left);
								var deltaY = Math.Abs(pos->Y - currentRect.Top);

								// If position is changing significantly without user dragging, block it
								if (deltaX > 10 || deltaY > 10)
								{
									// Force the position to stay at current location
									pos->X = currentRect.Left;
									pos->Y = currentRect.Top;
									pos->Flags |= 0x0002; // Add SWP_NOMOVE flag
								}
							}
					}

				break;
		}

		// Call the original window procedure
		return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
	}

	/// <summary>
	/// Handles the toggle visibility hotkey press.
	/// Only toggles visibility when in overlay mode.
	/// </summary>
	private void OnToggleVisibilityHotkeyPressed(object? sender, EventArgs e)
	{
		// Only toggle visibility when in overlay mode
		if (_overlayService?.IsOverlayMode == true)
		{
			// Enforce cooldown to prevent rapid toggling
			var timeSinceLastToggle = (DateTimeOffset.Now - _lastOverlayToggleTime).TotalMilliseconds;
			if (timeSinceLastToggle < _overlayToggleCooldownMs) return;

			_lastOverlayToggleTime = DateTimeOffset.Now;
			ToggleWindowVisibility();
		}
	}

	/// <summary>
	/// Toggles window visibility using Windows API.
	/// </summary>
	public void ToggleWindowVisibility()
	{
		if (Window == null)
			return;

		try
		{
			var windowTitle = Window.Title;
			var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

			if (handle != IntPtr.Zero)
			{
				_isWindowVisible = !_isWindowVisible;

				// Use Windows API to show/hide window
				const int SW_HIDE = 0;
				const int SW_SHOW = 5;

				ShowWindow(handle, _isWindowVisible ? SW_SHOW : SW_HIDE);
				Logger.Info(
					$"[Overlay] Overlay visibility toggled via hotkey: {(_isWindowVisible ? "Visible" : "Hidden")}");
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[Overlay] Error toggling window visibility: {ex.Message}");
		}
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	protected override void Update()
	{
		base.Update();

		// Check osu! process status and auto-enable/disable overlay mode
		CheckOsuProcessStatus();

		// Update overlay service
		_overlayService?.Update();

		// Update overlay position if in overlay mode
		if (_overlayService?.IsOverlayMode == true) UpdateOverlayPosition();

		// Periodically save window settings and prevent resizing using osu!framework API only
		if (Window != null)
		{
			_settingsSaveTimer += Clock.ElapsedFrameTime;
			if (_settingsSaveTimer >= _settingsSaveInterval)
			{
				_settingsSaveTimer = 0;

				// Prevent resizing by ensuring window state stays Normal (using osu!framework API)
				if (Window.WindowState != WindowState.Normal) Window.WindowState = WindowState.Normal;

				// Enforce window size using Windows API (skip if in replay analysis mode)
				if (!_isInReplayAnalysisMode)
				{
					var currentSize = Window.Size;
					if (currentSize.Width != _currentTargetWidth || currentSize.Height != _currentTargetHeight)
					{
						// Window was resized - force it back to target size
						Window.WindowState = WindowState.Normal;

						var windowTitle = Window.Title;
						var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
						if (handle != IntPtr.Zero)
							// Use SWP_NOMOVE to only change size, not position
							// This prevents interfering with user's window placement when moving between monitors
							SetWindowPos(handle, IntPtr.Zero, 0, 0, _currentTargetWidth, _currentTargetHeight,
								SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
					}
				}

				// Only save window position if not in overlay mode and not currently restoring
				if (!_overlayService?.IsOverlayMode == true && !_isRestoringWindowSettings)
					// Check if window size or position changed for saving
					if (Window.Size.Width != _lastWindowSize.Width ||
						Window.Size.Height != _lastWindowSize.Height ||
						Window.Position.X != _lastWindowPosition.X ||
						Window.Position.Y != _lastWindowPosition.Y)
						SaveWindowSettings();
			}
		}
	}

	/// <summary>
	/// Checks osu! process status and automatically enables/disables overlay mode.
	/// </summary>
	private void CheckOsuProcessStatus()
	{
		if (!_replayFileWatcherService.IsWatching && !string.IsNullOrEmpty(_processDetector.GetOsuDirectory()))
			_replayFileWatcherService.StartWatching();
		var isOsuRunning = _processDetector.IsOsuRunning;

		// osu! just started
		if (isOsuRunning && !_wasOsuRunning)
		{
			// Try to attach to osu! process
			if (_processDetector.TryAttachToOsu())
			{
				var processInfo = _processDetector.GetProcessInfo();
				if (processInfo != null)
					try
					{
						var osuProcess = Process.GetProcessById(processInfo.ProcessId);
						_overlayService.AttachToOsu(osuProcess);

						// Enable overlay mode only if user has it enabled in settings
						if (_userSettingsService.Settings.OverlayMode)
						{
							EnableOverlayMode();
							Logger.Info("[Overlay] osu! detected - overlay mode enabled");
						}
						else
						{
							Logger.Info("[Overlay] osu! detected - overlay mode disabled (per user setting)");
						}

						// Perform one-time restart after first connection for proper attachment
						if (!_hasPerformedStartupRestart)
						{
							PerformFirstConnectionRestart();
							return; // Exit early, we'll re-detect after restart
						}
					}
					catch (Exception ex)
					{
						Logger.Info($"[Overlay] Failed to attach to osu! process: {ex.Message}");
					}
			}
		}
		// osu! just closed
		else if (!isOsuRunning && _wasOsuRunning)
		{
			// Disable overlay mode and restore window position
			DisableOverlayMode();
			_overlayService.AttachToOsu(null);

			Logger.Info("[Overlay] osu! closed - overlay mode disabled");
		}
		// osu! is running but overlay service lost the process
		else if (isOsuRunning && _overlayService != null)
		{
			// Re-attach if needed
			var osuRect = _overlayService.GetOsuWindowRect();
			if (!osuRect.HasValue && _overlayService.IsOverlayMode)
				// Process might have been lost, try to reattach
				if (_processDetector.TryAttachToOsu())
				{
					var processInfo = _processDetector.GetProcessInfo();
					if (processInfo != null)
						try
						{
							var osuProcess = Process.GetProcessById(processInfo.ProcessId);
							_overlayService.AttachToOsu(osuProcess);
						}
						catch
						{
							// Process might be closing, ignore
						}
				}
		}

		_wasOsuRunning = isOsuRunning;
	}

	private void OnWindowFileDrop(string file)
	{
		if (file.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
		{
			Schedule(() => _mainScreen?.HandleFileDrop(file));
		}
		else if (file.EndsWith(".osr", StringComparison.OrdinalIgnoreCase))
		{
			// Handle replay file drop - trigger replay analysis
			HandleReplayFileDrop(file);
		}
	}

	/// <summary>
	/// Handles a dropped .osr replay file by finding the beatmap and opening replay analysis.
	/// </summary>
	private void HandleReplayFileDrop(string replayPath)
	{
		Logger.Info($"[FileDrop] Replay file dropped: {replayPath}");

		// Run the analysis in background to avoid blocking UI
		Task.Run(() =>
		{
			try
			{
				// Parse the replay file
				var replay = ReplayParserService.ParseReplay(replayPath);
				if (replay == null)
				{
					Logger.Info("[FileDrop] Failed to parse replay file");
					return;
				}

				// Find the corresponding beatmap by MD5 hash
				// First try fast lookup from maps.db
				var beatmapPath = _mapsDatabaseService.GetBeatmapPathByHash(replay.BeatmapMD5Hash);

				// Fall back to scanning songs folder if not found in maps.db
				if (string.IsNullOrEmpty(beatmapPath))
				{
					Logger.Info($"[FileDrop] Beatmap not in maps.db, scanning songs folder...");
					beatmapPath = _replayParserService.FindBeatmapByHash(replay.BeatmapMD5Hash);
				}

				if (string.IsNullOrEmpty(beatmapPath))
				{
					Logger.Info($"[FileDrop] Could not find beatmap for replay (hash: {replay.BeatmapMD5Hash})");
					Schedule(() =>
					{
						_resultsOverlay?.ShowError("Beatmap not found in Songs folder");
						EnterReplayAnalysisMode();
					});
					return;
				}

				Logger.Info($"[FileDrop] Found beatmap: {beatmapPath}");

				// Get key count from beatmap for extracting key events
				var osuFile = OsuFileParser.Parse(beatmapPath);
				var keyCount = (int)osuFile.CircleSize;

				// Extract key events from replay
				var keyEvents = ReplayParserService.ExtractManiaKeyEvents(replay, keyCount);

				if (keyEvents.Count == 0)
				{
					Logger.Info("[FileDrop] No key events in replay");
					Schedule(() =>
					{
						_resultsOverlay?.ShowError("No timing data in replay");
						EnterReplayAnalysisMode();
					});
					return;
				}

				// Get rate and mirror mods from replay
				var rate = ReplayParserService.GetRateFromMods(replay);
				var hasMirror = ReplayParserService.HasMirrorMod(replay);

				Logger.Info(
					$"[FileDrop] Replay: {replay.PlayerName}, rate: {rate}x, mirror: {hasMirror}, key events: {keyEvents.Count}");

				// Calculate timing deviations
				var analysis = TimingDeviationCalculator.CalculateDeviations(beatmapPath, keyEvents, rate, hasMirror);

				// Show results on UI thread
				Schedule(() =>
				{
					EnterReplayAnalysisMode();

					if (analysis.Success)
					{
						Logger.Info(
							$"[FileDrop] Analysis complete: UR={analysis.UnstableRate:F2}, Mean={analysis.MeanDeviation:F2}ms");
						_resultsOverlay?.ShowData(analysis);
					}
					else
					{
						Logger.Info($"[FileDrop] Analysis failed: {analysis.ErrorMessage}");
						_resultsOverlay?.ShowError(analysis.ErrorMessage ?? "Analysis failed");
					}
				});
			}
			catch (Exception ex)
			{
				Logger.Info($"[FileDrop] Error processing replay: {ex.Message}");
				Schedule(() =>
				{
					EnterReplayAnalysisMode();
					_resultsOverlay?.ShowError($"Error: {ex.Message}");
				});
			}
		});
	}

	/// <summary>
	/// Registers all available mods with the ModService.
	/// </summary>
	private void RegisterMods()
	{
		// Register built-in mods
		// _modService.RegisterMod(new ExampleMod());
		_modService.RegisterMod(new NoLNMod());
		_modService.RegisterMod(new FullLNMod());
		_modService.RegisterMod(new NormalizeSvMod());
		_modService.RegisterMod(new ReverseMod());
		_modService.RegisterMod(new ChordjackifyMod());

		Logger.Info($"[ModService] Registered {_modService.GetAllMods().Count} mods");
	}

	protected override void Dispose(bool isDisposing)
	{
		if (!isDisposing)
		{
			base.Dispose(isDisposing);
			return;
		}

		if (Window != null)
		{
			Window.DragDrop -= OnWindowFileDrop;
			Window.WindowStateChanged -= OnWindowStateChanged;

			// Save settings before closing
			SaveWindowSettings();
		}

		// Auto-end session on exit if enabled in settings and session is active
		if (_userSettingsService?.Settings.AutoEndSession == true && _sessionTrackerService?.IsTracking == true)
		{
			_sessionTrackerService.StopSession();
			Logger.Info("[Session] Auto-ended session on exit");
		}

		// Unsubscribe from tray icon events
		if (_trayIconService != null)
		{
			_trayIconService.CheckForUpdatesRequested -= OnTrayCheckForUpdatesRequested;
			_trayIconService.ExitRequested -= OnTrayExitRequested;
		}

		// Unsubscribe from session tracker events
		if (_sessionTrackerService != null) _sessionTrackerService.PlayEndedWithPauses -= OnPlayEndedWithPauses;

		_maniaTrackerService?.Dispose();
		_processDetector?.Dispose();
		_overlayService?.Dispose();
		_hotkeyService?.Dispose();
		_autoUpdaterService?.Dispose();
		_sessionTrackerService?.Dispose();
		_sessionDatabaseService?.Dispose();
		_mapsDatabaseService?.Dispose();
		_replayFileWatcherService?.Dispose();
		_trayIconService?.Dispose();
		_aptabaseService?.Dispose();
		_beatmapApiService?.Dispose();
		_resultsOverlay?.Dispose();
		_quitConfirmationDialog?.Dispose();
		_startupRestartDialog?.Dispose();
		_screenStack?.Dispose();
		_scaledContainer?.Dispose();
		_mainScreen?.Dispose();
		base.Dispose(isDisposing);
	}
}

/// <summary>
/// Resource store that reads from assembly manifest resources with proper path conversion.
/// </summary>
public sealed class AssemblyResourceStore : IResourceStore<byte[]>
{
	private readonly System.Reflection.Assembly _assembly;
	private readonly string _namespacePrefix;
	private readonly Dictionary<string, string> _pathToManifest = new();

	public AssemblyResourceStore(System.Reflection.Assembly assembly, string namespacePrefix)
	{
		_assembly = assembly;
		_namespacePrefix = namespacePrefix;

		// Build lookup from path-style names to manifest names
		foreach (var manifestName in assembly.GetManifestResourceNames())
			if (manifestName.StartsWith(namespacePrefix + ".", StringComparison.Ordinal))
			{
				// Convert manifest name to path: Companella.Resources.Fonts.Noto.file.bin -> Resources/Fonts/Noto/file.bin
				var withoutPrefix = manifestName.Substring(namespacePrefix.Length + 1);
				var pathStyle = ConvertManifestToPath(withoutPrefix);
				_pathToManifest[pathStyle] = manifestName;
			}
	}

	private static string ConvertManifestToPath(string manifestName)
	{
		// Find the last dot before the extension
		var lastDot = manifestName.LastIndexOf('.');
		if (lastDot <= 0)
			return manifestName.Replace('.', '/');

		// Everything before the extension uses dots for directories
		var pathPart = manifestName.Substring(0, lastDot);
		var extension = manifestName.Substring(lastDot);

		return pathPart.Replace('.', '/') + extension;
	}

	public byte[] Get(string name)
	{
		using var stream = GetStream(name);
		if (stream == null)
			return Array.Empty<byte>();

		using var ms = new MemoryStream();
		stream.CopyTo(ms);
		return ms.ToArray();
	}

	public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(Get(name));
	}

	public Stream? GetStream(string name)
	{
		// Try direct lookup
		if (_pathToManifest.TryGetValue(name, out var manifestName))
			return _assembly.GetManifestResourceStream(manifestName);

		// Try with namespace prefix prepended
		var fullPath = _namespacePrefix + "/" + name;
		if (_pathToManifest.TryGetValue(fullPath, out manifestName))
			return _assembly.GetManifestResourceStream(manifestName);

		return null;
	}

	public IEnumerable<string> GetAvailableResources()
	{
		return _pathToManifest.Keys;
	}

	public void Dispose()
	{
	}
}
