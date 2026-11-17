using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Short-lived status toast for the difficulty splitter.
/// </summary>
public partial class DifficultySplitterToast : CompositeDrawable
{
	private SpriteText _text = null!;
	private Box _background = null!;
	private double _hideAt;

	public DifficultySplitterToast()
	{
		Alpha = 0;
		Anchor = Anchor.TopCentre;
		Origin = Anchor.TopCentre;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new Container
		{
			AutoSizeAxes = Axes.Both,
			Padding = new MarginPadding { Horizontal = 14, Vertical = 8 },
			Children = new Drawable[]
			{
				_background = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(24, 24, 30, 230)
				},
				_text = new SpriteText
				{
					Font = new FontUsage("", 12, "Bold"),
					Colour = Color4.White
				}
			}
		};
	}

	public void Show(string message, bool success = true, int durationMs = 4000)
	{
		_text.Text = message;
		_background.Colour = success
			? new Color4(40, 70, 45, 235)
			: new Color4(70, 35, 35, 235);
		_text.Colour = success
			? new Color4(170, 230, 170, 255)
			: new Color4(255, 170, 170, 255);

		_hideAt = Clock.CurrentTime + durationMs;
		this.FadeIn(150);
	}

	protected override void Update()
	{
		base.Update();

		if (Alpha > 0 && Clock.CurrentTime >= _hideAt)
			this.FadeOut(250);
	}
}
