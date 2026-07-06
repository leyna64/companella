using Companella.Components.Misc;
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
/// Settings panel for selecting the MinaCalc version to use for MSD calculations.
/// </summary>
public partial class MinaCalcVersionPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	private MinaCalcVersionDropdown _versionDropdown = null!;

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
				_versionDropdown = new MinaCalcVersionDropdown
				{
					RelativeSizeAxes = Axes.X,
					Anchor = Anchor.TopLeft,
					Origin = Anchor.TopLeft
				},
				SettingsLayout.CreateHint("5.15 — 4K/6K/7K support, chordjack and stream adjustments"),
				SettingsLayout.CreateHint("5.05 — 4K only, legacy version")
			}
		};

		InternalChild = new SettingsSection("MinaCalc Version", "Choose the MSD calculator used for difficulty analysis", content);

		_versionDropdown.Items = new[] { "515", "505" };

		var currentVersion = UserSettingsService.Settings.MinaCalcVersion;
		if (currentVersion != "515" && currentVersion != "505")
			currentVersion = "515";

		_versionDropdown.Current.Value = currentVersion;
		ToolPaths.SelectedMinaCalcVersion = currentVersion;
		_versionDropdown.Current.BindValueChanged(OnVersionChanged);
	}

	private void OnVersionChanged(ValueChangedEvent<string> e)
	{
		ToolPaths.SelectedMinaCalcVersion = e.NewValue;
		UserSettingsService.Settings.MinaCalcVersion = e.NewValue;
		Task.Run(async () => await UserSettingsService.SaveAsync());
		Logger.Info($"[MinaCalcVersionPanel] Changed to MinaCalc {(e.NewValue == "515" ? "5.15" : "5.05")}");
	}
}

public partial class MinaCalcVersionDropdown : BasicDropdown<string>
{
	protected override LocalisableString GenerateItemText(string item) => item switch
	{
		"515" => "MinaCalc 5.15 (Latest)",
		"505" => "MinaCalc 5.05 (Legacy)",
		_ => item
	};
}
