using System.Globalization;
using Companella.Components.Misc;
using Companella.Components.Settings;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace Companella.Components.Tools;

/// <summary>
/// Panel for inputting and applying universal offset changes.
/// </summary>
public partial class OffsetInputPanel : CompositeDrawable
{
	private const float _rowHeight = 32f;
	private const float _stepButtonWidth = 44f;
	private const float _inputWidth = 72f;
	private const float _applyButtonWidth = 88f;

	private BasicTextBox _offsetTextBox = null!;
	private StyledButton _applyButton = null!;
	private StyledButton _plusButton = null!;
	private StyledButton _minusButton = null!;

	public event Action<double>? ApplyOffsetClicked;

	private double _currentOffset;

	public OffsetInputPanel()
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
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				SettingsLayout.CreateHint("Adjust the universal offset for all hit objects in the loaded beatmap."),
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(8, 0),
					Children = new Drawable[]
					{
						_minusButton = new StyledButton("-10", StyledButtonAppearance.Muted)
						{
							Size = new Vector2(_stepButtonWidth, _rowHeight),
							FontSize = 14,
							Bold = false,
							TooltipText = "Decrease offset by 10ms"
						},
						new FillFlowContainer
						{
							AutoSizeAxes = Axes.Both,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(6, 0),
							Children = new Drawable[]
							{
								new Container
								{
									Size = new Vector2(_inputWidth, _rowHeight),
									Masking = true,
									CornerRadius = StyledDialog.CornerRadius,
									Children = new Drawable[]
									{
										new Box
										{
											RelativeSizeAxes = Axes.Both,
											Colour = StyledButton.Theme.DialogInsetBg
										},
										_offsetTextBox = new BasicTextBox
										{
											RelativeSizeAxes = Axes.Both,
											Text = "0",
											CommitOnFocusLost = true
										}
									}
								},
								new SpriteText
								{
									Text = "ms",
									Font = new FontUsage("", 14),
									Colour = StyledButton.Theme.MutedLabel,
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								}
							}
						},
						_plusButton = new StyledButton("+10", StyledButtonAppearance.Muted)
						{
							Size = new Vector2(_stepButtonWidth, _rowHeight),
							FontSize = 14,
							Bold = false,
							TooltipText = "Increase offset by 10ms"
						},
						_applyButton = new StyledButton("Apply")
						{
							Size = new Vector2(_applyButtonWidth, _rowHeight),
							FontSize = 14
						}
					}
				}
			}
		};

		InternalChild = SettingsLayout.CreateSection("Universal Offset", content);

		_minusButton.Clicked += () => AdjustOffset(-10);
		_plusButton.Clicked += () => AdjustOffset(10);
		_applyButton.Clicked += () => ApplyOffsetClicked?.Invoke(_currentOffset);
		_offsetTextBox.OnCommit += OnOffsetCommitted;
	}

	private void OnOffsetCommitted(TextBox textBox, bool newValue)
	{
		if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
		{
			_currentOffset = offset;
			ApplyOffsetClicked?.Invoke(_currentOffset);
		}
	}

	private void AdjustOffset(double delta)
	{
		_currentOffset += delta;
		_offsetTextBox.Text = _currentOffset.ToString(CultureInfo.InvariantCulture);
		ApplyOffsetClicked?.Invoke(_currentOffset);
	}

	public void SetOffset(double offset)
	{
		_currentOffset = offset;
		_offsetTextBox.Text = offset.ToString(CultureInfo.InvariantCulture);
	}

	public void SetEnabled(bool enabled)
	{
		_minusButton.Enabled = enabled;
		_plusButton.Enabled = enabled;
		_applyButton.Enabled = enabled;
	}

	public void Reset()
	{
		_currentOffset = 0;
		_offsetTextBox.Text = "0";
	}
}
