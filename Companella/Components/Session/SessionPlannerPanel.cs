using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Models.Session;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Session;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// Panel for planning and generating structured practice sessions.
/// Uses an interactive MSD curve graph for customizing session difficulty progression.
/// </summary>
public partial class SessionPlannerPanel : CompositeDrawable
{
	[Resolved] private MapsDatabaseService MapsDatabase { get; set; } = null!;

	[Resolved] private OsuCollectionService CollectionService { get; set; } = null!;

	[Resolved] private SkillsTrendAnalyzer TrendAnalyzer { get; set; } = null!;

	private SessionPlannerService? _plannerService;
	private BeatmapIndexer? _beatmapIndexer;

	private MsdCurveGraph _curveGraph = null!;
	private DurationSlider _durationSlider = null!;
	private StyledButton _generateButton = null!;
	private StyledButton _resetButton = null!;
	private StyledButton _fromSessionsButton = null!;
	private SessionModeDropdown _sessionModeDropdown = null!;
	private TextFlowContainer _statusText = null!;
	private TextFlowContainer _summaryText = null!;
	private FillFlowContainer _previewContainer = null!;
	private SessionPlanningSpinner _loadingSpinner = null!;

	private SkillsTrendResult? _currentTrends;
	private SessionPlan? _currentPlan;
	private bool _isGenerating;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	/// <summary>
	/// Event raised when a loading operation starts.
	/// </summary>
	public event Action<string>? LoadingStarted;

	/// <summary>
	/// Event raised to update the loading status.
	/// </summary>
	public event Action<string>? LoadingStatusChanged;

	/// <summary>
	/// Event raised when a loading operation finishes.
	/// </summary>
	public event Action? LoadingFinished;

