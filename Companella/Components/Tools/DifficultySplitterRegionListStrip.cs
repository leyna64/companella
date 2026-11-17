using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Tools;

public readonly record struct DifficultySplitterRegionListItem(
	Guid? PairId,
	string Name,
	string TimeRange,
	string Badge,
	bool IsComplete,
	bool IsSelected,
	int ColorIndex);

/// <summary>
/// Compact clickable list of defined regions.
/// </summary>
public partial class DifficultySplitterRegionListStrip : CompositeDrawable
{
	public event Action<Guid>? PairSelected;
	public event Action<string>? IncompleteRegionSelected;

	private FillFlowContainer _rows = null!;

	public DifficultySplitterRegionListStrip()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 2),
			Children = new Drawable[]
			{
				_rows = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 2)
				}
			}
		};
	}

	public void SetItems(IReadOnlyList<DifficultySplitterRegionListItem> items)
	{
		_rows.Clear();

		if (items.Count == 0)
		{
			_rows.Add(new SpriteText
			{
				Text = "Press M to add a region marker",
				Font = new FontUsage("", 11),
				Colour = new Color4(120, 120, 120, 255)
			});
			_rows.Add(new SpriteText
			{
				Text = "Press Delete to remove a region",
				Font = new FontUsage("", 11),
				Colour = new Color4(120, 120, 120, 255)
			});
			_rows.Add(new SpriteText
			{
				Text = "Press F2 to rename a region!",
				Font = new FontUsage("", 11),
				Colour = new Color4(120, 120, 120, 255)
			});
			return;
		}

		foreach (var item in items)
			_rows.Add(new RegionListRow(item, OnRowClicked));
	}

	private void OnRowClicked(DifficultySplitterRegionListItem item)
	{
		if (item.PairId.HasValue)
			PairSelected?.Invoke(item.PairId.Value);
		else
			IncompleteRegionSelected?.Invoke(item.Name);
	}

	private partial class RegionListRow : CompositeDrawable
	{
		private static readonly Color4 _defaultBackground = new(35, 35, 42, 180);

		private readonly DifficultySplitterRegionListItem _item;
		private readonly Action<DifficultySplitterRegionListItem> _onClick;
		private readonly Color4 _accent;
		private Box _background = null!;

		public RegionListRow(DifficultySplitterRegionListItem item, Action<DifficultySplitterRegionListItem> onClick)
		{
			_item = item;
			_onClick = onClick;
			_accent = DifficultySplitterRegionColors.GetAccent(item.ColorIndex);
			RelativeSizeAxes = Axes.X;
			Height = 22;
		}

		[BackgroundDependencyLoader]
		private void load()
		{
			var status = _item.IsComplete
				? (string.IsNullOrWhiteSpace(_item.Badge) ? string.Empty : _item.Badge)
				: "…";
			var detail = string.IsNullOrWhiteSpace(status)
				? _item.TimeRange
				: $"{_item.TimeRange} · {status}";

			InternalChildren = new Drawable[]
			{
				_background = new Box
				{
					RelativeSizeAxes = Axes.Both
				},
				new Box
				{
					Size = new Vector2(4, 14),
					Colour = _accent,
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Position = new Vector2(8, 0)
				},
				new SpriteText
				{
					Text = _item.Name,
					Font = new FontUsage("", 12, _item.IsSelected ? "Bold" : ""),
					Colour = _item.IsSelected ? Color4.White : new Color4(210, 210, 210, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Position = new Vector2(18, 0)
				},
				new SpriteText
				{
					Text = detail,
					Font = new FontUsage("", 11, _item.IsComplete ? "Bold" : ""),
					Colour = _item.IsComplete ? _accent : new Color4(220, 170, 80, 255),
					Anchor = Anchor.CentreRight,
					Origin = Anchor.CentreRight,
					Position = new Vector2(-8, 0)
				}
			};

			UpdateBackgroundState(false);
		}

		protected override bool OnHover(HoverEvent e)
		{
			UpdateBackgroundState(hovered: true);
			return base.OnHover(e);
		}

		protected override void OnHoverLost(HoverLostEvent e)
		{
			UpdateBackgroundState(hovered: false);
			base.OnHoverLost(e);
		}

		private void UpdateBackgroundState(bool hovered)
		{
			if (hovered || _item.IsSelected)
			{
				var leftAlpha = hovered ? (byte)130 : (byte)75;
				_background.Colour = ColourInfo.GradientHorizontal(
					new Color4(_accent.R, _accent.G, _accent.B, leftAlpha),
					_defaultBackground);
				return;
			}

			_background.Colour = _defaultBackground;
		}

		protected override bool OnClick(ClickEvent e)
		{
			if (e.Button != MouseButton.Left)
				return false;

			_onClick(_item);
			return true;
		}
	}
}
