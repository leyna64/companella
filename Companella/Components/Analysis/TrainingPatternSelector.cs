using Companella.Components.Layout;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Analysis;

/// <summary>
/// Maps pattern types to MSD skillset names for getting pattern-specific MSD values.
/// </summary>
public static class PatternToMsdMapper
{
	/// <summary>
	/// Gets the MSD skillset value for a given pattern type.
	/// </summary>
	public static double GetMsdForPattern(PatternType patternType, SkillsetScores? scores)
	{
		if (scores == null)
			return 0;

		return patternType switch
		{
			PatternType.Stream => scores.Stream,
			PatternType.Jumpstream => scores.Jumpstream,
			PatternType.Handstream => scores.Handstream,
			PatternType.Jack => scores.Jackspeed,
			PatternType.Minijack => scores.Jackspeed,
			PatternType.Chordjack => scores.Chordjack,
			// For patterns without direct MSD mapping, use technical or overall
			PatternType.Trill => scores.Technical,
			PatternType.Roll => scores.Technical,
			PatternType.Bracket => scores.Technical,
			PatternType.Jumptrill => scores.Technical,
			PatternType.Jump => scores.Overall,
			PatternType.Hand => scores.Overall,
			PatternType.Quad => scores.Overall,
			_ => scores.Overall
		};
	}

	/// <summary>
	/// Gets the MSD skillset name for a given pattern type.
	/// </summary>
	public static string GetMsdNameForPattern(PatternType patternType)
	{
		return patternType switch
		{
			PatternType.Stream => "Stream",
			PatternType.Jumpstream => "JS",
			PatternType.Handstream => "HS",
			PatternType.Jack => "Jack",
			PatternType.Minijack => "Jack",
			PatternType.Chordjack => "CJ",
			PatternType.Trill => "Tech",
			PatternType.Roll => "Tech",
			PatternType.Bracket => "Tech",
			PatternType.Jumptrill => "Tech",
			_ => "Overall"
		};
	}
}

/// <summary>
/// Component for displaying and selecting patterns for training.
/// Shows detected patterns with checkboxes to select relevant ones.
/// </summary>
public partial class TrainingPatternSelector : CompositeDrawable
{
	private SpriteText _titleText = null!;
	private SpriteText _noDataText = null!;
	private FillFlowContainer _rowsContainer = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _valueColor = new(230, 230, 230, 255);

	private readonly List<PatternCheckboxRow> _rows = new();
	private double _currentYavsrgRating;

	/// <summary>
	/// Event fired when selection changes.
	/// </summary>
	public event Action? SelectionChanged;