	public SessionPlannerPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		_beatmapIndexer = new BeatmapIndexer();
		_plannerService = new SessionPlannerService(MapsDatabase, CollectionService, _beatmapIndexer);
		_plannerService.ProgressChanged += OnPlannerProgressChanged;

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				SettingsLayout.CreateHint("Right-click: add/remove points. Left-click point: cycle skillset. Drag: move points."),
				_curveGraph = new MsdCurveGraph
				{
					RelativeSizeAxes = Axes.X,
					Height = 180
				},
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 4),
					Children = new Drawable[]
					{
						SettingsLayout.CreateHint("Duration:"),
						_durationSlider = new DurationSlider
						{
							RelativeSizeAxes = Axes.X
						}
					}
				},
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8),
					Children = new Drawable[]
					{
						_generateButton = new StyledButton("Generate Session", StyledButton.Theme.Accent)
						{
							RelativeSizeAxes = Axes.X,
							Height = 32,
							TooltipText = "Generate a practice session based on the curve"
						},
						SettingsLayout.CreateButtonRow(28,
							_resetButton = new StyledButton("Reset", StyledButtonAppearance.Muted)
							{
								RelativeSizeAxes = Axes.Both,
								FontSize = 14,
								Bold = false,
								TooltipText = "Reset curve to default"
							},
							_sessionModeDropdown = new SessionModeDropdown
							{
								RelativeSizeAxes = Axes.Both
							}),
						SettingsLayout.CreateButtonRow(28,
							_fromSessionsButton = new StyledButton("From Sessions", StyledButtonAppearance.Muted)
							{
								RelativeSizeAxes = Axes.Both,
								FontSize = 14,
								Bold = false,
								TooltipText = "Generate curve from your session history"
							},
							new Container()),
						_loadingSpinner = new SessionPlanningSpinner
						{
							Size = new Vector2(20),
							Alpha = 0
						}
					}
				},
				_statusText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.DisabledLabel),
				_summaryText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.Accent, 0),
				_previewContainer = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 4),
					Alpha = 0
				}
			}
		};

		InternalChild = SettingsLayout.CreateSection(
			"Session Planner",
			"Build a custom MSD practice curve and generate a session from it",
			content);

		_statusText.Text = "Customize the curve and click Generate to create a session";

		// Wire up events
		_generateButton.Clicked += OnGenerateClicked;
		_resetButton.Clicked += OnResetClicked;
		_fromSessionsButton.Clicked += OnFromSessionsClicked;
		_durationSlider.Current.BindValueChanged(OnDurationChanged, true);
		_curveGraph.CurveChanged += OnCurveChanged;

		// Apply any trends that were set before load completed
		if (_currentTrends != null)
		{
			_fromSessionsButton.SetEnabled(_currentTrends.TotalPlays >= 5);
			_statusText.Text = $"Skill level from analysis: {_currentTrends.OverallSkillLevel:F1} MSD";
		}
		else
		{
			_fromSessionsButton.SetEnabled(false);
		}
	}

	private void OnCurveChanged()
	{
		UpdateStatusText();
	}

	private void OnDurationChanged(ValueChangedEvent<int> e)
	{
		_curveGraph.Config.TotalSessionMinutes = e.NewValue;
		UpdateStatusText();
	}

	private void UpdateStatusText()
	{
		var config = _curveGraph.Config;

		// Count skillsets used
		var skillsets = config.Points
			.Where(p => p.Skillset != null)
			.Select(p => p.Skillset)
			.Distinct()
			.ToList();

		var skillsetInfo = skillsets.Count > 0 ? $" | Skills: {string.Join(", ", skillsets)}" : "";
		_statusText.Text =
			$"MSD: {config.MinMsd:F1}-{config.MaxMsd:F1} | {config.TotalSessionMinutes}min | {config.Points.Count} pts{skillsetInfo}";
	}

	private void OnResetClicked()
	{
		_curveGraph.ResetToDefault();
	}

	private void OnFromSessionsClicked()
	{
		if (_currentTrends == null)
		{
			_statusText.Text = "No session data available. Run Skills Analysis first.";
			return;
		}

		var mode = _sessionModeDropdown.Current.Value;
		var sessionDuration = _durationSlider.Current.Value;
		var generatedConfig = MsdCurveConfig.GenerateFromTrends(_currentTrends, mode, sessionDuration);
		if (generatedConfig == null)
		{
			_statusText.Text = "Not enough session data to generate a curve (need at least 5 plays).";
			return;
		}

		// Apply the generated config to the graph
		_curveGraph.Config = generatedConfig;
		_curveGraph.Redraw();

		var modeLabel = mode switch
		{
			SessionGenerationMode.Push => " (Push - strongest skills +1 MSD)",
			SessionGenerationMode.Acc => " (Acc - 98%+ accuracy range)",
			SessionGenerationMode.Fix => " (Fix - weakest skills +1 MSD)",
			_ => ""
		};
		_statusText.Text = $"Curve generated from {_currentTrends.TotalPlays} plays{modeLabel}";
		UpdateStatusText();
	}

	private async void OnGenerateClicked()
	{
		if (_isGenerating || _plannerService == null)
			return;

		_isGenerating = true;
		_generateButton.SetEnabled(false);
		_loadingSpinner.FadeTo(1, 100);
		_previewContainer.FadeTo(0, 100);
		_summaryText.FadeTo(0, 100);

		LoadingStarted?.Invoke("Generating session plan...");

		try
		{
			var curveConfig = _curveGraph.Config.Clone();
			var plan = await _plannerService.GenerateFromCurveAsync(curveConfig);

			if (plan != null)
			{
				_currentPlan = plan;
				DisplayPlan(plan);
				_statusText.Text = $"Session created: {plan.CollectionName}";
				_summaryText.Text = plan.Summary;
				_summaryText.FadeTo(1, 200);
			}
			else
			{
				_statusText.Text = "Failed to generate session plan. Check that maps are indexed.";
			}
		}
		catch (Exception ex)
		{
			_statusText.Text = $"Error: {ex.Message}";
			Logger.Info($"[SessionPlanner] Error: {ex}");
		}
		finally
		{
			_isGenerating = false;
			_generateButton.SetEnabled(true);
			_loadingSpinner.FadeTo(0, 100);
			LoadingFinished?.Invoke();
		}
	}

	private void OnPlannerProgressChanged(object? sender, SessionPlanProgressEventArgs e)
	{
		Schedule(() =>
		{
			_statusText.Text = e.Status;
			LoadingStatusChanged?.Invoke($"{e.Percentage}%: {e.Status}");
		});
	}

	private void DisplayPlan(SessionPlan plan)
	{
		_previewContainer.Clear();

		var items = plan.Items;
		if (items.Count == 0)
			return;

		var minMsd = items.Min(i => i.ActualMsd);
		var maxMsd = items.Max(i => i.ActualMsd);
		var avgMsd = items.Average(i => i.ActualMsd);

		// Group by skillset
		var skillsetGroups = items.GroupBy(i => i.Skillset).ToList();
		var skillsetSummary = string.Join(", ", skillsetGroups.Select(g => $"{g.Key}: {g.Count()}"));

		_previewContainer.Add(new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Masking = true,
			CornerRadius = 4,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(_accentColor.R, _accentColor.G, _accentColor.B, 0.15f)
				},
				SettingsLayout.CreateWrappingText(
					$"{items.Count} maps | MSD: {minMsd:F1}-{maxMsd:F1} | ~{plan.TotalDurationMinutes:F0} min",
					14,
					new Color4(160, 160, 160, 255))
			}
		});

		// Show skillset breakdown if multiple
		if (skillsetGroups.Count > 1)
			_previewContainer.Add(new SpriteText
			{
				Text = skillsetSummary,
				Font = new FontUsage("", 13),
				Colour = new Color4(140, 140, 140, 255)
			});

		_previewContainer.FadeTo(1, 200);
	}

	/// <summary>
	/// Sets the current skill trends to enable "From Sessions" button.
	/// </summary>
	public void SetTrends(SkillsTrendResult? trends)
	{
		_currentTrends = trends;

		// Enable/disable the From Sessions button based on trends availability
		// (do this before early return so it works even if called before load completes)
		_fromSessionsButton?.SetEnabled(trends != null && trends.TotalPlays >= 5);

		if (_curveGraph == null)
			return;

		if (trends != null) _statusText.Text = $"Skill level from analysis: {trends.OverallSkillLevel:F1} MSD";
	}

	protected override void Dispose(bool isDisposing)
	{
		if (_plannerService != null) _plannerService.ProgressChanged -= OnPlannerProgressChanged;

		base.Dispose(isDisposing);
	}
}

