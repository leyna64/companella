using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Components.Settings;
using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel that allows users to re-run the tutorial and quick setup.
/// </summary>
public partial class TutorialPanel : CompositeDrawable
{
	private StyledButton _showTutorialButton = null!;
	private StyledButton _quickSetupButton = null!;

	public event Action? ShowTutorialRequested;
	public event Action? QuickSetupRequested;

	public TutorialPanel()
	{
		RelativeSizeAxes = Axes.Both;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new ChainedScrollContainer
		{
			RelativeSizeAxes = Axes.Both,
			ClampExtension = 100,
			ScrollbarVisible = true,
			Child = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 12),
				Padding = new MarginPadding { Horizontal = 4, Top = 8, Bottom = 48 },
				Children = new Drawable[]
				{
					SettingsLayout.CreateSection(
						"App Tutorial",
						"New to Companella? The tutorial walks you through rate changing, session tracking, skills analysis, and mapping tools.",
						CreateActionButton("Show Tutorial", StyledButton.Theme.Accent, out _showTutorialButton)),
					SettingsLayout.CreateSection(
						"Quick Setup",
						"Run all initial setup tasks at once: index beatmaps, import scores, and find missing replays. This may take several minutes depending on your library size.",
						CreateActionButton("Run Quick Setup", StyledButton.Theme.SuccessFill, out _quickSetupButton)),
					SettingsLayout.CreateSection(
						"Need Help?",
						"If you encounter issues or have questions, use the resources below.",
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 10),
							Children = new Drawable[]
							{
								SettingsLayout.CreateHint("Check the GitHub repository for documentation and issues."),
								SettingsLayout.CreateInlineLinkText(
									"Visit the ",
									"osu! Forum Thread",
									"https://osu.ppy.sh/community/forums/topics/2168176",
									" to ask questions."),
								SettingsLayout.CreateHint("Report bugs on GitHub Issues."),
								new StyledButton("Join the Discord", new Color4(88, 101, 242, 255))
								{
									Width = 160,
									Height = 36,
									Action = () =>
									{
										try
										{
											Process.Start(new ProcessStartInfo
											{
												FileName = "https://discord.gg/4xsku7y896",
												UseShellExecute = true
											});
										}
										catch
										{
										}
									}
								}
							}
						})
				}
			}
		};

		_showTutorialButton.Clicked += () => ShowTutorialRequested?.Invoke();
		_quickSetupButton.Clicked += () => QuickSetupRequested?.Invoke();
	}

	private static StyledButton CreateActionButton(string text, Color4 accent, out StyledButton button)
	{
		button = new StyledButton(text, accent)
		{
			Width = 160,
			Height = 36
		};
		return button;
	}

}
