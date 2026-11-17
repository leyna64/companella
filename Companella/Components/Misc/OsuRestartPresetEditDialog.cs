using Companella.Components.Tools;
using Companella.Models.Application;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// Dialog for editing an osu! restart preset.
/// </summary>
public partial class OsuRestartPresetEditDialog : CompositeDrawable
{
	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private StyledTextBox _nameTextBox = null!;
	private StyledTextBox _argumentsTextBox = null!;
	private StyledButton _saveButton = null!;
	private StyledButton _cancelButton = null!;
	private SpriteText _errorText = null!;

	private int _presetIndex;

	/// <summary>
	/// Event raised when the preset is saved.
	/// </summary>
	public event Action<int, OsuRestartPreset>? PresetSaved;

	/// <summary>
	/// Event raised when the dialog is closed.
	/// </summary>
	public event Action? Closed;

	public OsuRestartPresetEditDialog()
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
			_dialogContainer = StyledDialog.CreateShell(new Vector2(400, 260), out var content)
		};

		content.Child = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 14),
			Children = new Drawable[]
			{
				_titleText = StyledDialog.CreateTitle("Edit Preset"),
				CreateLabeledInput("Preset Name", out _nameTextBox, "e.g., Bancho, Mames"),
				CreateLabeledInput("Command Line Arguments", out _argumentsTextBox, "e.g., -devserver mamesosu.net"),
				_errorText = StyledDialog.CreateErrorText(),
				new FillFlowContainer
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(12, 0),
					Margin = new MarginPadding { Top = 4 },
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

	private static Container CreateLabeledInput(string label, out StyledTextBox textBox, string placeholder = "")
	{
		textBox = new StyledTextBox
		{
			RelativeSizeAxes = Axes.X,
			Height = 36,
			PlaceholderText = placeholder
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
				Spacing = new Vector2(0, 6),
				Children = new Drawable[]
				{
					StyledDialog.CreateFieldLabel(label),
					textBox
				}
			}
		};
	}

	/// <summary>
	/// Shows the dialog for editing a preset.
	/// </summary>
	public void Show(int presetIndex, OsuRestartPreset preset)
	{
		_presetIndex = presetIndex;

		_nameTextBox.Text = preset.Name;
		_argumentsTextBox.Text = preset.Arguments;

		_errorText.Alpha = 0;
		_titleText.Text = presetIndex >= 0 ? "Edit Preset" : "Add Preset";

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

		var arguments = _argumentsTextBox.Text.Trim();
		var updatedPreset = new OsuRestartPreset(name, arguments);
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
