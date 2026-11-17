using System.Diagnostics;
using System.Reflection;
using Companella.Components.Analysis;
using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Components.Settings;
using Companella.Components.Tools;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Models.Session;
using Companella.Models.Training;
using Companella.Mods;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Screenshot;
using Companella.Services.Session;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace Companella.Screens;

/// <summary>
/// Main screen of the Companella! application.
/// </summary>
public partial class MainScreen : osu.Framework.Screens.Screen
{
	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	[Resolved] private OsuFileParser FileParser { get; set; } = null!;

	[Resolved] private OsuWindowOverlayService OverlayService { get; set; } = null!;

	[Resolved] private ModService ModService { get; set; } = null!;

	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	[Resolved] private SquirrelUpdaterService AutoUpdaterService { get; set; } = null!;

	[Resolved] private AptabaseService AptabaseService { get; set; } = null!;

	[Resolved] private SessionDatabaseService SessionDatabaseService { get; set; } = null!;

	[Resolved] private SessionTrackerService SessionTrackerService { get; set; } = null!;

	[Resolved] private osu.Framework.Platform.GameHost Host { get; set; } = null!;

	[Resolved] private MapsDatabaseService MapsDatabaseService { get; set; } = null!;

	[Resolved] private ScoreImportService ScoreImportService { get; set; } = null!;

	[Resolved] private ReplayFileWatcherService ReplayFileWatcherService { get; set; } = null!;

	[Resolved] private OsuCollectionService OsuCollectionService { get; set; } = null!;

	// Header components
	private MapInfoDisplay _mapInfoDisplay = null!;

	// Tab container
	private TabContainer _tabContainer = null!;

	// Gameplay tab components
	private RateChangerPanel _rateChangerPanel = null!;
	private ModSelectionPanel _modSelectionPanel = null!;
	private SessionPanel _sessionPanel = null!;
	private SkillsAnalysisPanel _skillsAnalysisPanel = null!;
	private SessionPlannerPanel _sessionPlannerPanel = null!;

	// Mapping tab components
	private FunctionButtonPanel _functionPanel = null!;
	private OffsetInputPanel _offsetPanel = null!;
	private BulkRateChangerPanel _bulkRateChangerPanel = null!;
	private MarathonCreatorPanel _marathonCreatorPanel = null!;
	private MapPackManagerPanel _mapPackManagerPanel = null!;
	private DifficultySplitterPanel _difficultySplitterPanel = null!;

	// Split tab containers for tutorial navigation
	private SplitTabContainer _gameplaySplitContainer = null!;
	private SplitTabContainer _mappingSplitContainer = null!;

	private AppFooter _appFooter = null!;
	private LoadingOverlay _loadingOverlay = null!;

	// Update dialog
	private UpdateDialog _updateDialog = null!;

	// Preset edit dialog
	private PresetEditDialog _presetEditDialog = null!;

	// Dan rating dialog
	private DanRatingDialog _danRatingDialog = null!;
	private DanRatingSubmissionService _danRatingSubmissionService = null!;

	// Confirmation dialog
	private ConfirmationDialog _confirmationDialog = null!;

	// osu! restart dialog
	private OsuRestartDialog _osuRestartDialog = null!;

	// Tutorial overlay for first launch
	private TutorialOverlay _tutorialOverlay = null!;

	// Tutorial tab panel
	private TutorialPanel _tutorialPanel = null!;

	// Quick setup panel in settings
	private QuickSetupPanel _quickSetupPanel = null!;

	// Window decoration
	private CustomTitleBar _titleBar = null!;
	private ManiaTrackerAccountMenu _trackerAccountMenu = null!;
	private ChainedScrollContainer _settingsScroll = null!;
	private ManiaTrackerSettingsPanel _trackerSettings = null!;

	// Background box for transparency control
	private Box _backgroundBox = null!;

	private OsuFile? _currentOsuFile;
	private string? _lastDetectedBeatmap;
	private double _beatmapCheckTimer;
	private const double _beatmapCheckInterval = 1000; // Check every 1 second

	// Store BPM factor for use in background task
	private BpmFactor _pendingBpmFactor;

