using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// Shared appearance presets for <see cref="StyledButton"/>.
/// </summary>
public enum StyledButtonAppearance
{
	/// <summary>Dark surface with accent bar.</summary>
	Standard,

	/// <summary>Dark when idle, accent fill when selected.</summary>
	Toggle,

	/// <summary>Compact muted utility button.</summary>
	Muted,

	/// <summary>Solid accent or custom fill color (treated as Standard + AccentColor).</summary>
	Filled,
	/// <summary>Explicit normal/hover background colors.</summary>
	Custom,

	/// <summary>Bordered selection style for mod cards.</summary>
	Bordered,
}

/// <summary>
/// Unified styled button used across the application.
/// </summary>
public partial class StyledButton : CompositeDrawable, IHasTooltip
{
	public static class Theme
	{
		public static readonly Color4 NormalBg = new(44, 44, 50, 255);
		public static readonly Color4 HoverBg = new(54, 54, 62, 255);
		public static readonly Color4 DisabledBg = new(36, 36, 40, 255);
		public static readonly Color4 DialogBg = new(32, 32, 36, 255);
		public static readonly Color4 DialogInsetBg = new(26, 26, 30, 255);
		public static readonly Color4 Accent = new(255, 102, 170, 255);
		public static readonly Color4 AccentHover = new(255, 130, 190, 255);
		public static readonly Color4 DisabledAccent = new(70, 70, 76, 255);
		public static readonly Color4 MutedLabel = new(180, 180, 180, 255);
		public static readonly Color4 DisabledLabel = new(110, 110, 115, 255);
		public static readonly Color4 DestructiveAccent = new(220, 80, 80, 255);
		public static readonly Color4 SuccessFill = new(100, 200, 100, 255);
		public static readonly Color4 DangerFill = new(200, 100, 100, 255);
		public static readonly Color4 InfoFill = new(80, 150, 200, 255);
	}

	private string _text;
	private string? _subtitle;
	private string? _selectedText;
	private Box _background = null!;
	private Box _accentBar = null!;
	private Box _hoverOverlay = null!;
	private Container? _border;
	private SpriteText _label = null!;
	private SpriteText? _subtitleLabel;
	private FillFlowContainer? _labelFlow;
	private Container? _loadingSpinner;
	private Box? _loadingDot;
	private SpriteText? _progressLabel;
	private bool _isEnabled = true;
	private bool _isSelected;
	private bool _isHovered;
	private bool _isLoading;
	private bool _isRecording;

	public LocalisableString TooltipText { get; set; }

	public event Action? Clicked;
	public event Action? CtrlClicked;
	public event Action? ShiftClicked;
	public event Action? RightClicked;
	public event Action<bool>? SelectedChanged;

	/// <summary>
	/// Alternative to subscribing to <see cref="Clicked"/>.
	/// </summary>
	public Action? Action { get; set; }

	/// <summary>
	/// Invoked on shift+click instead of <see cref="Clicked"/>.
	/// </summary>
	public Action? ShiftAction { get; set; }

	public StyledButtonAppearance Appearance { get; set; } = StyledButtonAppearance.Standard;

