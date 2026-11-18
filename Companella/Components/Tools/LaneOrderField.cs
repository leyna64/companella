using System.Globalization;
using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Mods;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Draggable lane order editor for the Lane Swap mod
/// </summary>
public partial class LaneOrderField : CompositeDrawable
{
	private const float _rowHeight = 68, _noteHeight = 52,
		_maxNoteWidth = 140, _minNoteWidth = 14, _maxGap = 8, _inset = 10;
	private readonly double _shiftDuration = 110, _snapDuration = 130;

	// color scheme for key display
	private static readonly Color4 _whiteKey = new(242, 242, 242, 255);
	private static readonly Color4 _pinkKey = new(255, 102, 170, 255);
	private static readonly Color4 _goldKey = new(255, 200, 61, 255);
	private static readonly Color4 _greenKey = new(63, 217, 127, 255);

	private readonly LaneSwapMod _mod;
	private readonly int _keyCount;
	private readonly Color4 _accentColor;

	private readonly Dictionary<int, LaneNote> _notesByLane = new();
	private readonly List<int> _order = new();

	private Container _row = null!;
	private SpriteText _orderText = null!;

	private LaneNote? _draggedNote;

	private float _grabOffsetX;
	private float _noteWidth = _maxNoteWidth;
	private float _gap = _maxGap;
	private float _startX = _inset;
	private float _layoutWidth = -1;

	// the lane currently being dragged
	private int DraggedLane => _draggedNote?.Lane ?? 0;

