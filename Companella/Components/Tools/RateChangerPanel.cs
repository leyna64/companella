using System.Globalization;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Components.Settings;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
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
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace Companella.Components.Tools;

/// <summary>
/// Modern panel for changing beatmap playback rate.
/// </summary>
public partial class RateChangerPanel : CompositeDrawable
{
	private StyledTextBox _rateTextBox = null!;
	private StyledTextBox _targetBpmTextBox = null!;
	private StyledTextBox _formatTextBox = null!;
	private StyledButton _applyButton = null!;
	private SpriteText _previewText = null!;
	private SpriteText _currentBpmLabel = null!;
	private FillFlowContainer _quickRateButtons = null!;
	private SettingsCheckbox _pitchAdjustCheckbox = null!;
	private BasicSliderBar<double> _odSlider = null!;
	private BasicSliderBar<double> _hpSlider = null!;
	private SpriteText _odValueText = null!;
	private SpriteText _hpValueText = null!;
	private StyledButton _odLockButton = null!;
	private StyledButton _hpLockButton = null!;

	public event Action<double, string, bool, double, double>? ApplyRateClicked;
	public event Action<string>? FormatChanged;
	public event Action<double, string>? PreviewRequested;
	public event Action<bool>? PitchAdjustChanged;

	private double _currentRate = 1.0;
	private double _currentMapBpm = 120.0;
	private double _targetBpm = 120.0;
	private bool _pitchAdjust = true;
	private double _currentOd = 8.0;
	private double _currentHp = 8.0;
	private bool _odLocked;
	private bool _hpLocked;
	private string _currentFormat = RateChanger.DefaultNameFormat;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	public RateChangerPanel()
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
					// Pitch Adjust Checkbox
					_pitchAdjustCheckbox = new SettingsCheckbox
					{
						LabelText = "Change Pitch",
						IsChecked = true,
						TooltipText = "When unchecked, preserves original pitch (nightcore-style)"
					},
					// OD/HP Sliders Section
					CreateSection("Difficulty Settings", new Drawable[]
					{
						// OD Slider Row
						new Container
						{
							RelativeSizeAxes = Axes.X,
							Height = 28,
							Children = new Drawable[]
							{
								new SpriteText
								{
									Text = "OD",
									Font = new FontUsage("", 15),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Width = 25
								},
								new Container
								{
									RelativeSizeAxes = Axes.X,
									Height = 20,
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Padding = new MarginPadding { Left = 30, Right = 80 },
									Child = _odSlider = new BasicSliderBar<double>
									{
										RelativeSizeAxes = Axes.X,
										Height = 20,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Current = new BindableDouble(8.0)
											{ MinValue = 0, MaxValue = 10, Precision = 0.1 },
										BackgroundColour = new Color4(40, 40, 45, 255),
										SelectionColour = _accentColor
									}
								},
								_odValueText = new SpriteText
								{
									Text = "8.0",
									Font = new FontUsage("", 15),
									Colour = new Color4(200, 200, 200, 255),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									Margin = new MarginPadding { Right = 35 }
								},
								_odLockButton = new StyledButton("U", StyledButtonAppearance.Toggle)
								{
									Size = new Vector2(24, 24),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									SelectedText = "L",
									ToggleOnClick = true,
									FontSize = 12,
									TooltipText = "Lock OD value when changing maps"
								}
							}
						},
						// HP Slider Row
						new Container
						{
							RelativeSizeAxes = Axes.X,
							Height = 28,
							Margin = new MarginPadding { Top = 4 },
							Children = new Drawable[]
							{
								new SpriteText
								{
									Text = "HP",
									Font = new FontUsage("", 15),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Width = 25
								},
								new Container
								{
									RelativeSizeAxes = Axes.X,
									Height = 20,
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Padding = new MarginPadding { Left = 30, Right = 80 },
									Child = _hpSlider = new BasicSliderBar<double>
									{
										RelativeSizeAxes = Axes.X,
										Height = 20,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Current = new BindableDouble(8.0)
											{ MinValue = 0, MaxValue = 10, Precision = 0.1 },
										BackgroundColour = new Color4(40, 40, 45, 255),
										SelectionColour = _accentColor
									}
								},
								_hpValueText = new SpriteText
								{
									Text = "8.0",
									Font = new FontUsage("", 15),
									Colour = new Color4(200, 200, 200, 255),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									Margin = new MarginPadding { Right = 35 }
								},
								_hpLockButton = new StyledButton("U", StyledButtonAppearance.Toggle)
								{
									Size = new Vector2(24, 24),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									SelectedText = "L",
									ToggleOnClick = true,
									FontSize = 12,
									TooltipText = "Lock HP value when changing maps"
								}
							}
						}
					}),
					// Rate Selection Section
					CreateSection("Rate", new Drawable[]
					{
						// Quick rate buttons
						_quickRateButtons = new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(6, 6),
							Children = CreateQuickRateButtons()
						},
						// Custom rate input + Target BPM input on same row
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(8, 0),
							Margin = new MarginPadding { Top = 8 },
							Children = new Drawable[]
							{
								new SpriteText
								{
									Text = "Custom:",
									Font = new FontUsage("", 16),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								},
								_rateTextBox = new StyledTextBox
								{
									Size = new Vector2(53, 32),
									PlaceholderText = "1.00",
									CommitOnFocusLost = true
								},
								new SpriteText
								{
									Text = "x",
									Font = new FontUsage("", 17),
									Colour = new Color4(100, 100, 100, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								},
								new SpriteText
								{
									Text = "Target BPM:",
									Font = new FontUsage("", 16),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Margin = new MarginPadding { Left = 12 }
								},
								_targetBpmTextBox = new StyledTextBox
								{
									Size = new Vector2(53, 32),
									PlaceholderText = "120.00",
									CommitOnFocusLost = true
								},
								_currentBpmLabel = new SpriteText
								{
									Text = "(Current: --)",
									Font = new FontUsage("", 14),
									Colour = new Color4(100, 100, 100, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Margin = new MarginPadding { Left = 4 }
								}
							}
						}
					}),
					// Format Section
					CreateSection("Difficulty Name Format", new Drawable[]
					{
						_formatTextBox = new StyledTextBox
						{
							RelativeSizeAxes = Axes.X,
							Height = 32,
							PlaceholderText = "[[name]] [[rate]]"
						},
						new SpriteText
						{
							Text = "Available: [[name]] [[rate]] [[bpm]] [[od]] [[hp]] [[msd]]",
							Font = new FontUsage("", 17),
							Colour = new Color4(90, 90, 90, 255),
							Margin = new MarginPadding { Top = 4 }
						}
					}),
					// Preview Section
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
									new SpriteText
									{
										Text = "Preview:",
										Font = new FontUsage("", 15),
										Colour = new Color4(120, 120, 120, 255),
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft
									},
									_previewText = new SpriteText
									{
										Text = "...",
										Font = new FontUsage("", 15),
										Colour = _accentColor,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft
									}
								}
							}
						}
					},
					// Apply Button
					_applyButton = new StyledButton("Create Rate-Changed Beatmap")
					{
						RelativeSizeAxes = Axes.X,
						Height = 40,
						Enabled = false,
						TooltipText = "Create a new difficulty with modified audio speed"
					}
				}
			}
		};

		// Set initial values
		_rateTextBox.Text = RateChanger.FormatRateForDisplay(1.0);
		_targetBpmTextBox.Text = RateChanger.FormatBpmForDisplay(120.0);
		_formatTextBox.Text = _currentFormat;

		// Rate / BPM: validate and sync only on Enter or focus loss (OnCommit), not while typing
		_rateTextBox.OnCommit += OnRateTextCommit;
		_targetBpmTextBox.OnCommit += OnTargetBpmTextCommit;
		_formatTextBox.Current.BindValueChanged(_ => OnFormatTextChanged());
		_applyButton.Clicked += OnApplyClicked;
		_pitchAdjustCheckbox.CheckedChanged += OnPitchAdjustChanged;

		// OD/HP slider events
		_odSlider.Current.ValueChanged += e => OnOdSliderChanged(e.NewValue);
		_hpSlider.Current.ValueChanged += e => OnHpSliderChanged(e.NewValue);
		_odLockButton.SelectedChanged += OnOdLockChanged;
		_hpLockButton.SelectedChanged += OnHpLockChanged;
	}

	private void OnPitchAdjustChanged(bool isChecked)
	{
		_pitchAdjust = isChecked;
		PitchAdjustChanged?.Invoke(isChecked);
	}

	private void OnOdSliderChanged(double value)
	{
		_currentOd = value;
		_odValueText.Text = value.ToString("0.0", CultureInfo.InvariantCulture);
	}

	private void OnHpSliderChanged(double value)
	{
		_currentHp = value;
		_hpValueText.Text = value.ToString("0.0", CultureInfo.InvariantCulture);
	}

	private void OnOdLockChanged(bool isLocked)
	{
		_odLocked = isLocked;
		_odSlider.Alpha = isLocked ? 0.5f : 1.0f;
	}

	private void OnHpLockChanged(bool isLocked)
	{
		_hpLocked = isLocked;
		_hpSlider.Alpha = isLocked ? 0.5f : 1.0f;
	}

	private Drawable[] CreateQuickRateButtons()
	{
		var rates = new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 1.1, 1.2, 1.3, 1.4 };
		var buttons = new List<Drawable>();

		foreach (var rate in rates)
			buttons.Add(new StyledButton($"{rate:0.0#}x", StyledButtonAppearance.Toggle)
			{
				Size = new Vector2(42, 28),
				Tag = rate,
				Selected = Math.Abs(rate - 1.0) < 0.001,
				FontSize = 12,
				Bold = false,
				AccentColor = _accentColor,
				Action = () => SetRate(rate),
				TooltipText = $"Create a {rate:0.0#}x speed version"
			});

		return buttons.ToArray();
	}

	private static SettingsSection CreateSection(string title, Drawable[] content) =>
		SettingsLayout.CreateSection(title, content);

	/// <summary>
	/// Sets the rate value programmatically.
	/// </summary>
	public void SetRate(double rate)
	{
		_currentRate = rate;
		_rateTextBox.Text = RateChanger.FormatRateForDisplay(rate);

		// Update target BPM based on new rate
		if (_currentMapBpm > 0)
		{
			_targetBpm = _currentMapBpm * _currentRate;
			_targetBpmTextBox.Text = RateChanger.FormatBpmForDisplay(_targetBpm);
		}

		UpdateQuickRateButtonSelection(rate);
		UpdatePreview();
	}

	private void UpdateQuickRateButtonSelection(double selectedRate)
	{
		foreach (var child in _quickRateButtons.Children)
			if (child is StyledButton button && button.Tag is double rate)
				button.SetSelected(Math.Abs(rate - selectedRate) < 0.001);
	}

	private void OnRateTextCommit(TextBox sender, bool newText)
	{
		if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			_currentRate = Math.Clamp(value, 0.1, 5.0);
			sender.Text = RateChanger.FormatRateForDisplay(_currentRate);
		}
		else
		{
			sender.Text = RateChanger.FormatRateForDisplay(_currentRate);
		}

		// Update target BPM based on new rate
		if (_currentMapBpm > 0)
		{
			_targetBpm = _currentMapBpm * _currentRate;
			_targetBpmTextBox.Text = RateChanger.FormatBpmForDisplay(_targetBpm);
		}

		UpdateQuickRateButtonSelection(_currentRate);
		UpdatePreview();
	}

	private void OnTargetBpmTextCommit(TextBox sender, bool newText)
	{
		if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			_targetBpm = Math.Clamp(value, 10, 1000);
			sender.Text = RateChanger.FormatBpmForDisplay(_targetBpm);

			// Calculate rate from target BPM and update rate textbox
			if (_currentMapBpm > 0)
			{
				_currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
				_rateTextBox.Text = RateChanger.FormatRateForDisplay(_currentRate);
				UpdateQuickRateButtonSelection(_currentRate);
			}
		}
		else
		{
			sender.Text = RateChanger.FormatBpmForDisplay(_targetBpm);
		}

		UpdatePreview();
	}

	/// <summary>
	/// Applies rate and BPM from the text boxes (used when creating the beatmap so unsent edits are not lost).
	/// When a map BPM is known and both fields parse, target BPM takes precedence.
	/// </summary>
	private void CommitRateAndBpmInputsForApply()
	{
		var rateOk = double.TryParse(_rateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var rateVal);
		var bpmOk = double.TryParse(_targetBpmTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var bpmVal);

		if (_currentMapBpm > 0 && bpmOk)
		{
			_targetBpm = Math.Clamp(bpmVal, 10, 1000);
			_currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
		}
		else if (rateOk)
		{
			_currentRate = Math.Clamp(rateVal, 0.1, 5.0);
			if (_currentMapBpm > 0)
				_targetBpm = _currentMapBpm * _currentRate;
		}
		else if (bpmOk)
		{
			_targetBpm = Math.Clamp(bpmVal, 10, 1000);
			if (_currentMapBpm > 0)
				_currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
		}
		else
		{
			_rateTextBox.Text = RateChanger.FormatRateForDisplay(_currentRate);
			_targetBpmTextBox.Text = RateChanger.FormatBpmForDisplay(_targetBpm);
			UpdateQuickRateButtonSelection(_currentRate);
			UpdatePreview();
			return;
		}

		_rateTextBox.Text = RateChanger.FormatRateForDisplay(_currentRate);
		_targetBpmTextBox.Text = RateChanger.FormatBpmForDisplay(_targetBpm);
		UpdateQuickRateButtonSelection(_currentRate);
		UpdatePreview();
	}

	/// <summary>
	/// Sets the current map's BPM for target BPM calculations.
	/// </summary>
	public void SetCurrentMapBpm(double bpm)
	{
		_currentMapBpm = bpm > 0 ? bpm : 120;
		_currentBpmLabel.Text = $"(Current: {_currentMapBpm:0.#})";

		// Update target BPM based on current rate
		_targetBpm = _currentMapBpm * _currentRate;
		_targetBpmTextBox.Text = RateChanger.FormatBpmForDisplay(_targetBpm);

		UpdatePreview();
	}

	private void OnFormatTextChanged()
	{
		_currentFormat = _formatTextBox.Text;
		if (!string.IsNullOrWhiteSpace(_currentFormat))
		{
			UpdatePreview();
			FormatChanged?.Invoke(_currentFormat);
		}
	}

	private void OnFormatTextCommit(TextBox sender, bool newText)
	{
		_currentFormat = sender.Text;
		if (string.IsNullOrWhiteSpace(_currentFormat))
		{
			_currentFormat = RateChanger.DefaultNameFormat;
			sender.Text = _currentFormat;
		}

		UpdatePreview();
		FormatChanged?.Invoke(_currentFormat);
	}

	private void UpdatePreview()
	{
		PreviewRequested?.Invoke(_currentRate, _currentFormat);
	}

	private void OnApplyClicked()
	{
		CommitRateAndBpmInputsForApply();
		ApplyRateClicked?.Invoke(_currentRate, _currentFormat, _pitchAdjust, _currentOd, _currentHp);
	}

	/// <summary>
	/// Gets or sets whether pitch is adjusted with rate.
	/// </summary>
	public bool PitchAdjust
	{
		get => _pitchAdjust;
		set
		{
			if (_pitchAdjust == value)
				return;
			_pitchAdjust = value;
			if (_pitchAdjustCheckbox != null) _pitchAdjustCheckbox.IsChecked = value;
		}
	}

	public void SetPreviewText(string text)
	{
		_previewText.Text = text;
	}

	public void SetEnabled(bool enabled)
	{
		_applyButton.Enabled = enabled;
	}

	public void SetFormat(string format)
	{
		if (string.IsNullOrWhiteSpace(format))
			format = RateChanger.DefaultNameFormat;

		_currentFormat = format;
		_formatTextBox.Text = format;
	}

	public string CurrentFormat => _currentFormat;

	/// <summary>
	/// Updates OD/HP values from the current map (unless locked).
	/// </summary>
	public void SetMapDifficultyValues(double od, double hp)
	{
		if (!_odLocked)
		{
			_currentOd = od;
			_odSlider.Current.Value = od;
			_odValueText.Text = od.ToString("0.0", CultureInfo.InvariantCulture);
		}

		if (!_hpLocked)
		{
			_currentHp = hp;
			_hpSlider.Current.Value = hp;
			_hpValueText.Text = hp.ToString("0.0", CultureInfo.InvariantCulture);
		}
	}
}

/// <summary>
/// Modern styled text box with clean appearance.
/// </summary>
public partial class StyledTextBox : BasicTextBox
{
	// Use test font for Unicode character support (Greek letters, symbols)
	private static readonly FontUsage _unicodeFont = new("Noto-Basic", 16);

	public StyledTextBox()
	{
		CornerRadius = 4;
		BackgroundFocused = new Color4(50, 50, 55, 255);
		BackgroundUnfocused = new Color4(35, 35, 40, 255);
		Masking = true;
	}

	protected override SpriteText CreatePlaceholder()
	{
		return new SpriteText
		{
			Font = _unicodeFont,
			Colour = new Color4(80, 80, 80, 255)
		};
	}

	protected override Drawable GetDrawableCharacter(char c)
	{
		return new SpriteText
		{
			Text = c.ToString(),
			Font = _unicodeFont,
			Colour = Color4.White
		};
	}
}
