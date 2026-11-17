using Companella.Components.Misc;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;
using System.Globalization;
using System.Linq;

namespace Companella.Components.Settings;

/// <summary>
/// Card-style settings section matching <see cref="StyledButton"/> / <see cref="StyledDialog"/> theme.
/// </summary>
public partial class SettingsSection : CompositeDrawable
{
	private readonly string _title;
	private readonly string? _description;
	private readonly Drawable _content;

	public SettingsSection(string title, Drawable content)
		: this(title, null, content)
	{
	}

	public SettingsSection(string title, string? description, Drawable content)
	{
		_title = title;
		_description = description;
		_content = content;
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = StyledDialog.CornerRadius;

		var headerItems = new List<Drawable>
		{
			SettingsLayout.CreateSectionTitle(_title)
		};

		if (!string.IsNullOrEmpty(_description))
			headerItems.Add(SettingsLayout.CreateSectionDescription(_description));

		_content.RelativeSizeAxes = Axes.X;

		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = StyledButton.Theme.DialogBg
			},
			new Box
			{
				Width = StyledDialog.AccentBarWidth,
				RelativeSizeAxes = Axes.Y,
				Colour = StyledButton.Theme.Accent
			},
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Padding = new MarginPadding(16),
				Spacing = new Vector2(0, 12),
				Children = new Drawable[]
				{
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 4),
						Children = headerItems.ToArray()
					},
					_content
				}
			}
		};
	}
}

/// <summary>
/// Group heading for clusters of settings sections.
/// </summary>
public partial class SettingsGroupHeader : CompositeDrawable
{
	private readonly string _title;
	private readonly string? _subtitle;

	public SettingsGroupHeader(string title, string? subtitle = null)
	{
		_title = title;
		_subtitle = subtitle;
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var children = new List<Drawable>
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(8, 0),
				Children = new Drawable[]
				{
					new Box
					{
						Size = new Vector2(3, 18),
						Colour = StyledButton.Theme.Accent
					},
					new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Child = SettingsLayout.CreateWrappingText(_title, 15, Color4.White, "Bold")
					}
				}
			}
		};

		if (!string.IsNullOrEmpty(_subtitle))
		{
			children.Add(new Container
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Padding = new MarginPadding { Left = 11 },
				Child = SettingsLayout.CreateWrappingText(_subtitle, 13, StyledButton.Theme.DisabledLabel)
			});
		}

		InternalChild = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 4),
			Children = children.ToArray()
		};
	}
}

