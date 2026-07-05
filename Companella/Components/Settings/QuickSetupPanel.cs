using Companella.Components.Misc;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for quick setup that indexes maps, imports scores, and finds missing replays.
/// </summary>
public partial class QuickSetupPanel : CompositeDrawable
{
	private StyledButton _quickSetupButton = null!;
	private TextFlowContainer _statusText = null!;

	public event Action? QuickSetupRequested;

	public QuickSetupPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				SettingsLayout.CreateWrappingText(
					"Index beatmaps for recommendations, import existing scores as sessions, and find missing replay files — all in one step.",
					14,
					StyledButton.Theme.MutedLabel),
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8),
					Children = new Drawable[]
					{
						_quickSetupButton = new StyledButton("Run Quick Setup")
						{
							AccentColor = StyledButton.Theme.SuccessFill,
							Size = new Vector2(160, 36)
						},
						_statusText = SettingsLayout.CreateStatusText(13, StyledButton.Theme.MutedLabel, 0)
					}
				}
			}
		};

		InternalChild = new SettingsSection("Quick Setup", "Get started with a single click", content);
		_quickSetupButton.Clicked += () => QuickSetupRequested?.Invoke();
	}

	public void SetStatus(string status)
	{
		Schedule(() =>
		{
			_statusText.Text = status;
			_statusText.Alpha = string.IsNullOrEmpty(status) ? 0 : 1;
		});
	}
}
