using Companella.Components.Session;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring analytics/privacy settings.
/// </summary>
public partial class AnalyticsSettingsPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	[Resolved] private AptabaseService AptabaseService { get; set; } = null!;

	private SettingsCheckbox _analyticsCheckbox = null!;
	private SettingsCheckbox _danTrainingCheckbox = null!;

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
				_analyticsCheckbox = new SettingsCheckbox
				{
					LabelText = "Send anonymous usage data",
					IsChecked = SettingsService.Settings.SendAnalytics,
					TooltipText = "Help improve Companella by sending anonymous usage data"
				},
				SettingsLayout.CreateHint("Helps improve the app. No personal data is collected."),
				_danTrainingCheckbox = new SettingsCheckbox
				{
					LabelText = "Participate in Dan Training",
					IsChecked = SettingsService.Settings.ParticipateDanTraining,
					TooltipText = "Help improve dan classification by rating maps after completing them"
				},
				SettingsLayout.CreateHint("Shows a dan rating dialog after completing new maps.")
			}
		};

		InternalChild = new SettingsSection("Privacy", "Control analytics and community training features", content);

		_analyticsCheckbox.CheckedChanged += OnAnalyticsChanged;
		_danTrainingCheckbox.CheckedChanged += OnDanTrainingChanged;
		AptabaseService.IsEnabled = SettingsService.Settings.SendAnalytics;
	}

	private void OnAnalyticsChanged(bool isChecked)
	{
		SettingsService.Settings.SendAnalytics = isChecked;
		AptabaseService.IsEnabled = isChecked;
		Task.Run(async () => await SettingsService.SaveAsync());
	}

	private void OnDanTrainingChanged(bool isChecked)
	{
		SettingsService.Settings.ParticipateDanTraining = isChecked;
		Task.Run(async () => await SettingsService.SaveAsync());
	}
}
