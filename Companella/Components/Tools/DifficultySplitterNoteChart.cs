using Companella.Models.Application;
using Companella.Models.Beatmap;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Tools;

public readonly record struct DifficultySplitterChartContext(
	double TimeMs,
	Guid? PairId,
	Guid? MarkerId);

/// <summary>
/// Scrollable note preview for difficulty splitting. Scroll up to move forward in time.
/// </summary>
public partial class DifficultySplitterNoteChart : CompositeDrawable
{
	private const float _timeGutterWidth = 52f;
	private const float _minColumnWidth = 18f;
	private const float _defaultColumnWidth = 28f;
	private const float _defaultPxPerMs = 0.08f;
	private const float _minPxPerMs = 0.02f;
	private const float _maxPxPerMs = 0.5f;
	private const float _noteWidthRatio = 0.72f;
	private const float _circleHeight = 8f;

	private static readonly Color4 _backgroundColor = new(24, 24, 28, 255);
	private static readonly Color4 _gridColor = new(45, 45, 52, 255);

	private static readonly Color4[] _columnColors =
	{
		new(255, 100, 100, 255),
		new(100, 200, 255, 255),
		new(255, 220, 100, 255),
		new(100, 255, 150, 255),
		new(200, 150, 255, 255),
		new(255, 180, 100, 255),
		new(150, 220, 255, 255),
		new(255, 150, 200, 255),
		new(180, 255, 180, 255),
		new(220, 180, 255, 255)
	};

	private Container _plotArea = null!;
	private Container _notesContainer = null!;
	private Container _markersContainer = null!;
	private Box _viewCenterLine = null!;
	private Container _pairBandsContainer = null!;
	private Container _previewContainer = null!;
	private Container _horizGridContainer = null!;
	private Container _columnGridContainer = null!;
	private BasicScrollContainer _columnScrollContainer = null!;
	private Container _columnContentContainer = null!;
	private Container _timeLabelsContainer = null!;
	private SpriteText _emptyText = null!;

	private List<HitObject> _hitObjects = new();
	private List<DifficultyRegionMarker> _markers = new();
	private List<DifficultyRegionPair> _pairs = new();
	private Dictionary<Guid, DifficultyRegionMarker> _markerLookup = new();
	private IReadOnlyDictionary<Guid, DifficultyRegionAnalysisResult> _analysisCache =
		new Dictionary<Guid, DifficultyRegionAnalysisResult>();
	private IReadOnlyDictionary<string, int> _regionColorIndices =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	private string? _pendingRegionName;
	private double? _pendingStartTimeMs;
	private int _pendingColorIndex;
	private double? _previewEndTimeMs;

	private int _keyCount = 4;
	private double _viewCenterMs;
	private float _pxPerMs = _defaultPxPerMs;
	private float _columnWidth = _defaultColumnWidth;
	private float _columnPanOffset;
	private Guid? _selectedMarkerId;
	private Guid? _selectedPairId;
	private Guid? _draggingMarkerId;
	private bool _isPanning;
	private bool _suppressNextClick;
	private Vector2 _panStartPosition;
	private double _panStartViewCenterMs;
	private float _panStartColumnOffset;
	private double _panStartScrollCurrent;

	private const float _columnLeftPadding = 10f;
	private const float _noteInset = 1f;
	private const float _dragClickThreshold = 5f;
	private const int _horizontalScrollKeyThreshold = 4;

	public double ViewCenterMs => _viewCenterMs;

	/// <summary>
	/// Chart-local top-left position for an inline edit aligned with a marker label at the given time.
	/// </summary>
	public Vector2 GetInlineEditPositionForTime(double timeMs)
	{
		var y = TimeToScreenY(timeMs);
		return new Vector2(_timeGutterWidth + 6, _plotArea.DrawPosition.Y + y - 20);
	}

