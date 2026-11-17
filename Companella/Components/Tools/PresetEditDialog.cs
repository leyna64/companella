using System.Globalization;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Models.Application;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Dialog for editing a bulk rate preset.
/// </summary>
public partial class PresetEditDialog : CompositeDrawable
{
	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private StyledTextBox _nameTextBox = null!;
	private StyledTextBox _minRateTextBox = null!;
	private StyledTextBox _maxRateTextBox = null!;
	private StyledTextBox _stepTextBox = null!;
	private StyledTextBox _odTextBox = null!;
	private StyledTextBox _hpTextBox = null!;
	private SettingsCheckbox _excludeBaseRateCheckbox = null!;
	private StyledButton _saveButton = null!;
	private StyledButton _cancelButton = null!;
	private SpriteText _errorText = null!;

	private int _presetIndex;

	/// <summary>
	/// Event raised when the preset is saved.
	/// </summary>
	public event Action<int, BulkRatePreset>? PresetSaved;

	/// <summary>
	/// Event raised when the dialog is closed.
	/// </summary>
	public event Action? Closed;

	public PresetEditDialog()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			StyledDialog.CreateDimBackground(),
			_dialogContainer = StyledDialog.CreateShell(new Vector2(360, 420), out var content)
		};

		content.Child = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				_titleText = StyledDialog.CreateTitle("Edit Preset"),
				CreateLabeledInput("Preset Name", out _nameTextBox),
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(10, 0),
					Children = new Drawable[]
					{
						CreateSmallLabeledInput("Min Rate", out _minRateTextBox, 90),
						CreateSmallLabeledInput("Max Rate", out _maxRateTextBox, 90),
						CreateSmallLabeledInput("Step", out _stepTextBox, 90)
					}
				},
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(10, 0),
					Children = new Drawable[]
					{
						CreateSmallLabeledInput("OD (empty=map)", out _odTextBox, 150),
						CreateSmallLabeledInput("HP (empty=map)", out _hpTextBox, 150)
					}
				},
				_excludeBaseRateCheckbox = new SettingsCheckbox
				{
					LabelText = "Exclude Base Rate (1.0x)",
					IsChecked = false
				},
				_errorText = StyledDialog.CreateErrorText(),
				new FillFlowContainer
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(10, 0),
					Margin = new MarginPadding { Top = 8 },
					Children = new Drawable[]
					{
						_cancelButton = StyledDialog.CreateCancelButton(),
						_saveButton = StyledDialog.CreatePrimaryButton("Save")
					}
				}
			}
		};

		_cancelButton.Clicked += OnCancelClicked;
		_saveButton.Clicked += OnSaveClicked;
	}

	private static Container CreateLabeledInput(string label, out StyledTextBox textBox)
	{
		textBox = new StyledTextBox
		{
			RelativeSizeAxes = Axes.X,
			Height = 32
		};

		return new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Child = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					StyledDialog.CreateFieldLabel(label),
					textBox
				}
			}
		};
	}

	private static Container CreateSmallLabeledInput(string label, out StyledTextBox textBox, float width)
	{
		textBox = new StyledTextBox
		{
			Size = new Vector2(width, 32)
		};

		return new Container
		{
			AutoSizeAxes = Axes.Both,
			Child = new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("", 13),
						Colour = StyledButton.Theme.MutedLabel
					},
					textBox
				}
			}
		};
	}

	/// <summary>
	/// Shows the dialog for editing a preset.
	/// </summary>
	public void Show(int presetIndex, BulkRatePreset preset)
	{
		_presetIndex = presetIndex;

		_nameTextBox.Text = preset.Name;
		_minRateTextBox.Text = preset.MinRate.ToString("0.0#", CultureInfo.InvariantCulture);
		_maxRateTextBox.Text = preset.MaxRate.ToString("0.0#", CultureInfo.InvariantCulture);
		_stepTextBox.Text = preset.Step.ToString("0.0#", CultureInfo.InvariantCulture);
		_odTextBox.Text = preset.OD.HasValue ? preset.OD.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;
		_hpTextBox.Text = preset.HP.HasValue ? preset.HP.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;
		_excludeBaseRateCheckbox.IsChecked = preset.ExcludeBaseRate;

		_errorText.Alpha = 0;
		_titleText.Text = $"Edit Preset {presetIndex + 1}";

		StyledDialog.PlayShowAnimation(this, _dialogContainer);
	}

	/// <summary>
	/// Hides the dialog.
	/// </summary>
	public new void Hide()
	{
		StyledDialog.PlayHideAnimation(this);
		Closed?.Invoke();
	}

	private void OnCancelClicked() => Hide();

	private void OnSaveClicked()
	{
		var name = _nameTextBox.Text.Trim();
		if (string.IsNullOrEmpty(name))
		{
			ShowError("Name cannot be empty");
			return;
		}

		if (!double.TryParse(_minRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minRate) ||
			minRate < 0.1 || minRate > 3.0)
		{
			ShowError("Min rate must be between 0.1 and 3.0");
			return;
		}

		if (!double.TryParse(_maxRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxRate) ||
			maxRate < 0.1 || maxRate > 3.0)
		{
			ShowError("Max rate must be between 0.1 and 3.0");
			return;
		}

		if (maxRate < minRate)
		{
			ShowError("Max rate must be >= min rate");
			return;
		}

		if (!double.TryParse(_stepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var step) ||
			step < 0.01 || step > 1.0)
		{
			ShowError("Step must be between 0.01 and 1.0");
			return;
		}

		double? od = null;
		if (!string.IsNullOrWhiteSpace(_odTextBox.Text))
		{
			if (!double.TryParse(_odTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var odValue) ||
				odValue < 0 || odValue > 10)
			{
				ShowError("OD must be between 0 and 10");
				return;
			}

			od = odValue;
		}

		double? hp = null;
		if (!string.IsNullOrWhiteSpace(_hpTextBox.Text))
		{
			if (!double.TryParse(_hpTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hpValue) ||
				hpValue < 0 || hpValue > 10)
			{
				ShowError("HP must be between 0 and 10");
				return;
			}

			hp = hpValue;
		}

		var updatedPreset = new BulkRatePreset(name, minRate, maxRate, step, od, hp, _excludeBaseRateCheckbox.IsChecked);
		PresetSaved?.Invoke(_presetIndex, updatedPreset);
		Hide();
	}

	private void ShowError(string message)
	{
		_errorText.Text = message;
		_errorText.FadeIn(100).Then().Delay(3000).FadeOut(200);
	}

	protected override bool OnClick(ClickEvent e) => true;
}
