using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// Shared styling for modal dialogs, matching <see cref="StyledButton"/> appearance.
/// </summary>
public static class StyledDialog
{
	public static readonly Color4 DimOverlay = new(0, 0, 0, 210);
	public static readonly Color4 InsetBg = StyledButton.Theme.DialogInsetBg;
	public const float CornerRadius = 2f;
	public const float AccentBarWidth = 2f;
	public static readonly MarginPadding DefaultPadding = new(20);

	public static Box CreateDimBackground() => new()
	{
		RelativeSizeAxes = Axes.Both,
		Colour = DimOverlay
	};

	/// <summary>
	/// Creates a centered dialog shell with dark surface and left accent bar.
	/// </summary>
	/// <param name="size">Initial dialog size.</param>
	/// <param name="content">Container where dialog content should be added.</param>
	/// <param name="clipBackground">When false, only the background is clipped; content can overflow (e.g. dropdowns).</param>
	public static Container CreateShell(Vector2 size, out Container content, bool clipBackground = true)
	{
		content = new Container
		{
			RelativeSizeAxes = Axes.Both,
			Padding = DefaultPadding
		};

		var background = new Container
		{
			RelativeSizeAxes = Axes.Both,
			Masking = clipBackground,
			CornerRadius = CornerRadius,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = StyledButton.Theme.DialogBg
				},
				new Box
				{
					Width = AccentBarWidth,
					RelativeSizeAxes = Axes.Y,
					Colour = StyledButton.Theme.Accent
				}
			}
		};

		return new Container
		{
			Anchor = Anchor.Centre,
			Origin = Anchor.Centre,
			Size = size,
			Children = clipBackground
				? new Drawable[] { background, content }
				: new Drawable[] { background, content }
		};
	}

	/// <summary>
	/// Inset panel for scroll areas and grouped controls inside a dialog.
	/// </summary>
	public static Container CreateInsetSection(float? height = null)
	{
		var section = new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = height.HasValue ? Axes.None : Axes.Y,
			Masking = true,
			CornerRadius = CornerRadius,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = InsetBg
				}
			}
		};

		if (height.HasValue)
			section.Height = height.Value;

		return section;
	}

	public static SpriteText CreateTitle(string text) => new()
	{
		Anchor = Anchor.TopCentre,
		Origin = Anchor.TopCentre,
		Text = text,
		Font = new FontUsage("", 20, "Bold"),
		Colour = StyledButton.Theme.Accent
	};

	public static SpriteText CreateSubtitle(string text) => new()
	{
		Anchor = Anchor.TopCentre,
		Origin = Anchor.TopCentre,
		Text = text,
		Font = new FontUsage("", 15),
		Colour = StyledButton.Theme.MutedLabel
	};

	public static SpriteText CreateFieldLabel(string text) => new()
	{
		Text = text,
		Font = new FontUsage("", 14),
		Colour = StyledButton.Theme.MutedLabel
	};

	public static SpriteText CreateBodyText(string text) => new()
	{
		Anchor = Anchor.TopCentre,
		Origin = Anchor.TopCentre,
		Text = text,
		Font = new FontUsage("", 15),
		Colour = Color4.White
	};

	public static SpriteText CreateErrorText() => new()
	{
		Anchor = Anchor.TopCentre,
		Origin = Anchor.TopCentre,
		Text = string.Empty,
		Font = new FontUsage("", 14),
		Colour = StyledButton.Theme.DestructiveAccent,
		Alpha = 0
	};

	public static TextFlowContainer CreateMessageFlow(string text) => new(s =>
	{
		s.Font = new FontUsage("", 15);
		s.Colour = StyledButton.Theme.MutedLabel;
	})
	{
		Anchor = Anchor.TopCentre,
		Origin = Anchor.TopCentre,
		RelativeSizeAxes = Axes.X,
		AutoSizeAxes = Axes.Y,
		TextAnchor = Anchor.TopCentre,
		Text = text
	};

	public static StyledButton CreatePrimaryButton(string text, Color4? accent = null) => new(text)
	{
		AccentColor = accent ?? StyledButton.Theme.Accent,
		Size = new Vector2(110, 36)
	};

	public static StyledButton CreateSecondaryButton(string text) => new(text, StyledButtonAppearance.Muted)
	{
		Size = new Vector2(110, 36)
	};

	public static StyledButton CreateCancelButton(string text = "Cancel") => new(text)
	{
		AccentColor = StyledButton.Theme.DestructiveAccent,
		Size = new Vector2(110, 36)
	};

	public static StyledButton CreateDangerButton(string text) => CreateCancelButton(text);

	public static void PlayShowAnimation(CompositeDrawable overlay, Container panel, int duration = 200)
	{
		overlay.FadeIn(duration, Easing.OutQuint);
		panel.ScaleTo(0.96f).ScaleTo(1f, duration, Easing.OutQuint);
	}

	public static void PlayHideAnimation(CompositeDrawable overlay, int duration = 200) =>
		overlay.FadeOut(duration, Easing.OutQuint);
}
