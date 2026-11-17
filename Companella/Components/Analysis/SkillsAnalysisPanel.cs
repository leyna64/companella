using System.Globalization;
using Companella.Components.Charts;
using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Models.Session;
using Companella.Models.Training;
using Companella.Services.Analysis;
using Companella.Services.Common;
using Companella.Services.Database;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Analysis;

/// <summary>
/// Panel for analyzing skill trends over time and getting map recommendations.
/// Combines: TimeRegionSelector, SkillsOverTimeChart, skill level stats, and MapRecommendationPanel.
/// </summary>
public partial class SkillsAnalysisPanel : CompositeDrawable
{
	[Resolved] private SessionDatabaseService DatabaseService { get; set; } = null!;

	[Resolved] private SkillsTrendAnalyzer TrendAnalyzer { get; set; } = null!;

	private SkillsOverTimeChart _skillsChart = null!;
	private FillFlowContainer _statsContainer = null!;
	private Container _peakSkillLevelsContainer = null!;
	private Container _skillLevelsContainer = null!;
	private MapRecommendationPanel _recommendationPanel = null!;
	private TextFlowContainer _statusText = null!;
	private TextFlowContainer _trendSummaryText = null!;

	// Range input fields
	private BasicTextBox _msdMinInput = null!;
	private BasicTextBox _msdMaxInput = null!;
	private BasicTextBox _accMinInput = null!;
	private BasicTextBox _accMaxInput = null!;

	private SkillsTrendResult? _currentTrends;
	private HashSet<string> _selectedSkillsets = new();

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	/// <summary>
	/// Event raised when a map recommendation is selected.
	/// </summary>
	public event Action<MapRecommendation>? MapSelected;

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

	/// <summary>
	/// Event raised when trends are updated (for session planner integration).
	/// </summary>
	public event Action<SkillsTrendResult?>? TrendsUpdated;

	/// <summary>
	/// Event raised when the panel requests to show the osu! restart dialog.
	/// Parameters are (title, message).
	/// </summary>
	public event Action<string, string>? RestartOsuRequested;

