using Companella.Components.Session;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring metadata display preference (romanized vs unicode).
/// </summary>
public partial class MetadataPreferencePanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	private SettingsCheckbox _romanizedCheckbox = null!;

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
				_romanizedCheckbox = new SettingsCheckbox
				{
					LabelText = "Prefer romanized metadata",
					IsChecked = SettingsService.Settings.PreferRomanizedMetadata,
					TooltipText = "Show romanized (ASCII) titles and artists instead of unicode"
				},
				SettingsLayout.CreateHint("When enabled, shows 'Hitorigoto' instead of 'ひとりごと'")
			}
		};

		InternalChild = new SettingsSection("Metadata", "Choose how song titles and artists are displayed", content);
		_romanizedCheckbox.CheckedChanged += OnPreferenceChanged;
	}

	private void OnPreferenceChanged(bool isChecked)
	{
		SettingsService.Settings.PreferRomanizedMetadata = isChecked;
		Task.Run(async () => await SettingsService.SaveAsync());
	}
}
