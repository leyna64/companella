using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// Panel for configuring auto-start/end session settings.
/// </summary>
public partial class SessionAutoStartPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	private SettingsCheckbox _autoStartCheckbox = null!;
	private SettingsCheckbox _autoEndCheckbox = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				_autoStartCheckbox = new SettingsCheckbox
				{
					LabelText = "Auto-start session on startup",
					IsChecked = SettingsService.Settings.AutoStartSession,
					TooltipText = "Automatically start tracking when the app launches"
				},
				_autoEndCheckbox = new SettingsCheckbox
				{
					LabelText = "Auto-end session on exit",
					IsChecked = SettingsService.Settings.AutoEndSession,
					TooltipText = "Automatically end and save the session when closing the app"
				}
			}
		};

		InternalChild = new SettingsSection("Session Tracking", "Automatic session lifecycle behavior", content);

		_autoStartCheckbox.CheckedChanged += OnAutoStartChanged;
		_autoEndCheckbox.CheckedChanged += OnAutoEndChanged;
	}

	private void OnAutoStartChanged(bool isChecked)
	{
		SettingsService.Settings.AutoStartSession = isChecked;
		SaveSettings();
	}

	private void OnAutoEndChanged(bool isChecked)
	{
		SettingsService.Settings.AutoEndSession = isChecked;
		SaveSettings();
	}

	private void SaveSettings()
	{
		Task.Run(async () => await SettingsService.SaveAsync());
	}
}

/// <summary>
/// Simple checkbox control for settings.
/// </summary>
public partial class SettingsCheckbox : CompositeDrawable, IHasTooltip
{
	private Box _checkboxBackground = null!;
	private Box _checkmark = null!;
	private TextFlowContainer _label = null!;
	private bool _isChecked;

	public string LabelText { get; set; } = "Option";

	/// <summary>
	/// Font size for the label (default matches prior hard-coded styling).
	/// </summary>
	public float LabelFontSize { get; set; } = 15f;

	/// <summary>
	/// Colour for the label text.
	/// </summary>
	public Color4 LabelColour { get; set; } = new Color4(180, 180, 180, 255);

	/// <summary>
	/// Tooltip text displayed on hover.
	/// </summary>
	public LocalisableString TooltipText { get; set; }

	public bool IsChecked
	{
		get => _isChecked;
		set
		{
			if (_isChecked == value)
				return;
			_isChecked = value;
			UpdateVisual();
		}
	}

	public event Action<bool>? CheckedChanged;

	private readonly Color4 _uncheckedColor = StyledButton.Theme.DialogInsetBg;
	private readonly Color4 _checkedColor = StyledButton.Theme.Accent;
	private readonly Color4 _hoverColor = StyledButton.Theme.HoverBg;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		InternalChild = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(8, 0),
			Children = new Drawable[]
			{
				new Container
				{
					Size = new Vector2(18),
					Masking = true,
					CornerRadius = 2,
					Anchor = Anchor.TopLeft,
					Origin = Anchor.TopLeft,
					Children = new Drawable[]
					{
						_checkboxBackground = new Box
						{
							RelativeSizeAxes = Axes.Both,
							Colour = _uncheckedColor
						},
						_checkmark = new Box
						{
							Size = new Vector2(10),
							Anchor = Anchor.Centre,
							Origin = Anchor.Centre,
							Colour = Color4.White,
							Alpha = 0
						}
					}
				},
				_label = new TextFlowContainer(s =>
				{
					s.Font = new FontUsage("", LabelFontSize);
					s.Colour = LabelColour;
				})
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Text = LabelText
				}
			}
		};

		UpdateVisual();
	}

	private void UpdateVisual()
	{
		if (_checkboxBackground == null)
			return;

		_checkboxBackground.FadeColour(_isChecked ? _checkedColor : _uncheckedColor, 100);
		_checkmark.FadeTo(_isChecked ? 1 : 0, 100);
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (!_isChecked)
			_checkboxBackground.FadeColour(_hoverColor, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_checkboxBackground.FadeColour(_isChecked ? _checkedColor : _uncheckedColor, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		_isChecked = !_isChecked;
		UpdateVisual();
		CheckedChanged?.Invoke(_isChecked);
		return true;
	}
}
