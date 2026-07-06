using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Models.Application;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Fixed right-hand panel: map info, export, regions, analysis, and context hint.
/// </summary>
public partial class DifficultySplitterSidebar : Container
{
	public event Action? CreateClicked;
	public event Action<Guid>? PairSelected;
	public event Action<string>? IncompleteRegionSelected;

	private SpriteText _mapTitleText = null!;
	private StyledButton _createButton = null!;
	private DifficultySplitterRegionListStrip _regionList = null!;
	private DifficultySplitterRegionAnalysisDisplay _analysis = null!;
	private TextFlowContainer _contextText = null!;
	private SpriteText _stateText = null!;

	public DifficultySplitterSidebar()
	{
		RelativeSizeAxes = Axes.Both;
		Masking = true;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(18, 18, 22, 255)
			},
			new Box
			{
				Width = 1,
				RelativeSizeAxes = Axes.Y,
				Colour = new Color4(40, 40, 48, 255)
			},
			new GridContainer
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding(12),
				RowDimensions = new[]
				{
					new Dimension(GridSizeMode.AutoSize),
					new Dimension(GridSizeMode.Absolute, 128),
					new Dimension(GridSizeMode.Relative, 1f),
					new Dimension(GridSizeMode.AutoSize)
				},
				Content = new[]
				{
					new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 8),
							Children = new Drawable[]
							{
								_mapTitleText = new SpriteText
								{
									RelativeSizeAxes = Axes.X,
									Text = "No beatmap loaded",
									Font = new FontUsage("", 12, "Bold"),
									Colour = new Color4(210, 210, 210, 255),
									Truncate = true
								},
								_stateText = new SpriteText
								{
									RelativeSizeAxes = Axes.X,
									Text = string.Empty,
									Font = new FontUsage("", 11),
									Colour = new Color4(130, 130, 130, 255),
									Truncate = true
								},
								_createButton = new StyledButton("Create Difficulties", StyledButtonAppearance.Filled)
								{
									RelativeSizeAxes = Axes.X,
									Height = 34
								},
								new Box
								{
									RelativeSizeAxes = Axes.X,
									Height = 1,
									Colour = new Color4(45, 45, 52, 255)
								}
							}
						}
					},
					new Drawable[]
					{
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Masking = true,
							Child = new ChainedScrollContainer
							{
								RelativeSizeAxes = Axes.Both,
								ClampExtension = 0,
								Child = _regionList = new DifficultySplitterRegionListStrip()
							}
						}
					},
					new Drawable[]
					{
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Masking = true,
							Padding = new MarginPadding { Top = 4 },
							Child = new ChainedScrollContainer
							{
								RelativeSizeAxes = Axes.Both,
								ClampExtension = 0,
								Child = _analysis = new DifficultySplitterRegionAnalysisDisplay { Compact = true }
							}
						}
					},
					new Drawable[]
					{
						_contextText = new TextFlowContainer(s =>
						{
							s.Font = new FontUsage("", 10);
							s.Colour = new Color4(110, 110, 110, 255);
						})
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y
						}
					}
				}
			}
		};

		_createButton.Clicked += () => CreateClicked?.Invoke();
		_regionList.PairSelected += id => PairSelected?.Invoke(id);
		_regionList.IncompleteRegionSelected += name => IncompleteRegionSelected?.Invoke(name);
	}

	public void SetMapTitle(string title) => _mapTitleText.Text = title;

	public void SetStateText(string text) => _stateText.Text = text;

	public void SetContextText(string text) => _contextText.Text = text;

	public void SetCreateButton(string text, bool enabled)
	{
		_createButton.Text = text;
		_createButton.Enabled = enabled;
	}

	public void SetRegions(IReadOnlyList<DifficultySplitterRegionListItem> items) => _regionList.SetItems(items);

	public void SetAnalysisRegion(string? regionName, Color4 accent)
	{
		_analysis.SetRegionName(regionName);
		_analysis.SetAccentColor(accent);
	}

	public void SetAnalysisAnalyzing() => _analysis.SetAnalyzing();

	public void SetAnalysisResult(DifficultyRegionAnalysisResult? result) => _analysis.SetResult(result);

	public void SetAnalysisPlaceholder(string message) => _analysis.SetPlaceholder(message);

}