	[BackgroundDependencyLoader]
	private void load()
	{
		// Create tab contents
		var gameplayTabContent = CreateGameplayTab();
		var mappingTabContent = CreateMappingTab();
		var settingsTabContent = CreateSettingsTab();
		var tutorialTabContent = CreateTutorialTab();

		InternalChildren = new Drawable[]
		{
			// Dark background (transparent in overlay mode) - must be first (behind everything)
			_backgroundBox = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(25, 25, 30, 255)
			},
			// Main layout using GridContainer for proportional sizing
			new GridContainer
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding
				{
					Left = 15, Right = 15, Top = 47, Bottom = 43
				}, // Top padding for title bar, bottom padding for footer
				RowDimensions = new[]
				{
					new Dimension(GridSizeMode.Relative, 0.4f), // Map info header (2.5x bigger)
					new Dimension(GridSizeMode.Relative, 0.6f) // Tab container (takes remaining space)
				},
				Content = new[]
				{
					new Drawable[]
					{
						_mapInfoDisplay = new MapInfoDisplay
						{
							RelativeSizeAxes = Axes.Both
						}
					},
					new Drawable[]
					{
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Padding = new MarginPadding { Top = 10, Bottom = 10 },
							Child = _tabContainer = new TabContainer(
								["Gameplay", "Mapping", "Settings", "Tutorial"],
								new[] { gameplayTabContent, mappingTabContent, settingsTabContent, tutorialTabContent })
							{
								RelativeSizeAxes = Axes.Both
							}
						}
					}
				}
			},
			// App footer (bottom of screen)
			_appFooter = new AppFooter(),
			// Custom osu!-styled title bar (on top of content, visible in non-overlay mode)
			_titleBar = new CustomTitleBar
			{
				Anchor = Anchor.TopLeft,
				Origin = Anchor.TopLeft,
				Alpha = 1f // Visible by default, will be hidden in overlay mode
			},
			_trackerAccountMenu = new ManiaTrackerAccountMenu { ManageAccountRequested = OpenTrackerSettings },
			// Loading overlay (on top of everything)
			_loadingOverlay = new LoadingOverlay(),
			// Update dialog (topmost)
			_updateDialog = new UpdateDialog(),
			// Preset edit dialog (topmost)
			_presetEditDialog = new PresetEditDialog(),
			// Dan rating dialog (topmost)
			_danRatingDialog = new DanRatingDialog(),
			// Confirmation dialog (topmost)
			_confirmationDialog = new ConfirmationDialog(),
			// osu! restart dialog (topmost)
			_osuRestartDialog = new OsuRestartDialog(),
			// Tutorial overlay (absolutely topmost for first launch)
			_tutorialOverlay = new TutorialOverlay()
		};

		// Initialize dan rating submission service
		_danRatingSubmissionService = new DanRatingSubmissionService();

		// Wire up events
		//_dropZone.FileDropped += OnFileDropped;
		_titleBar.ScreenshotRequested += OnScreenshotRequested;
		_titleBar.CloseRequested += OnCloseRequested;
		_functionPanel.AnalyzeBpmClicked += OnAnalyzeBpmClicked;
		_functionPanel.NormalizeSvClicked += OnNormalizeSvClicked;
		_offsetPanel.ApplyOffsetClicked += OnApplyOffsetClicked;
		_rateChangerPanel.ApplyRateClicked += OnApplyRateClicked;
		_rateChangerPanel.PreviewRequested += OnRatePreviewRequested;
		_rateChangerPanel.FormatChanged += OnRateChangerFormatChanged;
		_rateChangerPanel.PitchAdjustChanged += OnRateChangerPitchAdjustChanged;
		_bulkRateChangerPanel.ApplyBulkRateClicked += OnApplyBulkRateClicked;
		_bulkRateChangerPanel.FormatChanged += OnRateChangerFormatChanged;
		_bulkRateChangerPanel.PitchAdjustChanged += OnBulkRateChangerPitchAdjustChanged;
		_bulkRateChangerPanel.PresetEditRequested += OnPresetEditRequested;
		_presetEditDialog.PresetSaved += OnPresetSaved;
		_danRatingDialog.RatingSubmitted += OnDanRatingSubmitted;
		SessionTrackerService.PlayRecorded += OnPlayRecordedForDanRating;
		_tabContainer.TabChanged += OnTabChanged;
		_tutorialOverlay.TutorialCompleted += OnTutorialCompleted;
		_tutorialOverlay.MainTabSwitchRequested += OnTutorialMainTabSwitchRequested;
		_tutorialOverlay.SplitTabSwitchRequested += OnTutorialSplitTabSwitchRequested;
		_tutorialOverlay.QuickSetupRequested += OnQuickSetupRequested;

		// Restore saved rate changer format to both panels
		var savedFormat = UserSettingsService.Settings.RateChangerFormat;
		if (!string.IsNullOrWhiteSpace(savedFormat))
		{
			_rateChangerPanel.SetFormat(savedFormat);
			_bulkRateChangerPanel.SetFormat(savedFormat);
		}

		// Restore saved pitch adjust setting to both panels
		var savedPitchAdjust = UserSettingsService.Settings.RateChangerPitchAdjust;
		_rateChangerPanel.PitchAdjust = savedPitchAdjust;
		_bulkRateChangerPanel.PitchAdjust = savedPitchAdjust;

		// Try to attach to osu! process
		TryAttachToOsu();

		// Check for updates in background
		CheckForUpdatesAsync();

		// Check if this is the first launch and show tutorial
		CheckFirstLaunchAsync();
	}

	private async void CheckForUpdatesAsync()
	{
		try
		{
			// Small delay to let the UI settle
			await Task.Delay(2000);

			var updateInfo = await AutoUpdaterService.CheckForUpdatesAsync();

			// Track analytics
			AptabaseService.TrackUpdateCheck(updateInfo != null);

			if (updateInfo != null) Schedule(() => { _updateDialog.Show(updateInfo, AutoUpdaterService); });
		}
		catch (Exception ex)
		{
			Logger.Info($"[MainScreen] Update check failed: {ex.Message}");
		}
	}

	private async void CheckFirstLaunchAsync()
	{
		try
		{
			// Wait a bit for the UI to settle and splash screen to close
			await Task.Delay(1500);

			// Check if this is the first launch
			if (!UserSettingsService.Settings.HasCompletedFirstLaunch)
				Schedule(() =>
				{
					Logger.Info("[MainScreen] First launch detected - showing tutorial");
					_tutorialOverlay.Show();
				});
		}
		catch (Exception ex)
		{
			Logger.Info($"[MainScreen] First launch check failed: {ex.Message}");
		}
	}

	private async void OnTutorialCompleted()
	{
		try
		{
			// Mark tutorial as completed
			UserSettingsService.Settings.HasCompletedFirstLaunch = true;
			await UserSettingsService.SaveAsync();
			Logger.Info("[MainScreen] Tutorial completed - saved first launch flag");
		}
		catch (Exception ex)
		{
			Logger.Info($"[MainScreen] Failed to save tutorial completion: {ex.Message}");
		}
	}

	private void OnTutorialMainTabSwitchRequested(int tabIndex)
	{
		// Switch to the requested main tab during tutorial
		Schedule(() => { _tabContainer.SelectTab(tabIndex); });
	}

	private void OnTutorialSplitTabSwitchRequested(int mainTabIndex, int splitTabIndex)
	{
		// Switch to the requested split tab within a main tab during tutorial
		Schedule(() =>
		{
			// First ensure we're on the correct main tab
			if (_tabContainer.SelectedIndex != mainTabIndex) _tabContainer.SelectTab(mainTabIndex);

			// Then switch the split tab within that main tab
			switch (mainTabIndex)
			{
				case 0: // Gameplay tab
					_gameplaySplitContainer?.SelectItem(splitTabIndex);
					break;
				case 1: // Mapping tab
					_mappingSplitContainer?.SelectItem(splitTabIndex);
					break;
					// Settings tab (2) doesn't have split tabs
			}
		});
	}

	private async void OnQuickSetupRequested()
	{
		Logger.Info("[MainScreen] Quick Setup requested - starting Index, Import, and Find Replays");

		// Show loading overlay
		Schedule(() => _loadingOverlay.Show("Running Quick Setup..."));

		var scoresImported = 0;
		var replaysFound = 0;

		await Task.Run(async () =>
		{
			try
			{
				// Step 1: Index Maps
				var songsFolder = ProcessDetector.GetSongsFolder();
				if (!string.IsNullOrEmpty(songsFolder))
				{
					Schedule(() => _loadingOverlay.UpdateStatus("Indexing beatmaps..."));
					await MapsDatabaseService.ScanOsuSongsFolderAsync(songsFolder, CancellationToken.None);
					Logger.Info("[MainScreen] Map indexing completed");
				}
				else
				{
					Logger.Info("[MainScreen] No songs folder found, skipping indexing");
				}

				// Step 2: Import Scores
				Schedule(() => _loadingOverlay.UpdateStatus("Importing scores..."));
				var importResult = ScoreImportService.ImportScoresAsSessions(progress =>
				{
					Schedule(() => _loadingOverlay.UpdateStatus($"Importing: {progress.Stage}"));
				});
				scoresImported = importResult.PlaysImported;
				Logger.Info(
					$"[MainScreen] Imported {scoresImported} plays from {importResult.SessionsCreated} sessions");

				// Step 3: Find Missing Replays
				Schedule(() => _loadingOverlay.UpdateStatus("Finding missing replays..."));
				replaysFound = ReplayFileWatcherService.FindAllMissingReplays((matched, total) =>
				{
					Schedule(() => _loadingOverlay.UpdateStatus($"Found {matched} replays..."));
				});
				Logger.Info($"[MainScreen] Found {replaysFound} replays");

				Schedule(() =>
				{
					_loadingOverlay.UpdateStatus(
						$"Done! Scores: {scoresImported}, Replays: {replaysFound}\nRestarting Companella...");
				});

				// Show completion message for a moment before restarting
				await Task.Delay(2000);

				Schedule(() =>
				{
					Logger.Info("[MainScreen] Quick Setup completed successfully, restarting application");
					RestartApplication();
				});
			}
			catch (Exception ex)
			{
				Logger.Info($"[MainScreen] Quick Setup failed: {ex.Message}");
				Schedule(() => { _loadingOverlay.UpdateStatus($"Error: {ex.Message}"); });
				await Task.Delay(3000);
				Schedule(() => _loadingOverlay.Hide());
			}
		});
	}

	private void RestartApplication()
	{
		try
		{
			// Get the current executable path
			var exePath = Environment.ProcessPath;

			if (string.IsNullOrEmpty(exePath))
				// Fallback for older .NET or special deployment scenarios
				exePath = Assembly.GetExecutingAssembly().Location;

			if (string.IsNullOrEmpty(exePath))
			{
				Logger.Info("[MainScreen] Could not determine executable path for restart");
				_loadingOverlay.Hide();
				return;
			}

			Logger.Info($"[MainScreen] Restarting application from: {exePath}");

			// Start new instance
			Process.Start(new ProcessStartInfo
			{
				FileName = exePath,
				UseShellExecute = true,
				WorkingDirectory = Path.GetDirectoryName(exePath)
			});

			// Exit current instance
			Host.Exit();
		}
		catch (Exception ex)
		{
			Logger.Info($"[MainScreen] Failed to restart application: {ex.Message}");
			_loadingOverlay.Hide();
		}
	}

	private Container CreateGameplayTab()
	{
		// Create panels for gameplay features (auto-sized)
		_rateChangerPanel = new RateChangerPanel();
		_modSelectionPanel = new ModSelectionPanel();
		_sessionPanel = new SessionPanel();
		_skillsAnalysisPanel = new SkillsAnalysisPanel();
		_sessionPlannerPanel = new SessionPlannerPanel();

		// Wire up mod selection panel
		_modSelectionPanel.ApplyModClicked += OnApplyModClicked;

		// Wire up session panel for replay and beatmap analysis
		_sessionPanel.ReplayAnalysisRequested += OnReplayAnalysisRequested;
		_sessionPanel.BeatmapAnalysisRequested += OnBeatmapAnalysisRequested;

		// Wire up skills analysis panel to load recommended maps
		_skillsAnalysisPanel.MapSelected += OnRecommendedMapSelected;

		// Wire up loading overlay events from recommendation panel
		_skillsAnalysisPanel.LoadingStarted += status => { Schedule(() => _loadingOverlay.Show(status)); };
		_skillsAnalysisPanel.LoadingStatusChanged += status =>
		{
			Schedule(() => _loadingOverlay.UpdateStatus(status));
		};
		_skillsAnalysisPanel.LoadingFinished += () => { Schedule(() => _loadingOverlay.Hide()); };

		// Wire up restart dialog request from skills analysis panel
		_skillsAnalysisPanel.RestartOsuRequested += OnRestartOsuRequested;

		// Wire up loading overlay events from session planner panel
		_sessionPlannerPanel.LoadingStarted += status => { Schedule(() => _loadingOverlay.Show(status)); };
		_sessionPlannerPanel.LoadingStatusChanged += status =>
		{
			Schedule(() => _loadingOverlay.UpdateStatus(status));
		};
		_sessionPlannerPanel.LoadingFinished += () => { Schedule(() => _loadingOverlay.Hide()); };

		// Pass skill trends from analysis to session planner
		_skillsAnalysisPanel.TrendsUpdated += trends => { _sessionPlannerPanel.SetTrends(trends); };

		_gameplaySplitContainer = new SplitTabContainer(new[]
		{
			new SplitTabItem("Rate Changer", _rateChangerPanel),
			new SplitTabItem("Mods", _modSelectionPanel),
			new SplitTabItem("Session", _sessionPanel),
			new SplitTabItem("Skills Analysis", _skillsAnalysisPanel),
			new SplitTabItem("Session Planner", _sessionPlannerPanel)
		})
		{
			RelativeSizeAxes = Axes.Both
		};

		return new Container
		{
			RelativeSizeAxes = Axes.Both,
			Child = _gameplaySplitContainer
		};
	}

	/// <summary>
	/// Handles replay analysis requests from the session panel.
	/// </summary>
	private void OnReplayAnalysisRequested(StoredSessionPlay play)
	{
		if (string.IsNullOrEmpty(play.ReplayPath) || !File.Exists(play.ReplayPath))
		{
			Logger.Info("[MainScreen] Replay file not available for analysis");
			return;
		}

		Logger.Info($"[MainScreen] Replay analysis requested: {play.ReplayPath}");

		// Raise event for CompanellaGame to handle
		ReplayAnalysisRequested?.Invoke(play.ReplayPath);
	}

	/// <summary>
	/// Handles beatmap analysis requests from the session panel (right-click).
	/// </summary>
	private void OnBeatmapAnalysisRequested(StoredSessionPlay play)
	{
		if (string.IsNullOrEmpty(play.BeatmapPath) || !File.Exists(play.BeatmapPath))
		{
			Logger.Info("[MainScreen] Beatmap file not available for analysis");
			return;
		}

		Logger.Info($"[MainScreen] Beatmap analysis requested: {play.BeatmapPath}");

		// Load the beatmap as if it was dropped
		HandleFileDrop(play.BeatmapPath);

		// Apply DT/HT mods if the play was done with rate mods
		if (Math.Abs(play.Rate - 1.0f) > 0.01f)
		{
			Logger.Info($"[MainScreen] Applying rate {play.Rate:F2}x from session play");
			_rateChangerPanel.SetRate(play.Rate);
		}
	}

	/// <summary>
	/// Event raised when replay analysis is requested.
	/// </summary>
	public event Action<string>? ReplayAnalysisRequested;

	private Container CreateMappingTab()
	{
		// Create panels for mapping features (auto-sized via constructors)
		_functionPanel = new FunctionButtonPanel();
		_offsetPanel = new OffsetInputPanel();
		_bulkRateChangerPanel = new BulkRateChangerPanel();
		_marathonCreatorPanel = new MarathonCreatorPanel();
		_mapPackManagerPanel = new MapPackManagerPanel();
		_difficultySplitterPanel = new DifficultySplitterPanel();

		// Wire up marathon creator events
		_marathonCreatorPanel.CreateMarathonRequested += OnCreateMarathonRequested;
		_marathonCreatorPanel.RecalculateMsdRequested += OnRecalculateMsdRequested;

		// Wire up map pack manager events
		_mapPackManagerPanel.BuildMapPackRequested += OnBuildMapPackRequested;
		_mapPackManagerPanel.RecalculateMsdRequested += OnRecalculateMapPackMsdRequested;

		// Wire up difficulty splitter events
		_difficultySplitterPanel.SplitRequested += OnSplitDifficultyRequested;

		// Combine BPM Analysis and Normalize SV into one panel
		var timingToolsContent = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				_functionPanel,
				_offsetPanel
			}
		};

		_mappingSplitContainer = new SplitTabContainer(new[]
		{
			new SplitTabItem("Timing Tools", timingToolsContent),
			new SplitTabItem("Bulk Rates", _bulkRateChangerPanel),
			new SplitTabItem("Marathon", _marathonCreatorPanel),
			new SplitTabItem("MapPack Manager", _mapPackManagerPanel),
			new SplitTabItem("Difficulty Splitter", _difficultySplitterPanel)
		})
		{
			RelativeSizeAxes = Axes.Both
		};

		return new Container
		{
			RelativeSizeAxes = Axes.Both,
			Child = _mappingSplitContainer
		};
	}

	private void OpenTrackerSettings()
	{
		_tabContainer.SelectTab(2);
		Schedule(() => _settingsScroll.ScrollTo(_trackerSettings));
	}

	private Container CreateSettingsTab()
	{
		_quickSetupPanel = new QuickSetupPanel
		{
			RelativeSizeAxes = Axes.X
		};

		// Wire up quick setup event from settings
		_quickSetupPanel.QuickSetupRequested += OnQuickSetupRequested;

		return new Container
		{
			RelativeSizeAxes = Axes.Both,
			Child = _settingsScroll = new ChainedScrollContainer
			{
				RelativeSizeAxes = Axes.Both,
				ClampExtension = 200,
				ScrollbarVisible = true,
				Child = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 12),
					Padding = new MarginPadding { Horizontal = 4, Top = 8, Bottom = 48 },
					Children = new Drawable[]
					{
						_quickSetupPanel,
						new SettingsGroupHeader("Appearance", "Visual preferences and scaling"),
						new UIScalePanel { RelativeSizeAxes = Axes.X },
						new MetadataPreferencePanel { RelativeSizeAxes = Axes.X },
						new SettingsGroupHeader("Overlay", "Window attachment and positioning"),
						new OverlayModePanel { RelativeSizeAxes = Axes.X },
						new OverlayPositionPanel { RelativeSizeAxes = Axes.X },
						new ReplayAnalysisSettingsPanel { RelativeSizeAxes = Axes.X },
						new SettingsGroupHeader("osu! Integration", "Paths and game connection"),
						new OsuDirectorySettingsPanel { RelativeSizeAxes = Axes.X },
						_trackerSettings = new ManiaTrackerSettingsPanel { RelativeSizeAxes = Axes.X },
						new SettingsGroupHeader("Difficulty & Analysis", "Calculators and map indexing"),
						new MinaCalcVersionPanel { RelativeSizeAxes = Axes.X },
						new RiceDanCalculatorPanel { RelativeSizeAxes = Axes.X },
						new MapIndexingPanel { RelativeSizeAxes = Axes.X },
						new SettingsGroupHeader("Sessions & Data", "Tracking, imports, and migrations"),
						new SessionAutoStartPanel { RelativeSizeAxes = Axes.X },
						new ScoreMigrationPanel { RelativeSizeAxes = Axes.X },
						new ScoreImportPanel { RelativeSizeAxes = Axes.X },
						new SettingsGroupHeader("Privacy & Controls", "Analytics and shortcuts"),
						new AnalyticsSettingsPanel { RelativeSizeAxes = Axes.X },
						new KeybindConfigPanel { RelativeSizeAxes = Axes.X }
					}
				}
			}
		};
	}

	private Container CreateTutorialTab()
	{
		_tutorialPanel = new TutorialPanel
		{
			RelativeSizeAxes = Axes.Both
		};

		// Wire up tutorial panel events
		_tutorialPanel.ShowTutorialRequested += OnShowTutorialRequested;
		_tutorialPanel.QuickSetupRequested += OnQuickSetupRequested;

		return new Container
		{
			RelativeSizeAxes = Axes.Both,
			Child = _tutorialPanel
		};
	}

	private void OnShowTutorialRequested()
	{
		// Show the tutorial overlay
		Schedule(() => { _tutorialOverlay.Show(); });
	}

	private void OnRestartOsuRequested(string title, string message)
	{
		Schedule(() =>
		{
			_osuRestartDialog.Confirmed -= OnOsuRestartConfirmed;
			_osuRestartDialog.Confirmed += OnOsuRestartConfirmed;
			_osuRestartDialog.Show(title, message);
		});
	}

	private void OnOsuRestartConfirmed(string arguments)
	{
		Task.Run(() => { OsuCollectionService.RestartOsu(arguments); });
	}

	private void TryAttachToOsu()
	{
		if (ProcessDetector.TryAttachToOsu())
		{
			var info = ProcessDetector.GetProcessInfo();
			_mapInfoDisplay.SetConnected();

			// Track analytics
			AptabaseService.TrackOsuConnection(true);

			// Subscribe to file modification events
			ProcessDetector.BeatmapFileModified += OnBeatmapFileModified;

			// Try to find recently modified beatmap
			var recentBeatmap = ProcessDetector.FindRecentlyModifiedBeatmap();
			if (recentBeatmap != null)
			{
				LoadBeatmap(recentBeatmap);
			}
			else
			{
				// Try to get beatmap from window title
				var titleBeatmap = ProcessDetector.GetBeatmapFromWindowTitle();
				if (titleBeatmap != null) LoadBeatmap(titleBeatmap);
			}
		}
		else
		{
			_mapInfoDisplay.SetNotConnected();
		}
	}

	private void OnBeatmapFileModified(object? sender, string filePath)
	{
		// This is called from a non-UI thread, so we need to schedule it
		Schedule(() =>
		{
			if (_currentOsuFile == null || _currentOsuFile.FilePath != filePath) LoadBeatmap(filePath);
		});
	}

	private void OnFileDropped(string filePath)
	{
		LoadBeatmap(filePath);
	}

	/// <summary>
	/// Handles file drop from the game level.
	/// </summary>
	public void HandleFileDrop(string filePath)
	{
		LoadBeatmap(filePath);
	}

	/// <summary>
	/// Handles selection of a recommended map from the skills analysis panel.
	/// </summary>
	private void OnRecommendedMapSelected(MapRecommendation recommendation)
	{
		if (string.IsNullOrEmpty(recommendation.BeatmapPath)) return;

		if (!File.Exists(recommendation.BeatmapPath)) return;

		// Track analytics
		AptabaseService.TrackRecommendationSelected(recommendation.Focus.ToString());

		// Load the beatmap in this application
		LoadBeatmap(recommendation.BeatmapPath);

		// If a rate change is suggested, pre-fill the rate changer
		if (recommendation.SuggestedRate.HasValue && Math.Abs(recommendation.SuggestedRate.Value - 1.0f) > 0.05f)
			_rateChangerPanel.SetRate(recommendation.SuggestedRate.Value);
	}

	private void LoadBeatmap(string filePath)
	{
		try
		{
			_currentOsuFile = OsuFileParser.Parse(filePath);
			_mapInfoDisplay.SetMapInfo(_currentOsuFile);
			_functionPanel.SetEnabled(true);
			_offsetPanel.SetEnabled(true);
			_rateChangerPanel.SetEnabled(true);
			_modSelectionPanel.SetEnabled(true);
			_bulkRateChangerPanel.SetEnabled(true);
			_marathonCreatorPanel.SetCurrentBeatmap(_currentOsuFile);
			_marathonCreatorPanel.SetEnabled(true);
			_mapPackManagerPanel.SetCurrentBeatmap(_currentOsuFile);
			_mapPackManagerPanel.SetEnabled(true);
			_difficultySplitterPanel.SetCurrentBeatmap(_currentOsuFile);
			_difficultySplitterPanel.SetEnabled(true);

			// Get dominant BPM and pass to rate changer panel
			var dominantBpm = GetDominantBpm(_currentOsuFile);
			_rateChangerPanel.SetCurrentMapBpm(dominantBpm);

			// Update OD/HP sliders with current map values
			_rateChangerPanel.SetMapDifficultyValues(_currentOsuFile.OverallDifficulty, _currentOsuFile.HPDrainRate);
			_bulkRateChangerPanel.SetMapDifficultyValues(_currentOsuFile.OverallDifficulty,
				_currentOsuFile.HPDrainRate);

			// Update rate changer preview
			UpdateRatePreview(1.0, RateChanger.DefaultNameFormat);
		}
		catch (Exception)
		{
			_functionPanel.SetEnabled(false);
			_offsetPanel.SetEnabled(false);
			_rateChangerPanel.SetEnabled(false);
			_modSelectionPanel.SetEnabled(false);
			_bulkRateChangerPanel.SetEnabled(false);
			_marathonCreatorPanel.SetCurrentBeatmap(null);
			_marathonCreatorPanel.SetEnabled(false);
			_mapPackManagerPanel.SetCurrentBeatmap(null);
			_mapPackManagerPanel.SetEnabled(false);
			_difficultySplitterPanel.SetCurrentBeatmap(null);
			_difficultySplitterPanel.SetEnabled(false);
		}
	}

	/// <summary>
	/// Gets the dominant BPM from the current osu file's timing points.
	/// </summary>
	private static double GetDominantBpm(OsuFile osuFile)
	{
		var uninherited = osuFile.TimingPoints.Where(tp => tp.Uninherited && tp.BeatLength > 0).ToList();
		if (uninherited.Count == 0)
			return 120;
		if (uninherited.Count == 1)
			return uninherited[0].Bpm;

		// Find BPM with longest duration
		var bpmDurations = new Dictionary<double, double>();
		for (var i = 0; i < uninherited.Count; i++)
		{
			var bpm = Math.Round(uninherited[i].Bpm, 1);
			var duration = i < uninherited.Count - 1
				? uninherited[i + 1].Time - uninherited[i].Time
				: 60000;
			if (!bpmDurations.ContainsKey(bpm))
				bpmDurations[bpm] = 0;
			bpmDurations[bpm] += duration;
		}

		return bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
	}

	private async void OnAnalyzeBpmClicked()
	{
		if (_currentOsuFile == null) return;

		// Capture the BPM factor before starting background task
		_pendingBpmFactor = _functionPanel.SelectedBpmFactor;
		var factorLabel = _pendingBpmFactor.GetLabel();

		// Track analytics
		AptabaseService.TrackBpmAnalysis(factorLabel);

		_loadingOverlay.Show($"Analyzing BPM ({factorLabel})...");
		SetAllPanelsEnabled(false);

		try
		{
			await Task.Run(() => PerformBpmAnalysis());
		}
		catch (Exception)
		{
		}
		finally
		{
			_loadingOverlay.Hide();
			SetAllPanelsEnabled(true);
		}
	}

	private void PerformBpmAnalysis()
	{
		if (_currentOsuFile == null)
			return;

		var audioExtractor = new AudioExtractor();

		// Use the local bpm.exe from tools directory
		if (!ToolPaths.BpmExeExists)
			throw new FileNotFoundException($"bpm.exe not found at {ToolPaths.BpmExe}. Run build.ps1 to copy tools.");

		var bpmAnalyzer = new BpmAnalyzer(ToolPaths.BpmExe);
		var timingConverter = new TimingPointConverter();
		var fileWriter = new OsuFileWriter();

		// Get audio path
		var audioPath = AudioExtractor.GetAudioPath(_currentOsuFile);
		Schedule(() => { _loadingOverlay.UpdateStatus($"Analyzing: {Path.GetFileName(audioPath)}"); });

		// Run BPM analysis
		var bpmResult = bpmAnalyzer.Analyze(audioPath, true);
		Logger.Info($"[Analysis] Got {bpmResult.Beats.Count} beats from bpm.exe");

		// Apply BPM factor
		var factor = _pendingBpmFactor.GetMultiplier();
		if (Math.Abs(factor - 1.0) > 0.001)
		{
			Logger.Info($"[Analysis] Applying BPM factor: {_pendingBpmFactor.GetLabel()} ({factor}x)");
			foreach (var beat in bpmResult.Beats) beat.Bpm *= factor;

			if (bpmResult.AverageBpm.HasValue)
				bpmResult.AverageBpm *= factor;
			if (bpmResult.EstimatedTempo.HasValue)
				bpmResult.EstimatedTempo *= factor;
		}

		var factorLabel = _pendingBpmFactor.GetLabel();
		Schedule(() =>
		{
			_loadingOverlay.UpdateStatus(
				$"Converting {bpmResult.Beats.Count} beats to timing points ({factorLabel})...");
		});

		// Convert to timing points
		var newTimingPoints = TimingPointConverter.Convert(bpmResult);
		var stats = TimingPointConverter.GetStats(bpmResult, newTimingPoints);
		Logger.Info($"[Analysis] Converted to {newTimingPoints.Count} timing points");

		// Merge with existing inherited timing points
		var mergedTimingPoints = OsuFileWriter.MergeTimingPoints(_currentOsuFile.TimingPoints, newTimingPoints);

		// Write back to file
		Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
		OsuFileWriter.Write(_currentOsuFile, mergedTimingPoints);

		// Update display
		Schedule(() =>
		{
			var factorInfo = _pendingBpmFactor != BpmFactor.Normal
				? $" ({_pendingBpmFactor.GetLabel()})"
				: "";
			LoadBeatmap(_currentOsuFile.FilePath);
		});
	}

	private async void OnNormalizeSvClicked()
	{
		if (_currentOsuFile == null) return;

		// Track analytics
		AptabaseService.TrackSvNormalization();

		_loadingOverlay.Show("Normalizing SV...");
		SetAllPanelsEnabled(false);

		try
		{
			await Task.Run(() => PerformSvNormalization());
		}
		catch (Exception)
		{
		}
		finally
		{
			_loadingOverlay.Hide();
			SetAllPanelsEnabled(true);
		}
	}

	private void PerformSvNormalization()
	{
		if (_currentOsuFile == null)
			return;

		var svNormalizer = new SvNormalizer();
		var fileWriter = new OsuFileWriter();

		var existingTimingPoints = _currentOsuFile.TimingPoints;
		var uninheritedCount = existingTimingPoints.Count(tp => tp.Uninherited);

		if (uninheritedCount <= 1) return;

		Schedule(() => _loadingOverlay.UpdateStatus($"Normalizing {uninheritedCount} BPM sections..."));

		// Get map end time from hit objects for accurate BPM duration calculation
		double? mapEndTime = null;
		try
		{
			var hitObjectSerializer = new HitObjectSerializer();
			var hitObjects = HitObjectSerializer.Parse(_currentOsuFile);
			if (hitObjects.Count > 0)
			{
				mapEndTime = hitObjects.Max(h => h.EndTime);
				Logger.Info($"[SV Normalize] Map end time: {mapEndTime:F0}ms (from {hitObjects.Count} hit objects)");
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[SV Normalize] Could not parse hit objects for map length: {ex.Message}");
		}

		// Determine base BPM using the normalizer's logic (which now uses map end time)
		var normalizedTimingPoints = SvNormalizer.Normalize(existingTimingPoints, null, mapEndTime);

		// Get the base BPM that was used (from stats)
		var uninherited = existingTimingPoints.Where(tp => tp.Uninherited).OrderBy(tp => tp.Time).ToList();
		var baseBpm = uninherited.Count > 0 ? uninherited[0].Bpm : 120;
		if (uninherited.Count > 1)
		{
			// Calculate using the same logic as SvNormalizer
			var bpmDurations = new Dictionary<double, double>();
			for (var i = 0; i < uninherited.Count; i++)
			{
				var bpm = Math.Round(uninherited[i].Bpm, 1);
				double endTime;
				if (i < uninherited.Count - 1)
					endTime = uninherited[i + 1].Time;
				else
					endTime = mapEndTime.HasValue && mapEndTime.Value > uninherited[i].Time
						? mapEndTime.Value
						: uninherited[i].Time + 60000;

				var duration = endTime - uninherited[i].Time;
				if (!bpmDurations.ContainsKey(bpm))
					bpmDurations[bpm] = 0;
				bpmDurations[bpm] += duration;
			}

			baseBpm = bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
		}

		var stats = SvNormalizer.GetStats(existingTimingPoints, normalizedTimingPoints, baseBpm);

		Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
		OsuFileWriter.Write(_currentOsuFile, normalizedTimingPoints);

		Schedule(() =>
		{
			var removedInfo = stats.InheritedPointsRemoved > 0
				? $"Removed {stats.InheritedPointsRemoved} SV points. "
				: "";
			LoadBeatmap(_currentOsuFile.FilePath);
		});
	}

	private async void OnApplyOffsetClicked(double offsetMs)
	{
		if (_currentOsuFile == null) return;

		if (Math.Abs(offsetMs) < 0.001) return;

		// Track analytics
		AptabaseService.TrackOffsetApplied(offsetMs);

		_loadingOverlay.Show($"Applying {offsetMs:+0.##;-0.##;0}ms offset...");
		SetAllPanelsEnabled(false);

		try
		{
			await Task.Run(() => PerformOffsetChange(offsetMs));
		}
		catch (Exception)
		{
		}
		finally
		{
			_loadingOverlay.Hide();
			SetAllPanelsEnabled(true);
		}
	}

	private void PerformOffsetChange(double offsetMs)
	{
		if (_currentOsuFile == null)
			return;

		var offsetChanger = new OffsetChanger();
		var fileWriter = new OsuFileWriter();

		var existingTimingPoints = _currentOsuFile.TimingPoints;
		var modifiedTimingPoints = OffsetChanger.ApplyOffset(existingTimingPoints, offsetMs);
		var stats = OffsetChanger.GetStats(existingTimingPoints, modifiedTimingPoints, offsetMs);

		Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
		OsuFileWriter.Write(_currentOsuFile, modifiedTimingPoints);

		Schedule(() =>
		{
			_offsetPanel.Reset();
			LoadBeatmap(_currentOsuFile.FilePath);
		});
	}

	private void OnRatePreviewRequested(double rate, string format)
	{
		UpdateRatePreview(rate, format);
	}

	private void OnRateChangerFormatChanged(string format)
	{
		// Save the new format to settings
		UserSettingsService.Settings.RateChangerFormat = format;
		Task.Run(async () => await UserSettingsService.SaveAsync());
	}

	private void OnRateChangerPitchAdjustChanged(bool pitchAdjust)
	{
		// Sync to bulk rate changer panel
		_bulkRateChangerPanel.PitchAdjust = pitchAdjust;

		// Save the setting
		UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
		Task.Run(async () => await UserSettingsService.SaveAsync());
	}

	private void OnBulkRateChangerPitchAdjustChanged(bool pitchAdjust)
	{
		// Sync to rate changer panel
		_rateChangerPanel.PitchAdjust = pitchAdjust;

		// Save the setting
		UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
		Task.Run(async () => await UserSettingsService.SaveAsync());
	}

	private void OnPresetEditRequested(int index, BulkRatePreset preset)
	{
		_presetEditDialog.Show(index, preset);
	}

	private void OnPresetSaved(int index, BulkRatePreset preset)
	{
		_bulkRateChangerPanel.UpdatePreset(index, preset);
	}

	private void OnPlayRecordedForDanRating(object? sender, SessionPlayResult play)
	{
		// Only show dialog if:
		// 1. Dan training is enabled in settings
		// 2. Play was completed (not quit/failed)
		// 3. Not using DT or HT (rate is 1.0)
		// 4. This map hasn't been rated before

		// Check if dan training is enabled
		if (!UserSettingsService.Settings.ParticipateDanTraining)
			return;

		if (play.Status != PlayStatus.Completed)
			return;

		// Check if DT/HT was used by checking the current rate
		var currentRate = ProcessDetector.GetCurrentRateFromMods();
		if (Math.Abs(currentRate - 1.0f) > 0.01f)
		{
			Logger.Info($"[DanRating] Skipping dialog - rate mod detected: {currentRate:F2}x");
			return;
		}

		// Check if already rated
		if (string.IsNullOrEmpty(play.BeatmapHash) || SessionDatabaseService.HasDanRating(play.BeatmapHash))
		{
			Logger.Info("[DanRating] Skipping dialog - map already rated or no hash");
			return;
		}

		// Show the dialog on UI thread
		Schedule(() =>
		{
			Logger.Info($"[DanRating] Showing rating dialog for {Path.GetFileName(play.BeatmapPath)}");
			_danRatingDialog.Show(play.BeatmapHash, play.BeatmapPath, play.Accuracy);
		});
	}

	private async void OnDanRatingSubmitted(string beatmapHash, string beatmapPath, string danLabel, float modifier,
		double accuracy)
	{
		// Format label with modifier prefix if applicable
		var formattedLabel = modifier switch
		{
			< -0.01f => $"-{danLabel}",
			> 0.01f => $"+{danLabel}",
			_ => danLabel
		};

		// Save rating locally
		SessionDatabaseService.SaveDanRating(beatmapHash, formattedLabel);

		// Submit to API
		var username = ProcessDetector.GetUsername();
		if (!string.IsNullOrEmpty(username))
			await _danRatingSubmissionService.SubmitRatingAsync(beatmapPath, username, formattedLabel, accuracy);
		else
			Logger.Info("[DanRating] Could not get username - skipping API submission");
	}

	private void OnTabChanged(int tabIndex)
	{
		// Track analytics
		var tabNames = new[] { "Gameplay", "Mapping", "Settings" };
		var tabName = tabIndex >= 0 && tabIndex < tabNames.Length ? tabNames[tabIndex] : "Unknown";
	}

	private void OnCloseRequested()
	{
		_confirmationDialog.Confirmed -= OnQuitConfirmed;
		_confirmationDialog.Confirmed += OnQuitConfirmed;
		_confirmationDialog.Show(
			"Quit Companella!?",
			"Are you sure you want to quit?",
			false
		);
	}

	private void OnQuitConfirmed()
	{
		Host.Exit();
	}

	private async void OnApplyModClicked(IMod mod)
	{
		if (_currentOsuFile == null)
		{
			_modSelectionPanel.SetStatus("No beatmap loaded");
			return;
		}

		_loadingOverlay.Show($"Applying {mod.Name}...");
		SetAllPanelsEnabled(false);

		try
		{
			var result = await ModService.ApplyModAsync(
				mod,
				_currentOsuFile,
				null,
				status => Schedule(() => _loadingOverlay.UpdateStatus(status)));

			if (result.Success && result.OutputFilePath != null)
				Schedule(() =>
				{
					_modSelectionPanel.SetStatus($"Created: {Path.GetFileName(result.OutputFilePath)}");
					LoadBeatmap(result.OutputFilePath);
				});
			else
				Schedule(() => { _modSelectionPanel.SetStatus(result.ErrorMessage ?? "Mod failed"); });
		}
		catch (Exception ex)
		{
			Schedule(() => { _modSelectionPanel.SetStatus($"Error: {ex.Message}"); });
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private void UpdateRatePreview(double rate, string format)
	{
		if (_currentOsuFile == null)
		{
			_rateChangerPanel.SetPreviewText("(no beatmap loaded)");
			return;
		}

		var rateChanger = new RateChanger();
		var dominantBpm = GetDominantBpm(_currentOsuFile);

		var newBpm = dominantBpm * rate;
		var previewName = RateChanger.FormatDifficultyName(format, _currentOsuFile, rate, newBpm);
		_rateChangerPanel.SetPreviewText(previewName);
	}

	private async void OnApplyRateClicked(double rate, string format, bool pitchAdjust, double customOd,
		double customHp)
	{
		if (_currentOsuFile == null) return;

		// Save pitch adjust setting
		UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
		_ = UserSettingsService.SaveAsync();

		// Track analytics
		AptabaseService.TrackRateChange(rate, false);

		var rateChanger = new RateChanger();
		_loadingOverlay.Show("Checking ffmpeg...");

		var ffmpegAvailable = await rateChanger.CheckFfmpegAvailableAsync();
		if (!ffmpegAvailable)
		{
			_loadingOverlay.Hide();
			return;
		}

		var pitchMode = pitchAdjust ? "with pitch change" : "preserving pitch";
		_loadingOverlay.Show($"Creating {rate:0.0#}x rate-changed beatmap ({pitchMode})...");
		SetAllPanelsEnabled(false);

		try
		{
			var newOsuPath = await rateChanger.CreateRateChangedBeatmapAsync(
				_currentOsuFile,
				rate,
				format,
				pitchAdjust,
				customOd,
				customHp,
				status => Schedule(() => { _loadingOverlay.UpdateStatus(status); }));

			Schedule(() => { LoadBeatmap(newOsuPath); });
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private async void OnApplyBulkRateClicked(double minRate, double maxRate, double step, string format,
		bool pitchAdjust, double customOd, double customHp, bool excludeBaseRate)
	{
		if (_currentOsuFile == null) return;

		// Save pitch adjust setting
		UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
		_ = UserSettingsService.SaveAsync();

		// Calculate how many rates will be created
		var rateCount = (int)Math.Floor((maxRate - minRate) / step) + 1;
		if (excludeBaseRate && minRate <= 1.0 && maxRate >= 1.0)
			rateCount--;

		// Track analytics
		AptabaseService.TrackBulkRateChange(minRate, maxRate, step, rateCount);

		var rateChanger = new RateChanger();
		_loadingOverlay.Show("Checking ffmpeg...");

		var ffmpegAvailable = await rateChanger.CheckFfmpegAvailableAsync();
		if (!ffmpegAvailable)
		{
			_loadingOverlay.Hide();
			return;
		}

		var pitchMode = pitchAdjust ? "with pitch change" : "preserving pitch";
		_loadingOverlay.Show($"Creating rate-changed beatmaps ({minRate:0.0#}x to {maxRate:0.0#}x, {pitchMode})...");
		SetAllPanelsEnabled(false);

		try
		{
			var createdFiles = await rateChanger.CreateBulkRateChangedBeatmapsAsync(
				_currentOsuFile,
				minRate,
				maxRate,
				step,
				format,
				pitchAdjust,
				customOd,
				customHp,
				excludeBaseRate,
				status => Schedule(() => { _loadingOverlay.UpdateStatus(status); }));
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private async void OnCreateMarathonRequested(List<MarathonEntry> entries, MarathonMetadata metadata)
	{
		if (entries.Count == 0) return;

		var marathonService = new MarathonCreatorService();
		_loadingOverlay.Show("Checking ffmpeg...");

		var ffmpegAvailable = await marathonService.CheckFfmpegAvailableAsync();
		if (!ffmpegAvailable)
		{
			_loadingOverlay.Hide();
			return;
		}

		// Use the osu! Songs folder as output directory
		var outputDir = ProcessDetector.GetSongsFolder();
		if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
		{
			_loadingOverlay.Hide();
			return;
		}

		_loadingOverlay.Show($"Creating marathon from {entries.Count} maps...");
		SetAllPanelsEnabled(false);

		try
		{
			var result = await marathonService.CreateMarathonAsync(
				entries,
				metadata,
				outputDir,
				status => Schedule(() => { _loadingOverlay.UpdateStatus(status); }));

			Schedule(() =>
			{
				if (result.Success && result.OutputPath != null)
				{
					var duration = TimeSpan.FromMilliseconds(result.TotalDurationMs);
					LoadBeatmap(result.OutputPath);

					// Track analytics
					var mapCount = entries.Count(e => !e.IsPause);
					var pauseCount = entries.Count(e => e.IsPause);
					AptabaseService.TrackMarathonCreated(mapCount, pauseCount, duration.TotalMinutes);
				}
			});
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private async void OnRecalculateMsdRequested(List<MarathonEntry> entries)
	{
		// Filter out pause entries - they don't have MSD
		var mapEntries = entries.Where(e => !e.IsPause).ToList();

		if (mapEntries.Count == 0) return;

		if (!ToolPaths.MsdCalculatorExists) return;

		_loadingOverlay.Show($"Calculating MSD for {mapEntries.Count} maps...");
		SetAllPanelsEnabled(false);

		try
		{
			var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);

			for (var i = 0; i < mapEntries.Count; i++)
			{
				var entry = mapEntries[i];
				Schedule(() =>
					_loadingOverlay.UpdateStatus(
						$"Calculating MSD for [{entry.Version}] ({i + 1}/{mapEntries.Count})..."));

				try
				{
					var result = await analyzer.AnalyzeSingleRateAsync(entry.OsuFile!.FilePath, (float)entry.Rate);
					entry.MsdValues = result?.Scores;
				}
				catch (Exception ex)
				{
					Logger.Info($"[MSD] Failed to calculate MSD for {entry.Title}: {ex.Message}");
					entry.MsdValues = null;
				}
			}

			Schedule(() => { _marathonCreatorPanel.RefreshList(); });
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private async void OnBuildMapPackRequested(MapPackDefinition definition)
	{
		if (definition.Entries.Count == 0)
			return;

		var mapPackService = new MapPackManagerService();
		var needsFfmpeg = definition.Entries.Any(e => Math.Abs(e.Rate - 1.0) > 0.01);
		if (needsFfmpeg)
		{
			_loadingOverlay.Show("Checking ffmpeg...");
			var ffmpegAvailable = await mapPackService.CheckFfmpegAvailableAsync();
			if (!ffmpegAvailable)
			{
				_loadingOverlay.Hide();
				return;
			}
		}

		var outputDir = ProcessDetector.GetSongsFolder();
		if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
		{
			_loadingOverlay.Hide();
			return;
		}

		_loadingOverlay.Show($"Building map pack ({definition.Entries.Count} difficulties)...");
		SetAllPanelsEnabled(false);

		try
		{
			var result = await mapPackService.BuildMapPackAsync(
				definition,
				outputDir,
				status => Schedule(() => { _loadingOverlay.UpdateStatus(status); }));

			Schedule(() =>
			{
				if (result.Success && result.FirstOsuPath != null)
				{
					definition.LoadedFolderPath = result.OutputFolder;
					definition.OutputFolderName = Path.GetFileName(result.OutputFolder ?? "") ?? definition.OutputFolderName;
					definition.ResetOnlineIdsOnExport = false;
					LoadBeatmap(result.FirstOsuPath);
					_mapPackManagerPanel.LoadDefinition(definition);
				}
				else if (!string.IsNullOrEmpty(result.ErrorMessage))
				{
					Logger.Info($"[MapPack] Build failed: {result.ErrorMessage}");
				}
			});
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private async void OnRecalculateMapPackMsdRequested(List<MapPackEntry> entries)
	{
		if (entries.Count == 0)
			return;

		if (!ToolPaths.MsdCalculatorExists)
			return;

		_loadingOverlay.Show($"Calculating MSD for {entries.Count} maps...");
		SetAllPanelsEnabled(false);

		try
		{
			var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);

			for (var i = 0; i < entries.Count; i++)
			{
				var entry = entries[i];
				if (entry.SourceMissing || entry.OsuFile == null)
					continue;

				Schedule(() =>
					_loadingOverlay.UpdateStatus(
						$"Calculating MSD for [{entry.EffectiveVersion}] ({i + 1}/{entries.Count})..."));

				try
				{
					var result = await analyzer.AnalyzeSingleRateAsync(entry.OsuFile.FilePath, (float)entry.Rate);
					entry.MsdValues = result?.Scores;
				}
				catch (Exception ex)
				{
					Logger.Info($"[MSD] Failed to calculate MSD for {entry.SourceTitle}: {ex.Message}");
					entry.MsdValues = null;
				}
			}

			Schedule(() => { _mapPackManagerPanel.RefreshList(); });
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	private void SetAllPanelsEnabled(bool enabled)
	{
		_functionPanel.SetEnabled(enabled);
		_offsetPanel.SetEnabled(enabled);
		_rateChangerPanel.SetEnabled(enabled);
		_modSelectionPanel.SetEnabled(enabled);
		_bulkRateChangerPanel.SetEnabled(enabled);
		_marathonCreatorPanel.SetEnabled(enabled);
		_mapPackManagerPanel.SetEnabled(enabled);
		_difficultySplitterPanel.SetEnabled(enabled);
	}

	private async void OnSplitDifficultyRequested(DifficultySplitterRequest request)
	{
		if (request.Pairs.Count == 0)
			return;

		_loadingOverlay.Show($"Splitting into {request.Pairs.Count} region(s)...");
		SetAllPanelsEnabled(false);

		try
		{
			var result = await DifficultySplitterService.SplitAsync(
				request.Source,
				request.Markers,
				request.Pairs,
				status => Schedule(() => { _loadingOverlay.UpdateStatus(status); }));

			Schedule(() =>
			{
				_difficultySplitterPanel.ShowExportResult(result);

				if (result.Success)
				{
					var firstOutput = result.PairResults.FirstOrDefault(r => r.Success)?.OutputPath;
					if (firstOutput != null)
						LoadBeatmap(firstOutput);
				}
			});
		}
		catch (Exception)
		{
		}
		finally
		{
			Schedule(() =>
			{
				_loadingOverlay.Hide();
				SetAllPanelsEnabled(true);
			});
		}
	}

	protected override void Update()
	{
		base.Update();

		// Keep background visible for color keying - don't set alpha to 0
		// The color key will make RGB(25, 25, 30) transparent
		if (_backgroundBox != null)
			// Keep background visible so color keying can work
			_backgroundBox.Alpha = 1f;

		// Show/hide title bar based on overlay mode
		// Default to visible (alpha = 1) if overlay service is not available
		if (_titleBar != null)
		{
			if (OverlayService != null)
				_titleBar.Alpha = OverlayService.IsOverlayMode ? 0f : 1f;
			else
				// If overlay service not available yet, keep it visible
				_titleBar.Alpha = 1f;
		}

		if (_trackerAccountMenu != null && _titleBar != null)
		{
			_trackerAccountMenu.Alpha = _titleBar.Alpha;
			if (_titleBar.Alpha == 0) _trackerAccountMenu.CloseMenu();
		}

		// Periodically check for beatmap changes
		_beatmapCheckTimer += Clock.ElapsedFrameTime;
		if (_beatmapCheckTimer >= _beatmapCheckInterval)
		{
			_beatmapCheckTimer = 0;
			CheckForBeatmapChanges();
		}
	}

	// Track last detected mods for change detection
	private float _lastDetectedRate = 1.0f;

	private void CheckForBeatmapChanges()
	{
		if (!ProcessDetector.IsOsuRunning)
		{
			// Show not connected overlay if no beatmap is loaded
			if (_currentOsuFile == null) _mapInfoDisplay.SetNotConnected();

			// Try to reattach if osu! was closed and reopened
			if (ProcessDetector.TryAttachToOsu())
			{
				var info = ProcessDetector.GetProcessInfo();
				_mapInfoDisplay.SetConnected();
				ProcessDetector.BeatmapFileModified += OnBeatmapFileModified;
			}

			return;
		}

		// osu! is running, ensure overlay is hidden
		_mapInfoDisplay.SetConnected();

		string? detectedBeatmap = null;

		// Try memory reading first (song select / gameplay)
		detectedBeatmap = ProcessDetector.GetBeatmapFromMemory();

		// Load if we found a beatmap and it's different from current
		if (detectedBeatmap != null && detectedBeatmap != _lastDetectedBeatmap)
		{
			_lastDetectedBeatmap = detectedBeatmap;

			if (_currentOsuFile == null || _currentOsuFile.FilePath != detectedBeatmap) LoadBeatmap(detectedBeatmap);
		}

		// Check for mod changes (DT/HT) on the same map
		var currentRate = ProcessDetector.GetCurrentRateFromMods();
		if (Math.Abs(currentRate - _lastDetectedRate) > 0.01f)
		{
			_lastDetectedRate = currentRate;

			// Refresh MSD analysis with new rate if we have a map loaded
			if (_currentOsuFile != null)
			{
				Logger.Info($"[MainScreen] Mod rate changed to {currentRate:F2}x, refreshing MSD");
				_mapInfoDisplay.RefreshMsdAnalysis();
			}
		}
	}

	/// <summary>
	/// Handles the screenshot request from the title bar camera button.
	/// Takes a screenshot of the MapInfoDisplay component.
	/// </summary>
	private void OnScreenshotRequested()
	{
		Logger.Info("[MainScreen] Screenshot requested for MapInfoDisplay");
		ScreenshotService.CaptureDrawable(_mapInfoDisplay, Host.Window);
	}
}
