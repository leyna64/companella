using System.Globalization;
using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Components.Settings;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Companella.Components.Tools;

/// <summary>
/// Panel for creating marathon beatmaps by combining multiple maps.
/// </summary>
public partial class MarathonCreatorPanel : CompositeDrawable
{
	private FillFlowContainer _listContainer = null!;
	private StyledButton _addButton = null!;
	private StyledButton _addPauseButton = null!;
	private StyledTextBox _pauseDurationTextBox = null!;
	private StyledButton _createButton = null!;
	private StyledButton _clearButton = null!;
	private StyledButton _msdButton = null!;
	private StyledTextBox _titleTextBox = null!;
	private StyledTextBox _artistTextBox = null!;
	private StyledTextBox _creatorTextBox = null!;
	private StyledTextBox _versionTextBox = null!;
	private StyledTextBox _centerTextBox = null!;
	private StyledTextBox _odTextBox = null!;
	private StyledTextBox _hpTextBox = null!;
	private SettingsCheckbox _preserveBookmarksCheckbox = null!;
	private SpriteText _summaryText = null!;
	private SpriteText _durationText = null!;
	private SpriteText _glitchValueText = null!;

	// Preview components
	private Container _previewContainer = null!;
	private Sprite _previewSprite = null!;
	private SpriteText _previewStatusText = null!;
	private MarathonPreviewOverlay _previewOverlay = null!;
	private SpriteText _previewShardInfoText = null!;
	private StyledButton _resetBgPanZoomButton = null!;
	private CancellationTokenSource? _previewCancellation;

	// Preview throttling (1/30th second = ~33ms)
	private const double _previewThrottleMs = 33.33;
	private DateTime _lastPreviewTime = DateTime.MinValue;
	private bool _previewPending;
	private bool _panZoomOnlyUpdate; // True when only pan/zoom changed (optimized path)

	private readonly MarathonCreatorService _marathonService = new();

	[Resolved] private IRenderer Renderer { get; set; } = null!;

	private readonly BindableFloat _glitchIntensity = new(0f)
	{
		MinValue = 0f,
		MaxValue = 1f,
		Precision = 0.05f
	};

	private readonly List<MarathonEntry> _entries = new();
	private MarathonEntry _startBoundaryBreak = null!;
	private MarathonEntry _endBoundaryBreak = null!;
	private OsuFile? _currentBeatmap;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private const double _boundaryBreakDuration = 3.0; // 3 seconds

	/// <summary>
	/// Event raised when marathon creation is requested.
	/// </summary>
	public event Action<List<MarathonEntry>, MarathonMetadata>? CreateMarathonRequested;

	/// <summary>
	/// Event raised when MSD recalculation is requested for all entries.
	/// </summary>
	public event Action<List<MarathonEntry>>? RecalculateMsdRequested;


