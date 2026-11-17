using Companella.Components.Misc;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring overlay position offset with +/- controls.
/// </summary>
public partial class OverlayPositionPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	[Resolved] private OsuWindowOverlayService OverlayService { get; set; } = null!;

	private SpriteText _xValueText = null!;
	private SpriteText _yValueText = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		var stepperOptions = SettingsStepperOptions.ForOverlayOffset();

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				SettingsLayout.CreateHint("Hold Shift for larger adjustment steps."),
				SettingsLayout.CreateStepperRow("X", () => SettingsService.Settings.OverlayOffsetX, SetOffsetX, out _xValueText, stepperOptions),
				SettingsLayout.CreateStepperRow("Y", () => SettingsService.Settings.OverlayOffsetY, SetOffsetY, out _yValueText, stepperOptions),
				new StyledButton("Reset", StyledButtonAppearance.Muted)
				{
					Width = 88,
					Height = 28,
					Action = OnResetClicked
				}
			}
		};

		InternalChild = new SettingsSection("Overlay Offset", "Fine-tune where the overlay appears relative to osu!", content);
	}

	private void SetOffsetX(int value)
	{
		SettingsService.Settings.OverlayOffsetX = value;
		UpdateOverlayOffset();
		SaveSettings();
	}

	private void SetOffsetY(int value)
	{
		SettingsService.Settings.OverlayOffsetY = value;
		UpdateOverlayOffset();
		SaveSettings();
	}

	private void OnResetClicked()
	{
		SettingsService.Settings.OverlayOffsetX = 0;
		SettingsService.Settings.OverlayOffsetY = 0;
		_xValueText.Text = "0";
		_yValueText.Text = "0";
		UpdateOverlayOffset();
		SaveSettings();
	}

	private void UpdateOverlayOffset()
	{
		if (OverlayService != null)
			OverlayService.OverlayOffset = new Point(
				SettingsService.Settings.OverlayOffsetX,
				SettingsService.Settings.OverlayOffsetY
			);
	}

	private void SaveSettings() => Task.Run(async () => await SettingsService.SaveAsync());
}
