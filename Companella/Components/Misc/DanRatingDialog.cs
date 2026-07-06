using Companella.Components.Layout;
using Companella.Models.Training;
using Companella.Services.Analysis;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// Dialog for rating a beatmap with a dan level after completing it.
/// </summary>
public partial class DanRatingDialog : CompositeDrawable
{
	[Resolved] private DanConfigurationService DanConfigService { get; set; } = null!;

	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private SpriteText _mapNameText = null!;
	private SpriteText _accuracyText = null!;
	private FillFlowContainer _danButtonsContainer = null!;
	private StyledButton _skipButton = null!;
	private SpriteText _selectedDanText = null!;
	private StyledButton _submitButton = null!;

	private string? _selectedDan;
	private float _selectedModifier;
	private string _beatmapHash = "";
	private string _beatmapPath = "";
	private double _accuracy;

	/// <summary>
	/// Converts dan label names to display labels (greek letters for extended dans).
	/// </summary>
	private static string ToGreekDisplay(string label) => DanLabelFormatter.ToDisplayLabel(label);

	/// <summary>
	/// Event raised when a dan rating is submitted.
	/// </summary>
	public event Action<string, string, string, float, double>? RatingSubmitted;

	/// <summary>
	/// Event raised when the dialog is closed (skipped or submitted).
	/// </summary>
	public event Action? Closed;

	public DanRatingDialog()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var danSection = StyledDialog.CreateInsetSection(180);
		danSection.Add(new ChainedScrollContainer
		{
			RelativeSizeAxes = Axes.Both,
			Padding = new MarginPadding(8),
			Child = _danButtonsContainer = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Full,
				Spacing = new Vector2(6, 6)
			}
		});

		InternalChildren = new Drawable[]
		{
			StyledDialog.CreateDimBackground(),
			_dialogContainer = StyledDialog.CreateShell(new Vector2(470, 400), out var content)
		};

		content.Child = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				_titleText = StyledDialog.CreateTitle("What dan is this map?"),
				_mapNameText = new SpriteText
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					Text = "Map Name",
					Font = new FontUsage("", 15),
					Colour = StyledButton.Theme.MutedLabel,
					Truncate = true,
					MaxWidth = 420
				},
				_accuracyText = StyledDialog.CreateSubtitle("Accuracy: 95.00%"),
				danSection,
				_selectedDanText = new SpriteText
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					Text = "Select a dan level",
					Font = new FontUsage("", 14),
					Colour = StyledButton.Theme.DisabledLabel
				},
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
						_skipButton = StyledDialog.CreateSecondaryButton("Skip"),
						_submitButton = StyledDialog.CreatePrimaryButton("Submit")
					}
				}
			}
		};

		_skipButton.Clicked += OnSkipClicked;
		_submitButton.Clicked += OnSubmitClicked;
		_submitButton.Enabled = false;

		PopulateDanButtons();
	}

	private void PopulateDanButtons()
	{
		_danButtonsContainer.Clear();

		foreach (var label in DanConfigService.GetAllLabels())
		{
			var displayLabel = ToGreekDisplay(label);
			var group = new DanSelectGroup(label, displayLabel);
			group.Selected += (dan, modifier) => OnDanSelected(dan, modifier);
			_danButtonsContainer.Add(group);
		}
	}

	private void OnDanSelected(string dan, float modifier)
	{
		_selectedDan = dan;
		_selectedModifier = modifier;

		var displayDan = ToGreekDisplay(dan);
		var modifierText = modifier switch
		{
			< 0 => "(Low)",
			> 0 => "(High)",
			_ => string.Empty
		};
		_selectedDanText.Text = $"Selected: {displayDan} {modifierText}";
		_selectedDanText.Colour = StyledButton.Theme.Accent;
		_submitButton.Enabled = true;

		foreach (var child in _danButtonsContainer.Children)
			if (child is DanSelectGroup group)
				group.SetSelected(group.DanLabel == dan, group.DanLabel == dan ? modifier : 0);
	}

	/// <summary>
	/// Shows the dialog for rating a beatmap.
	/// </summary>
	public void Show(string beatmapHash, string beatmapPath, double accuracy)
	{
		_beatmapHash = beatmapHash;
		_beatmapPath = beatmapPath;
		_accuracy = accuracy;
		_selectedDan = null;
		_selectedModifier = 0;

		_mapNameText.Text = Path.GetFileNameWithoutExtension(beatmapPath);
		_accuracyText.Text = $"Accuracy: {accuracy:F2}%";
		_selectedDanText.Text = "Select a dan level";
		_selectedDanText.Colour = StyledButton.Theme.DisabledLabel;
		_submitButton.Enabled = false;

		foreach (var child in _danButtonsContainer.Children)
			if (child is DanSelectGroup group)
				group.SetSelected(false, 0);

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

	private void OnSkipClicked() => Hide();

	private void OnSubmitClicked()
	{
		if (string.IsNullOrEmpty(_selectedDan))
			return;

		RatingSubmitted?.Invoke(_beatmapHash, _beatmapPath, _selectedDan, _selectedModifier, _accuracy);
		Hide();
	}

	protected override bool OnClick(ClickEvent e) => true;
}

/// <summary>
/// A group containing [-][Dan Label][+] buttons for fine-grained dan selection.
/// </summary>
public partial class DanSelectGroup : CompositeDrawable
{
	private StyledButton _minusButton = null!;
	private StyledButton _mainButton = null!;
	private StyledButton _plusButton = null!;

	private static readonly Color4 _modifierAccent = new(200, 80, 140, 255);

	public string DanLabel { get; }
	public string DisplayLabel { get; }

	/// <summary>
	/// Event raised when a selection is made. Parameters: danLabel, modifier (-0.33, 0, or +0.33)
	/// </summary>
	public event Action<string, float>? Selected;

	public DanSelectGroup(string danLabel, string displayLabel)
	{
		DanLabel = danLabel;
		DisplayLabel = displayLabel;
		AutoSizeAxes = Axes.Both;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(2, 0),
			Children = new Drawable[]
			{
				_minusButton = new StyledButton("-", StyledButtonAppearance.Toggle)
				{
					Size = new Vector2(22, 32),
					FontSize = 16,
					ShowAccentBar = false,
					AccentColor = _modifierAccent
				},
				_mainButton = new StyledButton(DisplayLabel, StyledButtonAppearance.Toggle)
				{
					Size = new Vector2(30, 32),
					FontSize = 14,
					ShowAccentBar = false,
					AccentColor = StyledButton.Theme.Accent
				},
				_plusButton = new StyledButton("+", StyledButtonAppearance.Toggle)
				{
					Size = new Vector2(22, 32),
					FontSize = 16,
					ShowAccentBar = false,
					AccentColor = _modifierAccent
				}
			}
		};

		_minusButton.Clicked += () => OnModifierClicked(-0.33f);
		_mainButton.Clicked += OnMainClicked;
		_plusButton.Clicked += () => OnModifierClicked(0.33f);
	}

	private void OnMainClicked() => Selected?.Invoke(DanLabel, 0);

	private void OnModifierClicked(float modifier) => Selected?.Invoke(DanLabel, modifier);

	public void SetSelected(bool selected, float modifier)
	{
		_mainButton.SetSelected(selected && Math.Abs(modifier) < 0.01f);
		_minusButton.SetSelected(selected && modifier < -0.01f);
		_plusButton.SetSelected(selected && modifier > 0.01f);
	}
}
