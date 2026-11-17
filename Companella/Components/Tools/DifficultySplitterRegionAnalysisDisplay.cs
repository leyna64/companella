using Companella.Models.Application;
using Companella.Models.Beatmap;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Compact region-scoped DAN and Interlude pattern display for the difficulty splitter.
/// </summary>
public partial class DifficultySplitterRegionAnalysisDisplay : CompositeDrawable
{
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

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private Color4 _accentColorField = new(255, 102, 170, 255);

	private SpriteText _regionTitle = null!;
	private SpriteText _danLabel = null!;
	private SpriteText _danValue = null!;
	private SpriteText _danDetail = null!;
	private SpriteText _categoryText = null!;
	private SpriteText _noteCountText = null!;
	private FillFlowContainer _rowsContainer = null!;
	private SpriteText _placeholderText = null!;

	public bool Compact { get; set; }

	public DifficultySplitterRegionAnalysisDisplay()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var danFontSize = Compact ? 11 : 14;
		var valueFontSize = Compact ? 14 : 16;
		var detailFontSize = Compact ? 11 : 13;
		var categoryFontSize = Compact ? 11 : 14;
		var placeholderFontSize = Compact ? 11 : 13;

		_danLabel = new SpriteText
		{
			Text = Compact ? "Dan:" : "Dan (BETA):",
			Font = new FontUsage("", danFontSize),
			Colour = new Color4(140, 140, 140, 255)
		};
		_danValue = new SpriteText
		{
			Text = "—",
			Font = new FontUsage("", valueFontSize, "Bold"),
			Colour = _accentColorField
		};
		_danDetail = new SpriteText
		{
			Text = "",
			Font = new FontUsage("", detailFontSize),
			Colour = new Color4(120, 120, 120, 255),
			Truncate = true,
			RelativeSizeAxes = Axes.X
		};
		_noteCountText = new SpriteText
		{
			Text = "",
			Font = new FontUsage("", detailFontSize),
			Colour = new Color4(160, 160, 160, 255),
			Truncate = true,
			RelativeSizeAxes = Axes.X
		};

