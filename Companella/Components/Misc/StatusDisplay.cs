using Companella.Components.Layout;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

public enum StatusType
{
	Info,
	Success,
	Warning,
	Error
}

/// <summary>
/// Displays status messages with history log.
/// </summary>
public partial class StatusDisplay : CompositeDrawable
{
	private FillFlowContainer _logContainer = null!;
	private ChainedScrollContainer _scrollContainer = null!;
	private Box _latestIndicator = null!;
	private const int _maxLogEntries = 50;

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new Container
			{
				RelativeSizeAxes = Axes.Both,
				Children = new Drawable[]
				{
					new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = new Color4(35, 35, 40, 255)
					},
					new Container
					{
						RelativeSizeAxes = Axes.Both,
						Padding = new MarginPadding(10),
						Children = new Drawable[]
						{
							_latestIndicator = new Box
							{
								Width = 4,
								RelativeSizeAxes = Axes.Y,
								Colour = new Color4(100, 100, 100, 255)
							},
							new Container
							{
								RelativeSizeAxes = Axes.Both,
								Padding = new MarginPadding { Left = 12 },
								Children = new Drawable[]
								{
									new SpriteText
									{
										Text = "Status Log",
										Font = new FontUsage("", 15, "Bold"),
										Colour = new Color4(150, 150, 150, 255)
									},
									_scrollContainer = new ChainedScrollContainer
									{
										RelativeSizeAxes = Axes.Both,
										Padding = new MarginPadding { Top = 18 },
										ClampExtension = 0,
										Child = _logContainer = new FillFlowContainer
										{
											RelativeSizeAxes = Axes.X,
											AutoSizeAxes = Axes.Y,
											Direction = FillDirection.Vertical,
											Spacing = new Vector2(0, 2)
										}
									}
								}
							}
						}
					}
				}
			}
		};

		// Add initial ready message
		AddLogEntry("Ready", StatusType.Info);
	}

	public void SetStatus(string message, StatusType type = StatusType.Info)
	{
		AddLogEntry(message, type);
	}

	private void AddLogEntry(string message, StatusType type)
	{
		var color = type switch
		{
			StatusType.Success => new Color4(100, 200, 100, 255),
			StatusType.Warning => new Color4(255, 200, 100, 255),
			StatusType.Error => new Color4(255, 100, 100, 255),
			_ => new Color4(100, 150, 255, 255)
		};

		// Update the indicator to match latest status
		_latestIndicator.FadeColour(color, 200);

		var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		var entry = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(8, 0),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = timestamp,
					Font = new FontUsage("", 14),
					Colour = new Color4(100, 100, 100, 255)
				},
				new Box
				{
					Width = 3,
					Height = 12,
					Colour = color,
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				},
				new TextFlowContainer(t =>
				{
					t.Font = new FontUsage("", 14);
					t.Colour = Color4.White;
				})
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Text = message
				}
			}
		};

		// Add at the beginning (newest first)
		_logContainer.Insert(0, entry);

		// Remove old entries if exceeding max
		while (_logContainer.Count > _maxLogEntries) _logContainer.Remove(_logContainer[^1], true);

		// Scroll to top to show latest entry
		Schedule(() => _scrollContainer.ScrollToStart());
	}
}