	public MarathonCreatorPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 12),
				Children = new Drawable[]
				{
					// Maps List Section
					CreateSection("Maps in Marathon", new Drawable[]
					{
						new Container
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Children = new Drawable[]
							{
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(8, 0),
									Children = new Drawable[]
									{
										_addButton = new StyledButton("Add Current Map")
										{
											Size = new Vector2(140, 32),
											Enabled = false,
											TooltipText = "Add the currently selected beatmap to the marathon"
										},
										_addPauseButton = new StyledButton("Pause")
										{
											Size = new Vector2(70, 32),
											AccentColor = new Color4(60, 180, 60, 255),
											TooltipText =
												"Add a pause break between maps. Ctrl+click: insert/replace pauses between every map."
										},
										_pauseDurationTextBox = new StyledTextBox
										{
											Size = new Vector2(40, 32),
											Text = "5",
											PlaceholderText = "sec"
										},
										_clearButton = new StyledButton("Clear All")
										{
											Size = new Vector2(90, 32),
											Enabled = false,
											AccentColor = StyledButton.Theme.DestructiveAccent,
											TooltipText = "Remove all entries from the marathon"
										},
										_msdButton = new StyledButton("MSD")
										{
											Size = new Vector2(50, 32),
											Enabled = false,
											TooltipText = "Recalculate MSD ratings for all entries"
										}
									}
								}
							}
						},
						// List container with scroll
						new Container
						{
							RelativeSizeAxes = Axes.X,
							Height = 200,
							Masking = true,
							CornerRadius = 6,
							Margin = new MarginPadding { Top = 8 },
							Children = new Drawable[]
							{
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = new Color4(30, 30, 35, 255)
								},
								new ChainedScrollContainer
								{
									RelativeSizeAxes = Axes.Both,
									ClampExtension = 10,
									ScrollbarVisible = true,
									Child = _listContainer = new FillFlowContainer
									{
										RelativeSizeAxes = Axes.X,
										AutoSizeAxes = Axes.Y,
										Direction = FillDirection.Vertical,
										Spacing = new Vector2(0, 4),
										Padding = new MarginPadding(8)
									}
								}
							}
						},
						// Summary
						new Container
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Margin = new MarginPadding { Top = 4 },
							Children = new Drawable[]
							{
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(16, 0),
									Children = new Drawable[]
									{
										_summaryText = new SpriteText
										{
											Text = "0 maps",
											Font = new FontUsage("", 15),
											Colour = new Color4(140, 140, 140, 255)
										},
										_durationText = new SpriteText
										{
											Text = "Total: 0:00",
											Font = new FontUsage("", 15),
											Colour = new Color4(140, 140, 140, 255)
										}
									}
								}
							}
						}
					}),
					// Metadata Section
					CreateSection("Marathon Metadata", new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 8),
							Children = new Drawable[]
							{
								CreateLabeledInput("Title", out _titleTextBox, "Marathon"),
								CreateLabeledInput("Artist", out _artistTextBox, "Various Artists"),
								CreateLabeledInput("Creator", out _creatorTextBox, "Companella"),
								CreateLabeledInput("Difficulty Name", out _versionTextBox, "Marathon"),
								// OD/HP row
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(12, 0),
									Children = new Drawable[]
									{
										CreateSmallLabeledInput("OD (empty=avg)", out _odTextBox, 100),
										CreateSmallLabeledInput("HP (empty=avg)", out _hpTextBox, 100)
									}
								},
								_preserveBookmarksCheckbox = new SettingsCheckbox
								{
									LabelText = "Preserve Bookmarks",
									IsChecked = false,
									TooltipText = "Keep bookmarks from source maps, adjusted for the marathon timeline"
								},
								CreateLabeledInput("Center Symbol (max 3)", out _centerTextBox, ""),
								CreateSymbolSelector(),
								CreateGlitchSlider()
							}
						}
					}),
					// Background Preview Section
					CreateSection(
						"Background Preview (click to select, drag to pan, Shift for axis-only pan, scroll to zoom)",
						new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 6),
							Children = new Drawable[]
							{
								// Preview container with 16:9 aspect ratio
								_previewContainer = new Container
								{
									RelativeSizeAxes = Axes.X,
									Height = 135, // 240 * 9/16 = 135 (for width ~240)
									Masking = true,
									CornerRadius = 6,
									Children = new Drawable[]
									{
										new Box
										{
											RelativeSizeAxes = Axes.Both,
											Colour = new Color4(20, 20, 25, 255)
										},
										_previewSprite = new Sprite
										{
											RelativeSizeAxes = Axes.Both,
											FillMode = FillMode.Fit,
											Anchor = Anchor.Centre,
											Origin = Anchor.Centre,
											Alpha = 0
										},
										_previewStatusText = new SpriteText
										{
											Text = "Add maps to see preview",
											Font = new FontUsage("", 14),
											Colour = new Color4(100, 100, 100, 255),
											Anchor = Anchor.Centre,
											Origin = Anchor.Centre
										},
										_previewOverlay = new MarathonPreviewOverlay()
									}
								},
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(8, 0),
									Children = new Drawable[]
									{
										new Container
										{
											RelativeSizeAxes = Axes.X,
											AutoSizeAxes = Axes.Y,
											Child = _previewShardInfoText = new SpriteText
											{
												RelativeSizeAxes = Axes.X,
												Font = new FontUsage("", 11),
												Colour = new Color4(200, 200, 200, 255),
												Padding = new MarginPadding { Left = 4 },
												Alpha = 0
											}
										},
										_resetBgPanZoomButton = new StyledButton("Reset pan & zoom")
										{
											Size = new Vector2(140, 28),
											Enabled = false,
											AccentColor = new Color4(70, 120, 160, 255),
											TooltipText =
												"Reset pan (center) and zoom (1.0) for the selected background shard"
										}
									}
								}
							}
						}
					}),
					// Create Button
					_createButton = new StyledButton("Create Marathon")
					{
						RelativeSizeAxes = Axes.X,
						Height = 40,
						Enabled = false,
						TooltipText = "Combine multiple beatmaps into a single marathon"
					}
				}
			}
		};

		// Wire up events
		_addButton.Clicked += OnAddClicked;
		_addPauseButton.Clicked += OnAddPauseClicked;
		_addPauseButton.CtrlClicked += OnInsertPausesBetweenAllMaps;
		_clearButton.Clicked += OnClearClicked;
		_msdButton.Clicked += OnMsdClicked;
		_createButton.Clicked += OnCreateClicked;
		_centerTextBox.Current.BindValueChanged(e =>
		{
			MarkPreviewNeedsUpdate();
			_ = GeneratePreviewAsync();
		});

		// Wire up preview overlay events
		_previewOverlay.PanChanged += OnOverlayPanChanged;
		_previewOverlay.ZoomChanged += OnOverlayZoomChanged;
		_previewOverlay.ShardSelectionInfoChanged += OnShardSelectionInfoChanged;
		_previewOverlay.SelectionChanged += OnPreviewOverlaySelectionChanged;
		_resetBgPanZoomButton.Clicked += OnResetBgPanZoomClicked;

		// Initialize locked boundary breaks (always present at start and end)
		_startBoundaryBreak = MarathonEntry.CreateLockedBreak(_boundaryBreakDuration);
		_endBoundaryBreak = MarathonEntry.CreateLockedBreak(_boundaryBreakDuration);
		RefreshList();
	}

	private static SettingsSection CreateSection(string title, Drawable[] content) =>
		SettingsLayout.CreateSection(title, content);

	private static Container CreateLabeledInput(string label, out StyledTextBox textBox, string defaultValue)
	{
		textBox = new StyledTextBox
		{
			RelativeSizeAxes = Axes.X,
			Height = 32,
			Text = defaultValue
		};

		return new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Child = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("Roboto-Regular", 13),
						Colour = new Color4(100, 100, 100, 255)
					},
					textBox
				}
			}
		};
	}

	private static Container CreateSmallLabeledInput(string label, out StyledTextBox textBox, float width)
	{
		textBox = new StyledTextBox
		{
			Size = new Vector2(width, 32),
			PlaceholderText = ""
		};

		return new Container
		{
			AutoSizeAxes = Axes.Both,
			Child = new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("Roboto-Regular", 13),
						Colour = new Color4(100, 100, 100, 255)
					},
					textBox
				}
			}
		};
	}

	// Symbol categories for the center text selector
	private static readonly string[] _greekUppercase =
	{
		"\u0391", "\u0392", "\u0393", "\u0394", "\u0395", "\u0396", "\u0397", "\u0398",
		"\u0399", "\u039A", "\u039B", "\u039C", "\u039D", "\u039E", "\u039F", "\u03A0",
		"\u03A1", "\u03A3", "\u03A4", "\u03A5", "\u03A6", "\u03A7", "\u03A8", "\u03A9"
	}; // A-O (Alpha to Omega)

	private static readonly string[] _greekLowercase =
	{
		"\u03B1", "\u03B2", "\u03B3", "\u03B4", "\u03B5", "\u03B6", "\u03B7", "\u03B8",
		"\u03B9", "\u03BA", "\u03BB", "\u03BC", "\u03BD", "\u03BE", "\u03BF", "\u03C0",
		"\u03C1", "\u03C3", "\u03C4", "\u03C5", "\u03C6", "\u03C7", "\u03C8", "\u03C9"
	}; // alpha to omega

	private static readonly string[] _specialSymbols =
	{
		"\u2190", "\u2191", "\u2192", "\u2193", // Arrows: left, up, right, down
		"\u2194", "\u2195", // Arrows: left-right, up-down
		"\u221E", "\u2022", "\u2020", "\u2021" // Misc: infinity, bullet, dagger, double dagger
	};

	private Container CreateSymbolSelector()
	{
		var symbolFlow = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Full,
			Spacing = new Vector2(2, 2)
		};

		// Add Greek uppercase
		foreach (var symbol in _greekUppercase) symbolFlow.Add(CreateSymbolButton(symbol));

		// Add Greek lowercase
		foreach (var symbol in _greekLowercase) symbolFlow.Add(CreateSymbolButton(symbol));

		// Add special symbols
		foreach (var symbol in _specialSymbols) symbolFlow.Add(CreateSymbolButton(symbol));

		return new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Child = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = "Insert Symbol:",
						Font = new FontUsage("", 13),
						Colour = new Color4(100, 100, 100, 255)
					},
					symbolFlow
				}
			}
		};
	}

	private StyledButton CreateSymbolButton(string symbol)
	{
		var btn = new StyledButton(symbol, StyledButtonAppearance.Custom)
		{
			Size = new Vector2(24, 24),
			UseUnicodeFont = true,
			FontSize = 14,
			ShowAccentBar = false,
			CustomNormalBg = new Color4(50, 50, 55, 255),
			CustomHoverBg = new Color4(70, 70, 80, 255)
		};
		btn.Clicked += () => InsertSymbol(symbol);
		return btn;
	}

	private void InsertSymbol(string symbol)
	{
		// Only allow up to 3 characters
		if (_centerTextBox.Text.Length >= 3)
			return;
		_centerTextBox.Text += symbol;
	}

	private Container CreateGlitchSlider()
	{
		return new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Child = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = "Glitch Effects:",
						Font = new FontUsage("", 13),
						Colour = new Color4(100, 100, 100, 255)
					},
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Horizontal,
						Spacing = new Vector2(8, 0),
						Children = new Drawable[]
						{
							new SpriteText
							{
								Text = "0%",
								Font = new FontUsage("", 13),
								Colour = new Color4(100, 100, 100, 255),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							},
							new Container
							{
								Width = 200,
								Height = 24,
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft,
								Children = new Drawable[]
								{
									// Track background
									new Container
									{
										RelativeSizeAxes = Axes.X,
										Height = 4,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Masking = true,
										CornerRadius = 2,
										Child = new Box
										{
											RelativeSizeAxes = Axes.Both,
											Colour = new Color4(60, 60, 65, 255)
										}
									},
									new GlitchSliderBar
									{
										RelativeSizeAxes = Axes.X,
										Height = 24,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Current = _glitchIntensity
									}
								}
							},
							new SpriteText
							{
								Text = "100%",
								Font = new FontUsage("", 13),
								Colour = new Color4(100, 100, 100, 255),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							},
							_glitchValueText = new SpriteText
							{
								Text = "0%",
								Font = new FontUsage("", 15, "Bold"),
								Colour = _accentColor,
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft,
								Margin = new MarginPadding { Left = 8 }
							}
						}
					}
				}
			}
		};
	}

	protected override void LoadComplete()
	{
		base.LoadComplete();

		_glitchIntensity.BindValueChanged(e =>
		{
			_glitchValueText.Text = $"{e.NewValue:P0}";
			MarkPreviewNeedsUpdate();
			_ = GeneratePreviewAsync();
		}, true);
	}

	private void MarkPreviewNeedsUpdate()
	{
		_panZoomOnlyUpdate = false; // Full update needed, invalidate cache
		_marathonService.InvalidatePreviewCache();
	}

	private void MarkPanZoomOnlyUpdate()
	{
		_panZoomOnlyUpdate = true; // Only pan/zoom changed, can use cached overlay
	}

	private async Task GeneratePreviewAsync()
	{
		// Throttle preview generation to max 30 fps
		var now = DateTime.UtcNow;
		var timeSinceLastPreview = (now - _lastPreviewTime).TotalMilliseconds;

		if (timeSinceLastPreview < _previewThrottleMs)
		{
			// If already pending, don't schedule another
			if (_previewPending)
				return;

			_previewPending = true;
			var delayMs = (int)(_previewThrottleMs - timeSinceLastPreview) + 1;

			// Schedule delayed generation
			Scheduler.AddDelayed(() =>
			{
				_previewPending = false;
				_ = GeneratePreviewAsync();
			}, delayMs);

			return;
		}

		_lastPreviewTime = now;

		// Cancel any existing preview generation
		_previewCancellation?.Cancel();
		_previewCancellation = new CancellationTokenSource();
		var token = _previewCancellation.Token;

		var mapEntries = _entries.Where(e => !e.IsPause && e.OsuFile != null).ToList();
		if (mapEntries.Count == 0)
		{
			Schedule(() =>
			{
				_previewSprite.FadeTo(0, 150);
				_previewStatusText.Text = "Add maps to see preview";
				_previewStatusText.FadeTo(1, 150);
			});
			return;
		}

		Schedule(() =>
		{
			_previewStatusText.Text = "Generating preview...";
			_previewStatusText.FadeTo(1, 100);
		});

		// Capture the pan/zoom flag and reset it
		var panZoomOnly = _panZoomOnlyUpdate;
		_panZoomOnlyUpdate = false;

		try
		{
			var previewBytes = await Task.Run(async () =>
			{
				return await _marathonService.GenerateBackgroundPreviewAsync(
					new List<MarathonEntry>(_entries),
					_centerTextBox.Text,
					_glitchIntensity.Value,
					480, 270,
					panZoomOnly,
					token
				);
			}, token);

			if (token.IsCancellationRequested)
				return;

			if (previewBytes == null || previewBytes.Length == 0)
			{
				Schedule(() => { _previewStatusText.Text = "Preview generation failed"; });
				return;
			}

			// Load the image as a texture
			Schedule(() =>
			{
				try
				{
					using var stream = new MemoryStream(previewBytes);
					using var image = Image.Load<Rgba32>(stream);

					var texture = Renderer.CreateTexture(image.Width, image.Height);
					texture.SetData(new TextureUpload(image.Clone()));

					_previewSprite.Texture = texture;
					_previewSprite.FadeTo(1, 200, Easing.OutQuint);
					_previewStatusText.FadeTo(0, 100);
				}
				catch
				{
					_previewStatusText.Text = "Failed to display preview";
				}
			});
		}
		catch (OperationCanceledException)
		{
			// Preview generation was cancelled
		}
		catch (Exception ex)
		{
			Schedule(() => { _previewStatusText.Text = $"Error: {ex.Message}"; });
		}
	}

	private partial class GlitchSliderBar : BasicSliderBar<float>
	{
		private readonly Color4 _accentColor = new(255, 102, 170, 255);

		[BackgroundDependencyLoader]
		private void load()
		{
			BackgroundColour = Color4.Transparent;
			SelectionColour = _accentColor;
		}
	}

	/// <summary>
	/// Sets the current beatmap that can be added to the list.
	/// </summary>
	public void SetCurrentBeatmap(OsuFile? osuFile)
	{
		_currentBeatmap = osuFile;
		_addButton.Enabled = osuFile != null;
	}

	private void OnAddClicked()
	{
		if (_currentBeatmap == null)
			return;

		// Check if already in list (only for non-pause entries)
		if (_entries.Any(e => !e.IsPause && e.OsuFile?.FilePath == _currentBeatmap.FilePath)) return; // Already added

		// Create entry
		var entry = MarathonEntry.FromOsuFile(_currentBeatmap);
		_entries.Add(entry);

		// Add to UI
		RefreshList();
		UpdateSummary();
		MarkPreviewNeedsUpdate();
		_ = GeneratePreviewAsync();
	}

	private void OnAddPauseClicked()
	{
		// Parse duration from text box
		if (!double.TryParse(_pauseDurationTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
				out var duration))
			duration = 5.0; // Default to 5 seconds

		duration = Math.Clamp(duration, 0.1, 300.0); // Clamp between 0.1s and 5 minutes

		// Create pause entry
		var pauseEntry = MarathonEntry.CreatePause(duration);
		_entries.Add(pauseEntry);

		// Add to UI
		RefreshList();
		UpdateSummary();
		// No need to regenerate preview for pause entries
	}

	/// <summary>
	/// Rebuilds the list as: map, pause, map, pause, ... using the duration field (seconds).
	/// </summary>
	private void OnInsertPausesBetweenAllMaps()
	{
		var mapsOnly = _entries.Where(e => !e.IsPause).ToList();
		if (mapsOnly.Count < 2)
			return;

		if (!double.TryParse(_pauseDurationTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
				out var duration))
			duration = 5.0;
		duration = Math.Clamp(duration, 0.1, 300.0);

		var rebuilt = new List<MarathonEntry>();
		for (var i = 0; i < mapsOnly.Count; i++)
		{
			rebuilt.Add(mapsOnly[i]);
			if (i < mapsOnly.Count - 1)
				rebuilt.Add(MarathonEntry.CreatePause(duration));
		}

		_entries.Clear();
		_entries.AddRange(rebuilt);
		RefreshList();
		UpdateSummary();
	}

	private void OnClearClicked()
	{
		_entries.Clear();
		RefreshList();
		UpdateSummary();
		MarkPreviewNeedsUpdate();
		_ = GeneratePreviewAsync();
	}

	private void OnMsdClicked()
	{
		if (_entries.Count == 0)
			return;
		RecalculateMsdRequested?.Invoke(new List<MarathonEntry>(_entries));
	}

	private void OnCreateClicked()
	{
		if (_entries.Count == 0)
			return;

		// Parse OD (optional)
		double? od = null;
		if (!string.IsNullOrWhiteSpace(_odTextBox.Text))
			if (double.TryParse(_odTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var odValue))
				od = Math.Clamp(odValue, 0, 10);

		// Parse HP (optional)
		double? hp = null;
		if (!string.IsNullOrWhiteSpace(_hpTextBox.Text))
			if (double.TryParse(_hpTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hpValue))
				hp = Math.Clamp(hpValue, 0, 10);

		var metadata = new MarathonMetadata
		{
			Title = _titleTextBox.Text,
			Artist = _artistTextBox.Text,
			Creator = _creatorTextBox.Text,
			Version = _versionTextBox.Text,
			CenterText = _centerTextBox.Text,
			GlitchIntensity = _glitchIntensity.Value,
			OD = od,
			HP = hp,
			PreserveBookmarks = _preserveBookmarksCheckbox.IsChecked
		};

		// Include boundary breaks in the final marathon
		var fullList = new List<MarathonEntry> { _startBoundaryBreak };
		fullList.AddRange(_entries);
		fullList.Add(_endBoundaryBreak);

		CreateMarathonRequested?.Invoke(fullList, metadata);
	}

	public void RefreshList()
	{
		_listContainer.Clear();

		// Build full list: start boundary + user entries + end boundary
		var displayList = new List<MarathonEntry> { _startBoundaryBreak };
		displayList.AddRange(_entries);
		displayList.Add(_endBoundaryBreak);

		for (var i = 0; i < displayList.Count; i++)
		{
			var entry = displayList[i];
			var entryRow = new MarathonEntryRow(entry, i, _accentColor)
			{
				RelativeSizeAxes = Axes.X,
				Height = entry.IsLocked ? 32 : entry.IsPause ? 52 : 72
			};
			entryRow.DeleteRequested += OnEntryDeleteRequested;
			entryRow.MoveUpRequested += OnEntryMoveUpRequested;
			entryRow.MoveDownRequested += OnEntryMoveDownRequested;
			entryRow.RateChanged += OnEntryRateChanged;
			entryRow.RatePitchAdjustChanged += OnEntryRatePitchAdjustChanged;
			_listContainer.Add(entryRow);
		}

		_clearButton.Enabled = _entries.Count > 0;
		_msdButton.Enabled = _entries.Count > 0;
		_createButton.Enabled = _entries.Count > 0;

		// Update overlay with current entries
		_previewOverlay.SetEntries(new List<MarathonEntry>(_entries));
	}

	private void OnEntryDeleteRequested(MarathonEntry entry)
	{
		// Locked entries cannot be deleted
		if (entry.IsLocked)
			return;

		var wasMapEntry = !entry.IsPause;
		_entries.Remove(entry);
		RefreshList();
		UpdateSummary();
		if (wasMapEntry)
		{
			MarkPreviewNeedsUpdate();
			_ = GeneratePreviewAsync();
		}
	}

	private void OnEntryMoveUpRequested(MarathonEntry entry)
	{
		// Locked entries cannot be moved
		if (entry.IsLocked)
			return;

		var index = _entries.IndexOf(entry);
		if (index > 0)
		{
			_entries.RemoveAt(index);
			_entries.Insert(index - 1, entry);
			RefreshList();
			if (!entry.IsPause)
			{
				MarkPreviewNeedsUpdate();
				_ = GeneratePreviewAsync();
			}
		}
	}

	private void OnEntryMoveDownRequested(MarathonEntry entry)
	{
		// Locked entries cannot be moved
		if (entry.IsLocked)
			return;

		var index = _entries.IndexOf(entry);
		if (index < _entries.Count - 1)
		{
			_entries.RemoveAt(index);
			_entries.Insert(index + 1, entry);
			RefreshList();
			if (!entry.IsPause)
			{
				MarkPreviewNeedsUpdate();
				_ = GeneratePreviewAsync();
			}
		}
	}

	private void OnEntryRateChanged(MarathonEntry entry, double newRate)
	{
		entry.Rate = newRate;
		UpdateSummary();
	}

	private void OnEntryRatePitchAdjustChanged(MarathonEntry entry, bool pitchAdjust)
	{
		entry.RatePitchAdjust = pitchAdjust;
	}

	private void OnOverlayZoomChanged(MarathonEntry entry, float newZoom)
	{
		// Entry's BackgroundZoom is already updated by the overlay
		// Use optimized pan/zoom-only path (skips overlay recalculation)
		MarkPanZoomOnlyUpdate();
		_ = GeneratePreviewAsync();
	}

	private void OnOverlayPanChanged(MarathonEntry entry, float newPanX, float newPanY)
	{
		// Entry's BackgroundPanX/Y are already updated by the overlay
		// Use optimized pan/zoom-only path (skips overlay recalculation)
		MarkPanZoomOnlyUpdate();
		_ = GeneratePreviewAsync();
	}

	private void OnShardSelectionInfoChanged(string text, float targetAlpha)
	{
		_previewShardInfoText.Text = text;
		_previewShardInfoText.FadeTo(targetAlpha, 100);
	}

	private void OnPreviewOverlaySelectionChanged(MarathonEntry? entry)
	{
		_resetBgPanZoomButton.Enabled = entry != null;
	}

	private void OnResetBgPanZoomClicked()
	{
		if (!_previewOverlay.TryResetSelectedPanAndZoom())
			return;

		MarkPanZoomOnlyUpdate();
		_ = GeneratePreviewAsync();
	}

	private void UpdateSummary()
	{
		// Only count actual maps (not pauses/breaks)
		var mapCount = _entries.Count(e => !e.IsPause);
		_summaryText.Text = $"{mapCount} map{(mapCount != 1 ? "s" : "")}";

		// Calculate total duration at rate (including boundary breaks)
		var totalMs = _entries.Sum(e => e.EffectiveDurationAtRate);
		totalMs += _startBoundaryBreak.EffectiveDurationAtRate;
		totalMs += _endBoundaryBreak.EffectiveDurationAtRate;
		var totalTime = TimeSpan.FromMilliseconds(totalMs);
		_durationText.Text = $"Total: {(int)totalTime.TotalMinutes}:{totalTime.Seconds:D2}";
	}

	/// <summary>
	/// Sets the enabled state of the create button.
	/// </summary>
	public void SetEnabled(bool enabled)
	{
		_createButton.Enabled = enabled && _entries.Count > 0;
		_addButton.Enabled = enabled && _currentBeatmap != null;
	}
}