/// <summary>
/// Shared layout helpers for the settings tab.
/// </summary>
public static class SettingsLayout
{
	public static TextFlowContainer CreateWrappingText(
		string text,
		float fontSize = 13,
		Color4? colour = null,
		string weight = "")
	{
		var resolvedColour = colour ?? StyledButton.Theme.DisabledLabel;
		return new TextFlowContainer(s =>
		{
			s.Font = new FontUsage("", fontSize, weight);
			s.Colour = resolvedColour;
		})
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Text = text
		};
	}

	public static TextFlowContainer CreateHint(string text) => CreateWrappingText(text);

	public static TextFlowContainer CreateInlineLinkText(
		string prefix,
		string linkText,
		string url,
		string suffix,
		float fontSize = 13,
		Color4? textColour = null,
		Color4? linkColour = null)
	{
		var resolvedTextColour = textColour ?? StyledButton.Theme.DisabledLabel;
		var resolvedLinkColour = linkColour ?? StyledButton.Theme.Accent;

		var flow = new TextFlowContainer(s =>
		{
			s.Font = new FontUsage("", fontSize);
			s.Colour = resolvedTextColour;
		})
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y
		};

		if (!string.IsNullOrEmpty(prefix))
			flow.AddText(prefix);

		flow.AddText(new ClickableLinkText(linkText, url, resolvedLinkColour)
		{
			Font = new FontUsage("", fontSize),
			Colour = resolvedLinkColour
		});

		if (!string.IsNullOrEmpty(suffix))
			flow.AddText(suffix);

		return flow;
	}

	public static TextFlowContainer CreateSubHeading(string text) =>
		CreateWrappingText(text, 13, StyledButton.Theme.MutedLabel, "Bold");

	public static TextFlowContainer CreateSectionTitle(string text) =>
		CreateWrappingText(text, 18, StyledButton.Theme.Accent, "Bold");

	public static TextFlowContainer CreateSectionDescription(string text) =>
		CreateWrappingText(text, 14, StyledButton.Theme.MutedLabel);

	public static TextFlowContainer CreateStatusText(
		float fontSize = 14,
		Color4? colour = null,
		float alpha = 1f)
	{
		return new TextFlowContainer(s =>
		{
			s.Font = new FontUsage("", fontSize);
			s.Colour = colour ?? StyledButton.Theme.MutedLabel;
		})
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Alpha = alpha,
			Text = string.Empty
		};
	}

	public static SettingsSection CreateSection(string title, params Drawable[] content) =>
		CreateSection(title, null, content);

	public static SettingsSection CreateSection(string title, string? description, params Drawable[] content)
	{
		var body = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = content
		};

		return string.IsNullOrEmpty(description)
			? new SettingsSection(title, body)
			: new SettingsSection(title, description, body);
	}

	public static GridContainer CreateRow(params Drawable[] columns)
	{
		if (columns.Length == 0)
			return new GridContainer();

		var share = 1f / columns.Length;
		var cells = columns.Select((column, index) => new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Padding = new MarginPadding { Right = index < columns.Length - 1 ? 12 : 0 },
			Child = column
		}).ToArray();

		return new GridContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			ColumnDimensions = columns.Select(_ => new Dimension(GridSizeMode.Relative, share)).ToArray(),
			RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
			Content = new[] { cells }
		};
	}

	/// <summary>
	/// Equal-width row with a fixed height (e.g. button/toolbar rows).
	/// GridContainer AutoSize rows cannot measure cells that use RelativeSizeAxes.Y.
	/// </summary>
	public static GridContainer CreateButtonRow(float height, params Drawable[] columns)
	{
		if (columns.Length == 0)
			return new GridContainer();

		var share = 1f / columns.Length;
		var cells = columns.Select((column, index) => new Container
		{
			RelativeSizeAxes = Axes.Both,
			Padding = new MarginPadding { Right = index < columns.Length - 1 ? 8 : 0 },
			Child = column
		}).ToArray();

		return new GridContainer
		{
			RelativeSizeAxes = Axes.X,
			Height = height,
			ColumnDimensions = columns.Select(_ => new Dimension(GridSizeMode.Relative, share)).ToArray(),
			RowDimensions = new[] { new Dimension(GridSizeMode.Absolute, height) },
			Content = new[] { cells }
		};
	}

	/// <summary>
	/// Single row with fixed side columns and a flexible centre column.
	/// </summary>
	public static GridContainer CreateFlexibleRow(float leftWidth, float rightWidth, float height, Drawable left, Drawable centre, Drawable right)
	{
		return new GridContainer
		{
			RelativeSizeAxes = Axes.X,
			Height = height,
			ColumnDimensions = new[]
			{
				new Dimension(GridSizeMode.Absolute, leftWidth),
				new Dimension(GridSizeMode.Relative, 1f),
				new Dimension(GridSizeMode.Absolute, rightWidth)
			},
			RowDimensions = new[] { new Dimension(GridSizeMode.Absolute, height) },
			Content = new[]
			{
				new[]
				{
					new Container { RelativeSizeAxes = Axes.Both, Child = left },
					new Container
					{
						RelativeSizeAxes = Axes.Both,
						Padding = new MarginPadding { Horizontal = 4 },
						Child = centre
					},
					new Container { RelativeSizeAxes = Axes.Both, Child = right }
				}
			}
		};
	}

	public static SpriteText CreateInlineLabel(string text, float fontSize = 13, Color4? colour = null) =>
		new()
		{
			Text = text,
			Font = new FontUsage("", fontSize),
			Colour = colour ?? StyledButton.Theme.DisabledLabel
		};

	public static Container CreateTextInput(string value, out BasicTextBox textBox, float width = 80, string placeholder = "")
	{
		textBox = new BasicTextBox
		{
			RelativeSizeAxes = Axes.Both,
			Text = value,
			PlaceholderText = placeholder,
			CommitOnFocusLost = true
		};

		return new Container
		{
			Width = width,
			Height = 30,
			Masking = true,
			CornerRadius = StyledDialog.CornerRadius,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = StyledButton.Theme.DialogInsetBg
				},
				textBox
			}
		};
	}

	public static FillFlowContainer CreateStepperRow(
		string label,
		Func<int> getValue,
		Action<int> setValue,
		out SpriteText valueText,
		SettingsStepperOptions? options = null)
	{
		options ??= SettingsStepperOptions.Default;
		SpriteText capturedValueText = null!;

		void adjust(int direction, bool largeStep)
		{
			var step = largeStep ? options.StepLarge : options.StepSmall;
			var newValue = Math.Clamp(getValue() + direction * step, options.Min, options.Max);
			setValue(newValue);
			capturedValueText.Text = newValue.ToString(CultureInfo.InvariantCulture);
		}

		var row = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(8, 0),
			Children = new Drawable[]
			{
				new Container
				{
					Width = options.LabelWidth,
					AutoSizeAxes = Axes.Y,
					Child = CreateHint(label)
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(4, 0),
					Children = new Drawable[]
					{
						new StyledButton("-", StyledButtonAppearance.Muted)
						{
							Width = 28,
							Height = 28,
							FontSize = 15,
							Action = () => adjust(-1, false),
							ShiftAction = () => adjust(-1, true)
						},
						new Container
						{
							Width = options.ValueWidth,
							Height = 28,
							Masking = true,
							CornerRadius = StyledDialog.CornerRadius,
							Children = new Drawable[]
							{
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = StyledButton.Theme.DialogInsetBg
								},
								capturedValueText = new SpriteText
								{
									Text = getValue().ToString(CultureInfo.InvariantCulture),
									Font = new FontUsage("", 14, "Bold"),
									Colour = Color4.White,
									Anchor = Anchor.Centre,
									Origin = Anchor.Centre
								}
							}
						},
						new StyledButton("+", StyledButtonAppearance.Muted)
						{
							Width = 28,
							Height = 28,
							FontSize = 15,
							Action = () => adjust(1, false),
							ShiftAction = () => adjust(1, true)
						}
					}
				}
			}
		};

		valueText = capturedValueText;
		return row;
	}

	public static GridContainer CreateStepperPairRow(
		(string Label, Func<int> Get, Action<int> Set, SettingsStepperOptions? Options) left,
		(string Label, Func<int> Get, Action<int> Set, SettingsStepperOptions? Options) right)
	{
		return CreateRow(
			CreateStepperRow(left.Label, left.Get, left.Set, out _, left.Options),
			CreateStepperRow(right.Label, right.Get, right.Set, out _, right.Options));
	}
}

public sealed class SettingsStepperOptions
{
	public static SettingsStepperOptions Default { get; } = new();

	public float LabelWidth { get; init; } = 20;
	public float ValueWidth { get; init; } = 52;
	public int StepSmall { get; init; } = 10;
	public int StepLarge { get; init; } = 50;
	public int Min { get; init; } = int.MinValue;
	public int Max { get; init; } = int.MaxValue;

	public static SettingsStepperOptions ForOverlayOffset() => new()
	{
		LabelWidth = 52,
		ValueWidth = 64,
		Min = -2000,
		Max = 2000
	};

	public static SettingsStepperOptions ForReplaySize() => new()
	{
		LabelWidth = 52,
		ValueWidth = 64,
		StepSmall = 20,
		StepLarge = 100,
		Min = 100,
		Max = 2400
	};

	public static SettingsStepperOptions ForReplayHeight() => new()
	{
		LabelWidth = 52,
		ValueWidth = 64,
		StepSmall = 20,
		StepLarge = 100,
		Min = 50,
		Max = 1600
	};

	public static SettingsStepperOptions ForReplayPosition() => new()
	{
		LabelWidth = 52,
		ValueWidth = 64,
		Min = -500,
		Max = 4000
	};
}