		var danSection = Compact
			? new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Children = new Drawable[]
				{
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Horizontal,
						Spacing = new Vector2(6, 0),
						Children = new Drawable[] { _danLabel, _danValue }
					},
					_danDetail,
					_noteCountText
				}
			}
			: new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(12, 0),
				Children = new Drawable[]
				{
					_danLabel,
					_danValue,
					_danDetail,
					_noteCountText
				}
			};

		InternalChild = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, Compact ? 4 : 6),
			Children = new Drawable[]
			{
				_regionTitle = new SpriteText
				{
					Text = string.Empty,
					Font = new FontUsage("", 14, "Bold"),
					Colour = _accentColorField,
					Alpha = 0,
					Truncate = true,
					RelativeSizeAxes = Axes.X
				},
				danSection,
				_categoryText = new SpriteText
				{
					Text = "",
					Font = new FontUsage("", categoryFontSize),
					Colour = new Color4(200, 200, 200, 255),
					Alpha = 0,
					Truncate = true,
					RelativeSizeAxes = Axes.X
				},
				_rowsContainer = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, Compact ? 2 : 3)
				},
				_placeholderText = new SpriteText
				{
					Text = "Select or create a valid region pair to see analysis.",
					Font = new FontUsage("", placeholderFontSize),
					Colour = new Color4(120, 120, 120, 255),
					RelativeSizeAxes = Axes.X
				}
			}
		};
	}

	public void SetPlaceholder(string message)
	{
		_danValue.Text = "—";
		_danDetail.Text = string.Empty;
		_noteCountText.Text = string.Empty;
		_categoryText.Alpha = 0;
		_rowsContainer.Clear();
		_regionTitle.Alpha = 0;
		_placeholderText.Text = message;
		_placeholderText.Alpha = 1;
	}

	public void SetRegionName(string? regionName)
	{
		if (string.IsNullOrWhiteSpace(regionName))
		{
			_regionTitle.Alpha = 0;
			return;
		}

		_regionTitle.Text = regionName;
		_regionTitle.Alpha = Compact ? 0 : 1;
	}

	public void SetAccentColor(Color4 accent)
	{
		_accentColorField = accent;
		_regionTitle.Colour = accent;
		_danValue.Colour = accent;
	}

	public void SetAnalyzing()
	{
		_danValue.Text = "...";
		_danDetail.Text = "Calculating...";
		_categoryText.Alpha = 0;
		_rowsContainer.Clear();
		_placeholderText.Alpha = 0;
	}

	public void SetResult(DifficultyRegionAnalysisResult? result)
	{
		_rowsContainer.Clear();

		if (result == null)
		{
			Clear();
			return;
		}

		_placeholderText.Alpha = 0;
		_noteCountText.Text = $"{result.NoteCount} notes";

		if (!result.IsValid)
		{
			_danValue.Text = "—";
			_danDetail.Text = result.ErrorMessage ?? "Invalid region";
			_categoryText.Alpha = 0;
			return;
		}

		if (result.Dan != null)
		{
			_danValue.Text = result.Dan.DisplayName;
			var details = new List<string>();
			if (result.Dan.RawModelOutput.HasValue)
				details.Add($"Raw: {result.Dan.RawModelOutput.Value:F2}");
			if (result.Dan.UsedDanielCalculator && result.Dan.DanielStarRating.HasValue)
				details.Add($"SR: {result.Dan.DanielStarRating.Value:F2}");
			_danDetail.Text = string.Join(" | ", details);
			_danValue.Colour = result.Dan.Confidence > 0.7
				? _accentColorField
				: new Color4(200, 180, 100, 255);
		}
		else
		{
			_danValue.Text = "?";
			_danDetail.Text = "Dan model unavailable";
		}

		if (result.Patterns != null)
		{
			var category = result.Patterns.GetInterludeCategoryDisplay();
			if (!string.IsNullOrWhiteSpace(category))
			{
				_categoryText.Text = category.Trim();
				_categoryText.Alpha = 1;
			}
			else
			{
				_categoryText.Alpha = 0;
			}

			var topPatterns = result.Patterns.GetTopPatterns()
				.Where(p => p.Type != PatternType.Jump && p.Type != PatternType.Quad && p.Type != PatternType.Hand)
				.Take(Compact ? 3 : 5)
				.ToList();

			var visibleTotal = topPatterns.Sum(p => p.Percentage);
			if (visibleTotal > 0)
			{
				foreach (var pattern in topPatterns)
					pattern.Percentage = pattern.Percentage / visibleTotal * 100.0;
			}

			foreach (var pattern in topPatterns)
			{
				var color = _patternColors.GetValueOrDefault(pattern.Type, Color4.White);
				_rowsContainer.Add(new RegionPatternRow(pattern, color, Compact));
			}
		}
	}

	public void Clear()
	{
		SetPlaceholder("Select a region to see DAN and patterns.");
	}

	private partial class RegionPatternRow : CompositeDrawable
	{
		public RegionPatternRow(TopPattern pattern, Color4 color, bool compact)
		{
			RelativeSizeAxes = Axes.X;
			Height = compact ? 15 : 18;
			var fontSize = compact ? 11 : 14;
			var percentSize = compact ? 11 : 13;

			InternalChildren = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(35, 33, 43, 255),
					Alpha = compact ? 0.25f : 0.4f
				},
				new GridContainer
				{
					RelativeSizeAxes = Axes.Both,
					Padding = new MarginPadding { Horizontal = 4, Vertical = compact ? 1 : 2 },
					ColumnDimensions = new[]
					{
						new Dimension(GridSizeMode.Relative, 1f),
						new Dimension(GridSizeMode.AutoSize)
					},
					Content = new[]
					{
						new Drawable[]
						{
							new FillFlowContainer
							{
								RelativeSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(4, 0),
								Children = new Drawable[]
								{
									new Container
									{
										Size = new Vector2(3, compact ? 10 : 14),
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Masking = true,
										CornerRadius = 1,
										Child = new Box
										{
											RelativeSizeAxes = Axes.Both,
											Colour = color
										}
									},
									new SpriteText
									{
										Text = pattern.CompactDisplay,
										Font = new FontUsage("", fontSize),
										Colour = color,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Truncate = true,
										RelativeSizeAxes = Axes.X
									}
								}
							},
							new SpriteText
							{
								Text = pattern.PercentageDisplay,
								Font = new FontUsage("", percentSize),
								Colour = new Color4(160, 160, 160, 255),
								Anchor = Anchor.CentreRight,
								Origin = Anchor.CentreRight
							}
						}
					}
				}
			};
		}
	}
}