/// <summary>
/// A single row in the marathon entry list.
/// </summary>
public partial class MarathonEntryRow : CompositeDrawable
{
	private readonly MarathonEntry _entry;
	private readonly int _index;
	private readonly Color4 _accentColor;

	private Box _background = null!;
	private StyledTextBox _rateTextBox = null!;
	private SettingsCheckbox? _pitchCheckbox;

	public event Action<MarathonEntry>? DeleteRequested;
	public event Action<MarathonEntry>? MoveUpRequested;
	public event Action<MarathonEntry>? MoveDownRequested;
	public event Action<MarathonEntry, double>? RateChanged;
	public event Action<MarathonEntry, bool>? RatePitchAdjustChanged;

	private readonly Color4 _normalBg = new(40, 40, 45, 255);
	private readonly Color4 _hoverBg = new(50, 50, 55, 255);
	private readonly Color4 _pauseBg = new(80, 80, 100, 255);
	private readonly Color4 _pauseHoverBg = new(90, 90, 115, 255);
	private readonly Color4 _lockedBreakBg = new(35, 35, 45, 255);
	private readonly Color4 _deleteBg = new(180, 60, 60, 255);
	private readonly Color4 _deleteHoverBg = new(200, 80, 80, 255);

	// Skillset colors matching the MSD charts
	private static readonly Dictionary<string, Color4> _skillsetColors = new()
	{
		{ "overall", new Color4(200, 200, 200, 255) },
		{ "stream", new Color4(100, 180, 255, 255) },
		{ "jumpstream", new Color4(100, 220, 100, 255) },
		{ "handstream", new Color4(255, 180, 100, 255) },
		{ "stamina", new Color4(180, 100, 255, 255) },
		{ "jackspeed", new Color4(255, 100, 100, 255) },
		{ "chordjack", new Color4(255, 220, 100, 255) },
		{ "technical", new Color4(100, 220, 220, 255) },
		{ "unknown", new Color4(150, 150, 150, 255) }
	};