/// <summary>
/// Slider for session duration selection.
/// </summary>
public partial class DurationSlider : CompositeDrawable
{
	public BindableInt Current { get; } = new(80)
	{
		MinValue = 30,
		MaxValue = 180
	};

	private DurationSliderBar _slider = null!;
	private SpriteText _valueText = null!;

	public DurationSlider()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new Container
		{
			RelativeSizeAxes = Axes.X,
			Height = 24,
			Children = new Drawable[]
			{
				_slider = new DurationSliderBar
				{
					RelativeSizeAxes = Axes.X,
					Height = 20,
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Current = Current
				},
				_valueText = new SpriteText
				{
					Font = new FontUsage("", 14),
					Colour = new Color4(255, 102, 170, 255),
					Anchor = Anchor.CentreRight,
					Origin = Anchor.CentreRight
				}
			}
		};

		Current.BindValueChanged(e => _valueText.Text = $"{e.NewValue} min", true);
	}
}

public partial class DurationSliderBar : BasicSliderBar<int>
{
	[BackgroundDependencyLoader]
	private void load()
	{
		BackgroundColour = StyledButton.Theme.DialogInsetBg;
		SelectionColour = StyledButton.Theme.Accent;
	}
}

/// <summary>
/// Loading spinner for session planning.
/// </summary>
public partial class SessionPlanningSpinner : CompositeDrawable
{
	private Box _spinner = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = _spinner = new Box
		{
			RelativeSizeAxes = Axes.Both,
			Colour = new Color4(255, 102, 170, 255),
			Anchor = Anchor.Centre,
			Origin = Anchor.Centre
		};
	}

	protected override void Update()
	{
		base.Update();
		_spinner.Rotation += (float)(Clock.ElapsedFrameTime * 0.2);
	}
}

/// <summary>
/// Dropdown for selecting session generation mode.
/// </summary>
public partial class SessionModeDropdown : CompositeDrawable
{
	public Bindable<SessionGenerationMode> Current { get; } = new(SessionGenerationMode.Normal);

	private Container _buttonContainer = null!;
	private Box _buttonBackground = null!;
	private SpriteText _label = null!;
	private SpriteText _arrow = null!;
	private Container _dropdownMenu = null!;
	private FillFlowContainer _menuItems = null!;
	private bool _isOpen;

	private static readonly Dictionary<SessionGenerationMode, string> _modeLabels = new()
	{
		{ SessionGenerationMode.Normal, "Normal" },
		{ SessionGenerationMode.Push, "Push" },
		{ SessionGenerationMode.Acc, "Acc" },
		{ SessionGenerationMode.Fix, "Fix" }
	};

