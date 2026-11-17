using Companella.Components.Tools;
using Companella.Models.Application;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// A confirmation dialog for osu! restart with command line arguments support.
/// </summary>
public partial class OsuRestartDialog : CompositeDrawable
{
	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private TextFlowContainer _messageText = null!;
	private StyledTextBox _argsTextBox = null!;
	private OsuRestartPresetDropdown _presetDropdown = null!;
	private StyledButton _editPresetsButton = null!;
	private StyledButton _confirmButton = null!;
	private StyledButton _cancelButton = null!;
	private StyledButton? _skipButton;
	private FillFlowContainer _buttonContainer = null!;
	private OsuRestartPresetEditDialog? _presetEditDialog;

	private List<OsuRestartPreset> _presets = new();
	private OsuRestartPreset? _selectedPreset;

	/// <summary>
	/// Event raised when the user confirms the action.
	/// </summary>
	public event Action<string>? Confirmed;

	/// <summary>
	/// Event raised when the user skips the action (optional).
	/// </summary>
	public event Action? Skipped;

	/// <summary>
	/// Event raised when the dialog is closed (confirmed, skipped, or cancelled).
	/// </summary>
	public event Action? Closed;

	public OsuRestartDialog()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		LoadPresets();

		InternalChildren = new Drawable[]
		{
			StyledDialog.CreateDimBackground(),
			_dialogContainer = StyledDialog.CreateShell(new Vector2(500, 340), out var content, clipBackground: false),
			_presetEditDialog = new OsuRestartPresetEditDialog()
		};