	public MarathonEntryRow(MarathonEntry entry, int index, Color4 accentColor)
	{
		_entry = entry;
		_index = index;
		_accentColor = accentColor;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 4;

		var pauseColor = new Color4(80, 80, 100, 255);

		// Determine background color based on entry type
		Color4 bgColor;
		if (_entry.IsLocked)
			bgColor = _lockedBreakBg;
		else if (_entry.IsPause)
			bgColor = pauseColor;
		else
			bgColor = _normalBg;

		// Create background box first (need to store reference for hover effects)
		_background = new Box
		{
			RelativeSizeAxes = Axes.Both,
			Colour = bgColor
		};

		var children = new List<Drawable>
		{
			_background,
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Padding = new MarginPadding
					{ Left = 10, Right = 6, Top = _entry.IsLocked ? 4 : 6, Bottom = _entry.IsLocked ? 4 : 6 },
				Spacing = new Vector2(8, 0),
				Children = new Drawable[]
				{
					// Index number (smaller for locked entries)
					new Container
					{
						Size = new Vector2(24, _entry.IsLocked ? 20 : (_entry.IsPause ? 38 : 56)),
						Child = new SpriteText
						{
							Text = $"{_index + 1}.",
							Font = new FontUsage("", _entry.IsLocked ? 13 : 17, "Bold"),
							Colour = _entry.IsLocked ? new Color4(100, 100, 120, 255) : _accentColor,
							Anchor = Anchor.Centre,
							Origin = Anchor.Centre
						}
					},
					// Map info section, pause info, or locked break info
					_entry.IsLocked
						? CreateLockedBreakInfoSection()
						: _entry.IsPause
							? CreatePauseInfoSection()
							: CreateMapInfoSection()
				}
			}
		};

