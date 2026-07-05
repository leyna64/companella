using Companella.Components.Misc;
using Companella.Services.Analysis;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Analysis;

/// <summary>
/// Selector for choosing time regions for trend analysis.
/// Displays preset buttons: Last Week, Last Month, Last 3 Months, All Time.
/// </summary>
public partial class TimeRegionSelector : CompositeDrawable
{
	/// <summary>
	/// The currently selected time region.
	/// </summary>
	public Bindable<TimeRegion> Current { get; } = new(TimeRegion.LastMonth);

	private FillFlowContainer _buttonsContainer = null!;
	private readonly Dictionary<TimeRegion, StyledButton> _buttons = new();

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	public TimeRegionSelector()
	{
		AutoSizeAxes = Axes.Both;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(6, 0),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = "Period:",
					Font = new FontUsage("", 17),
					Colour = new Color4(140, 140, 140, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				},
				_buttonsContainer = new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(4, 0)
				}
			}
		};

		// Create buttons for each time region
		foreach (var region in Enum.GetValues<TimeRegion>())
		{
			var button = new StyledButton(GetRegionLabel(region), StyledButtonAppearance.Toggle)
			{
				ContentPadding = new MarginPadding { Horizontal = 10, Vertical = 4 },
				FontSize = 14,
				Bold = false,
				ShowAccentBar = false,
				AccentColor = _accentColor,
				TooltipText = GetRegionTooltip(region)
			};
			button.Clicked += () => OnButtonClicked(region);
			_buttons[region] = button;
			_buttonsContainer.Add(button);
		}

		// Subscribe to value changes
		Current.BindValueChanged(OnValueChanged, true);
	}

	private void OnValueChanged(ValueChangedEvent<TimeRegion> e)
	{
		// Update button states
		foreach (var (region, button) in _buttons) button.SetSelected(region == e.NewValue);
	}

	private void OnButtonClicked(TimeRegion region)
	{
		Current.Value = region;
	}

	private static string GetRegionLabel(TimeRegion region)
	{
		return region switch
		{
			TimeRegion.LastWeek => "Week",
			TimeRegion.LastMonth => "Month",
			TimeRegion.Last3Months => "3 Mo",
			TimeRegion.AllTime => "All",
			_ => region.ToString()
		};
	}

	private static string GetRegionTooltip(TimeRegion region)
	{
		return region switch
		{
			TimeRegion.LastWeek => "Analyze plays from the last 7 days",
			TimeRegion.LastMonth => "Analyze plays from the last 30 days",
			TimeRegion.Last3Months => "Analyze plays from the last 3 months",
			TimeRegion.AllTime => "Analyze all recorded plays",
			_ => ""
		};
	}
}