	public event Action<double>? MarkerAddedAtTime;
	public event Action<Guid, double>? MarkerMoved;
	public event Action<Guid>? MarkerSelected;
	public event Action<Guid>? PairSelected;
	public event Action<DifficultySplitterChartContext>? ContextMenuRequested;
	public event Action? InteractionBegan;

	private float _lastPlotHeight;

	public DifficultySplitterNoteChart()
	{
		RelativeSizeAxes = Axes.Both;
		Masking = true;
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
				Width = _timeGutterWidth,
				RelativeSizeAxes = Axes.Y,
				Colour = new Color4(14, 14, 18, 255)
			},
			_plotArea = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				Padding = new MarginPadding { Left = _timeGutterWidth },
				Children = new Drawable[]
				{
					_pairBandsContainer = new Container { RelativeSizeAxes = Axes.Both },
					_previewContainer = new Container { RelativeSizeAxes = Axes.Both },
					_horizGridContainer = new Container { RelativeSizeAxes = Axes.Both },
					_columnScrollContainer = new ColumnScrollContainer
					{
						RelativeSizeAxes = Axes.Both,
						ScrollbarVisible = false,
						Masking = true,
						Child = _columnContentContainer = new Container
						{
							RelativeSizeAxes = Axes.Y,
							Children = new Drawable[]
							{
								_columnGridContainer = new Container { RelativeSizeAxes = Axes.Both },
								_notesContainer = new Container { RelativeSizeAxes = Axes.Both }
							}
						}
					},
					_markersContainer = new Container { RelativeSizeAxes = Axes.Both }
				}
			},
			_viewCenterLine = new Box
			{
				Height = 2,
				Colour = new Color4(255, 255, 255, 160),
				Alpha = 0
			},
			_timeLabelsContainer = new Container
			{
				Width = _timeGutterWidth,
				RelativeSizeAxes = Axes.Y
			},
			_emptyText = new SpriteText
			{
				Text = "Load a beatmap to preview notes",
				Font = new FontUsage("", 14),
				Colour = new Color4(140, 140, 140, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Alpha = 0
			}
		};
	}

	public void SetBeatmap(OsuFile? osuFile, List<HitObject> hitObjects)
	{
		_hitObjects = hitObjects;
		_keyCount = osuFile != null ? Math.Max(1, (int)osuFile.CircleSize) : 4;
		_emptyText.Alpha = hitObjects.Count == 0 ? 1 : 0;

		if (hitObjects.Count > 0)
		{
			var minTime = hitObjects.Min(h => h.Time);
			var maxTime = hitObjects.Max(h => Math.Max(h.Time, h.EndTime));
			_viewCenterMs = (minTime + maxTime) / 2.0;
		}
		else
		{
			_viewCenterMs = 0;
		}

		_columnPanOffset = 0;
		_columnScrollContainer.ScrollTo(0, true);
		_pxPerMs = _defaultPxPerMs;
		Redraw();
		UpdateViewCenterLine();
	}

	public void SetMarkers(IReadOnlyList<DifficultyRegionMarker> markers)
	{
		_markers = markers.ToList();
		_markerLookup = _markers.ToDictionary(m => m.Id);
		Redraw();
	}

	public void SetPairs(IReadOnlyList<DifficultyRegionPair> pairs, Guid? selectedPairId = null)
	{
		_pairs = pairs.ToList();
		_selectedPairId = selectedPairId;
		Redraw();
	}

	public void SetRegionAnalysis(IReadOnlyDictionary<Guid, DifficultyRegionAnalysisResult> analysisCache)
	{
		_analysisCache = analysisCache;
		Redraw();
	}

	public void SetSelectedMarkerId(Guid? markerId)
	{
		_selectedMarkerId = markerId;
		Redraw();
	}

	public void SetRegionColorIndices(IReadOnlyDictionary<string, int> colorIndices)
	{
		_regionColorIndices = colorIndices;
		Redraw();
	}

	public void SetPendingPlacement(string? regionName, double? startTimeMs, int colorIndex)
	{
		_pendingRegionName = regionName;
		_pendingStartTimeMs = startTimeMs;
		_pendingColorIndex = colorIndex;
		_previewEndTimeMs = startTimeMs;
		UpdatePreviewBand();
	}

	public void ResetView()
	{
		if (_hitObjects.Count == 0)
			return;

		var minTime = _hitObjects.Min(h => h.Time);
		var maxTime = _hitObjects.Max(h => Math.Max(h.Time, h.EndTime));
		_viewCenterMs = (minTime + maxTime) / 2.0;
		_pxPerMs = _defaultPxPerMs;
		_columnPanOffset = 0;
		_columnScrollContainer.ScrollTo(0, true);
		Redraw();
	}

	public void CenterOnTime(double timeMs)
	{
		_viewCenterMs = timeMs;
		Redraw();
	}

	public void ZoomToRegion(double startMs, double endMs)
	{
		if (endMs <= startMs)
			return;

		_viewCenterMs = (startMs + endMs) / 2.0;
		var plotHeight = _plotArea.DrawHeight;
		if (plotHeight > 0)
		{
			var rangeMs = (float)(endMs - startMs);
			_pxPerMs = Math.Clamp(plotHeight / rangeMs * 0.85f, _minPxPerMs, _maxPxPerMs);
		}

		Redraw();
	}

	public void PanTime(double deltaMs)
	{
		_viewCenterMs += deltaMs;
		Redraw();
	}

	public void ZoomTime(float factor)
	{
		_pxPerMs = Math.Clamp(_pxPerMs * factor, _minPxPerMs, _maxPxPerMs);
		Redraw();
	}

	public double TimeAtScreenY(float screenY) => ScreenYToTime(screenY);

	public (double StartMs, double EndMs) GetVisibleTimeRange()
	{
		var plotHeight = _plotArea.DrawHeight;
		if (plotHeight <= 0)
			return (_viewCenterMs, _viewCenterMs);

		var visibleMs = plotHeight / _pxPerMs;
		return (_viewCenterMs - visibleMs / 2.0, _viewCenterMs + visibleMs / 2.0);
	}

	protected override bool OnScroll(ScrollEvent e)
	{
		if (!Contains(e.ScreenSpaceMousePosition))
			return base.OnScroll(e);

		BeginInteraction();

		var delta = e.ScrollDelta.Y;
		if (delta == 0 && Math.Abs(e.ScrollDelta.X) < float.Epsilon)
			return true;

		if (e.ControlPressed)
		{
			var mouseTime = ScreenYToTime(e.MousePosition.Y);
			var oldPxPerMs = _pxPerMs;
			var factor = delta > 0 ? 1.1f : 0.9f;
			_pxPerMs = Math.Clamp(_pxPerMs * factor, _minPxPerMs, _maxPxPerMs);

			var plotHeight = _plotArea.DrawHeight;
			var centerY = plotHeight / 2f;
			var mouseOffsetFromCenter = e.MousePosition.Y - _plotArea.DrawPosition.Y - centerY;
			_viewCenterMs += mouseOffsetFromCenter * (1f / oldPxPerMs - 1f / _pxPerMs);
		}
		else if (TryScrollColumns(e))
		{
			// Horizontal column scroll handled above.
		}
		else if (delta != 0)
		{
			// Scroll up increases time
			_viewCenterMs += delta * 50;
		}

		Redraw();
		return true;
	}

	private bool TryScrollColumns(ScrollEvent e)
	{
		if (!UsesHorizontalColumnScroll())
			return false;

		var horizontalDelta = Math.Abs(e.ScrollDelta.X) > float.Epsilon
			? e.ScrollDelta.X
			: e.ShiftPressed ? e.ScrollDelta.Y : 0;

		if (Math.Abs(horizontalDelta) < float.Epsilon)
			return false;

		_columnScrollContainer.ScrollTo((float)(_columnScrollContainer.Current - horizontalDelta * 12));
		return true;
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (_suppressNextClick)
		{
			_suppressNextClick = false;
			return true;
		}

		if (e.Button != MouseButton.Left)
			return base.OnClick(e);

		BeginInteraction();
		return TryHandlePlotAreaClick(e.MousePosition) || base.OnClick(e);
	}

	protected override bool OnDoubleClick(DoubleClickEvent e)
	{
		if (_suppressNextClick)
		{
			_suppressNextClick = false;
			return true;
		}

		if (e.Button != MouseButton.Left)
			return base.OnDoubleClick(e);

		BeginInteraction();
		return TryHandlePlotAreaClick(e.MousePosition) || base.OnDoubleClick(e);
	}

	private bool TryHandlePlotAreaClick(Vector2 mousePosition)
	{
		if (!IsInPlotArea(mousePosition))
			return false;

		var markerHit = HitTestMarker(mousePosition);
		if (markerHit.HasValue)
		{
			_selectedMarkerId = markerHit;
			MarkerSelected?.Invoke(markerHit.Value);
			Redraw();
			return true;
		}

		var pairHit = HitTestPair(mousePosition);
		if (pairHit.HasValue)
		{
			PairSelected?.Invoke(pairHit.Value);
			return true;
		}

		var timeMs = ScreenYToTime(mousePosition.Y);
		MarkerAddedAtTime?.Invoke(timeMs);
		return true;
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button != MouseButton.Right)
			return base.OnMouseDown(e);

		if (!IsInPlotArea(e.MousePosition))
			return base.OnMouseDown(e);

		BeginInteraction();

		var markerHit = HitTestMarker(e.MousePosition);
		var pairHit = HitTestPair(e.MousePosition);
		var timeMs = ScreenYToTime(e.MousePosition.Y);
		ContextMenuRequested?.Invoke(new DifficultySplitterChartContext(timeMs, pairHit, markerHit));
		return true;
	}

	protected override bool OnDragStart(DragStartEvent e)
	{
		if (e.Button != MouseButton.Left && e.Button != MouseButton.Right && e.Button != MouseButton.Middle)
			return base.OnDragStart(e);

		if (!Contains(e.ScreenSpaceMousePosition))
			return base.OnDragStart(e);

		BeginInteraction();

		if (e.Button == MouseButton.Left && IsInPlotArea(e.MousePosition))
		{
			var markerHit = HitTestMarker(e.MousePosition);
			if (markerHit.HasValue)
			{
				_draggingMarkerId = markerHit;
				_selectedMarkerId = markerHit;
				MarkerSelected?.Invoke(markerHit.Value);
				return true;
			}
		}

		_isPanning = true;
		_panStartPosition = e.MousePosition;
		_panStartViewCenterMs = _viewCenterMs;
		_panStartColumnOffset = _columnPanOffset;
		_panStartScrollCurrent = _columnScrollContainer.Current;
		return true;
	}

	protected override void OnDrag(DragEvent e)
	{
		if (_draggingMarkerId.HasValue)
		{
			var timeMs = ScreenYToTime(e.MousePosition.Y);
			MarkerMoved?.Invoke(_draggingMarkerId.Value, timeMs);
			Redraw();
			return;
		}

		if (!_isPanning)
		{
			base.OnDrag(e);
			return;
		}

		var delta = e.MousePosition - _panStartPosition;
		if (delta.Length > _dragClickThreshold)
			_suppressNextClick = true;

		_viewCenterMs = _panStartViewCenterMs + delta.Y / _pxPerMs;
		if (UsesHorizontalColumnScroll())
			_columnScrollContainer.ScrollTo((float)(_panStartScrollCurrent - delta.X), true);
		else
		{
			_columnPanOffset = _panStartColumnOffset + delta.X;
			ClampColumnPan();
		}

		Redraw();
	}

	protected override void OnDragEnd(DragEndEvent e)
	{
		_draggingMarkerId = null;
		_isPanning = false;
		base.OnDragEnd(e);
	}

	protected override bool OnMouseMove(MouseMoveEvent e)
	{
		if (_pendingStartTimeMs.HasValue && IsInPlotArea(e.MousePosition))
		{
			_previewEndTimeMs = ScreenYToTime(e.MousePosition.Y);
			UpdatePreviewBand();
		}

		return base.OnMouseMove(e);
	}

	protected override void Update()
	{
		base.Update();

		var plotHeight = _plotArea.DrawHeight;
		if (_hitObjects.Count > 0 && plotHeight > 0 && Math.Abs(_lastPlotHeight - plotHeight) > 1)
		{
			_lastPlotHeight = plotHeight;
			Redraw();
		}

		UpdateViewCenterLine();
	}

	private void Redraw()
	{
		_notesContainer.Clear();
		_markersContainer.Clear();
		_pairBandsContainer.Clear();
		_previewContainer.Clear();
		_horizGridContainer.Clear();
		_columnGridContainer.Clear();
		_timeLabelsContainer.Clear();

		if (_hitObjects.Count == 0)
		{
			UpdateViewCenterLine();
			return;
		}

		UpdateColumnLayout();

		DrawPairBands();
		UpdatePreviewBand();
		DrawGridAndTimeLabels();
		DrawNotes();
		DrawMarkers();
		UpdateViewCenterLine();
	}

	private void UpdateViewCenterLine()
	{
		if (_hitObjects.Count == 0 || _plotArea.DrawHeight <= 0)
		{
			_viewCenterLine.Alpha = 0;
			return;
		}

		var centerY = _plotArea.DrawPosition.Y + _plotArea.DrawHeight * 0.5f;
		_viewCenterLine.X = _timeGutterWidth;
		_viewCenterLine.Y = centerY - _viewCenterLine.Height * 0.5f;
		_viewCenterLine.Width = Math.Max(1f, DrawWidth - _timeGutterWidth);
		_viewCenterLine.Alpha = 1;
	}

	private void DrawPairBands()
	{
		var anySelected = _selectedPairId.HasValue;

		foreach (var pair in _pairs)
		{
			if (!_markerLookup.TryGetValue(pair.StartMarkerId, out var start))
				continue;

			if (!_markerLookup.TryGetValue(pair.EndMarkerId, out var end))
				continue;

			var startMs = Math.Min(start.TimeMs, end.TimeMs);
			var endMs = Math.Max(start.TimeMs, end.TimeMs);
			if (startMs >= endMs)
				continue;

			var yTop = TimeToScreenY(endMs);
			var yBottom = TimeToScreenY(startMs);
			var top = Math.Min(yTop, yBottom);
			var height = Math.Abs(yBottom - yTop);

			if (height <= 0)
				continue;

			var isSelected = _selectedPairId == pair.Id;
			var accent = GetRegionAccent(pair.VersionName);
			var fill = DifficultySplitterRegionColors.GetBandFill(accent, isSelected, anySelected);
			_analysisCache.TryGetValue(pair.Id, out var analysis);
			var tooltip = DifficultyRegionAnalysisTooltipFormatter.Format(pair, analysis);

			_pairBandsContainer.Add(new RegionPairBand(
				pair.Id,
				fill,
				tooltip,
				id => PairSelected?.Invoke(id))
			{
				Position = new Vector2(0, top),
				Size = new Vector2(_plotArea.DrawWidth, height)
			});
		}
	}

	private void UpdatePreviewBand()
	{
		_previewContainer.Clear();

		if (!_pendingStartTimeMs.HasValue || !_previewEndTimeMs.HasValue)
			return;

		var startMs = Math.Min(_pendingStartTimeMs.Value, _previewEndTimeMs.Value);
		var endMs = Math.Max(_pendingStartTimeMs.Value, _previewEndTimeMs.Value);
		var yTop = TimeToScreenY(endMs);
		var yBottom = TimeToScreenY(startMs);
		var top = Math.Min(yTop, yBottom);
		var height = Math.Max(2f, Math.Abs(yBottom - yTop));
		var accent = DifficultySplitterRegionColors.GetAccent(_pendingColorIndex);
		var fill = DifficultySplitterRegionColors.GetPreviewFill(accent);

		_previewContainer.Add(new Container
		{
			Position = new Vector2(0, top),
			Size = new Vector2(_plotArea.DrawWidth, height),
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = fill
				},
				new Box
				{
					RelativeSizeAxes = Axes.X,
					Height = 2,
					Colour = accent
				},
				new Box
				{
					RelativeSizeAxes = Axes.X,
					Height = 2,
					Anchor = Anchor.BottomLeft,
					Origin = Anchor.BottomLeft,
					Colour = accent
				},
				new SpriteText
				{
					Text = _pendingRegionName ?? string.Empty,
					Font = new FontUsage("", 10, "Bold"),
					Colour = accent,
					Position = new Vector2(6, 4),
					Alpha = _pendingRegionName != null ? 1 : 0
				}
			}
		});
	}

	private partial class RegionPairBand : CompositeDrawable, IHasTooltip
	{
		public LocalisableString TooltipText { get; }

		private readonly Guid _pairId;
		private readonly Action<Guid> _onSelected;

		public RegionPairBand(
			Guid pairId,
			Color4 fillColour,
			string tooltipText,
			Action<Guid> onSelected)
		{
			_pairId = pairId;
			_onSelected = onSelected;
			TooltipText = tooltipText;

			InternalChild = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = fillColour
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

	private void DrawGridAndTimeLabels()
	{
		var plotHeight = _plotArea.DrawHeight;
		if (plotHeight <= 0)
			return;

		var visibleMs = plotHeight / _pxPerMs;
		var startTime = _viewCenterMs - visibleMs / 2.0;
		var endTime = _viewCenterMs + visibleMs / 2.0;

		var tickStep = visibleMs > 60000 ? 10000.0 : visibleMs > 20000 ? 5000.0 : visibleMs > 5000 ? 1000.0 : 500.0;
		var firstTick = Math.Ceiling(startTime / tickStep) * tickStep;

		for (var t = firstTick; t <= endTime; t += tickStep)
		{
			var y = TimeToScreenY(t);
			if (y < -2 || y > plotHeight + 2)
				continue;

			_horizGridContainer.Add(new Box
			{
				Position = new Vector2(0, y),
				RelativeSizeAxes = Axes.X,
				Height = 1,
				Colour = _gridColor
			});

			_timeLabelsContainer.Add(new SpriteText
			{
				Text = FormatTime(t),
				Font = new FontUsage("", 11),
				Colour = new Color4(130, 130, 130, 255),
				Position = new Vector2(4, y - 7)
			});
		}

		var startX = GetColumnAreaStartX();
		for (var col = 0; col <= _keyCount; col++)
		{
			var x = startX + col * _columnWidth;
			_columnGridContainer.Add(new Box
			{
				Position = new Vector2(x, 0),
				RelativeSizeAxes = Axes.Y,
				Width = 1,
				Colour = _gridColor
			});
		}
	}

	private void UpdateColumnLayout()
	{
		var plotWidth = _plotArea.DrawWidth;
		var plotHeight = _plotArea.DrawHeight;
		var horizontalScroll = UsesHorizontalColumnScroll();

		_columnWidth = horizontalScroll
			? _defaultColumnWidth
			: Math.Max(_minColumnWidth, Math.Min(_defaultColumnWidth, (plotWidth - 8) / _keyCount));

		var columnAreaWidth = _keyCount * _columnWidth + _columnLeftPadding * 2;
		_columnContentContainer.Width = horizontalScroll ? columnAreaWidth : plotWidth;
		_columnContentContainer.Height = plotHeight;
		_columnScrollContainer.ScrollbarVisible = horizontalScroll && columnAreaWidth > plotWidth + 1;

		if (!horizontalScroll)
		{
			_columnScrollContainer.ScrollTo(0, true);
			_columnPanOffset = 0;
		}
		else
			ClampColumnScroll();
	}

	private bool UsesHorizontalColumnScroll() => _keyCount > _horizontalScrollKeyThreshold;

	private void ClampColumnScroll()
	{
		if (_columnScrollContainer.ScrollableExtent <= 0)
		{
			_columnScrollContainer.ScrollTo(0, true);
			return;
		}

		_columnScrollContainer.ScrollTo(
			Math.Clamp(_columnScrollContainer.Current, 0, _columnScrollContainer.ScrollableExtent),
			true);
	}

	private void DrawNotes()
	{
		var plotHeight = _plotArea.DrawHeight;
		var visibleMs = plotHeight / _pxPerMs;
		var startTime = _viewCenterMs - visibleMs / 2.0 - 500;
		var endTime = _viewCenterMs + visibleMs / 2.0 + 500;
		var noteWidth = _columnWidth * _noteWidthRatio;

		foreach (var note in _hitObjects)
		{
			if (note.Time > endTime && note.EndTime > endTime)
				continue;

			if (note.Time < startTime && note.EndTime < startTime)
				continue;

			var color = _columnColors[note.Column % _columnColors.Length];
			var x = ColumnToScreenX(note.Column) + _noteInset;
			var headY = TimeToScreenY(note.Time);

			if (note.IsHold)
			{
				var tailY = TimeToScreenY(note.EndTime);
				var top = Math.Min(headY, tailY);
				var height = Math.Max(2f, Math.Abs(tailY - headY));

				_notesContainer.Add(new Box
				{
					Position = new Vector2(x, top),
					Size = new Vector2(noteWidth, height),
					Colour = color,
					Alpha = 0.85f
				});
			}
			else
			{
				_notesContainer.Add(new Box
				{
					Position = new Vector2(x, headY - _circleHeight / 2f),
					Size = new Vector2(noteWidth, _circleHeight),
					Colour = color
				});
			}
		}
	}

	private void DrawMarkers()
	{
		var roleLookup = new Dictionary<Guid, string>();
		foreach (var group in _markers.GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase))
		{
			var ordered = group.OrderBy(m => m.TimeMs).ToList();
			if (ordered.Count != 2)
				continue;

			roleLookup[ordered[0].Id] = " start";
			roleLookup[ordered[1].Id] = " end";
		}

		foreach (var marker in _markers)
		{
			var y = TimeToScreenY(marker.TimeMs);
			var isSelected = _selectedMarkerId == marker.Id;
			var isPairSelected = _selectedPairId.HasValue &&
								 _pairs.Any(p => p.Id == _selectedPairId &&
												 (p.StartMarkerId == marker.Id || p.EndMarkerId == marker.Id));
			var showLabel = isSelected || isPairSelected;
			var accent = GetRegionAccent(marker.Name);
			var color = DifficultySplitterRegionColors.GetMarkerColor(accent, isSelected);
			var label = showLabel
				? marker.Name + roleLookup.GetValueOrDefault(marker.Id, string.Empty)
				: string.Empty;

			_markersContainer.Add(new Container
			{
				Position = new Vector2(0, y - 1),
				RelativeSizeAxes = Axes.X,
				Height = isSelected ? 3 : 2,
				Children = new Drawable[]
				{
					new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = color
					},
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("", 10, isSelected ? "Bold" : ""),
						Colour = color,
						Position = new Vector2(4, -12),
						Alpha = showLabel ? 1 : 0
					}
				}
			});
		}
	}

	private float TimeToScreenY(double timeMs)
	{
		var plotHeight = _plotArea.DrawHeight;
		var centerY = plotHeight / 2f;
		return (float)(centerY - (timeMs - _viewCenterMs) * _pxPerMs);
	}

	private double ScreenYToTime(float screenY)
	{
		var plotHeight = _plotArea.DrawHeight;
		var localY = screenY - _plotArea.DrawPosition.Y;
		var centerY = plotHeight / 2f;
		return _viewCenterMs + (centerY - localY) / _pxPerMs;
	}

	private float ColumnToScreenX(int column)
	{
		return GetColumnAreaStartX() + column * _columnWidth;
	}

	private float GetColumnAreaStartX() =>
		UsesHorizontalColumnScroll() ? _columnLeftPadding : _columnLeftPadding + _columnPanOffset;

	private void ClampColumnPan()
	{
		if (UsesHorizontalColumnScroll())
		{
			ClampColumnScroll();
			return;
		}

		var totalWidth = _keyCount * _columnWidth;
		var plotWidth = _plotArea.DrawWidth;
		if (totalWidth + _columnLeftPadding <= plotWidth)
		{
			_columnPanOffset = 0;
			return;
		}

		var minOffset = plotWidth - totalWidth - _columnLeftPadding - 8;
		var maxOffset = 0;
		_columnPanOffset = Math.Clamp(_columnPanOffset, minOffset, maxOffset);
	}

	private bool IsInPlotArea(Vector2 screenPos)
	{
		var rect = _plotArea.ScreenSpaceDrawQuad.AABB;
		return rect.Contains(screenPos);
	}

	private Guid? HitTestMarker(Vector2 screenPos)
	{
		const float hitSlop = 6f;
		var localY = screenPos.Y - _plotArea.DrawPosition.Y;

		Guid? closest = null;
		var closestDist = float.MaxValue;

		foreach (var marker in _markers)
		{
			var y = TimeToScreenY(marker.TimeMs);
			var dist = Math.Abs(localY - y);
			if (dist <= hitSlop && dist < closestDist)
			{
				closestDist = dist;
				closest = marker.Id;
			}
		}

		return closest;
	}

	private Guid? HitTestPair(Vector2 screenPos)
	{
		if (!IsInPlotArea(screenPos))
			return null;

		var localY = screenPos.Y - _plotArea.DrawPosition.Y;

		foreach (var pair in _pairs)
		{
			if (!_markerLookup.TryGetValue(pair.StartMarkerId, out var start))
				continue;

			if (!_markerLookup.TryGetValue(pair.EndMarkerId, out var end))
				continue;

			var startMs = Math.Min(start.TimeMs, end.TimeMs);
			var endMs = Math.Max(start.TimeMs, end.TimeMs);
			if (startMs >= endMs)
				continue;

			var yTop = TimeToScreenY(endMs);
			var yBottom = TimeToScreenY(startMs);
			var top = Math.Min(yTop, yBottom);
			var bottom = Math.Max(yTop, yBottom);

			if (localY >= top && localY <= bottom)
				return pair.Id;
		}

		return null;
	}

	private static string FormatTime(double ms)
	{
		var totalSeconds = (int)(ms / 1000);
		var minutes = totalSeconds / 60;
		var seconds = totalSeconds % 60;
		return $"{minutes}:{seconds:D2}";
	}

	private Color4 GetRegionAccent(string regionName)
	{
		if (_regionColorIndices.TryGetValue(regionName.Trim(), out var index))
			return DifficultySplitterRegionColors.GetAccent(index);

		return DifficultySplitterRegionColors.GetAccent(0);
	}

	private void BeginInteraction() => InteractionBegan?.Invoke();

	/// <summary>
	/// Horizontal scroll container that ignores wheel input; the chart handles column pan via drag/shift-scroll.
	/// </summary>
	private partial class ColumnScrollContainer : BasicScrollContainer
	{
		public ColumnScrollContainer()
			: base(Direction.Horizontal)
		{
		}

		protected override bool OnScroll(ScrollEvent e) => false;
	}
}
