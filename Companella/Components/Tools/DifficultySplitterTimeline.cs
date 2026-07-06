using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Tools;

public readonly record struct DifficultySplitterTimelineRegion(
	Guid PairId,
	double StartMs,
	double EndMs,
	Color4 Color,
	bool IsSelected);

public readonly record struct DifficultySplitterTimelineMarker(
	Guid MarkerId,
	double TimeMs,
	Color4 Color,
	bool IsSelected);

/// <summary>
/// Full-width map timeline with region spans, marker stubs, visible viewport, and playhead.
/// </summary>
public partial class DifficultySplitterTimeline : CompositeDrawable
{
	public const float PreferredHeight = 26f;

	private const float _horizontalPadding = 8f;
	private const float _trackTop = 7f;
	private const float _trackHeight = 11f;
	private const float _trackBottom = 4f;
	private const float _playheadWidth = 1f;
	private const float _playheadHeight = _trackHeight * 0.5f;

	private static readonly Color4 _backgroundColor = new(16, 16, 20, 255);
	private static readonly Color4 _trackColor = new(28, 28, 34, 255);
	private static readonly Color4 _borderColor = new(42, 42, 50, 255);

	private Container _trackContainer = null!;
	private Container _regionsContainer = null!;
	private Container _markersContainer = null!;
	private Box _viewportBox = null!;
	private Container _playheadContainer = null!;

	private double _mapMinMs;
	private double _mapMaxMs = 1;
	private double _viewCenterMs;
	private double _visibleStartMs;
	private double _visibleEndMs;
	private IReadOnlyList<DifficultySplitterTimelineRegion> _regions = Array.Empty<DifficultySplitterTimelineRegion>();
	private IReadOnlyList<DifficultySplitterTimelineMarker> _markers = Array.Empty<DifficultySplitterTimelineMarker>();
	private bool _hasMap;

	public event Action<double>? SeekRequested;
	public event Action<Guid>? PairSelected;
	public event Action<Guid>? MarkerSelected;