	public SkillsAnalysisPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Padding = new MarginPadding { Bottom = 40 },
			Children = new Drawable[]
			{
				_statusText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.DisabledLabel),
				_trendSummaryText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.Accent, 0),
					// Stats container
					_statsContainer = new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 6),
						Alpha = 0,
						Children = new Drawable[]
						{
							_peakSkillLevelsContainer = new Container
							{
								RelativeSizeAxes = Axes.X,
								AutoSizeAxes = Axes.Y
							},
							_skillLevelsContainer = new Container
							{
								RelativeSizeAxes = Axes.X,
								AutoSizeAxes = Axes.Y
							}
						}
					},
					// Skills over time chart
					new Container
					{
						RelativeSizeAxes = Axes.X,
						Height = 220,
						Masking = true,
						CornerRadius = StyledDialog.CornerRadius,
						Child = _skillsChart = new SkillsOverTimeChart
						{
							RelativeSizeAxes = Axes.Both
						}
					},
					// Chart range controls
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 8),
						Children = new Drawable[]
						{
							SettingsLayout.CreateRow(
								CreateRangeInput("MSD Min:", out _msdMinInput,
									SkillsOverTimeChart.DefaultMsdMin.ToString("F0", CultureInfo.InvariantCulture)),
								CreateRangeInput("MSD Max:", out _msdMaxInput,
									SkillsOverTimeChart.DefaultMsdMax.ToString("F0", CultureInfo.InvariantCulture))),
							SettingsLayout.CreateRow(
								CreateRangeInput("Acc Min:", out _accMinInput,
									SkillsOverTimeChart.DefaultAccuracyMin.ToString("F0", CultureInfo.InvariantCulture)),
								CreateRangeInput("Acc Max:", out _accMaxInput,
									SkillsOverTimeChart.DefaultAccuracyMax.ToString("F0", CultureInfo.InvariantCulture)))
						}
					},
					// Map recommendations panel
					_recommendationPanel = new MapRecommendationPanel()
				}
		};

		InternalChild = SettingsLayout.CreateSection(
			"Skills Analysis",
			"Analyze skill trends over time and get map recommendations",
			content);

		_statusText.Text = "Loading...";

		_recommendationPanel.MapSelected += rec => MapSelected?.Invoke(rec);

		// Forward loading events from recommendation panel
		_recommendationPanel.LoadingStarted += status => LoadingStarted?.Invoke(status);
		_recommendationPanel.LoadingStatusChanged += status => LoadingStatusChanged?.Invoke(status);
		_recommendationPanel.LoadingFinished += () => LoadingFinished?.Invoke();

		// Forward restart dialog request
		_recommendationPanel.RestartOsuRequested += (title, message) => RestartOsuRequested?.Invoke(title, message);

		// Wire up range input events - use value change for immediate feedback
		_msdMinInput.Current.BindValueChanged(_ => UpdateChartRanges());
		_msdMaxInput.Current.BindValueChanged(_ => UpdateChartRanges());
		_accMinInput.Current.BindValueChanged(_ => UpdateChartRanges());
		_accMaxInput.Current.BindValueChanged(_ => UpdateChartRanges());

		// Apply default range values to chart immediately
		UpdateChartRanges();

		// Analyze all time data on load
		AnalyzeTrends(TimeRegion.AllTime);
	}

	private static FillFlowContainer CreateRangeInput(string label, out BasicTextBox textBox, string defaultValue)
	{
		textBox = new BasicTextBox
		{
			Size = new Vector2(50, 24),
			Text = defaultValue,
			CornerRadius = 3,
			CommitOnFocusLost = true
		};

		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(4, 0),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = label,
					Font = new FontUsage("", 13),
					Colour = new Color4(150, 150, 150, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				},
				textBox
			}
		};
	}

	private void UpdateChartRanges()
	{
		float? msdMin = float.TryParse(_msdMinInput.Text, out var mMin) ? mMin : null;
		float? msdMax = float.TryParse(_msdMaxInput.Text, out var mMax) ? mMax : null;
		float? accMin = float.TryParse(_accMinInput.Text, out var aMin) ? aMin : null;
		float? accMax = float.TryParse(_accMaxInput.Text, out var aMax) ? aMax : null;

		_skillsChart.SetRanges(msdMin, msdMax, accMin, accMax);
	}

	/// <summary>
	/// Analyzes trends for the selected time region.
	/// </summary>
	private void AnalyzeTrends(TimeRegion region)
	{
		_statusText.Text = "Analyzing trends...";

		try
		{
			_currentTrends = TrendAnalyzer.AnalyzeTrends(region);

			if (_currentTrends.TotalPlays == 0)
			{
				_statusText.Text = $"No plays found for selected period";
				_trendSummaryText.Alpha = 0;
				_statsContainer.FadeTo(0, 200);
				_skillsChart.Clear();
				_recommendationPanel.SetTrends(null);
				TrendsUpdated?.Invoke(null);
				return;
			}

			// Update status
			var regionName = GetRegionDisplayName(region);
			_statusText.Text = $"Analyzed {_currentTrends.TotalPlays} plays ({regionName})";

			// Update trend summary
			UpdateTrendSummary();

			// Update skill levels display
			UpdateSkillLevelsDisplay();

			// Update chart
			_skillsChart.SetTrendData(_currentTrends);

			// Update recommendations panel
			_recommendationPanel.SetTrends(_currentTrends);

			// Notify listeners (for session planner integration)
			TrendsUpdated?.Invoke(_currentTrends);

			_statsContainer.FadeTo(1, 200);
			_trendSummaryText.FadeTo(1, 200);
		}
		catch (Exception ex)
		{
			_statusText.Text = $"Error: {ex.Message}";
			Logger.Info($"[SkillsAnalysis] Error analyzing trends: {ex.Message}");
		}
	}

	private void UpdateTrendSummary()
	{
		if (_currentTrends == null)
			return;

		var overallTrend = _currentTrends.TrendSlopes.GetValueOrDefault("overall", 0);
		var trendDirection = overallTrend > 0.3 ? "improving"
			: overallTrend < -0.3 ? "declining"
			: "stable";

		var strongest = _currentTrends.GetStrongestSkillsets(1).FirstOrDefault() ?? "N/A";
		var weakest = _currentTrends.GetWeakestSkillsets(1).FirstOrDefault() ?? "N/A";

		_trendSummaryText.Text =
			$"Trend: {trendDirection} | Best: {strongest} ({_currentTrends.CurrentSkillLevels.GetValueOrDefault(strongest, 0):F1}) | Weakest: {weakest} ({_currentTrends.CurrentSkillLevels.GetValueOrDefault(weakest, 0):F1})";
	}

	private void UpdateSkillLevelsDisplay()
	{
		_peakSkillLevelsContainer.Clear();
		_skillLevelsContainer.Clear();
		_selectedSkillsets.Clear();

		if (_currentTrends == null || _currentTrends.CurrentSkillLevels.Count == 0)
			return;

		// Peak levels (top 100 by per-skill map MSD, same weighting as recent — different score set)
		var peakFlow = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Full,
			Spacing = new Vector2(6, 6),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = "Peak:",
					Font = new FontUsage("", 14),
					Colour = new Color4(140, 140, 140, 255)
				}
			}
		};

		var sortedPeak = _currentTrends.PeakSkillLevels
			.Where(kvp => kvp.Key != "overall" && kvp.Key != "unknown" && kvp.Value > 0)
			.OrderByDescending(kvp => kvp.Value)
			.ToList();

		foreach (var (skillset, level) in sortedPeak)
		{
			var color = SkillsOverTimeChart.SkillsetColors.GetValueOrDefault(
				skillset.ToLowerInvariant(),
				SkillsOverTimeChart.SkillsetColors["unknown"]);

			var badge = new SkillLevelBadge(skillset.ToLowerInvariant(), level, 0, color);
			badge.Hovered += OnBadgeHovered;
			badge.HoverLost += OnBadgeHoverLost;
			badge.Clicked += OnBadgeClicked;
			peakFlow.Add(badge);
		}

		_peakSkillLevelsContainer.Add(peakFlow);

		var flowContainer = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Full,
			Spacing = new Vector2(6, 6),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = "Recent (time-weighted):",
					Font = new FontUsage("", 14),
					Colour = new Color4(140, 140, 140, 255)
				}
			}
		};

		// Sort by skill level descending
		var sortedSkills = _currentTrends.CurrentSkillLevels
			.Where(kvp => kvp.Key != "overall" && kvp.Key != "unknown" && kvp.Value > 0)
			.OrderByDescending(kvp => kvp.Value)
			.ToList();

		foreach (var (skillset, level) in sortedSkills)
		{
			var color = SkillsOverTimeChart.SkillsetColors.GetValueOrDefault(
				skillset.ToLowerInvariant(),
				SkillsOverTimeChart.SkillsetColors["unknown"]);

			var trend = _currentTrends.TrendSlopes.GetValueOrDefault(skillset, 0);

			var badge = new SkillLevelBadge(skillset.ToLowerInvariant(), level, trend, color);
			badge.Hovered += OnBadgeHovered;
			badge.HoverLost += OnBadgeHoverLost;
			badge.Clicked += OnBadgeClicked;
			flowContainer.Add(badge);
		}

		_skillLevelsContainer.Add(flowContainer);
	}

	private void OnBadgeHovered(string skillset)
	{
		_skillsChart.SetHoveredSkillset(skillset);
	}

	private void OnBadgeHoverLost(string skillset)
	{
		_skillsChart.SetHoveredSkillset(null);
	}

	private void OnBadgeClicked(SkillLevelBadge badge)
	{
		var skillset = badge.Skillset;

		var selected = _selectedSkillsets.Remove(skillset);
		if (!selected)
			_selectedSkillsets.Add(skillset);

		badge.SetSelected(!selected);


		_skillsChart.SetSkillsetFilter(_selectedSkillsets.Count > 0 ? _selectedSkillsets : null);
	}

	private static string GetRegionDisplayName(TimeRegion region)
	{
		return region switch
		{
			TimeRegion.LastWeek => "last 7 days",
			TimeRegion.LastMonth => "last 30 days",
			TimeRegion.Last3Months => "last 3 months",
			TimeRegion.AllTime => "all time",
			_ => region.ToString()
		};
	}

	/// <summary>
	/// Refreshes the analysis with current data.
	/// </summary>
	public void Refresh()
	{
		AnalyzeTrends(TimeRegion.AllTime);
	}
}

