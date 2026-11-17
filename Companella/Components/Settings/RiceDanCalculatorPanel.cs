using Companella.Components.Misc;
using Companella.Models.Application;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Settings panel for selecting the rice dan calculator (Companella ONNX vs Daniel).
/// </summary>
public partial class RiceDanCalculatorPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	private RiceDanCalculatorDropdown _calculatorDropdown = null!;

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
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				_calculatorDropdown = new RiceDanCalculatorDropdown
				{
					RelativeSizeAxes = Axes.X,
					Anchor = Anchor.TopLeft,
					Origin = Anchor.TopLeft
				},
				SettingsLayout.CreateHint("Daniel falls back to Companella ONNX below Alpha."),
				SettingsLayout.CreateHint("LN dan always uses the Companella Sunny-based estimator."),
				SettingsLayout.CreateHint("Daniel algorithm © 2026 TheBagelOfMan (MIT)")
			}
		};

		InternalChild = new SettingsSection("Rice Dan Calculator", "Choose how 4K rice dan is estimated", content);

		_calculatorDropdown.Items = Enum.GetValues<RiceDanCalculatorMode>();
		_calculatorDropdown.Current.Value = UserSettingsService.Settings.RiceDanCalculator;
		_calculatorDropdown.Current.BindValueChanged(OnCalculatorChanged);
	}

	private void OnCalculatorChanged(ValueChangedEvent<RiceDanCalculatorMode> e)
	{
		UserSettingsService.Settings.RiceDanCalculator = e.NewValue;
		Task.Run(async () => await UserSettingsService.SaveAsync());
	}
}

public partial class RiceDanCalculatorDropdown : BasicDropdown<RiceDanCalculatorMode>
{
	protected override LocalisableString GenerateItemText(RiceDanCalculatorMode item) => item switch
	{
		RiceDanCalculatorMode.CompanellaOnnx => "Companella ONNX (default)",
		RiceDanCalculatorMode.Daniel => "Daniel",
		_ => item.ToString()
	};
}