	public DifficultySplitterTimeline()
	{
		RelativeSizeAxes = Axes.X;
		Height = PreferredHeight;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _backgroundColor
			},
			new Box
			{
				RelativeSizeAxes = Axes.X,
				Height = 1,
				Colour = _borderColor
			},
			_trackContainer = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding
				{
					Horizontal = _horizontalPadding,
					Top = _trackTop,
					Bottom = _trackBottom
				},
				Children = new Drawable[]
				{
					new Box
					{
						RelativeSizeAxes = Axes.X,
						Height = _trackHeight,
						Colour = _trackColor
					},
					_regionsContainer = new Container
					{
						RelativeSizeAxes = Axes.X,
						Height = _trackHeight
					},
					new Container
					{
						RelativeSizeAxes = Axes.X,
						Height = _trackHeight,
						Child = _viewportBox = new Box
						{
							Height = _trackHeight,
							Colour = new Color4(255, 255, 255, 18),
							Alpha = 0
						}
					},
					_markersContainer = new Container
					{
						RelativeSizeAxes = Axes.X,
						Height = _trackHeight
					}
				}
			},
			_playheadContainer = new Container
			{
				Width = _playheadWidth,
				Height = _playheadHeight,
				Y = _trackTop + (_trackHeight - _playheadHeight) * 0.5f,
				Child = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = Color4.White
				}
			}
		};
	}

	public void SetEmpty()
	{
		_hasMap = false;
		_regions = Array.Empty<DifficultySplitterTimelineRegion>();
		_markers = Array.Empty<DifficultySplitterTimelineMarker>();
		_playheadContainer.Alpha = 0;
		_viewportBox.Alpha = 0;
		_regionsContainer.Clear();
		_markersContainer.Clear();
	}

	public void SetMapState(
		double mapMinMs,
		double mapMaxMs,
		IReadOnlyList<DifficultySplitterTimelineRegion> regions,
		IReadOnlyList<DifficultySplitterTimelineMarker> markers)
	{
		_hasMap = mapMaxMs > mapMinMs;
		_mapMinMs = mapMinMs;
		_mapMaxMs = mapMaxMs;
		_regions = regions;
		_markers = markers;
		_playheadContainer.Alpha = _hasMap ? 1 : 0;
		_viewportBox.Alpha = _hasMap ? 1 : 0;
		RedrawRegionsAndMarkers();
		UpdatePlayheadAndViewport();
	}

	public void SetViewState(double viewCenterMs, double visibleStartMs, double visibleEndMs)
	{
		_viewCenterMs = viewCenterMs;
		_visibleStartMs = visibleStartMs;
		_visibleEndMs = visibleEndMs;
		UpdatePlayheadAndViewport();
	}

	protected override void Update()
	{
		base.Update();

		if (_hasMap && _trackContainer.DrawWidth > 0)
			UpdatePlayheadAndViewport();
	}

	private void RedrawRegionsAndMarkers()
	{
		_regionsContainer.Clear();
		_markersContainer.Clear();

		if (!_hasMap)
			return;

		foreach (var region in _regions)
		{
			var x = TimeToX(region.StartMs);
			var width = Math.Max(2f, TimeToX(region.EndMs) - x);
			_regionsContainer.Add(new TimelineRegionBand(region.PairId, region.Color, region.IsSelected, id => PairSelected?.Invoke(id))
			{
				Position = new Vector2(x, 0),
				Size = new Vector2(width, _trackHeight)
			});
		}

		foreach (var marker in _markers)
		{
			var x = TimeToX(marker.TimeMs);
			_markersContainer.Add(new TimelineMarkerStub(marker.MarkerId, marker.Color, marker.IsSelected, id => MarkerSelected?.Invoke(id))
			{
				Position = new Vector2(x - (marker.IsSelected ? 1f : 0.5f), marker.IsSelected ? -1 : 0),
				Size = new Vector2(marker.IsSelected ? 2 : 1, marker.IsSelected ? _trackHeight + 2 : _trackHeight)
			});
		}
	}

	private void UpdatePlayheadAndViewport()
	{
		if (!_hasMap || _trackContainer.DrawWidth <= 0)
			return;

		var trackX = _trackContainer.DrawPosition.X;
		var playheadX = trackX + TimeToX(_viewCenterMs);
		_playheadContainer.X = playheadX - _playheadWidth * 0.5f;

		var viewportStart = TimeToX(_visibleStartMs);
		var viewportEnd = TimeToX(_visibleEndMs);
		_viewportBox.Position = new Vector2(viewportStart, 0);
		_viewportBox.Width = Math.Max(2f, viewportEnd - viewportStart);
	}

	private float TimeToX(double timeMs)
	{
		var trackWidth = _trackContainer.DrawWidth;
		if (trackWidth <= 0 || _mapMaxMs <= _mapMinMs)
			return 0;

		var t = (timeMs - _mapMinMs) / (_mapMaxMs - _mapMinMs);
		return (float)Math.Clamp(t, 0, 1) * trackWidth;
	}

	private double XToTime(float localX)
	{
		var trackWidth = _trackContainer.DrawWidth;
		if (trackWidth <= 0 || _mapMaxMs <= _mapMinMs)
			return _mapMinMs;

		var t = Math.Clamp(localX / trackWidth, 0, 1);
		return _mapMinMs + t * (_mapMaxMs - _mapMinMs);
	}

	private bool TryHitRegion(float localX, out Guid pairId)
	{
		foreach (var region in _regions)
		{
			var x0 = TimeToX(region.StartMs);
			var x1 = TimeToX(region.EndMs);
			if (localX >= x0 && localX <= x1)
			{
				pairId = region.PairId;
				return true;
			}
		}

		pairId = Guid.Empty;
		return false;
	}

	private bool TryHitMarker(float localX, out Guid markerId)
	{
		const float hitSlop = 5f;
		Guid? closest = null;
		var closestDist = float.MaxValue;

		foreach (var marker in _markers)
		{
			var x = TimeToX(marker.TimeMs);
			var dist = Math.Abs(localX - x);
			if (dist <= hitSlop && dist < closestDist)
			{
				closestDist = dist;
				closest = marker.MarkerId;
			}
		}

		if (closest.HasValue)
		{
			markerId = closest.Value;
			return true;
		}

		markerId = Guid.Empty;
		return false;
	}

	private void SeekToMouse(Vector2 screenSpaceMousePosition)
	{
		if (!_hasMap)
			return;

		var local = ToLocalSpace(screenSpaceMousePosition);
		var trackLocalX = local.X - _trackContainer.DrawPosition.X;
		SeekRequested?.Invoke(XToTime(trackLocalX));
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button != MouseButton.Left || !_hasMap)
			return base.OnMouseDown(e);

		return true;
	}

	protected override bool OnDragStart(DragStartEvent e)
	{
		if (e.Button != MouseButton.Left || !_hasMap)
			return base.OnDragStart(e);

		SeekToMouse(e.ScreenSpaceMousePosition);
		return true;
	}

	protected override void OnDrag(DragEvent e)
	{
		if (_hasMap)
			SeekToMouse(e.ScreenSpaceMousePosition);

		base.OnDrag(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (e.Button != MouseButton.Left || !_hasMap)
			return base.OnClick(e);

		var local = ToLocalSpace(e.ScreenSpaceMousePosition);
		var trackLocalX = local.X - _trackContainer.DrawPosition.X;

		if (TryHitMarker(trackLocalX, out var markerId))
		{
			MarkerSelected?.Invoke(markerId);
			return true;
		}

		if (TryHitRegion(trackLocalX, out var pairId))
		{
			PairSelected?.Invoke(pairId);
			return true;
		}

		SeekToMouse(e.ScreenSpaceMousePosition);
		return true;
	}

	private partial class TimelineRegionBand : CompositeDrawable
	{
		private readonly Guid _pairId;
		private readonly Action<Guid> _onSelected;

		public TimelineRegionBand(Guid pairId, Color4 color, bool selected, Action<Guid> onSelected)
		{
			_pairId = pairId;
			_onSelected = onSelected;

			InternalChild = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(color.R, color.G, color.B, (byte)(selected ? 130 : 75))
			};
		}

		protected override bool OnClick(ClickEvent e)
		{
			if (e.Button != MouseButton.Left)
				return false;

			_onSelected(_pairId);
			return true;
		}
	}

	private partial class TimelineMarkerStub : CompositeDrawable
	{
		private readonly Guid _markerId;
		private readonly Action<Guid> _onSelected;

		public TimelineMarkerStub(Guid markerId, Color4 color, bool selected, Action<Guid> onSelected)
		{
			_markerId = markerId;
			_onSelected = onSelected;

			InternalChild = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = selected ? Color4.White : color
			};
		}

		protected override bool OnClick(ClickEvent e)
		{
			if (e.Button != MouseButton.Left)
				return false;

			_onSelected(_markerId);
			return true;
		}
	}
}