/// <summary>
/// Badge displaying a skill level with trend indicator.
/// Supports hover and click interactions for filtering.
/// </summary>
public partial class SkillLevelBadge : CompositeDrawable
{
	public string Skillset { get; }

	public event Action<string>? Hovered;
	public event Action<string>? HoverLost;
	public event Action<SkillLevelBadge>? Clicked;

	private readonly Color4 _color;
	private readonly Box _background = null!;
	private bool _isSelected;

	public SkillLevelBadge(string skillset, double level, double trend, Color4 color)
	{
		Skillset = skillset;
		_color = color;
		AutoSizeAxes = Axes.Both;

		var trendIndicator = trend > 0.3 ? "^" : trend < -0.3 ? "v" : "";
		var trendColor = trend > 0.3 ? new Color4(100, 255, 100, 255)
			: trend < -0.3 ? new Color4(255, 100, 100, 255)
			: new Color4(180, 180, 180, 255);

		InternalChild = new Container
		{
			AutoSizeAxes = Axes.Both,
			Masking = true,
			CornerRadius = 4,
			Children = new Drawable[]
			{
				_background = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(color.R, color.G, color.B, 0.2f)
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
					Children = new Drawable[]
					{
						new SpriteText
						{
							Text = $"{skillset}: ",
							Font = new FontUsage("", 13),
							Colour = color
						},
						new SpriteText
						{
							Text = $"{level:F1}",
							Font = new FontUsage("", 13, "Bold"),
							Colour = color
						},
						new SpriteText
						{
							Text = trendIndicator,
							Font = new FontUsage("", 13, "Bold"),
							Colour = trendColor,
							Margin = new MarginPadding { Left = 2 }
						}
					}
				}
			}
		};
	}

	public void SetSelected(bool selected)
	{
		_isSelected = selected;
		UpdateBackgroundColor();
	}

	private void UpdateBackgroundColor()
	{
		var alpha = _isSelected ? 0.5f : 0.2f;
		_background.FadeColour(new Color4(_color.R, _color.G, _color.B, alpha), 100);
	}

	protected override bool OnHover(HoverEvent e)
	{
		_background.FadeColour(new Color4(_color.R, _color.G, _color.B, 0.4f), 100);
		Hovered?.Invoke(Skillset);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		UpdateBackgroundColor();
		HoverLost?.Invoke(Skillset);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		Clicked?.Invoke(this);
		return true;
	}
}