		// Only add action buttons for non-locked entries
		if (!_entry.IsLocked) children.Add(CreateActionButtonsSection());

		InternalChildren = children;

		if (!_entry.IsPause && !_entry.IsLocked)
		{
			_rateTextBox.Current.BindValueChanged(e => OnRateChanged(e.NewValue));
			if (_pitchCheckbox != null)
				_pitchCheckbox.CheckedChanged += pitch => RatePitchAdjustChanged?.Invoke(_entry, pitch);
		}
	}

	private void OnRateChanged(string text)
	{
		if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			var clampedRate = Math.Clamp(value, 0.1, 5.0);
			RateChanged?.Invoke(_entry, clampedRate);
		}
	}

	private FillFlowContainer CreateMapInfoSection()
	{
		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.X,
			RelativeSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Padding = new MarginPadding { Bottom = 60 },
			Spacing = new Vector2(0, 0),
			Children = new Drawable[]
			{
				new MarqueeText
				{
					Text = _entry.Title,
					Font = new FontUsage("", 15, "Bold"),
					Colour = Color4.White,
					Width = 200,
					Height = 16
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.X,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(6, 0),
					Children = new Drawable[]
					{
						new MarqueeText
						{
							Text = $"[{_entry.Version}]",
							Font = new FontUsage("", 13),
							Colour = new Color4(160, 160, 160, 255),
							Width = 100,
							Height = 16
						},
						new MarqueeText
						{
							Text = $"by {_entry.Creator}",
							Font = new FontUsage("", 13),
							Colour = new Color4(120, 120, 120, 255),
							Width = 75,
							Height = 16
						}
					}
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Padding = new MarginPadding { Top = 8 },
					Spacing = new Vector2(3, 0),
					Children = new Drawable[]
					{
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Overall ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("overall")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Stream ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("stream")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Jumpstream ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("jumpstream")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Handstream ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("handstream")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Stamina ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("stamina")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Jackspeed ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("jackspeed")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Chordjack ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("chordjack")
						},
						new SpriteText
						{
							Text = $"{_entry.MsdValues?.Technical ?? 0.0f}",
							Font = new FontUsage("", 13),
							Colour = _skillsetColors.GetValueOrDefault("technical")
						}
					}
				}
			}
		};
	}

	private FillFlowContainer CreatePauseInfoSection()
	{
		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.X,
			RelativeSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 4),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = "PAUSE",
					Font = new FontUsage("", 15, "Bold"),
					Colour = new Color4(180, 180, 220, 255)
				},
				new SpriteText
				{
					Text = $"{_entry.PauseDurationSeconds:F1} seconds",
					Font = new FontUsage("", 17),
					Colour = new Color4(140, 140, 180, 255)
				}
			}
		};
	}

	private FillFlowContainer CreateLockedBreakInfoSection()
	{
		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(8, 0),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = "BREAK",
					Font = new FontUsage("", 12, "Bold"),
					Colour = new Color4(90, 90, 110, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				},
				new SpriteText
				{
					Text = $"{_entry.PauseDurationSeconds:F0}s",
					Font = new FontUsage("", 12),
					Colour = new Color4(70, 70, 90, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				}
			}
		};
	}

	private static StyledButton CreateRowActionButton(string text, Action action, Color4 normalBg, Color4 hoverBg,
		float width)
	{
		var button = new StyledButton(text, StyledButtonAppearance.Custom)
		{
			Size = new Vector2(width, 28),
			UseUnicodeFont = true,
			FontSize = 14,
			ShowAccentBar = false,
			CustomNormalBg = normalBg,
			CustomHoverBg = hoverBg
		};
		button.Clicked += action;
		return button;
	}

	private FillFlowContainer CreateActionButtonsSection()
	{
		var children = new List<Drawable>();

		// Only show rate input for non-pause entries
		if (!_entry.IsPause)
			children.Add(new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Anchor = Anchor.CentreLeft,
				Origin = Anchor.CentreLeft,
				Children = new Drawable[]
				{
					new FillFlowContainer
					{
						AutoSizeAxes = Axes.Both,
						Direction = FillDirection.Horizontal,
						Spacing = new Vector2(2, 0),
						Children = new Drawable[]
						{
							new SpriteText
							{
								Text = "Rate:",
								Font = new FontUsage("", 11),
								Colour = new Color4(120, 120, 120, 255),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							},
							_rateTextBox = new StyledTextBox
							{
								Size = new Vector2(45, 24),
								Text = _entry.Rate.ToString("0.0#", CultureInfo.InvariantCulture),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							}
						}
					},
					_pitchCheckbox = new SettingsCheckbox
					{
						LabelText = "Pitch shift",
						LabelFontSize = 11,
						LabelColour = new Color4(120, 120, 120, 255),
						IsChecked = _entry.RatePitchAdjust,
						TooltipText =
							"When enabled, pitch follows the rate (DT/HT style). When disabled, pitch is preserved."
					}
				}
			});
		else
		{
			// For pause entries, create dummy textbox (not displayed but needed to avoid null reference)
			_rateTextBox = new StyledTextBox { Alpha = 0, Size = Vector2.Zero };
			_pitchCheckbox = null;
		}

		children.Add(CreateRowActionButton("\u2191", OnMoveUp, new Color4(70, 70, 75, 255), new Color4(90, 90, 95, 255), 28));
		children.Add(CreateRowActionButton("\u2193", OnMoveDown, new Color4(70, 70, 75, 255), new Color4(90, 90, 95, 255), 28));
		children.Add(CreateRowActionButton("\u2190", OnDelete, _deleteBg, _deleteHoverBg, 32));

		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(4, 0),
			Anchor = Anchor.CentreRight,
			Origin = Anchor.CentreRight,
			Padding = new MarginPadding { Right = 8 },
			Children = children
		};
	}

	private void OnMoveUp()
	{
		MoveUpRequested?.Invoke(_entry);
	}

	private void OnMoveDown()
	{
		MoveDownRequested?.Invoke(_entry);
	}

	private void OnDelete()
	{
		DeleteRequested?.Invoke(_entry);
	}

	/// <summary>
	/// Gets the color for the MSD value based on the dominant skillset.
	/// </summary>
	private Color4 GetMsdColor()
	{
		if (_entry.MsdValues == null)
			return _skillsetColors["unknown"];

		var (dominantSkillset, _) = _entry.MsdValues.GetDominantSkillset();
		return _skillsetColors.GetValueOrDefault(dominantSkillset.ToLowerInvariant(), _skillsetColors["unknown"]);
	}

	protected override bool OnHover(HoverEvent e)
	{
		// Locked entries don't change on hover
		if (_entry.IsLocked)
			return base.OnHover(e);

		_background.FadeColour(_entry.IsPause ? _pauseHoverBg : _hoverBg, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		// Locked entries don't change on hover
		if (_entry.IsLocked)
		{
			base.OnHoverLost(e);
			return;
		}

		_background.FadeColour(_entry.IsPause ? _pauseBg : _normalBg, 100);
		base.OnHoverLost(e);
	}
}
