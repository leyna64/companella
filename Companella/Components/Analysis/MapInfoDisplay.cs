using Companella.Components.Charts;
using Companella.Components.Misc;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Services.Analysis;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Companella.Components.Analysis;

/// <summary>
/// Displays comprehensive map metadata with the beatmap background image, MSD chart, and pattern analysis.
/// </summary>
public partial class MapInfoDisplay : CompositeDrawable
{
	private MarqueeText _artistTitleText = null!;
	private MarqueeText _mapperDiffText = null!;
	private MarqueeText _sourceText = null!;
	private SpriteText _difficultyStatsText = null!;
	private SpriteText _timingStatsText = null!;
	private MarqueeText _tagsText = null!;
	private FillFlowContainer _beatmapIdContainer = null!;

	private double? _yavsrgDifficulty;
	private double? _sunnyDifficulty;

	private SpriteText _sunnyDifficultyText = null!;

	// Background
	private Sprite _backgroundSprite = null!;
	private Box _backgroundDimOverlay = null!;

	// Not connected overlay
	private Container _notConnectedOverlay = null!;

	// MSD Chart
	private MsdChart _msdChart = null!;
	private string? _currentBeatmapPath;
	private CancellationTokenSource? _msdCancellation;

	// Pattern Display
	private PatternDisplay _patternDisplay = null!;
	private CancellationTokenSource? _patternCancellation;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _labelColor = new(160, 160, 160, 255);
	private readonly Color4 _valueColor = new(230, 230, 230, 255);

	[Resolved] private IRenderer Renderer { get; set; } = null!;

	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	[Resolved] private UserSettingsService UserSettings { get; set; } = null!;

	private string? _currentBackgroundPath;
	private OsuFile? _currentOsuFile;
	private float _lastMsdRate = 1.0f;

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 8;