	private readonly Color4 _backgroundColor = new(60, 60, 65, 255);
	private readonly Color4 _hoverColor = new(75, 75, 80, 255);
	private readonly Color4 _menuColor = new(45, 45, 50, 255);

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			_buttonContainer = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				CornerRadius = 4,
				Children = new Drawable[]
				{
					_buttonBackground = new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = _backgroundColor
					},
					_label = new SpriteText
					{
						Text = _modeLabels[Current.Value],
						Font = new FontUsage("", 13),
						Colour = new Color4(200, 200, 200, 255),
						Anchor = Anchor.CentreLeft,
						Origin = Anchor.CentreLeft,
						X = 8
					},
					_arrow = new SpriteText
					{
						Text = "v",
						Font = new FontUsage("", 10),
						Colour = new Color4(150, 150, 150, 255),
						Anchor = Anchor.CentreRight,
						Origin = Anchor.CentreRight,
						X = -6
					}
				}
			},
			_dropdownMenu = new Container
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Y = 26,
				Alpha = 0,
				Masking = true,
				CornerRadius = 4,
				Depth = -1000, // Render on top
				Children = new Drawable[]
				{
					new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = _menuColor
					},
					_menuItems = new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Padding = new MarginPadding(2),
						Spacing = new Vector2(0, 1),
						Children = CreateMenuItems()
					}
				}
			}
		};

		Current.BindValueChanged(e => _label.Text = _modeLabels[e.NewValue], true);
	}

	private Drawable[] CreateMenuItems()
	{
		var items = new List<Drawable>();
		foreach (var mode in Enum.GetValues<SessionGenerationMode>())
			items.Add(new SessionModeMenuItem(mode, _modeLabels[mode], () =>
			{
				Current.Value = mode;
				CloseMenu();
			}));

		return items.ToArray();
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (_isOpen)
			CloseMenu();
		else
			OpenMenu();
		return true;
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		// Prevent closing when clicking inside the dropdown area
		return true;
	}

	private void OpenMenu()
	{
		_isOpen = true;
		_dropdownMenu.FadeTo(1, 100);
		_buttonBackground.FadeColour(_hoverColor, 100);
		_arrow.Text = "^";
	}

	public void CloseMenu()
	{
		if (!_isOpen)
			return;
		_isOpen = false;
		_dropdownMenu.FadeTo(0, 100);
		_buttonBackground.FadeColour(_backgroundColor, 100);
		_arrow.Text = "v";
	}

	protected override bool OnHover(HoverEvent e)
	{
		_buttonBackground.FadeColour(_hoverColor, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		base.OnHoverLost(e);
		if (!_isOpen)
			_buttonBackground.FadeColour(_backgroundColor, 100);
	}

	protected override void Update()
	{
		base.Update();

		if (!_isOpen)
			return;

		// Check if mouse is outside both button and menu
		var inputManager = GetContainingInputManager();
		if (inputManager == null)
			return;

		var mousePos = inputManager.CurrentState.Mouse.Position;
		var buttonBounds = _buttonContainer.ScreenSpaceDrawQuad.AABBFloat;
		var menuBounds = _dropdownMenu.ScreenSpaceDrawQuad.AABBFloat;

		// Expand bounds slightly to prevent accidental closing
		buttonBounds = buttonBounds.Inflate(2);
		menuBounds = menuBounds.Inflate(2);

		if (!buttonBounds.Contains(mousePos) && !menuBounds.Contains(mousePos)) CloseMenu();
	}
}

/// <summary>
/// Menu item for session mode dropdown.
/// </summary>
public partial class SessionModeMenuItem : CompositeDrawable
{
	private readonly SessionGenerationMode _mode;
	private readonly string _label;
	private readonly Action _onClick;
	private Container _container = null!;
	private Box _hoverOverlay = null!;

	public SessionModeMenuItem(SessionGenerationMode mode, string label, Action onClick)
	{
		_mode = mode;
		_label = label;
		_onClick = onClick;

		RelativeSizeAxes = Axes.X;
		Height = 22;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = _container = new Container
		{
			RelativeSizeAxes = Axes.Both,
			Masking = true,
			CornerRadius = 3,
			Children = new Drawable[]
			{
				_hoverOverlay = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = Color4.White,
					Alpha = 0
				},
				new SpriteText
				{
					Text = _label,
					Font = new FontUsage("", 12),
					Colour = new Color4(200, 200, 200, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					X = 8
				}
			}
		};
	}

	protected override bool OnHover(HoverEvent e)
	{
		_hoverOverlay.FadeTo(0.1f, 80);
		return true;
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 80);
		base.OnHoverLost(e);
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		return true; // Capture mouse down
	}

	protected override bool OnClick(ClickEvent e)
	{
		_onClick?.Invoke();
		return true;
	}
}