	// Pattern type colors (matching PatternDisplay)
	private static readonly Dictionary<PatternType, Color4> _patternColors = new()
	{
		{ PatternType.Trill, new Color4(100, 200, 255, 255) },
		{ PatternType.Jack, new Color4(255, 100, 100, 255) },
		{ PatternType.Minijack, new Color4(255, 150, 150, 255) },
		{ PatternType.Stream, new Color4(100, 180, 255, 255) },
		{ PatternType.Jump, new Color4(100, 220, 100, 255) },
		{ PatternType.Hand, new Color4(255, 180, 100, 255) },
		{ PatternType.Quad, new Color4(255, 220, 100, 255) },
		{ PatternType.Jumpstream, new Color4(100, 220, 100, 255) },
		{ PatternType.Handstream, new Color4(255, 180, 100, 255) },
		{ PatternType.Chordjack, new Color4(255, 220, 100, 255) },
		{ PatternType.Roll, new Color4(180, 100, 255, 255) },
		{ PatternType.Bracket, new Color4(100, 220, 220, 255) },
		{ PatternType.Jumptrill, new Color4(220, 100, 220, 255) }
	};

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 6;

		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(35, 33, 43, 255)
			},
			new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding(12),
				Children = new Drawable[]
				{
					_titleText = new SpriteText
					{
						Text = "Select Patterns for Training",
						Font = new FontUsage("", 19, "Bold"),
						Colour = _accentColor,
						Anchor = Anchor.TopLeft,
						Origin = Anchor.TopLeft
					},
					_noDataText = new SpriteText
					{
						Text = "Drop a map to analyze patterns",
						Font = new FontUsage("", 17),
						Colour = new Color4(120, 120, 120, 255),
						Anchor = Anchor.Centre,
						Origin = Anchor.Centre
					},
					new ChainedScrollContainer
					{
						RelativeSizeAxes = Axes.Both,
						Padding = new MarginPadding { Top = 28 },
						ClampExtension = 0,
						Child = _rowsContainer = new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 4)
						}
					}
				}
			}
		};
	}

	/// <summary>
	/// Sets the pattern analysis result to display.
	/// Shows ALL patterns with pattern-specific MSD values.
	/// </summary>
	public void SetPatternResult(PatternAnalysisResult result, SkillsetScores? msdScores, double yavsrgRating)
	{
		_rowsContainer.Clear();
		_rows.Clear();
		_currentYavsrgRating = yavsrgRating;

		if (!result.Success || result.TotalPatterns == 0)
		{
			_noDataText.Alpha = 1;
			_noDataText.Text = result.Success ? "No patterns detected" : result.ErrorMessage ?? "Analysis failed";
			_noDataText.Colour = new Color4(120, 120, 120, 255);
			return;
		}

		_noDataText.Alpha = 0;

		// Get ALL patterns, not just top 5
		var allPatterns = result.GetAllPatternsSorted();

		foreach (var pattern in allPatterns)
		{
			var color = _patternColors.GetValueOrDefault(pattern.Type, _valueColor);
			// Get pattern-specific MSD for display
			var patternMsd = PatternToMsdMapper.GetMsdForPattern(pattern.Type, msdScores);
			var msdName = PatternToMsdMapper.GetMsdNameForPattern(pattern.Type);
			var row = new PatternCheckboxRow(pattern, patternMsd, msdName, color);
			row.CheckedChanged += () => SelectionChanged?.Invoke();
			_rows.Add(row);
			_rowsContainer.Add(row);
		}
	}

	/// <summary>
	/// Clears the pattern display.
	/// </summary>
	public void Clear()
	{
		_rowsContainer.Clear();
		_rows.Clear();
		_currentYavsrgRating = 0;
		_noDataText.Alpha = 1;
		_noDataText.Text = "Drop a map to analyze patterns";
	}

	/// <summary>
	/// Shows an error message.
	/// </summary>
	public void ShowError(string message)
	{
		_rowsContainer.Clear();
		_rows.Clear();
		_noDataText.Alpha = 1;
		_noDataText.Text = message;
		_noDataText.Colour = new Color4(255, 120, 120, 255);
	}

	/// <summary>
	/// Shows loading state.
	/// </summary>
	public void ShowLoading()
	{
		_rowsContainer.Clear();
		_rows.Clear();
		_noDataText.Alpha = 1;
		_noDataText.Text = "Analyzing patterns...";
		_noDataText.Colour = new Color4(120, 120, 120, 255);
	}

	/// <summary>
	/// Gets all selected patterns with their YAVSRG rating.
	/// All patterns from the same map share the same YAVSRG rating.
	/// </summary>
	public List<(string PatternType, double YavsrgRating)> GetSelectedPatterns()
	{
		return _rows
			.Where(r => r.IsChecked)
			.Select(r => (r.PatternType, _currentYavsrgRating))
			.ToList();
	}

	/// <summary>
	/// Gets whether any patterns are selected.
	/// </summary>
	public bool HasSelection => _rows.Any(r => r.IsChecked);

	/// <summary>
	/// Selects all patterns.
	/// </summary>
	public void SelectAll()
	{
		foreach (var row in _rows)
			row.IsChecked = true;
	}

	/// <summary>
	/// Deselects all patterns.
	/// </summary>
	public void DeselectAll()
	{
		foreach (var row in _rows)
			row.IsChecked = false;
	}

	/// <summary>
	/// A row with checkbox for a pattern type.
	/// </summary>
	private partial class PatternCheckboxRow : CompositeDrawable
	{
		private Box _checkboxBg = null!;
		private Box _checkboxCheck = null!;
		private bool _isChecked;

		public string PatternType { get; }
		public double Bpm { get; }
		public double Msd { get; }

		public event Action? CheckedChanged;

		public bool IsChecked
		{
			get => _isChecked;
			set
			{
				if (_isChecked == value)
					return;
				_isChecked = value;
				UpdateCheckboxVisual();
				CheckedChanged?.Invoke();
			}
		}

		public PatternCheckboxRow(TopPattern pattern, double patternMsd, string msdName, Color4 color)
		{
			PatternType = pattern.Type.ToString();
			Bpm = pattern.Bpm;
			Msd = patternMsd; // Use pattern-specific MSD

			RelativeSizeAxes = Axes.X;
			Height = 28;

			InternalChildren = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(45, 42, 55, 255),
					Alpha = 0.6f
				},
				new GridContainer
				{
					RelativeSizeAxes = Axes.Both,
					Padding = new MarginPadding { Horizontal = 8 },
					ColumnDimensions = new[]
					{
						new Dimension(GridSizeMode.Absolute, 28), // Checkbox
						new Dimension(GridSizeMode.Relative, 0.30f), // Pattern name
						new Dimension(GridSizeMode.Relative, 0.22f), // BPM
						new Dimension(GridSizeMode.Relative, 0.28f), // MSD (with type)
						new Dimension(GridSizeMode.Relative, 0.20f) // Percentage
					},
					Content = new[]
					{
						new Drawable[]
						{
							// Checkbox
							new Container
							{
								Size = new Vector2(20),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft,
								Masking = true,
								CornerRadius = 3,
								BorderThickness = 2,
								BorderColour = new Color4(100, 100, 100, 255),
								Children = new Drawable[]
								{
									_checkboxBg = new Box
									{
										RelativeSizeAxes = Axes.Both,
										Colour = new Color4(30, 28, 38, 255)
									},
									_checkboxCheck = new Box
									{
										RelativeSizeAxes = Axes.Both,
										Colour = new Color4(255, 102, 170, 255),
										Alpha = 0,
										Scale = new Vector2(0.6f),
										Anchor = Anchor.Centre,
										Origin = Anchor.Centre
									}
								}
							},
							// Pattern name with color indicator
							new FillFlowContainer
							{
								RelativeSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(6, 0),
								Children = new Drawable[]
								{
									new Container
									{
										Size = new Vector2(4, 16),
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Masking = true,
										CornerRadius = 2,
										Child = new Box
										{
											RelativeSizeAxes = Axes.Both,
											Colour = color
										}
									},
									new SpriteText
									{
										Text = pattern.ShortName,
										Font = new FontUsage("", 17),
										Colour = color,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft
									}
								}
							},
							// BPM
							new SpriteText
							{
								Text = pattern.Bpm > 0 ? $"{pattern.Bpm:F0} BPM" : "-",
								Font = new FontUsage("", 16),
								Colour = new Color4(200, 200, 200, 255),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							},
							// MSD with type label
							new SpriteText
							{
								Text = patternMsd > 0 ? $"{msdName} {patternMsd:F1}" : "-",
								Font = new FontUsage("", 16),
								Colour = new Color4(180, 180, 180, 255),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							},
							// Percentage
							new SpriteText
							{
								Text = pattern.PercentageDisplay,
								Font = new FontUsage("", 15),
								Colour = new Color4(140, 140, 140, 255),
								Anchor = Anchor.CentreRight,
								Origin = Anchor.CentreRight
							}
						}
					}
				}
			};
		}

		protected override bool OnClick(ClickEvent e)
		{
			IsChecked = !IsChecked;
			return true;
		}

		protected override bool OnHover(HoverEvent e)
		{
			this.FadeColour(new Color4(1.1f, 1.1f, 1.1f, 1f), 100);
			return base.OnHover(e);
		}

		protected override void OnHoverLost(HoverLostEvent e)
		{
			this.FadeColour(Color4.White, 100);
			base.OnHoverLost(e);
		}

		private void UpdateCheckboxVisual()
		{
			if (_isChecked)
			{
				_checkboxCheck.FadeTo(1, 100);
				_checkboxCheck.ScaleTo(0.7f, 100);
			}
			else
			{
				_checkboxCheck.FadeTo(0, 100);
				_checkboxCheck.ScaleTo(0.5f, 100);
			}
		}
	}
}
