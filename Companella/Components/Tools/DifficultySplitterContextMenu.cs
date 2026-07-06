using Companella.Components.Misc;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Tools;

public readonly record struct DifficultySplitterContextMenuItem(
	string Id,
	string Label,
	bool Enabled = true);

/// <summary>
/// Lightweight right-click menu for the difficulty splitter chart.
/// </summary>
public partial class DifficultySplitterContextMenu : CompositeDrawable
{
	public event Action<string>? ItemSelected;
	public event Action? Dismissed;

	private FillFlowContainer _items = null!;
	private bool _isOpen;
	private double _openedAt;

	public DifficultySplitterContextMenu()
	{
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new Container
		{
			AutoSizeAxes = Axes.Both,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(28, 28, 34, 245)
				},
				_items = new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Vertical,
					Padding = new MarginPadding(4),
					Spacing = new Vector2(0, 2)
				}
			}
		};
	}

	public void Show(Vector2 screenPosition, IReadOnlyList<DifficultySplitterContextMenuItem> items)
	{
		_items.Clear();

		foreach (var item in items)
			_items.Add(new ContextMenuRow(item, OnRowClicked));

		Position = screenPosition;
		_isOpen = true;
		_openedAt = Clock.CurrentTime;
		this.FadeIn(100);
	}

	public void CloseMenu()
	{
		if (!_isOpen)
			return;

		_isOpen = false;
		this.FadeOut(80);
		Dismissed?.Invoke();
	}

	protected override bool OnClick(ClickEvent e) => true;

	protected override void Update()
	{
		base.Update();

		if (!_isOpen || Alpha <= 0 || Clock.CurrentTime - _openedAt < 120)
			return;

		var inputManager = GetContainingInputManager();
		if (inputManager == null)
			return;

		if (inputManager.CurrentState.Mouse.Buttons.Any(b => b == MouseButton.Left || b == MouseButton.Right))
		{
			var mousePos = inputManager.CurrentState.Mouse.Position;
			if (!ReceivePositionalInputAt(mousePos))
				CloseMenu();
		}
	}

	private void OnRowClicked(string id)
	{
		ItemSelected?.Invoke(id);
		CloseMenu();
	}

	private partial class ContextMenuRow : CompositeDrawable
	{
		private readonly DifficultySplitterContextMenuItem _item;
		private readonly Action<string> _onClick;
		private Box _background = null!;

		public ContextMenuRow(DifficultySplitterContextMenuItem item, Action<string> onClick)
		{
			_item = item;
			_onClick = onClick;
			Width = 190;
			Height = 28;
		}

		[BackgroundDependencyLoader]
		private void load()
		{
			InternalChildren = new Drawable[]
			{
				_background = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(40, 40, 48, 255)
				},
				new SpriteText
				{
					Text = _item.Label,
					Font = new FontUsage("", 12),
					Colour = _item.Enabled ? Color4.White : new Color4(100, 100, 100, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Padding = new MarginPadding { Horizontal = 10 }
				}
			};

			if (!_item.Enabled)
				Alpha = 0.5f;
		}

		protected override bool OnClick(ClickEvent e)
		{
			if (!_item.Enabled || e.Button != MouseButton.Left)
				return false;

			_onClick(_item.Id);
			return true;
		}

		protected override bool OnHover(HoverEvent e)
		{
			if (_item.Enabled)
				_background.Colour = StyledButton.Theme.Accent;
			return base.OnHover(e);
		}

		protected override void OnHoverLost(HoverLostEvent e)
		{
			_background.Colour = new Color4(40, 40, 48, 255);
			base.OnHoverLost(e);
		}
	}
}
