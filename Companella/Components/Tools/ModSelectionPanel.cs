using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Mods;
using Companella.Mods.Parameters;
using Companella.Services.Tools;
using osu.Framework.Allocation;
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

namespace Companella.Components.Tools;

/// <summary>
/// Panel for selecting and applying beatmap mods.
/// Displays mods grouped by category with automatic layout.
/// </summary>
public partial class ModSelectionPanel : CompositeDrawable
{
	[Resolved] private ModService ModService { get; set; } = null!;

	private FillFlowContainer _categoriesContainer = null!;
	private SpriteText _selectedModName = null!;
	private TextFlowContainer _selectedModDescription = null!;
	private FillFlowContainer _parametersContainer = null!;
	private StyledButton _applyButton = null!;
	private TextFlowContainer _statusText = null!;

	private IMod? _selectedMod;
	private StyledButton? _selectedButton;
	private bool _enabled;
	private List<ParameterSlider> _parameterSliders = new();
	private List<ParameterTextBox> _parameterTextBoxes = new();

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _categoryHeaderColor = new(180, 180, 180, 255);
	private readonly Color4 _descriptionColor = new(120, 120, 120, 255);

	/// <summary>
	/// Event raised when a mod should be applied.
	/// </summary>
	public event Action<IMod>? ApplyModClicked;


	public ModSelectionPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var modListSection = StyledDialog.CreateInsetSection(280);
		modListSection.Child = new ChainedScrollContainer
		{
			RelativeSizeAxes = Axes.Both,
			ClampExtension = 20,
			ScrollbarVisible = true,
			Child = _categoriesContainer = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 12),
				Padding = new MarginPadding(12)
			}
		};

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				modListSection,
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 4),
					Children = new Drawable[]
					{
						_selectedModName = new SpriteText
						{
							Text = "No mod selected",
							Font = new FontUsage("", 16, "Bold"),
							Colour = StyledButton.Theme.Accent
						},
						_selectedModDescription = SettingsLayout.CreateWrappingText(
							"Select a mod from the list above to apply it.",
							14,
							StyledButton.Theme.DisabledLabel)
					}
				},
				_parametersContainer = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8)
				},
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8),
					Children = new Drawable[]
					{
						_applyButton = new StyledButton("Apply Mod")
						{
							RelativeSizeAxes = Axes.X,
							Height = 36,
							Enabled = false,
							TooltipText = "Apply the selected mod to create a new difficulty"
						},
						_statusText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.DisabledLabel)
					}
				}
			}
		};

		InternalChild = SettingsLayout.CreateSection(
			"Beatmap Mods",
			"Select and apply mods to the loaded beatmap",
			content);

		_applyButton.Clicked += OnApplyClicked;
		Schedule(PopulateMods);
	}

	/// <summary>
	/// Populates the panel with registered mods grouped by category.
	/// </summary>
	private void PopulateMods()
	{
		_categoriesContainer.Clear();

		var mods = ModService.GetAllMods();
		if (mods.Count == 0)
		{
			_categoriesContainer.Add(new SpriteText
			{
				Text = "No mods registered",
				Font = new FontUsage("", 14),
				Colour = _descriptionColor
			});
			return;
		}

		// Group mods by category
		var categories = mods
			.GroupBy(m => m.Category)
			.OrderBy(g => g.Key);

		foreach (var category in categories)
		{
			// Category header
			_categoriesContainer.Add(new SpriteText
			{
				Text = category.Key,
				Font = new FontUsage("", 15, "Bold"),
				Colour = _categoryHeaderColor,
				Margin = new MarginPadding { Top = 4 }
			});

			// Mod buttons in a flow container
			var modFlow = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Full,
				Spacing = new Vector2(8, 8)
			};

			foreach (var mod in category.OrderBy(m => m.Name))
			{
				var button = new StyledButton(mod.Name + " (+" + mod.Icon + ")", StyledButtonAppearance.Bordered)
				{
					Size = new Vector2(140, 36),
					FontSize = 23,
					Bold = false,
					ShowAccentBar = false,
					AccentColor = _accentColor,
					Tag = mod
				};
				button.Clicked += () => OnModSelected(button, mod);
				modFlow.Add(button);
			}

			_categoriesContainer.Add(modFlow);
		}
	}

	/// <summary>
	/// Refreshes the mod list from the ModService.
	/// </summary>
	public void RefreshMods()
	{
		PopulateMods();
	}

	private void OnModSelected(StyledButton button, IMod mod)
	{
		// Deselect previous button
		_selectedButton?.SetSelected(false);

		// Select new button
		_selectedButton = button;
		_selectedButton.SetSelected(true);
		_selectedMod = mod;

		// Update info display
		_selectedModName.Text = mod.Name;
		_selectedModDescription.Text = mod.Description;

		// Populate parameter sliders
		PopulateParameterSliders(mod);

		// Enable apply button if panel is enabled
		_applyButton.Enabled = _enabled;
	}

	/// <summary>
	/// Populates the parameter sliders for the selected mod.
	/// </summary>
	private void PopulateParameterSliders(IMod mod)
	{
		_parametersContainer.Clear();
		_parameterSliders.Clear();
		_parameterTextBoxes.Clear();

		if (mod.Parameters.Count == 0)
			return;

		foreach (var param in mod.Parameters)
		{
			// check if string parameter
			if (param is StringModParameter stringParam)
			{
				var textBox = new ParameterTextBox(stringParam, _accentColor);
				_parameterTextBoxes.Add(textBox);
				_parametersContainer.Add(textBox);
			}
			else
			{
				var slider = new ParameterSlider(param, _accentColor);
				_parameterSliders.Add(slider);
				_parametersContainer.Add(slider);
			}
		}
	}

	private void OnApplyClicked()
	{
		if (_selectedMod == null || !_enabled)
			return;

		ApplyModClicked?.Invoke(_selectedMod);
	}

	/// <summary>
	/// Sets whether the panel is enabled for interaction.
	/// </summary>
	public void SetEnabled(bool enabled)
	{
		_enabled = enabled;
		_applyButton.Enabled = enabled && _selectedMod != null;
	}

	/// <summary>
	/// Sets the status text displayed next to the apply button.
	/// </summary>
	public void SetStatus(string status)
	{
		_statusText.Text = status;
	}

	/// <summary>
	/// Clears the current mod selection.
	/// </summary>
	public void ClearSelection()
	{
		_selectedButton?.SetSelected(false);
		_selectedButton = null;
		_selectedMod = null;

		_selectedModName.Text = "No mod selected";
		_selectedModDescription.Text = "Select a mod from the list above to apply it.";
		_applyButton.Enabled = false;

		// Clear parameter controls
		_parametersContainer.Clear();
		_parameterSliders.Clear();
		_parameterTextBoxes.Clear();
	}
}
