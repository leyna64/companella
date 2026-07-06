using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Settings panel for adjusting the global UI scale.
/// </summary>
public partial class UIScalePanel : CompositeDrawable
{
	[Resolved] private ScaledContentContainer ScaledContainer { get; set; } = null!;

	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	private StyledSliderBar _scaleSlider = null!;
	private SpriteText _scaleValue = null!;

	private readonly BindableNumber<float> _scaleBindable = new BindableFloat(1.0f)
	{
		MinValue = 0.5f,
		MaxValue = 2.0f,
		Precision = 0.01f
	};

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
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				new Container
				{
					RelativeSizeAxes = Axes.X,
					Height = 24,
					Child = _scaleSlider = new StyledSliderBar
					{
						RelativeSizeAxes = Axes.X,
						Height = 24,
						Current = _scaleBindable
					}
				},
				new Container
				{
					RelativeSizeAxes = Axes.X,
					Height = 18,
					Children = new Drawable[]
					{
						SettingsLayout.CreateInlineLabel("50%"),
						new SpriteText
						{
							Text = "200%",
							Font = new FontUsage("", 13),
							Colour = StyledButton.Theme.DisabledLabel,
							Anchor = Anchor.CentreRight,
							Origin = Anchor.CentreRight
						}
					}
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(8, 0),
					Children = new Drawable[]
					{
						SettingsLayout.CreateInlineLabel("Current:"),
						_scaleValue = new SpriteText
						{
							Text = "100%",
							Font = new FontUsage("", 17, "Bold"),
							Colour = Color4.White
						}
					}
				},
				SettingsLayout.CreateButtonRow(28,
					CreatePresetButton("50%", 0.5f),
					CreatePresetButton("75%", 0.75f),
					CreatePresetButton("100%", 1.0f),
					CreatePresetButton("125%", 1.25f)),
				SettingsLayout.CreateButtonRow(28,
					CreatePresetButton("150%", 1.5f),
					CreatePresetButton("175%", 1.75f),
					CreatePresetButton("200%", 2.0f),
					new Container())
			}
		};

		InternalChild = new SettingsSection("UI Scale", "Adjust the size of all UI elements", content);

		_scaleBindable.Value = ScaledContainer.UIScale;
		_scaleBindable.BindValueChanged(e =>
		{
			ScaledContainer.UIScale = e.NewValue;
			UpdateScaleDisplay(e.NewValue);
			SaveSettings(e.NewValue);
		});

		UpdateScaleDisplay(ScaledContainer.UIScale);
	}

	private StyledButton CreatePresetButton(string label, float scale) => new(label, StyledButtonAppearance.Muted)
	{
		RelativeSizeAxes = Axes.Both,
		FontSize = 14,
		Bold = false,
		Action = () => _scaleBindable.Value = scale,
		TooltipText = $"Set window scale to {scale * 100:0}%"
	};

	private void UpdateScaleDisplay(float scale) => _scaleValue.Text = $"{scale:P0}";

	private void SaveSettings(float scale)
	{
		if (UserSettingsService == null)
			return;

		UserSettingsService.Settings.UIScale = scale;
		Task.Run(async () => await UserSettingsService.SaveAsync());
	}

	private partial class StyledSliderBar : BasicSliderBar<float>
	{
		[BackgroundDependencyLoader]
		private void load()
		{
			BackgroundColour = StyledButton.Theme.DialogInsetBg;
			SelectionColour = StyledButton.Theme.Accent;
			KeyboardStep = 0.01f;
		}
	}
}
