using Companella.Components.Session;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring overlay mode (window follows osu! vs independent window).
/// </summary>
public partial class OverlayModePanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	[Resolved] private OsuWindowOverlayService OverlayService { get; set; } = null!;

	private SettingsCheckbox _overlayModeCheckbox = null!;

	public event Action<bool>? OverlayModeChanged;

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
			Spacing = new Vector2(0, 8),
			Children = new Drawable[]
			{
				_overlayModeCheckbox = new SettingsCheckbox
				{
					LabelText = "Attach window to osu! as overlay",
					IsChecked = SettingsService.Settings.OverlayMode,
					TooltipText = "Window follows osu! position and hides when osu! loses focus"
				},
				SettingsLayout.CreateHint("When enabled, the window follows osu! and hides when osu! loses focus."),
				SettingsLayout.CreateHint("When disabled, the window stays independent and always visible.")
			}
		};

		InternalChild = new SettingsSection("Overlay Mode", "Control how Companella attaches to osu!", content);
		_overlayModeCheckbox.CheckedChanged += OnOverlayModeChanged;
	}

	private void OnOverlayModeChanged(bool isChecked)
	{
		SettingsService.Settings.OverlayMode = isChecked;
		Task.Run(async () => await SettingsService.SaveAsync());
		OverlayService.RequestOverlayModeChange(isChecked);
		OverlayModeChanged?.Invoke(isChecked);
	}
}
