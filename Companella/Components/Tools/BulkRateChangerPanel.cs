using System.Globalization;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Components.Settings;
using Companella.Models.Application;
using Companella.Services.Common;
using Companella.Services.Tools;
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
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Modern panel for bulk rate changing with min/max/step inputs.
/// </summary>
public partial class BulkRateChangerPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	private StyledTextBox _minRateTextBox = null!;
	private StyledTextBox _maxRateTextBox = null!;
	private StyledTextBox _stepTextBox = null!;
	private StyledTextBox _formatTextBox = null!;
	private StyledButton _applyButton = null!;
	private SpriteText _summaryText = null!;
	private SpriteText _ratesPreviewText = null!;
	private FillFlowContainer _presetContainer = null!;
	private SettingsCheckbox _pitchAdjustCheckbox = null!;
	private SettingsCheckbox _excludeBaseRateCheckbox = null!;
	private BasicSliderBar<double> _odSlider = null!;
	private BasicSliderBar<double> _hpSlider = null!;
	private SpriteText _odValueText = null!;
	private SpriteText _hpValueText = null!;
	private StyledButton _odLockButton = null!;
	private StyledButton _hpLockButton = null!;

	private List<BulkRatePreset> _presets = BulkRatePreset.GetDefaults();

	public event Action<double, double, double, string, bool, double, double, bool>? ApplyBulkRateClicked;
	public event Action<string>? FormatChanged;
	public event Action<bool>? PitchAdjustChanged;
	public event Action<int, BulkRatePreset>? PresetEditRequested;

	private double _minRate = 0.5;
	private double _maxRate = 1.5;
	private double _step = 0.1;
	private bool _pitchAdjust = true;
	private bool _excludeBaseRate;
	private double _currentOd = 8.0;
	private double _currentHp = 8.0;
	private bool _odLocked;
	private bool _hpLocked;
	private string _format = RateChanger.DefaultNameFormat;

	private const double _minRateLimit = 0.1;
	private const double _maxRateLimit = 3.0;
	private const double _minStep = 0.01;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	public BulkRateChangerPanel()
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
					// Presets Section
					CreateSection("Quick Presets", new Drawable[]
					{
						_presetContainer = new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(8, 0),
							Children = CreatePresetButtons()
						}
					}),
					// Range Section
					CreateSection("Rate Range", new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(12, 0),
							Children = new Drawable[]
							{
								CreateLabeledInput("Min", out _minRateTextBox, RateChanger.FormatDecimalForDisplay(0.5), 39),
								new SpriteText
								{
									Text = "to",
									Font = new FontUsage("", 16),
									Colour = new Color4(100, 100, 100, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								},
								CreateLabeledInput("Max", out _maxRateTextBox, RateChanger.FormatDecimalForDisplay(1.5), 39),
								new Container { Width = 10 }, // Spacer
								CreateLabeledInput("Step", out _stepTextBox, RateChanger.FormatDecimalForDisplay(0.1), 39)
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
						}
					}),
					// Pitch Adjust Checkbox
					_pitchAdjustCheckbox = new SettingsCheckbox
					{
						LabelText = "Change Pitch",
						IsChecked = true
					},
					// Exclude Base Rate Checkbox
					_excludeBaseRateCheckbox = new SettingsCheckbox
					{
						LabelText = "Exclude Base Rate (1.0x)",
						IsChecked = false,
						TooltipText = "Skip creating the 1.0x rate version"
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
					// Summary Section
					new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Masking = true,
						CornerRadius = 6,
						Children = new Drawable[]
						{
							new Box
							{
								RelativeSizeAxes = Axes.Both,
								Colour = new Color4(30, 30, 35, 255)
							},
							new FillFlowContainer
							{
								RelativeSizeAxes = Axes.X,
								AutoSizeAxes = Axes.Y,
								Direction = FillDirection.Vertical,
								Padding = new MarginPadding(12),
								Spacing = new Vector2(0, 4),
								Children = new Drawable[]
								{
									_summaryText = new SpriteText
									{
										Text = "Will create: 11 beatmaps",
										Font = new FontUsage("", 16, "Bold"),
										Colour = _accentColor
									},
									_ratesPreviewText = new SpriteText
									{
										Text = "0.5x, 0.6x, 0.7x, ... 1.5x",
										Font = new FontUsage("", 14),
										Colour = new Color4(120, 120, 120, 255)
									}
								}
							}
						}
					},
					// Apply Button
					_applyButton = new StyledButton("Create All Beatmaps", StyledButton.Theme.SuccessFill)
					{
						RelativeSizeAxes = Axes.X,
						Height = 40,
						Enabled = false,
						TooltipText = "Create multiple rate-changed difficulties at once"
					}
				}
			}
		};

		// Load presets from settings
		LoadPresetsFromSettings();

		// Set initial values
		_formatTextBox.Text = _format;

		// Min / max / step: validate and reformat only on Enter or focus loss, not while typing
		_minRateTextBox.OnCommit += OnRateRangeCommit;
		_maxRateTextBox.OnCommit += OnRateRangeCommit;
		_stepTextBox.OnCommit += OnRateRangeCommit;
		_formatTextBox.Current.BindValueChanged(_ => OnFormatChanged());
		_applyButton.Clicked += OnApplyClicked;
		_pitchAdjustCheckbox.CheckedChanged += OnPitchAdjustChanged;
		_excludeBaseRateCheckbox.CheckedChanged += OnExcludeBaseRateChanged;

		// OD/HP slider events
		_odSlider.Current.ValueChanged += e => OnOdSliderChanged(e.NewValue);
		_hpSlider.Current.ValueChanged += e => OnHpSliderChanged(e.NewValue);
		_odLockButton.SelectedChanged += OnOdLockChanged;
		_hpLockButton.SelectedChanged += OnHpLockChanged;

		UpdatePreview();
	}

	private void LoadPresetsFromSettings()
	{
		var savedPresets = UserSettingsService.Settings.BulkRatePresets;
		if (savedPresets != null && savedPresets.Count == 4)
			_presets = savedPresets;
		else
			_presets = BulkRatePreset.GetDefaults();
	}

	private void OnPitchAdjustChanged(bool isChecked)
	{
		_pitchAdjust = isChecked;
		PitchAdjustChanged?.Invoke(isChecked);
	}

	private void OnExcludeBaseRateChanged(bool isChecked)
	{
		_excludeBaseRate = isChecked;
		UpdatePreview();
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

	private Drawable[] CreatePresetButtons()
	{
		var buttons = new List<Drawable>();
		for (var i = 0; i < _presets.Count; i++)
		{
			var preset = _presets[i];
			var index = i;
			var button = new StyledButton(preset.Name)
			{
				Size = new Vector2(90, 36),
				Subtitle = preset.GetSubtitle(),
				FontSize = 15,
				Action = () => ApplyPreset(preset),
				TooltipText = preset.GetTooltip() + " (right-click to edit)"
			};
			button.RightClicked += () => PresetEditRequested?.Invoke(index, _presets[index]);
			buttons.Add(button);
		}

		return buttons.ToArray();
	}

	/// <summary>
	/// Updates a preset at the specified index and saves to settings.
	/// </summary>
	public void UpdatePreset(int index, BulkRatePreset preset)
	{
		if (index < 0 || index >= _presets.Count)
			return;

		_presets[index] = preset;

		// Save to settings
		UserSettingsService.Settings.BulkRatePresets = new List<BulkRatePreset>(_presets);
		Task.Run(async () => await UserSettingsService.SaveAsync());

		// Rebuild buttons
		RefreshPresetButtons();
	}

	private void RefreshPresetButtons()
	{
		_presetContainer.Clear();
		_presetContainer.AddRange(CreatePresetButtons());
	}

	/// <summary>
	/// Gets the current presets.
	/// </summary>
	public List<BulkRatePreset> GetPresets()
	{
		return _presets;
	}

	private void ApplyPreset(BulkRatePreset preset)
	{
		_minRate = preset.MinRate;
		_maxRate = preset.MaxRate;
		_step = preset.Step;
		_minRateTextBox.Text = RateChanger.FormatDecimalForDisplay(preset.MinRate);
		_maxRateTextBox.Text = RateChanger.FormatDecimalForDisplay(preset.MaxRate);
		_stepTextBox.Text = RateChanger.FormatDecimalForDisplay(preset.Step);

		// Apply OD/HP if specified in preset
		if (preset.OD.HasValue)
		{
			_currentOd = preset.OD.Value;
			_odSlider.Current.Value = preset.OD.Value;
			_odValueText.Text = preset.OD.Value.ToString("0.0", CultureInfo.InvariantCulture);
		}

		if (preset.HP.HasValue)
		{
			_currentHp = preset.HP.Value;
			_hpSlider.Current.Value = preset.HP.Value;
			_hpValueText.Text = preset.HP.Value.ToString("0.0", CultureInfo.InvariantCulture);
		}

		// Apply exclude base rate setting
		_excludeBaseRate = preset.ExcludeBaseRate;
		_excludeBaseRateCheckbox.IsChecked = preset.ExcludeBaseRate;

		UpdatePreview();
	}

	private static SettingsSection CreateSection(string title, Drawable[] content) =>
		SettingsLayout.CreateSection(title, content);

	private static Container CreateLabeledInput(string label, out StyledTextBox textBox, string defaultValue,
		float width)
	{
		textBox = new StyledTextBox
		{
			Size = new Vector2(width, 32),
			Text = defaultValue,
			CommitOnFocusLost = true
		};

		return new Container
		{
			AutoSizeAxes = Axes.Both,
			Child = new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("", 13),
						Colour = new Color4(100, 100, 100, 255)
					},
					textBox
				}
			}
		};
	}

	private void OnRateRangeCommit(TextBox sender, bool newText)
	{
		CommitRateRangeInputs();
	}

	/// <summary>
	/// Parses min/max/step, clamps, reformats fields, and refreshes the preview.
	/// </summary>
	private void CommitRateRangeInputs()
	{
		// Parse min rate
		if (double.TryParse(_minRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minVal))
			_minRate = Math.Clamp(minVal, _minRateLimit, _maxRateLimit);

		_minRateTextBox.Text = RateChanger.FormatDecimalForDisplay(_minRate);

		// Parse max rate
		if (double.TryParse(_maxRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxVal))
			_maxRate = Math.Clamp(maxVal, _minRateLimit, _maxRateLimit);

		if (_maxRate < _minRate)
			_maxRate = _minRate;
		_maxRateTextBox.Text = RateChanger.FormatDecimalForDisplay(_maxRate);

		// Parse step
		if (double.TryParse(_stepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stepVal))
			_step = Math.Max(stepVal, _minStep);

		_stepTextBox.Text = RateChanger.FormatDecimalForDisplay(_step);

		UpdatePreview();
	}

	private void OnFormatChanged()
	{
		_format = _formatTextBox.Text;
		if (string.IsNullOrWhiteSpace(_format))
		{
			_format = RateChanger.DefaultNameFormat;
			_formatTextBox.Text = _format;
		}

		FormatChanged?.Invoke(_format);
	}

	public void SetFormat(string format)
	{
		if (string.IsNullOrWhiteSpace(format))
			format = RateChanger.DefaultNameFormat;

		_format = format;
		_formatTextBox.Text = format;
	}

	private void UpdatePreview()
	{
		var rates = CalculateRates();
		_summaryText.Text = $"Will create: {rates.Count} beatmap{(rates.Count != 1 ? "s" : "")}";

		if (rates.Count == 0)
		{
			_ratesPreviewText.Text = "No rates in range";
		}
		else if (rates.Count <= 6)
		{
			_ratesPreviewText.Text = string.Join(", ", rates.Select(r => $"{r:0.0#}x"));
		}
		else
		{
			var first = rates.Take(3).Select(r => $"{r:0.0#}x");
			var last = rates.TakeLast(2).Select(r => $"{r:0.0#}x");
			_ratesPreviewText.Text = $"{string.Join(", ", first)}, ... {string.Join(", ", last)}";
		}
	}

	private List<double> CalculateRates()
	{
		var rates = new List<double>();

		for (var rate = _minRate; rate < _maxRate - 0.001; rate += _step) rates.Add(Math.Round(rate, 2));

		if (rates.Count == 0 || Math.Abs(rates[^1] - _maxRate) > 0.001) rates.Add(Math.Round(_maxRate, 2));

		// Filter out base rate (1.0x) if excluded
		if (_excludeBaseRate) rates = rates.Where(r => Math.Abs(r - 1.0) > 0.001).ToList();

		return rates;
	}

	private void OnApplyClicked()
	{
		CommitRateRangeInputs();
		ApplyBulkRateClicked?.Invoke(_minRate, _maxRate, _step, _format, _pitchAdjust, _currentOd, _currentHp,
			_excludeBaseRate);
	}

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

	public void SetEnabled(bool enabled)
	{
		_applyButton.Enabled = enabled;
	}
}