		InternalChildren = new Drawable[]
		{
			// Dark fallback background
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(30, 28, 38, 255)
			},
			// Beatmap background image
			_backgroundSprite = new Sprite
			{
				RelativeSizeAxes = Axes.Both,
				FillMode = FillMode.Fill,
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Alpha = 0
			},
			// Dim overlay for readability (200 alpha = ~78% opacity for better text visibility on bright backgrounds)
			_backgroundDimOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(0, 0, 0, 185),
				Alpha = 0
			},
			// Content - Two rows: info on top, MSD chart + patterns on bottom
			new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding(12),
				Child = new GridContainer
				{
					RelativeSizeAxes = Axes.Both,
					RowDimensions = new[]
					{
						new Dimension(GridSizeMode.Absolute, 95), // Top row (map info)
						new Dimension() // Bottom row (MSD chart + patterns, fills remaining)
					},
					Content = new[]
					{
						// Top row - Map info (two columns)
						new Drawable[]
						{
							new GridContainer
							{
								RelativeSizeAxes = Axes.Both,
								ColumnDimensions = new[]
								{
									new Dimension(GridSizeMode.Relative, 0.6f), // Left column (main info)
									new Dimension(GridSizeMode.Relative, 0.4f) // Right column (stats)
								},
								Content = new[]
								{
									new Drawable[]
									{
										// Left column - Main info
										new FillFlowContainer
										{
											RelativeSizeAxes = Axes.Both,
											Direction = FillDirection.Vertical,
											Spacing = new Vector2(0, 4),
											Children = new Drawable[]
											{
												// Artist - Title (large)
												_artistTitleText = new MarqueeText
												{
													Text = "No map loaded",
													Font = new FontUsage("", 26, "Bold"),
													Colour = Color4.White,
													RelativeSizeAxes = Axes.X,
													Height = 28
												},
												// Mapped by X [Difficulty]
												_mapperDiffText = new MarqueeText
												{
													Text = "",
													Font = new FontUsage("", 20),
													Colour = _valueColor,
													RelativeSizeAxes = Axes.X,
													Height = 22
												},
												// Source
												_sourceText = new MarqueeText
												{
													Text = "",
													Font = new FontUsage("", 18),
													Colour = _labelColor,
													RelativeSizeAxes = Axes.X,
													Height = 20
												},
												// Tags (marquee animated)
												_tagsText = new MarqueeText
												{
													Text = "",
													Font = new FontUsage("", 19),
													Colour = new Color4(120, 120, 120, 255),
													RelativeSizeAxes = Axes.X,
													Height = 18
												}
											}
										},
										// Right column - Stats
										new FillFlowContainer
										{
											RelativeSizeAxes = Axes.Both,
											Direction = FillDirection.Vertical,
											Spacing = new Vector2(0, 6),
											Padding = new MarginPadding { Left = 15 },
											Children = new Drawable[]
											{
												// Difficulty settings (CS, AR, OD, HP)
												_difficultyStatsText = new SpriteText
												{
													Text = "",
													Font = new FontUsage("", 19),
													Colour = _accentColor
												},
												// Timing points stats
												_timingStatsText = new SpriteText
												{
													Text = "",
													Font = new FontUsage("", 18),
													Colour = _valueColor
												},
												// Beatmap ID info (clickable links)
												_beatmapIdContainer = new FillFlowContainer
												{
													Direction = FillDirection.Horizontal,
													AutoSizeAxes = Axes.Both,
													Spacing = new Vector2(8, 0),
													Children = []
												},
												new FillFlowContainer
												{
													AutoSizeAxes = Axes.Both,
													Direction = FillDirection.Horizontal,
													Children = new Drawable[]
													{
														// Sunny difficulty
												_sunnyDifficultyText = new SpriteText
												{
													Text = "",
													Font = new FontUsage("", 23),
													Colour = _valueColor
												}
													}
												}
												
											}
										}
									}
								}
							}
						},
						// Bottom row - MSD Chart (left) + Pattern Display (right)
						new Drawable[]
						{
							new Container
							{
								RelativeSizeAxes = Axes.Both,
								Padding = new MarginPadding { Top = 8 },
								Child = new GridContainer
								{
									RelativeSizeAxes = Axes.Both,
									ColumnDimensions = new[]
									{
										new Dimension(GridSizeMode.Relative, 0.6f), // MSD Chart
										new Dimension(GridSizeMode.Relative, 0.4f) // Pattern Display
									},
									Content = new[]
									{
										new Drawable[]
										{
											// MSD Chart
											_msdChart = new MsdChart
											{
												RelativeSizeAxes = Axes.Both
											},
											// Pattern Display
											new Container
											{
												RelativeSizeAxes = Axes.Both,
												Padding = new MarginPadding { Left = 8 },
												Child = _patternDisplay = new PatternDisplay
												{
													RelativeSizeAxes = Axes.Both
												}
											}
										}
									}
								}
							}
						}
					}
				}
			},
			// Accent border at bottom
			new Box
			{
				RelativeSizeAxes = Axes.X,
				Height = 3,
				Anchor = Anchor.BottomLeft,
				Origin = Anchor.BottomLeft,
				Colour = _accentColor
			},
			// Not connected overlay (hidden by default)
			_notConnectedOverlay = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Alpha = 0,
				Children = new Drawable[]
				{
					// Dark semi-transparent background
					new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = new Color4(20, 18, 25, 230)
					},
					// Centered message
					new FillFlowContainer
					{
						Anchor = Anchor.Centre,
						Origin = Anchor.Centre,
						AutoSizeAxes = Axes.Both,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 8),
						Children = new Drawable[]
						{
							new SpriteText
							{
								Text = "osu! not connected !!",
								Font = new FontUsage("", 39, "Bold"),
								Colour = new Color4(255, 100, 100, 255),
								Anchor = Anchor.Centre,
								Origin = Anchor.Centre
							},
							new SpriteText
							{
								Text = "Start osu! or drop a .osu file",
								Font = new FontUsage("", 19),
								Colour = new Color4(180, 180, 180, 255),
								Anchor = Anchor.Centre,
								Origin = Anchor.Centre
							}
						}
					}
				}
			}
		};
	}

	public void SetMapInfo(OsuFile osuFile)
	{
		_currentOsuFile = osuFile;

		// Hide not connected overlay when a map is loaded
		_notConnectedOverlay.FadeTo(0, 200);

		// Use metadata based on user preference
		var preferRomanized = UserSettings.Settings.PreferRomanizedMetadata;
		var artist = osuFile.GetArtist(preferRomanized);
		var title = osuFile.GetTitle(preferRomanized);

		_artistTitleText.Text = $"{artist} - {title}";
		_mapperDiffText.Text = $"Mapped by {osuFile.Creator} [{osuFile.Version}]";

		// Source
		if (!string.IsNullOrEmpty(osuFile.Source))
			_sourceText.Text = $"Source: {osuFile.Source}";
		else
			_sourceText.Text = "";

		// Tags (marquee animated for long tags)
		if (!string.IsNullOrEmpty(osuFile.Tags))
			_tagsText.Text = $"Tags: {osuFile.Tags}";
		else
			_tagsText.Text = "";

		// Reset difficulty ratings (will be calculated asynchronously)
		_yavsrgDifficulty = null;
		_sunnyDifficulty = null;

		// Difficulty settings
		UpdateDifficultyStats(osuFile);

		// Timing points stats
		// Get BPM range
		var bpms = osuFile.TimingPoints
			.Where(tp => tp.Uninherited && tp.BeatLength > 0)
			.Select(tp => tp.Bpm)
			.ToList();

		var bpmInfo = "No BPM";
		if (bpms.Count > 0)
		{
			var minBpm = bpms.Min();
			var maxBpm = bpms.Max();
			bpmInfo = Math.Abs(minBpm - maxBpm) < 0.1
				? $"{minBpm:F0} BPM"
				: $"{minBpm:F0}-{maxBpm:F0} BPM";
		}

		// Calculate LN% for mania maps
		var lnInfo = "";
		if (osuFile.Mode == 3) // osu!mania
		{
			var hitObjects = PatternFinder.ParseHitObjects(osuFile);
			if (hitObjects.Count > 0)
			{
				var lnCount = hitObjects.Count(h => h.Type == HitObjectType.Hold);
				var lnPercent = (double)lnCount / hitObjects.Count * 100;
				lnInfo = $"  |  {lnPercent:F0}% LN";
			}
		}

		_timingStatsText.Text = $"{bpmInfo}{lnInfo}";

		// Beatmap ID info (clickable links)
		_beatmapIdContainer.Clear();
		var idParts = new List<string>();

		if (osuFile.BeatmapSetID > 0)
		{
			var setUrl = $"https://osu.ppy.sh/beatmapsets/{osuFile.BeatmapSetID}";
			_beatmapIdContainer.Add(new LinkText($"Set: {osuFile.BeatmapSetID}", setUrl, 19, "", _labelColor));
			idParts.Add("set");
		}

		if (osuFile.BeatmapID > 0)
		{
			// For map link, we need both set ID and map ID for proper URL
			var mapUrl = osuFile.BeatmapSetID > 0
				? $"https://osu.ppy.sh/beatmapsets/{osuFile.BeatmapSetID}#mania/{osuFile.BeatmapID}"
				: $"https://osu.ppy.sh/beatmapsets/{osuFile.BeatmapID}"; // Fallback if no set ID

			_beatmapIdContainer.Add(new LinkText($"Map: {osuFile.BeatmapID}", mapUrl, 19, "", _labelColor));
			idParts.Add("map");
		}

		if (idParts.Count == 0)
			_beatmapIdContainer.Add(new SpriteText
			{
				Text = "Not submitted",
				Font = new FontUsage("", 19),
				Colour = _labelColor
			});

		// Load background image
		LoadBackground(osuFile.BackgroundFilePath);

		// Load MSD analysis for mania maps
		LoadMsdAnalysis(osuFile);

		// Load pattern analysis for mania maps
		LoadPatternAnalysis(osuFile);

		// Calculate difficulty ratings for mania maps with detected rate
		var rate = ProcessDetector.GetCurrentRateFromMods();
		LoadYavsrgDifficulty(osuFile, rate);
		LoadSunnyDifficulty(osuFile, rate);
		_patternDisplay.RefreshLnDan(osuFile, rate);
	}

	private void UpdateDifficultyStats(OsuFile osuFile)
	{
		var statsParts = new List<string>
		{
			$"OD {osuFile.OverallDifficulty:F1}",
			$"HP {osuFile.HPDrainRate:F1}"
		};

		// Add YAVSRG difficulty if available
		if (_yavsrgDifficulty.HasValue) statsParts.Add($"{_yavsrgDifficulty.Value:F2} Interlude");

		// Add Sunny difficulty if available
		if (_sunnyDifficulty.HasValue)
			_sunnyDifficultyText.Text = $"{_sunnyDifficulty.Value:F2} Sunny";
		else
			_sunnyDifficultyText.Text = "";

		_difficultyStatsText.Text = string.Join("  |  ", statsParts);
	}

	private void LoadYavsrgDifficulty(OsuFile osuFile, float rate = 1.0f)
	{
		// Only calculate for supported mania key counts (YAVSRG only supports 4K)
		if (osuFile.Mode != 3 || Math.Abs(osuFile.CircleSize - 4.0) > 0.1)
		{
			_yavsrgDifficulty = null;
			return;
		}

		// Calculate YAVSRG difficulty in background
		Task.Run(() =>
		{
			try
			{
				var difficultyService = new InterludeDifficultyService();
				var difficulty = InterludeDifficultyService.CalculateDifficulty(osuFile, rate);

				Schedule(() =>
				{
					_yavsrgDifficulty = difficulty;
					if (_currentOsuFile?.FilePath == osuFile.FilePath) UpdateDifficultyStats(_currentOsuFile);
				});
			}
			catch (Exception ex)
			{
				Logger.Info($"[YAVSRG] Difficulty calculation failed: {ex.Message}");
				Schedule(() =>
				{
					_yavsrgDifficulty = null;
					if (_currentOsuFile?.FilePath == osuFile.FilePath) UpdateDifficultyStats(_currentOsuFile);
				});
			}
		});
	}

	private void LoadSunnyDifficulty(OsuFile osuFile, float rate = 1.0f)
	{
		// Only calculate for mania maps
		if (osuFile.Mode != 3)
		{
			_sunnyDifficulty = null;
			return;
		}

		// Calculate Sunny difficulty in background
		Task.Run(() =>
		{
			try
			{
				var difficultyService = new SunnyDifficultyService();
				var difficulty = SunnyDifficultyService.CalculateDifficulty(osuFile, rate);

				Schedule(() =>
				{
					_sunnyDifficulty = difficulty > 0 ? difficulty : null;
					if (_currentOsuFile?.FilePath == osuFile.FilePath) UpdateDifficultyStats(_currentOsuFile);
				});
			}
			catch (Exception ex)
			{
				Logger.Info($"[Sunny] Difficulty calculation failed: {ex.Message}");
				Schedule(() =>
				{
					_sunnyDifficulty = null;
					if (_currentOsuFile?.FilePath == osuFile.FilePath) UpdateDifficultyStats(_currentOsuFile);
				});
			}
		});
	}

	public void SetNoMap()
	{
		_currentOsuFile = null;
		_yavsrgDifficulty = null;
		_sunnyDifficulty = null;

		_artistTitleText.Text = "No map loaded";
		_mapperDiffText.Text = "Select a beatmap or drop a .osu file";
		_sourceText.Text = "";
		_tagsText.Text = "";
		_difficultyStatsText.Text = "";
		_timingStatsText.Text = "";
		_beatmapIdContainer.Clear();
		_sunnyDifficultyText.Text = "";
		ClearBackground();
		ClearMsdAnalysis();
		ClearPatternAnalysis();

		// Hide not connected overlay
		_notConnectedOverlay.FadeTo(0, 200);
	}

	/// <summary>
	/// Shows the "osu! not connected" overlay.
	/// </summary>
	public void SetNotConnected()
	{
		_notConnectedOverlay.FadeTo(1, 200);
	}

	/// <summary>
	/// Hides the "osu! not connected" overlay.
	/// </summary>
	public void SetConnected()
	{
		_notConnectedOverlay.FadeTo(0, 200);
	}

	private void LoadMsdAnalysis(OsuFile osuFile)
	{
		// Detect rate from DT/HT mods
		// DT/NC = 1.5x, HT = 0.75x, no mod = 1.0x
		var rate = ProcessDetector.GetCurrentRateFromMods();

		// Don't re-analyze the same beatmap at the same rate (only if not currently loading)
		if (_currentBeatmapPath == osuFile.FilePath && Math.Abs(_lastMsdRate - rate) < 0.01f && !_msdChart.IsLoading)
			return;

		// Cancel any pending MSD analysis
		_msdCancellation?.Cancel();
		_msdCancellation = new CancellationTokenSource();
		var token = _msdCancellation.Token;

		// Only analyze supported mania key counts (4K/6K/7K for MinaCalc 5.15+, 4K only for 5.05)
		if (osuFile.Mode != 3 || !ToolPaths.IsKeyCountSupported(osuFile.CircleSize))
		{
			_msdChart.ShowError($"MSD: {ToolPaths.SupportedKeyCountsDisplay} mania only");
			return;
		}

		// Check if selected msd-calculator exists
		if (!ToolPaths.MsdCalculatorExists)
		{
			_msdChart.ShowError("msd-calculator not found");
			return;
		}

		_currentBeatmapPath = osuFile.FilePath;
		_lastMsdRate = rate;
		_msdChart.ShowLoading();

		// Log rate detection
		if (Math.Abs(rate - 1.0f) > 0.01f) Logger.Info($"[MSD] Detected rate mod: {rate:F2}x");

		// Run analysis in background with detected rate
		Task.Run(async () =>
		{
			try
			{
				// Run user's selected calculator for MSD chart display
				var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
				var result = await analyzer.AnalyzeSingleRateAsync(osuFile.FilePath, rate);

				if (token.IsCancellationRequested)
					return;

				// Also run MinaCalc 515 specifically for dan classification (model trained on 515 data)
				SkillsetScores? scores515 = null;
				if (ToolPaths.MsdCalculator515Exists)
					try
					{
						var analyzer515 = new MsdAnalyzer(ToolPaths.MsdCalculator515);
						var result515 = await analyzer515.AnalyzeSingleRateAsync(osuFile.FilePath, rate);
						scores515 = result515?.Scores;
					}
					catch (Exception ex515)
					{
						Logger.Info($"[MSD] MinaCalc 515 analysis failed: {ex515.Message}");
					}

				// Fall back to selected calculator scores if 515 not available
				var scoresForDan = scores515 ?? result?.Scores;

				Schedule(() =>
				{
					if (!token.IsCancellationRequested)
					{
						if (result != null) _msdChart.SetSingleRateResult(result);

						// Pass MinaCalc 515 scores to pattern display for dan classification
						if (scoresForDan != null) _patternDisplay.SetMsdScores(scoresForDan, rate);
					}
				});
			}
			catch (Exception ex)
			{
				Logger.Info($"[MSD] Analysis failed: {ex.Message}");

				if (token.IsCancellationRequested)
					return;

				Schedule(() =>
				{
					if (!token.IsCancellationRequested)
						_msdChart.ShowError($"Error: {ex.Message.Split('\n')[0]}");
				});
			}
		}, token);
	}

	private void ClearMsdAnalysis()
	{
		_msdCancellation?.Cancel();
		_currentBeatmapPath = null;
		_msdChart.Clear();
	}

	/// <summary>
	/// Refreshes the MSD analysis for the current map (e.g., when mods change).
	/// Also refreshes Interlude and Sunny calculations with the new rate.
	/// </summary>
	public void RefreshMsdAnalysis()
	{
		if (_currentOsuFile != null)
		{
			// Get current rate from mods
			var rate = ProcessDetector.GetCurrentRateFromMods();

			// Reset the path to force re-analysis
			_currentBeatmapPath = null;
			_lastMsdRate = 0;
			LoadMsdAnalysis(_currentOsuFile);

			// Also refresh Interlude and Sunny with new rate
			LoadYavsrgDifficulty(_currentOsuFile, rate);
			LoadSunnyDifficulty(_currentOsuFile, rate);
			_patternDisplay.RefreshLnDan(_currentOsuFile, rate);
		}
	}

	private void LoadPatternAnalysis(OsuFile osuFile)
	{
		// Cancel any pending pattern analysis
		_patternCancellation?.Cancel();
		_patternCancellation = new CancellationTokenSource();
		var token = _patternCancellation.Token;

		// Only analyze 4K mania maps
		if (osuFile.Mode != 3 || Math.Abs(osuFile.CircleSize - 4.0) > 0.1)
		{
			_patternDisplay.ShowError("4K mania only");
			return;
		}

		_patternDisplay.ShowLoading();

		// Run analysis in background
		Task.Run(() =>
		{
			try
			{
				var patternFinder = new PatternFinder(osuFile);
				var result = PatternFinder.FindAllPatterns(osuFile);

				if (token.IsCancellationRequested)
					return;

				Schedule(() =>
				{
					if (!token.IsCancellationRequested)
						_patternDisplay.SetPatternResult(result, osuFile);
				});
			}
			catch (Exception ex)
			{
				Logger.Info($"[Pattern] Analysis failed: {ex.Message}");

				if (token.IsCancellationRequested)
					return;

				Schedule(() =>
				{
					if (!token.IsCancellationRequested)
						_patternDisplay.ShowError($"Error: {ex.Message.Split('\n')[0]}");
				});
			}
		}, token);
	}

	private void ClearPatternAnalysis()
	{
		_patternCancellation?.Cancel();
		_patternDisplay.Clear();
	}

	private void LoadBackground(string? backgroundPath)
	{
		if (string.IsNullOrEmpty(backgroundPath) || !File.Exists(backgroundPath))
		{
			ClearBackground();
			return;
		}

		// Don't reload if same background
		if (_currentBackgroundPath == backgroundPath)
			return;

		_currentBackgroundPath = backgroundPath;

		try
		{
			// Load texture from file using ImageSharp
			// Note: Do NOT dispose the image - TextureUpload takes ownership and disposes it
			using var stream = File.OpenRead(backgroundPath);
			var image = Image.Load<Rgba32>(stream);

			var texture = Renderer.CreateTexture(image.Width, image.Height);
			// TextureUpload takes ownership of the image and will dispose it after upload
			texture.SetData(new TextureUpload(image));

			Schedule(() =>
			{
				_backgroundSprite.Texture = texture;
				_backgroundSprite.FadeTo(1, 200);
				_backgroundDimOverlay.FadeTo(1, 200);
			});
		}
		catch
		{
			// Failed to load background, use default
			ClearBackground();
		}
	}

	private void ClearBackground()
	{
		_currentBackgroundPath = null;
		_backgroundSprite.FadeTo(0, 200);
		_backgroundDimOverlay.FadeTo(0, 200);
	}
}
