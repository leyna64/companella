using System.Globalization;
using Companella.Components.Misc;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// A compact calendar picker component for selecting dates with session data.
/// </summary>
public partial class SessionCalendarPicker : CompositeDrawable
{
	/// <summary>
	/// The currently selected date.
	/// </summary>
	public Bindable<DateTime?> SelectedDate { get; } = new();

	/// <summary>
	/// The currently displayed month.
	/// </summary>
	public Bindable<DateTime> DisplayedMonth { get; } = new(DateTime.Today);

	private HashSet<DateTime> _datesWithSessions = new();
	private FillFlowContainer _daysContainer = null!;
	private SpriteText _monthYearText = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _backgroundColor = new(30, 30, 35, 255);

	private const float _daySize = 28f;
	private const float _daySpacing = 2f;
	private const float _padding = 8f;

	public SessionCalendarPicker()
	{
		AutoSizeAxes = Axes.Both;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var totalWidth = _daySize * 7 + _daySpacing * 6 + _padding * 2;

		Masking = true;
		CornerRadius = 6;

		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _backgroundColor
			},
			new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Padding = new MarginPadding(_padding),
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					// Month navigation header
					new Container
					{
						Width = totalWidth - _padding * 2,
						Height = 24,
						Children = new Drawable[]
						{
							new StyledButton("<", StyledButtonAppearance.Custom)
							{
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft,
								Size = new Vector2(22, 22),
								FontSize = 14,
								ShowAccentBar = false,
								CustomNormalBg = new Color4(50, 50, 55, 0),
								CustomHoverBg = new Color4(50, 50, 55, 255),
								Action = () => NavigateMonth(-1)
							},
							_monthYearText = new SpriteText
							{
								Anchor = Anchor.Centre,
								Origin = Anchor.Centre,
								Font = new FontUsage("", 13, "Bold"),
								Colour = new Color4(220, 220, 220, 255)
							},
							new StyledButton(">", StyledButtonAppearance.Custom)
							{
								Anchor = Anchor.CentreRight,
								Origin = Anchor.CentreRight,
								Size = new Vector2(22, 22),
								FontSize = 14,
								ShowAccentBar = false,
								CustomNormalBg = new Color4(50, 50, 55, 0),
								CustomHoverBg = new Color4(50, 50, 55, 255),
								Action = () => NavigateMonth(1)
							}
						}
					},
					// Day of week headers
					CreateDayOfWeekHeaders(),
					// Days grid
					_daysContainer = new FillFlowContainer
					{
						AutoSizeAxes = Axes.Both,
						Direction = FillDirection.Full,
						MaximumSize = new Vector2(totalWidth - _padding * 2, float.MaxValue),
						Spacing = new Vector2(_daySpacing, _daySpacing)
					}
				}
			}
		};

		DisplayedMonth.BindValueChanged(_ => RefreshCalendar(), true);
	}

	/// <summary>
	/// Sets the dates that have session data.
	/// </summary>
	public void SetDatesWithSessions(IEnumerable<DateTime> dates)
	{
		_datesWithSessions = new HashSet<DateTime>(dates.Select(d => d.Date));
		RefreshCalendar();
	}

	/// <summary>
	/// Navigates to a different month.
	/// </summary>
	private void NavigateMonth(int offset)
	{
		DisplayedMonth.Value = DisplayedMonth.Value.AddMonths(offset);
	}

	/// <summary>
	/// Refreshes the calendar display.
	/// </summary>
	private void RefreshCalendar()
	{
		var month = DisplayedMonth.Value;
		_monthYearText.Text = month.ToString("MMM yyyy", CultureInfo.InvariantCulture);

		_daysContainer.Clear();

		var firstDay = new DateTime(month.Year, month.Month, 1);
		var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

		// Calculate offset for first day (Sunday = 0)
		var startOffset = (int)firstDay.DayOfWeek;

		// Add empty cells for days before first of month
		for (var i = 0; i < startOffset; i++) _daysContainer.Add(new Container { Size = new Vector2(_daySize) });

		// Add day cells
		for (var day = 1; day <= daysInMonth; day++)
		{
			var date = new DateTime(month.Year, month.Month, day);
			var hasSession = _datesWithSessions.Contains(date);
			var isSelected = SelectedDate.Value?.Date == date;
			var isToday = date == DateTime.Today;

			var dayCell = new CompactDayCell(day, hasSession, isSelected, isToday, _accentColor)
			{
				Size = new Vector2(_daySize),
				Action = () => OnDayClicked(date)
			};

			_daysContainer.Add(dayCell);
		}
	}

	/// <summary>
	/// Creates the day of week header row.
	/// </summary>
	private static FillFlowContainer CreateDayOfWeekHeaders()
	{
		var headers = new[] { "S", "M", "T", "W", "T", "F", "S" };

		var container = new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(_daySpacing, 0)
		};

		foreach (var header in headers)
			container.Add(new Container
			{
				Size = new Vector2(_daySize, 16),
				Child = new SpriteText
				{
					Text = header,
					Font = new FontUsage("", 10),
					Colour = new Color4(100, 100, 110, 255),
					Anchor = Anchor.Centre,
					Origin = Anchor.Centre
				}
			});

		return container;
	}

	/// <summary>
	/// Called when a day is clicked.
	/// </summary>
	private void OnDayClicked(DateTime date)
	{
		if (SelectedDate.Value?.Date == date)
			SelectedDate.Value = null;
		else
			SelectedDate.Value = date;

		RefreshCalendar();
	}
}

/// <summary>
/// Compact day cell for the calendar.
/// </summary>
public partial class CompactDayCell : CompositeDrawable
{
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private readonly int _day;
	private readonly bool _hasSession;
	private readonly bool _isSelected;
	private readonly bool _isToday;
	private readonly Color4 _accentColor;

	public Action? Action { get; set; }

	public CompactDayCell(int day, bool hasSession, bool isSelected, bool isToday, Color4 accentColor)
	{
		_day = day;
		_hasSession = hasSession;
		_isSelected = isSelected;
		_isToday = isToday;
		_accentColor = accentColor;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 4;

		var bgColor = _isSelected ? _accentColor :
			_hasSession ? new Color4(255, 102, 170, 25) :
			new Color4(40, 40, 45, 255);

		var textColor = _isSelected ? Color4.White :
			_hasSession ? new Color4(255, 140, 180, 255) :
			_isToday ? _accentColor :
			new Color4(170, 170, 170, 255);

		InternalChildren = new Drawable[]
		{
			_background = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = bgColor
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.White,
				Alpha = 0
			},
			new SpriteText
			{
				Text = _day.ToString(CultureInfo.InvariantCulture),
				Font = new FontUsage("", 12, _isToday || _isSelected ? "Bold" : ""),
				Colour = textColor,
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre
			}
		};

		// Today indicator - subtle ring
		if (_isToday && !_isSelected)
			AddInternal(new CircularContainer
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				BorderColour = _accentColor.Opacity(0.5f),
				BorderThickness = 1.5f,
				Child = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Alpha = 0
				}
			});
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (!_isSelected)
		{
			_hoverOverlay.FadeTo(0.08f, 80, Easing.OutQuint);
			this.ScaleTo(1.05f, 80, Easing.OutQuint);
		}

		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 120, Easing.OutQuint);
		this.ScaleTo(1f, 120, Easing.OutQuint);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		this.ScaleTo(0.92f, 40, Easing.OutQuint).Then().ScaleTo(1f, 100, Easing.OutQuint);
		Action?.Invoke();
		return true;
	}
}
