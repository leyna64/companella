using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// Clickable inline link text for use inside a <see cref="TextFlowContainer"/>.
/// </summary>
public partial class ClickableLinkText : SpriteText
{
	private readonly string _url;
	private readonly Color4 _linkColour;

	public ClickableLinkText(string text, string url, Color4 linkColour)
	{
		_url = url;
		_linkColour = linkColour;
		Text = text;
	}

	protected override bool OnHover(HoverEvent e)
	{
		this.FadeColour(new Color4(255, 150, 200, 255), 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		this.FadeColour(_linkColour, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = _url,
				UseShellExecute = true
			});
		}
		catch
		{
		}

		return true;
	}
}

/// <summary>
/// A clickable text link that opens a URL in the default browser.
/// </summary>
public partial class LinkText : CompositeDrawable
{
	private readonly string _text;
	private readonly string _url;
	private readonly Color4 _normalColor;
	private readonly Color4 _hoverColor;
	private readonly float _fontSize;
	private readonly string _fontWeight;

	private SpriteText _textSprite = null!;
	private Box _underline = null!;

	public LinkText(
		string text,
		string url,
		float fontSize = 12f,
		string fontWeight = "",
		Color4? normalColor = null,
		Color4? hoverColor = null)
	{
		_text = text;
		_url = url;
		_fontSize = fontSize;
		_fontWeight = fontWeight;
		_normalColor = normalColor ?? new Color4(180, 180, 180, 255);
		_hoverColor = hoverColor ?? new Color4(255, 102, 170, 255);
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		AutoSizeAxes = Axes.Both;

		InternalChildren = new Drawable[]
		{
			_textSprite = new SpriteText
			{
				Text = _text,
				Font = new FontUsage("", _fontSize, _fontWeight),
				Colour = _normalColor
			},
			_underline = new Box
			{
				RelativeSizeAxes = Axes.X,
				Height = 1,
				Anchor = Anchor.BottomLeft,
				Origin = Anchor.BottomLeft,
				Colour = _normalColor,
				Alpha = 0
			}
		};
	}

	protected override bool OnHover(HoverEvent e)
	{
		_textSprite.FadeColour(_hoverColor, 100);
		_underline.FadeColour(_hoverColor, 100);
		_underline.FadeTo(1, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_textSprite.FadeColour(_normalColor, 100);
		_underline.FadeTo(0, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		try
		{
			// Open URL in default browser
			Process.Start(new ProcessStartInfo
			{
				FileName = _url,
				UseShellExecute = true
			});
		}
		catch
		{
			// Silently fail if browser cannot be opened
		}

		return true;
	}
}