	public LaneOrderField(LaneSwapMod mod, int keyCount, Color4 accentColor)
	{
		_mod = mod;
		_keyCount = keyCount;
		_accentColor = accentColor;

		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	private float Step => _noteWidth + _gap;

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 6),
			Children = new Drawable[]
			{
				new Container
				{
					RelativeSizeAxes = Axes.X,
					Height = 16,
					Children = new Drawable[]
					{
						new SpriteText
						{
							Text = "Lane Order",
							Font = new FontUsage("", 14),
							Colour = Color4.White,
							Anchor = Anchor.CentreLeft,
							Origin = Anchor.CentreLeft
						},
						_orderText = new SpriteText
						{
							Text = string.Empty,
							Font = new FontUsage("", 14, "Bold"),
							Colour = _accentColor,
							Anchor = Anchor.CentreRight,
							Origin = Anchor.CentreRight
						}
					}
				},
				new Container
				{
					RelativeSizeAxes = Axes.X,
					Height = _rowHeight,
					Masking = true,
					CornerRadius = 6,
					Children = new Drawable[]
					{
						new Box
						{
							RelativeSizeAxes = Axes.Both,
							Colour = StyledButton.Theme.NormalBg
						},
						_row = new Container
						{
							RelativeSizeAxes = Axes.Both
						}
					}
				},
				SettingsLayout.CreateHint("Drag a note to change the order of the lanes")
			}
		};

		BuildNotes();
	}

	protected override void Update()
	{
		base.Update();

		if (_row.DrawWidth <= 0 || Math.Abs(_row.DrawWidth - _layoutWidth) < 0.5f)
			return;

		_layoutWidth = _row.DrawWidth;
		UpdateLayout();
	}

	private void BuildNotes()
	{
		var lanes = LaneSwapMod.TryParseOrder(_mod.LaneOrder, _keyCount, out var parsed)
			? parsed
			: Enumerable.Range(1, _keyCount).ToArray();

		_order.Clear();
		_order.AddRange(lanes);

		_draggedNote = null;
		_notesByLane.Clear();
		_row.Clear();

		for (var i = 0; i < lanes.Length; i++)
		{
			var lane = lanes[i];
			var note = new LaneNote(lane, LaneColor(lane - 1, _keyCount), this)
			{
				Anchor = Anchor.TopLeft,
				Origin = Anchor.TopLeft
			};

			_notesByLane[lane] = note;
			_row.Add(note);
		}

		WriteOrder();
		_layoutWidth = -1;
	}

	private void UpdateLayout()
	{
		var available = Math.Max(0, _row.DrawWidth - _inset * 2);
		var slots = Math.Max(1, _keyCount - 1);

		_noteWidth = Math.Clamp((available - _maxGap * slots) / _keyCount, _minNoteWidth, _maxNoteWidth);
		_gap = Math.Clamp((available - _noteWidth * _keyCount) / slots, 0, _maxGap);

		var totalWidth = _noteWidth * _keyCount + _gap * slots;
		_startX = _inset + Math.Max(0, (available - totalWidth) / 2);

		var y = (_rowHeight - _noteHeight) / 2;
		var fontSize = Math.Clamp(Math.Min(_noteWidth * 0.42f, _noteHeight * 0.45f), 9, 20);

		foreach (var note in _notesByLane.Values)
		{
			note.Size = new Vector2(_noteWidth, _noteHeight);
			note.Y = y;
			note.LabelFontSize = fontSize;
		}

		if (_draggedNote != null)
		{
			_draggedNote.X = Math.Clamp(_draggedNote.X, _startX, _startX + Step * slots);
			return;
		}

		AlignNotes(0);
	}

	private void AlignNotes(double duration)
	{
		for (var i = 0; i < _order.Count; i++)
		{
			var note = _notesByLane[_order[i]];

			if (note == _draggedNote)
				continue;

			var x = _startX + i * Step;

			if (duration <= 0)
				note.X = x;
			else
				note.MoveToX(x, duration, Easing.OutQuint);
		}
	}

	private void WriteOrder()
	{
		_mod.LaneOrder = string.Join(",", _order);
		_orderText.Text = _mod.LaneOrder;
	}

	private static Color4 LaneColor(int column, int keyCount)
	{
		// assign each lane colors
		var half = keyCount / 2;
		var left = new Color4[half];

		for (var i = 0; i < half; i++)
			left[i] = i % 2 == 0 ? _whiteKey : _pinkKey;

		if (keyCount % 2 == 0 && half > 2)
			left[half - 1] = _goldKey;

		var colours = new Color4[keyCount];
		left.CopyTo(colours, 0);

		for (var i = 0; i < half; i++)
			colours[keyCount - 1 - i] = left[i];

		if (keyCount % 2 == 1)
			colours[half] = _goldKey;
		else if (half > 2)
			colours[half] = _greenKey;

		return colours[column];
	}

	private void BeginDrag(LaneNote note, DragStartEvent e)
	{
		_draggedNote = note;
		_grabOffsetX = _row.ToLocalSpace(e.ScreenSpaceMousePosition).X - note.X;
		AlignNotes(0);
		_row.ChangeChildDepth(note, -1);
	}

	private void DragNote(LaneNote note, DragEvent e)
	{
		if (_draggedNote != note)
			return;

		// make them move horizontal only so they don't go out of bounds
		var x = Math.Clamp(_row.ToLocalSpace(e.ScreenSpaceMousePosition).X - _grabOffsetX, _startX,
			_startX + Step * (_keyCount - 1));

		note.X = x;

		var targetIndex = Math.Clamp((int)MathF.Round((x - _startX) / Step), 0, _keyCount - 1);
		var currentIndex = _order.IndexOf(note.Lane);

		if (targetIndex == currentIndex)
			return;

		_order.RemoveAt(currentIndex);
		_order.Insert(targetIndex, note.Lane);

		WriteOrder();
		AlignNotes(_shiftDuration);
	}

	private void EndDrag(LaneNote note)
	{
		if (_draggedNote != note)
			return;

		_draggedNote = null;
		_row.ChangeChildDepth(note, 0);
		AlignNotes(_snapDuration);
	}

	internal partial class LaneNote : CompositeDrawable
	{
		private readonly int _lane;
		private readonly LaneOrderField _field;

		private readonly Color4 _colour;
		private Box _background = null!;
		private SpriteText _label = null!;
		private float _labelFontSize = 15;

		public int Lane => _lane;

		public float LabelFontSize
		{
			get => _labelFontSize;
			set
			{
				_labelFontSize = value;
				_label?.Font = new FontUsage("", value, "Bold");
			}
		}

		public LaneNote(int lane, Color4 colour, LaneOrderField field)
		{
			_lane = lane;
			_colour = colour;
			_field = field;

			Masking = true;
			CornerRadius = 6;
		}

		[BackgroundDependencyLoader]
		private void load()
		{
			InternalChildren = new Drawable[]
			{
				_background = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = _colour
				},
				_label = new SpriteText
				{
					Text = _lane.ToString(CultureInfo.InvariantCulture),
					Font = new FontUsage("", _labelFontSize, "Bold"),
					Colour = _colour == _pinkKey ? Color4.White : StyledButton.Theme.DialogBg,
					Anchor = Anchor.Centre,
					Origin = Anchor.Centre
				}
			};
		}

		protected override bool OnDragStart(DragStartEvent e)
		{
			if (_field.DraggedLane != 0)
				return false;

			_field.BeginDrag(this, e);
			return true;
		}

		protected override void OnDrag(DragEvent e)
		{
			_field.DragNote(this, e);
			base.OnDrag(e);
		}

		protected override void OnDragEnd(DragEndEvent e)
		{
			_field.EndDrag(this);
			base.OnDragEnd(e);
		}
	}
}
