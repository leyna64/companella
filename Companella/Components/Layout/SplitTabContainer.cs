using Companella.Components.Misc;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Layout;

/// <summary>
/// A split container with a sidebar navigation on the left and content on the right.
/// Designed for organizing multiple features within a tab.
/// </summary>
public partial class SplitTabContainer : CompositeDrawable
{
	private readonly SplitTabItem[] _items;
	private int _selectedIndex;

	private Container _sidebarContainer = null!;
	private Container _contentContainer = null!;
	private Box _selectionIndicator = null!;
	private SidebarButton[] _sidebarButtons = null!;

	private const float _sidebarWidth = 140f;
	private const float _buttonHeight = 36f;

	/// <summary>
	/// Event raised when the selected item changes.
	/// </summary>
	public event Action<int>? SelectionChanged;

	public SplitTabContainer(SplitTabItem[] items)
	{
		if (items == null || items.Length == 0)
			throw new ArgumentException("At least one item is required");

		_items = items;
		_selectedIndex = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		_sidebarButtons = new SidebarButton[_items.Length];

		InternalChildren = new Drawable[]
		{
			new GridContainer
			{
				RelativeSizeAxes = Axes.Both,
				ColumnDimensions = new[]
				{
					new Dimension(GridSizeMode.Absolute, _sidebarWidth),
					new Dimension()
				},
				Content = new[]
				{
					new Drawable[]
					{
						_sidebarContainer = new Container
						{
							RelativeSizeAxes = Axes.Both,
							Children = new Drawable[]
							{
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = StyledButton.Theme.DialogBg
								},
								new Box
								{
									Width = 1,
									RelativeSizeAxes = Axes.Y,
									Anchor = Anchor.TopRight,
									Origin = Anchor.TopRight,
									Colour = StyledButton.Theme.DialogInsetBg
								},
								_selectionIndicator = new Box
								{
									Width = 3,
									Height = _buttonHeight,
									Colour = StyledButton.Theme.Accent,
									Anchor = Anchor.TopLeft,
									Origin = Anchor.TopLeft
								},
								new ChainedScrollContainer
								{
									RelativeSizeAxes = Axes.Both,
									ClampExtension = 10,
									Child = new FillFlowContainer
									{
										RelativeSizeAxes = Axes.X,
										AutoSizeAxes = Axes.Y,
										Direction = FillDirection.Vertical,
										Padding = new MarginPadding { Top = 5, Bottom = 5 },
										Children = CreateSidebarButtons()
									}
								}
							}
						},
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Children = new Drawable[]
							{
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = StyledButton.Theme.NormalBg
								},
								new ChainedScrollContainer
								{
									RelativeSizeAxes = Axes.Both,
									ClampExtension = 20,
									ScrollbarVisible = true,
									Child = _contentContainer = new Container
									{
										RelativeSizeAxes = Axes.X,
										AutoSizeAxes = Axes.Y,
										Masking = true,
										Padding = new MarginPadding { Horizontal = 4, Top = 8, Bottom = 48 }
									}
								}
							}
						}
					}
				}
			}
		};

		foreach (var item in _items)
		{
			item.Content.RelativeSizeAxes = Axes.X;

			var wrapper = new Container
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Alpha = 0,
				Child = item.Content
			};
			_contentContainer.Add(wrapper);
		}

		if (_contentContainer.Children.Count > 0)
			_contentContainer.Children[0].Alpha = 1;

		Schedule(() => UpdateIndicator(false));
	}

	private Drawable[] CreateSidebarButtons()
	{
		var buttons = new Drawable[_items.Length];

		for (var i = 0; i < _items.Length; i++)
		{
			var index = i;
			var button = new SidebarButton(_items[i].Name, i == _selectedIndex)
			{
				RelativeSizeAxes = Axes.X,
				Height = _buttonHeight
			};
			button.Clicked += () => SelectItem(index);
			_sidebarButtons[i] = button;
			buttons[i] = button;
		}

		return buttons;
	}

	public void SelectItem(int index)
	{
		if (index < 0 || index >= _items.Length || index == _selectedIndex)
			return;

		_contentContainer.Children[_selectedIndex].FadeOut(150, Easing.OutQuad);
		_sidebarButtons[_selectedIndex].SetSelected(false);

		_selectedIndex = index;

		_contentContainer.Children[_selectedIndex].FadeIn(150, Easing.OutQuad);
		_sidebarButtons[_selectedIndex].SetSelected(true);

		UpdateIndicator(true);

		SelectionChanged?.Invoke(_selectedIndex);
	}

	private void UpdateIndicator(bool animate)
	{
		if (_sidebarButtons == null || _sidebarButtons.Length == 0)
			return;

		var targetY = 5 + _selectedIndex * _buttonHeight;

		if (animate)
			_selectionIndicator.MoveTo(new Vector2(0, targetY), 200, Easing.OutQuad);
		else
			_selectionIndicator.Y = targetY;
	}

	public int SelectedIndex => _selectedIndex;

	private partial class SidebarButton : CompositeDrawable
	{
		private readonly string _text;
		private bool _isSelected;

		private SpriteText _label = null!;
		private Box _hoverOverlay = null!;

		public event Action? Clicked;

		public SidebarButton(string text, bool isSelected)
		{
			_text = text;
			_isSelected = isSelected;
		}

		[BackgroundDependencyLoader]
		private void load()
		{
			InternalChildren = new Drawable[]
			{
				_hoverOverlay = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = Color4.White,
					Alpha = 0
				},
				_label = new SpriteText
				{
					Text = _text,
					Font = new FontUsage("", 15, _isSelected ? "Bold" : ""),
					Colour = _isSelected ? StyledButton.Theme.Accent : StyledButton.Theme.DisabledLabel,
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Padding = new MarginPadding { Left = 12 },
					RelativeSizeAxes = Axes.X,
					Truncate = true
				}
			};
		}

		public void SetSelected(bool selected)
		{
			_isSelected = selected;
			_label.FadeColour(_isSelected ? StyledButton.Theme.Accent : StyledButton.Theme.DisabledLabel, 150);
			_label.Font = new FontUsage("", 15, _isSelected ? "Bold" : "");
		}

		protected override bool OnHover(HoverEvent e)
		{
			_hoverOverlay.FadeTo(0.05f, 100);
			if (!_isSelected)
				_label.FadeColour(Color4.White, 100);
			return base.OnHover(e);
		}

		protected override void OnHoverLost(HoverLostEvent e)
		{
			_hoverOverlay.FadeTo(0, 100);
			if (!_isSelected)
				_label.FadeColour(StyledButton.Theme.DisabledLabel, 100);
			base.OnHoverLost(e);
		}

		protected override bool OnClick(ClickEvent e)
		{
			Clicked?.Invoke();
			return true;
		}
	}
}

/// <summary>
/// Represents an item in a SplitTabContainer.
/// </summary>
public class SplitTabItem
{
	public string Name { get; }

	public Drawable Content { get; }

	public SplitTabItem(string name, Drawable content)
	{
		Name = name;
		Content = content;
	}
}