	public bool Enabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			if (_background != null)
				UpdateVisualState(_isHovered);
		}
	}

	public bool Selected
	{
		get => _isSelected;
		set
		{
			if (_isSelected == value)
				return;

			_isSelected = value;
			if (_background != null)
				UpdateVisualState(_isHovered);
		}
	}

	/// <summary>
	/// When set, clicking toggles <see cref="Selected"/> and raises <see cref="SelectedChanged"/>.
	/// </summary>
	public bool ToggleOnClick { get; set; }

	/// <summary>
	/// When set, click handlers are not invoked if already selected.
	/// </summary>
	public bool SuppressClickWhenSelected { get; set; }

	/// <summary>
	/// When set, the button can receive keyboard focus for key capture.
	/// </summary>
	public bool AcceptsKeyboardFocus { get; set; }

	public Func<KeyDownEvent, bool>? OnKeyDownCallback { get; set; }

	public Action<KeyUpEvent>? OnKeyUpCallback { get; set; }

	public Color4? RecordingFillColor { get; set; }

	public override bool AcceptsFocus => AcceptsKeyboardFocus;

	public bool ShowAccentBar { get; set; } = true;

	public float FontSize { get; set; } = 17;

	public bool Bold { get; set; } = true;

	public bool UseUnicodeFont { get; set; }

	public Color4? AccentColor { get; set; }

	public Color4? CustomNormalBg { get; set; }

	public Color4? CustomHoverBg { get; set; }

	public Color4? CustomFillColor { get; set; }

	public string Text
	{
		get => _text;
		set
		{
			_text = value;
			if (_label != null)
				UpdateLabelText();
		}
	}

	public string? Subtitle
	{
		get => _subtitle;
		set
		{
			_subtitle = value;
			if (_subtitleLabel != null)
				_subtitleLabel.Text = value ?? string.Empty;
		}
	}

	/// <summary>
	/// Optional alternate label shown when <see cref="Selected"/> is true.
	/// </summary>
	public string? SelectedText
	{
		get => _selectedText;
		set
		{
			_selectedText = value;
			if (_label != null)
				UpdateLabelText();
		}
	}

	public BindableBool? BindEnabled { get; set; }

	/// <summary>
	/// Optional user data (e.g. rate value for quick-rate buttons).
	/// </summary>
	public object? Tag { get; set; }

	public MarginPadding ContentPadding { get; set; }

	/// <summary>
	/// When set, the button shows a loading spinner and supports <see cref="SetLoading"/>.
	/// </summary>
	public bool ShowLoadingIndicator { get; set; }

	public StyledButton(string text, StyledButtonAppearance appearance = StyledButtonAppearance.Standard)
	{
		_text = text;
		Appearance = appearance;
	}

	public StyledButton(string text, Color4 accentColor)
		: this(text, StyledButtonAppearance.Standard)
	{
		AccentColor = accentColor;
	}

	public void SetSelected(bool selected) => Selected = selected;

	public void SetEnabled(bool enabled) => Enabled = enabled;

	public void ApplyFillStyle(Color4 accentColor) => SetAccentColor(accentColor);

	public void SetAccentColor(Color4 accentColor)
	{
		AccentColor = accentColor;
		CustomFillColor = null;
		Appearance = StyledButtonAppearance.Standard;
		ShowAccentBar = true;
		if (_background != null)
			UpdateVisualState(_isHovered);
	}

	public void SetKeybindText(string text) => Text = text;

	public void SetRecording(bool recording)
	{
		_isRecording = recording;
		if (_background != null)
			UpdateVisualState(_isHovered);
	}

	public void SetLoading(bool loading)
	{
		if (_isLoading == loading)
			return;

		_isLoading = loading;
		Enabled = !loading;

		if (!ShowLoadingIndicator)
			return;

		if (loading)
		{
			_loadingSpinner?.FadeTo(1, 100);
			_label?.FadeTo(0, 100);
			_progressLabel?.FadeTo(0, 100);
		}
		else
		{
			_loadingSpinner?.FadeTo(0, 100);
			_label?.FadeTo(1, 100);
		}

		if (_background != null)
			UpdateVisualState(_isHovered);
	}

	public void SetProgress(string text)
	{
		if (_progressLabel == null)
			return;

		_progressLabel.Text = text;
		_progressLabel.ClearTransforms();

		if (string.IsNullOrEmpty(text))
		{
			_progressLabel.FadeTo(0, 100);
			return;
		}

		if (_isLoading)
		{
			_progressLabel.FadeTo(1, 100);
			_label?.FadeTo(0, 100);
			return;
		}

		_progressLabel.FadeTo(1, 100).Then().Delay(2000).FadeTo(0, 500);
	}

	public void SetTrackingState(bool isTracking)
	{
		Text = isTracking ? "Stop Session" : "Start Session";
		SetAccentColor(isTracking ? Theme.DangerFill : Theme.SuccessFill);
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		if (HasContentPadding)
			AutoSizeAxes = Axes.Both;

		Masking = true;
		CornerRadius = 2;

		InternalChildren = CreateContent();
		ConfigureBoundedLabel();
		UpdateVisualState(false);

		if (BindEnabled != null)
		{
			BindEnabled.BindValueChanged(e =>
			{
				Enabled = e.NewValue;
				this.FadeTo(e.NewValue ? 1 : 0.5f, 100);
			}, true);
		}
	}

	private bool HasContentPadding =>
		ContentPadding.Top != 0 || ContentPadding.Bottom != 0 ||
		ContentPadding.Left != 0 || ContentPadding.Right != 0;

	private Drawable[] CreateContent()
	{
		_background = new Box
		{
			RelativeSizeAxes = Axes.Both,
			Colour = Theme.NormalBg
		};
		_accentBar = new Box
		{
			Width = 2,
			RelativeSizeAxes = Axes.Y,
			Colour = Theme.Accent
		};
		_hoverOverlay = new Box
		{
			RelativeSizeAxes = Axes.Both,
			Colour = Color4.White,
			Alpha = 0
		};

		var content = new List<Drawable> { _background, _accentBar, _hoverOverlay };

		if (Appearance == StyledButtonAppearance.Bordered)
		{
			content.Add(_border = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				CornerRadius = 2,
				BorderThickness = 2,
				BorderColour = new Color4(60, 60, 65, 255),
				Child = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Alpha = 0,
					AlwaysPresent = true
				}
			});
		}

		content.Add(CreateLabelContent());

		return content.ToArray();
	}

	private void ConfigureBoundedLabel()
	{
		if (_label == null || (RelativeSizeAxes & Axes.X) == 0 || _labelFlow != null || ShowLoadingIndicator)
			return;

		_label.Anchor = Anchor.CentreLeft;
		_label.Origin = Anchor.CentreLeft;
		_label.RelativeSizeAxes = Axes.X;
		_label.Truncate = true;
		_label.Margin = new MarginPadding { Horizontal = 10 };
	}

	private Drawable CreateLabelContent()
	{
		_label = CreateLabel(_text, Bold ? "Bold" : "", FontSize, Color4.White);

		if (!string.IsNullOrEmpty(_subtitle))
		{
			return _labelFlow = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Spacing = new Vector2(0, 1),
				Children = new Drawable[]
				{
					_label,
					_subtitleLabel = CreateLabel(_subtitle, "", Math.Max(11, FontSize - 3), Theme.MutedLabel)
				}
			};
		}

		if (ShowLoadingIndicator)
		{
			return new Container
			{
				RelativeSizeAxes = Axes.Both,
				Children = new Drawable[]
				{
					_label,
					_loadingSpinner = new Container
					{
						Anchor = Anchor.Centre,
						Origin = Anchor.Centre,
						Size = new Vector2(14),
						Alpha = 0,
						AlwaysPresent = true,
						Child = _loadingDot = new Box
						{
							RelativeSizeAxes = Axes.Both,
							Colour = Theme.Accent
						}
					},
					_progressLabel = new SpriteText
					{
						Text = string.Empty,
						Font = CreateFont(Math.Max(11, FontSize - 2), string.Empty),
						Colour = Theme.MutedLabel,
						Anchor = Anchor.Centre,
						Origin = Anchor.Centre,
						Alpha = 0
					}
				}
			};
		}

		if (HasContentPadding)
		{
			return new Container
			{
				AutoSizeAxes = Axes.Both,
				Padding = ContentPadding,
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Child = _label
			};
		}

		return _label;
	}

	private SpriteText CreateLabel(string text, string weight, float size, Color4 colour)
	{
		return new SpriteText
		{
			Text = text,
			Font = CreateFont(size, weight),
			Colour = colour,
			Anchor = string.IsNullOrEmpty(_subtitle) ? Anchor.Centre : Anchor.TopCentre,
			Origin = string.IsNullOrEmpty(_subtitle) ? Anchor.Centre : Anchor.TopCentre
		};
	}

	private FontUsage CreateFont(float size, string weight)
	{
		return UseUnicodeFont
			? new FontUsage("Noto-Basic", size, weight)
			: new FontUsage("", size, weight);
	}

	private void UpdateLabelText()
	{
		_label.Text = _isSelected && !string.IsNullOrEmpty(_selectedText) ? _selectedText : _text;
	}

	private Color4 ResolveAccent() => AccentColor ?? CustomFillColor ?? Theme.Accent;

	private Color4 ResolveAccentHover() => ResolveAccent().Lighten(0.08f);

	private bool HasCustomAccent => AccentColor.HasValue || CustomFillColor.HasValue;

	private static Color4 BlendWithAccent(Color4 baseColor, Color4 accent, float accentWeight = 0.5f)
	{
		var inverse = 1f - accentWeight;
		return new Color4(
			baseColor.R * inverse + accent.R * accentWeight,
			baseColor.G * inverse + accent.G * accentWeight,
			baseColor.B * inverse + accent.B * accentWeight,
			1f);
	}

	private Color4 ResolveSurfaceBackground(bool hovered)
	{
		if (!hovered)
			return Theme.NormalBg;

		return HasCustomAccent
			? BlendWithAccent(Theme.HoverBg, ResolveAccent())
			: Theme.HoverBg;
	}

	private (Color4 bg, Color4 label, bool showAccent, Color4 accent, Color4? border) ResolveColors(bool hovered)
	{
		var accent = ResolveAccent();
		var accentHover = ResolveAccentHover();

		if (_isRecording)
		{
			var recording = RecordingFillColor ?? accent;
			return (Theme.NormalBg, new Color4(50, 50, 60, 255), ShowAccentBar, recording, null);
		}

		if (!_isEnabled)
		{
			return Appearance switch
			{
				StyledButtonAppearance.Muted => (Theme.DisabledBg, Theme.DisabledLabel, false, Theme.DisabledAccent, null),
				StyledButtonAppearance.Bordered => (Theme.DisabledBg, Theme.DisabledLabel, false, Theme.DisabledAccent, new Color4(50, 50, 55, 255)),
				_ => (Theme.DisabledBg, Theme.DisabledLabel, ShowAccentBar, Theme.DisabledAccent, null)
			};
		}

		switch (Appearance)
		{
			case StyledButtonAppearance.Bordered:
				if (_isSelected)
					return (HasCustomAccent ? BlendWithAccent(Theme.NormalBg, accent, 0.3f) : accent.Opacity(0.3f), accent, false, accentHover, accent);

				if (hovered)
					return (ResolveSurfaceBackground(true), Color4.White, false, accentHover, new Color4(80, 80, 85, 255));

				return (Theme.NormalBg, Color4.White, false, accent, new Color4(60, 60, 65, 255));

			case StyledButtonAppearance.Toggle:
				if (_isSelected)
					return (ResolveSurfaceBackground(hovered), Color4.White, ShowAccentBar, accentHover, null);

				return (ResolveSurfaceBackground(hovered), Theme.MutedLabel, ShowAccentBar, hovered ? accentHover : accent, null);

			case StyledButtonAppearance.Muted:
				return (ResolveSurfaceBackground(hovered), Theme.MutedLabel, HasCustomAccent && ShowAccentBar, accent, null);

			case StyledButtonAppearance.Custom:
				{
					var normal = CustomNormalBg ?? Theme.NormalBg;
					var hover = CustomHoverBg ?? Theme.HoverBg;
					return (hovered ? hover : normal, Color4.White, ShowAccentBar, hovered ? accentHover : accent, null);
				}

			case StyledButtonAppearance.Filled:
			case StyledButtonAppearance.Standard:
			default:
				return (ResolveSurfaceBackground(hovered), Color4.White, ShowAccentBar, hovered ? accentHover : accent, null);
		}
	}

	private void UpdateVisualState(bool hovered)
	{
		if (_background == null || _label == null)
			return;

		_isHovered = hovered;
		var (bg, label, showAccent, accent, border) = ResolveColors(hovered);

		_background.Colour = bg;
		_accentBar.Alpha = showAccent ? 1 : 0;
		_accentBar.Colour = accent;

		if (_border != null && border.HasValue)
			_border.BorderColour = border.Value;

		UpdateLabelText();

		if (Appearance == StyledButtonAppearance.Toggle && _isSelected)
			_label.Font = CreateFont(FontSize, "Bold");
		else
			_label.Font = CreateFont(FontSize, Bold ? "Bold" : "");

		_label.Colour = _isRecording ? new Color4(50, 50, 60, 255) : label;

		if (!_isEnabled || _isLoading)
			_hoverOverlay.Alpha = 0;
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (_isEnabled && !_isLoading && !_isRecording)
		{
			UpdateVisualState(true);
			_hoverOverlay.FadeTo(Appearance == StyledButtonAppearance.Muted ? 0.12f : 0.06f, 100);
		}

		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		UpdateVisualState(false);
		_hoverOverlay.FadeTo(0, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button == osuTK.Input.MouseButton.Right && RightClicked != null)
		{
			RightClicked.Invoke();
			_hoverOverlay.FadeTo(0.14f, 50).Then().FadeTo(_isHovered ? 0.06f : 0, 100);
			return true;
		}

		return base.OnMouseDown(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (!_isEnabled || _isLoading)
			return false;

		if (SuppressClickWhenSelected && _isSelected)
			return true;

		if (ToggleOnClick)
		{
			Selected = !Selected;
			SelectedChanged?.Invoke(Selected);
		}

		if (e.ShiftPressed && (ShiftClicked != null || ShiftAction != null))
		{
			ShiftClicked?.Invoke();
			ShiftAction?.Invoke();
		}
		else if (e.ControlPressed && CtrlClicked != null)
			CtrlClicked.Invoke();
		else
		{
			Clicked?.Invoke();
			Action?.Invoke();
		}

		_hoverOverlay.FadeTo(0.14f, 50).Then().FadeTo(_isHovered ? 0.06f : 0, 100);
		return true;
	}

	protected override bool OnKeyDown(KeyDownEvent e)
	{
		if (_isRecording && OnKeyDownCallback != null)
			return OnKeyDownCallback(e);

		return base.OnKeyDown(e);
	}

	protected override void OnKeyUp(KeyUpEvent e)
	{
		if (_isRecording)
			OnKeyUpCallback?.Invoke(e);

		base.OnKeyUp(e);
	}

	protected override void OnFocus(FocusEvent e)
	{
		base.OnFocus(e);
		if (_isRecording)
			UpdateVisualState(_isHovered);
	}

	protected override void Update()
	{
		base.Update();

		if (!_isLoading || _loadingDot == null)
			return;

		_loadingDot.Alpha = (float)(Math.Sin(Clock.CurrentTime / 200) * 0.35 + 0.65);
	}
}
