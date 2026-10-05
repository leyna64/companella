using Companella.Mods.Parameters;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace Companella.Components.Tools;

/// <summary>
/// A textbox control for string mod parameters.
/// displays a title and textbox input field (value text, if we need that i guess)
/// </summary>
public partial class ParameterTextBox : CompositeDrawable, IHasTooltip
{
	private readonly StringModParameter _parameter;
	private readonly Color4 _accentColor;

	private TextBox _textBox = null!;

	public LocalisableString TooltipText => _parameter.Description;

	public ParameterTextBox(StringModParameter parameter, Color4 accentColor)
	{
		_parameter = parameter;
		_accentColor = accentColor;

		RelativeSizeAxes = Axes.X;
		Height = 60;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					// Title row with name and current value
					new Container
					{
						RelativeSizeAxes = Axes.X,
						Height = 16,
						Children = new Drawable[]
						{
							new SpriteText
							{
								Text = _parameter.Name,
								Font = new FontUsage("", 14),
								Colour = Color4.White,
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							}
						}
					},
					// Textbox
					_textBox = new StyledTextBox
					{
						RelativeSizeAxes = Axes.X,
						Height = 36,
						Text = _parameter.Value,
						PlaceholderText = _parameter.DefaultValue
					}
				}
			}
		};

		_textBox.Current.BindValueChanged(_ => OnTextBoxChanged());
		_textBox.OnCommit += OnTextBoxCommit;
	}

	private void OnTextBoxChanged()
	{
		_parameter.Value = _textBox.Text;
	}

	private void OnTextBoxCommit(TextBox sender, bool newInputSource)
	{
		_parameter.Value = sender.Text;
	}

	/// <summary>
	/// Modern styled text box with clean appearance.
	/// Extends BasicTextBox to get standard TextBox behavior.
	/// </summary>
	private partial class StyledTextBox : BasicTextBox
	{
		private static readonly FontUsage _font = new("", 14);

		public StyledTextBox()
		{
			CornerRadius = 4;
			BackgroundFocused = new Color4(50, 50, 55, 255);
			BackgroundUnfocused = new Color4(40, 40, 45, 255);
			Masking = true;
		}

		protected override SpriteText CreatePlaceholder()
		{
			return new SpriteText
			{
				Font = _font,
				Colour = new Color4(80, 80, 80, 255)
			};
		}

		protected override Drawable GetDrawableCharacter(char c)
		{
			return new SpriteText
			{
				Text = c.ToString(),
				Font = _font,
				Colour = Color4.White
			};
		}
	}
}