		content.Children = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 14),
				Children = new Drawable[]
				{
					_titleText = StyledDialog.CreateTitle("Restart osu!"),
					_messageText = StyledDialog.CreateMessageFlow("osu! will be restarted."),
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 6),
						Depth = -1,
						Children = new Drawable[]
						{
							StyledDialog.CreateFieldLabel("Preset"),
							new Container
							{
								RelativeSizeAxes = Axes.X,
								Height = 32,
								Children = new Drawable[]
								{
									new Container
									{
										RelativeSizeAxes = Axes.Both,
										Padding = new MarginPadding { Right = 46 },
										Child = _presetDropdown = new OsuRestartPresetDropdown
										{
											RelativeSizeAxes = Axes.Both,
											Items = _presets
										}
									},
									_editPresetsButton = new StyledButton("Edit", StyledButtonAppearance.Muted)
									{
										Anchor = Anchor.CentreRight,
										Origin = Anchor.CentreRight,
										Size = new Vector2(42, 32)
									}
								}
							}
						}
					},
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 6),
						Depth = 1,
						Children = new Drawable[]
						{
							StyledDialog.CreateFieldLabel("Command Line Arguments"),
							_argsTextBox = new StyledTextBox
							{
								RelativeSizeAxes = Axes.X,
								Height = 36,
								PlaceholderText = "e.g., -devserver mamesosu.net"
							}
						}
					}
				}
			},
			_buttonContainer = new FillFlowContainer
			{
				Anchor = Anchor.BottomCentre,
				Origin = Anchor.BottomCentre,
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(10, 0),
				Children = new Drawable[]
				{
					_cancelButton = StyledDialog.CreateCancelButton(),
					_confirmButton = StyledDialog.CreateDangerButton("Restart")
				}
			}
		};

		_cancelButton.Clicked += OnCancelClicked;
		_confirmButton.Clicked += OnConfirmClicked;
		_editPresetsButton.Clicked += OnEditPresetsClicked;
		_presetDropdown.Current.ValueChanged += OnPresetChanged;
		_presetEditDialog.PresetSaved += OnPresetSaved;

		if (_presets.Count > 0)
			_presetDropdown.Current.Value = _presets[0];
	}

	private void LoadPresets()
	{
		var savedPresets = UserSettingsService.Settings.OsuRestartPresets;
		if (savedPresets != null && savedPresets.Count > 0)
			_presets = savedPresets.ToList();
		else
			_presets = OsuRestartPreset.GetDefaults();
	}

	private void SavePresets()
	{
		UserSettingsService.Settings.OsuRestartPresets = new List<OsuRestartPreset>(_presets);
		Task.Run(async () => await UserSettingsService.SaveAsync());
	}

	private void OnPresetChanged(ValueChangedEvent<OsuRestartPreset?> e)
	{
		_selectedPreset = e.NewValue;
		if (_selectedPreset != null)
			_argsTextBox.Text = _selectedPreset.Arguments;
	}

	private void OnEditPresetsClicked()
	{
		var index = _selectedPreset != null ? _presets.IndexOf(_selectedPreset) : 0;
		if (index >= 0 && index < _presets.Count)
			_presetEditDialog?.Show(index, _presets[index]);
		else if (_presets.Count > 0)
			_presetEditDialog?.Show(0, _presets[0]);
	}

	private void OnPresetSaved(int index, OsuRestartPreset preset)
	{
		if (index >= 0 && index < _presets.Count)
			_presets[index] = preset;
		else
			_presets.Add(preset);

		SavePresets();
		RefreshDropdown();
	}

	private void RefreshDropdown()
	{
		var currentSelection = _selectedPreset;
		_presetDropdown.Items = _presets;

		if (currentSelection != null)
		{
			var matchingPreset = _presets.FirstOrDefault(p => p.Name == currentSelection.Name);
			if (matchingPreset != null)
				_presetDropdown.Current.Value = matchingPreset;
			else if (_presets.Count > 0)
				_presetDropdown.Current.Value = _presets[0];
		}
		else if (_presets.Count > 0)
		{
			_presetDropdown.Current.Value = _presets[0];
		}
	}

	/// <summary>
	/// Shows the restart dialog with the specified title and message.
	/// </summary>
	public void Show(string title, string message, bool showSkip = false)
	{
		_titleText.Text = title;
		_messageText.Text = message;

		LoadPresets();
		RefreshDropdown();

		if (_buttonContainer.Contains(_cancelButton))
			_buttonContainer.Remove(_cancelButton, false);
		if (_skipButton != null && _buttonContainer.Contains(_skipButton))
			_buttonContainer.Remove(_skipButton, false);
		if (_buttonContainer.Contains(_confirmButton))
			_buttonContainer.Remove(_confirmButton, false);

		if (showSkip && _skipButton == null)
		{
			_skipButton = StyledDialog.CreateSecondaryButton("Skip");
			_skipButton.Clicked += OnSkipClicked;
		}
		else if (!showSkip && _skipButton != null)
		{
			_skipButton.Clicked -= OnSkipClicked;
			_skipButton = null;
		}

		_buttonContainer.Add(_cancelButton);
		if (showSkip && _skipButton != null)
			_buttonContainer.Add(_skipButton);

		_buttonContainer.Add(_confirmButton);

		var estimatedHeight = message.Length > 100 ? 370f : 340f;
		if (showSkip)
			estimatedHeight += 10f;

		_dialogContainer.ResizeHeightTo(estimatedHeight, 0);
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

	/// <summary>
	/// Gets the currently entered command line arguments.
	/// </summary>
	public string GetArguments() => _argsTextBox.Text?.Trim() ?? string.Empty;

	private void OnCancelClicked() => Hide();

	private void OnConfirmClicked()
	{
		Confirmed?.Invoke(GetArguments());
		Hide();
	}

	private void OnSkipClicked()
	{
		Skipped?.Invoke();
		Hide();
	}

	protected override bool OnClick(ClickEvent e) => true;
}

/// <summary>
/// Dropdown for selecting osu! restart presets.
/// </summary>
public partial class OsuRestartPresetDropdown : BasicDropdown<OsuRestartPreset?>
{
	public OsuRestartPresetDropdown()
	{
		AutoSizeAxes = Axes.None;
	}

	protected override LocalisableString GenerateItemText(OsuRestartPreset? item)
	{
		if (item == null)
			return "Select preset...";

		if (string.IsNullOrEmpty(item.Arguments))
			return $"{item.Name} (no args)";

		return item.Name;
	}
}
