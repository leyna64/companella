using Companella.Models.Session;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// Interactive graph for editing MSD curves in session planning.
/// Displays time (0-100%) on X-axis and absolute MSD (0-50) on Y-axis.
/// </summary>
public partial class MsdCurveGraph : CompositeDrawable
{
	private const float _chartPaddingLeft = 45f;
	private const float _chartPaddingRight = 15f;
	private const float _chartPaddingTop = 15f;
	private const float _chartPaddingBottom = 25f;
	private const float _pointRadius = 6f;
	private const float _lineThickness = 2f;

	// Y-axis range for absolute MSD values
	private const float _msdMin = 0f;
	private const float _msdMax = 50f;

	private readonly Color4 _backgroundColor = new(30, 30, 35, 255);
	private readonly Color4 _gridColor = new(50, 50, 55, 255);
	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _lineColor = new(255, 102, 170, 200);
	private readonly Color4 _pointColor = new(255, 102, 170, 255);
	private readonly Color4 _pointHoverColor = new(255, 150, 200, 255);
	private readonly Color4 _labelColor = new(150, 150, 150, 255);
	private readonly Color4 _zeroLineColor = new(100, 100, 100, 255);

	// Skillset colors for points
	private static readonly Dictionary<string, Color4> _skillsetColors = new()
	{
		{ "stream", new Color4(100, 180, 255, 255) },
		{ "jumpstream", new Color4(100, 220, 100, 255) },
		{ "handstream", new Color4(255, 180, 100, 255) },
		{ "stamina", new Color4(180, 100, 255, 255) },
		{ "jackspeed", new Color4(255, 100, 100, 255) },
		{ "chordjack", new Color4(255, 220, 100, 255) },
		{ "technical", new Color4(100, 220, 220, 255) }
	};

	private static readonly string[] _availableSkillsets = new[]
	{
		null!, "stream", "jumpstream", "handstream", "stamina", "jackspeed", "chordjack", "technical"
	};

	private Container _chartArea = null!;
	private Container _gridContainer = null!;
	private Container _linesContainer = null!;
	private Container _pointsContainer = null!;
	private Container _labelsContainer = null!;
	private SpriteText _tooltipText = null!;

	private MsdCurveConfig _config = new();
	private MsdCurvePointDrawable? _draggingPoint;
	private bool _isDragging;

	/// <summary>
	/// Event raised when the curve is modified.
	/// </summary>
	public event Action? CurveChanged;

	/// <summary>
	/// The current curve configuration.
	/// </summary>
	public MsdCurveConfig Config
	{
		get => _config;
		set
		{
			_config = value;
			Redraw();
		}
	}

	public MsdCurveGraph()
	{
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			// Background
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _backgroundColor
			},
			// Labels container (outside chart area for axis labels)
			_labelsContainer = new Container
			{
				RelativeSizeAxes = Axes.Both
			},
			// Chart area container
			_chartArea = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding
				{
					Left = _chartPaddingLeft,
					Right = _chartPaddingRight,
					Top = _chartPaddingTop,
					Bottom = _chartPaddingBottom
				},
				Children = new Drawable[]
				{
					// Grid lines
					_gridContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					},
					// Curve lines
					_linesContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					},
					// Control points
					_pointsContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					}
				}
			},
			// Tooltip
			_tooltipText = new SpriteText
			{
				Font = new FontUsage("", 12),
				Colour = Color4.White,
				Alpha = 0,
				Depth = -100
			}
		};

		Redraw();
	}

	/// <summary>
	/// Redraws the entire graph.
	/// </summary>
	public void Redraw()
	{
		if (_gridContainer == null)
			return;

		_gridContainer.Clear();
		_linesContainer.Clear();
		_pointsContainer.Clear();
		_labelsContainer.Clear();

		DrawGrid();
		DrawAxisLabels();
		DrawCurve();
		DrawPoints();
	}

	/// <summary>
	/// Draws grid lines on the chart.
	/// </summary>
	private void DrawGrid()
	{
		// Horizontal grid lines (MSD values: 0, 12.5, 25, 37.5, 50)
		for (var i = 0; i <= 4; i++)
		{
			var y = i / 4f;

			_gridContainer.Add(new Box
			{
				RelativeSizeAxes = Axes.X,
				Height = 1,
				RelativePositionAxes = Axes.Y,
				Y = y,
				Colour = _gridColor
			});
		}

		// Vertical grid lines (time percentages at 25% intervals)
		for (var i = 0; i <= 4; i++)
		{
			var x = i / 4f;
			_gridContainer.Add(new Box
			{
				RelativeSizeAxes = Axes.Y,
				Width = 1,
				RelativePositionAxes = Axes.X,
				X = x,
				Colour = _gridColor
			});
		}
	}

	/// <summary>
	/// Draws axis labels.
	/// </summary>
	private void DrawAxisLabels()
	{
		var chartHeight = DrawHeight - _chartPaddingTop - _chartPaddingBottom;
		var chartWidth = DrawWidth - _chartPaddingLeft - _chartPaddingRight;

		// MSD axis labels (left side) - show absolute MSD values (50, 37.5, 25, 12.5, 0)
		for (var i = 0; i <= 4; i++)
		{
			var msd = _msdMax - (_msdMax - _msdMin) * (i / 4f);
			var y = _chartPaddingTop + chartHeight * (i / 4f);

			_labelsContainer.Add(new SpriteText
			{
				Text = $"{msd:F0}",
				Font = new FontUsage("", 12),
				Colour = _labelColor,
				Position = new Vector2(5, y),
				Origin = Anchor.CentreLeft
			});
		}

		// Time axis labels (bottom) - show percentages
		for (var i = 0; i <= 4; i++)
		{
			var timePercent = i * 25;
			var x = _chartPaddingLeft + chartWidth * (i / 4f);

			_labelsContainer.Add(new SpriteText
			{
				Text = $"{timePercent}%",
				Font = new FontUsage("", 11),
				Colour = _labelColor,
				Position = new Vector2(x, DrawHeight - 8),
				Origin = Anchor.TopCentre
			});
		}

		// Axis titles
		_labelsContainer.Add(new SpriteText
		{
			Text = "MSD",
			Font = new FontUsage("", 11, "Bold"),
			Colour = _accentColor,
			Position = new Vector2(5, 3),
			Origin = Anchor.TopLeft
		});

		_labelsContainer.Add(new SpriteText
		{
			Text = "Time",
			Font = new FontUsage("", 11, "Bold"),
			Colour = _accentColor,
			Position = new Vector2(DrawWidth - 5, DrawHeight - 8),
			Origin = Anchor.BottomRight
		});
	}

	/// <summary>
	/// Draws the curve lines connecting control points.
	/// </summary>
	private void DrawCurve()
	{
		var points = _config.Points;
		if (points.Count < 2)
			return;

		for (var i = 0; i < points.Count - 1; i++)
		{
			var fromPoint = points[i];
			var toPoint = points[i + 1];
			var from = PointToRelative(fromPoint);
			var to = PointToRelative(toPoint);
			var fromColor = GetPointColor(fromPoint);
			var toColor = GetPointColor(toPoint);
			DrawLine(from, to, fromColor, toColor);
		}
	}

	/// <summary>
	/// Draws control points.
	/// </summary>
	private void DrawPoints()
	{
		var points = _config.Points;
		for (var i = 0; i < points.Count; i++)
		{
			var point = points[i];
			var isEndpoint = i == 0 || i == points.Count - 1;

			// Color by skillset
			var pointColor = GetPointColor(point);

			var pointDrawable = new MsdCurvePointDrawable(point, isEndpoint)
			{
				RelativePositionAxes = Axes.Both,
				Position = PointToRelative(point),
				Origin = Anchor.Centre,
				Size = new Vector2(_pointRadius * 2),
				Colour = pointColor
			};

			pointDrawable.DragStarted += OnPointDragStarted;
			pointDrawable.Dragged += OnPointDragged;
			pointDrawable.DragEnded += OnPointDragEnded;
			pointDrawable.Hovered += OnPointHovered;
			pointDrawable.HoverLost += OnPointHoverLost;
			pointDrawable.RightClicked += OnPointRightClicked;
			pointDrawable.LeftClicked += OnPointLeftClicked;

			_pointsContainer.Add(pointDrawable);
		}
	}

	/// <summary>
	/// Gets the color for a point based on its skillset.
	/// </summary>
	private Color4 GetPointColor(MsdControlPoint point)
	{
		if (point.Skillset != null && _skillsetColors.TryGetValue(point.Skillset, out var color))
			return color;
		return _pointColor;
	}

	/// <summary>
	/// Draws a line between two relative points with gradient colors.
	/// </summary>
	private void DrawLine(Vector2 from, Vector2 to, Color4 fromColor, Color4 toColor)
	{
		_linesContainer.Add(new GradientCurveLine(from, to, fromColor, toColor, _lineThickness));
	}

	/// <summary>
	/// Converts a control point to relative position (0-1).
	/// </summary>
	private static Vector2 PointToRelative(MsdControlPoint point)
	{
		var x = (float)(point.TimePercent / 100.0);
		var y = 1f - (float)((point.Msd - _msdMin) / (_msdMax - _msdMin));
		return new Vector2(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
	}

	/// <summary>
	/// Converts relative position (0-1) to control point values.
	/// </summary>
	private static (double timePercent, double msd) RelativeToPoint(Vector2 relative)
	{
		var timePercent = relative.X * 100.0;
		var msd = _msdMax - relative.Y * (_msdMax - _msdMin);
		return (timePercent, msd);
	}

	/// <summary>
	/// Converts screen position to relative position within chart area.
	/// </summary>
	private Vector2 ScreenToRelative(Vector2 screenPos)
	{
		var chartBounds = _chartArea.DrawRectangle;
		var localPos = screenPos - chartBounds.TopLeft;
		var x = localPos.X / chartBounds.Width;
		var y = localPos.Y / chartBounds.Height;
		return new Vector2(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
	}

	private void OnPointDragStarted(MsdCurvePointDrawable pointDrawable)
	{
		_draggingPoint = pointDrawable;
		_isDragging = true;
	}

	private void OnPointDragged(MsdCurvePointDrawable pointDrawable, Vector2 delta)
	{
		if (!_isDragging || _draggingPoint != pointDrawable)
			return;

		var currentPos = pointDrawable.Position;
		var newPos = currentPos + new Vector2(delta.X / _chartArea.DrawWidth, delta.Y / _chartArea.DrawHeight);

		// Clamp to valid range
		newPos.X = Math.Clamp(newPos.X, 0, 1);
		newPos.Y = Math.Clamp(newPos.Y, 0, 1);

		// Lock endpoints horizontally
		if (pointDrawable.IsEndpoint)
		{
			var isFirst = _config.IndexOf(pointDrawable.ControlPoint) == 0;
			newPos.X = isFirst ? 0 : 1;
		}

		var (timePercent, msd) = RelativeToPoint(newPos);
		_config.UpdatePoint(pointDrawable.ControlPoint, timePercent, msd);

		// Update visual position
		pointDrawable.Position = PointToRelative(pointDrawable.ControlPoint);

		// Redraw lines
		_linesContainer.Clear();
		DrawCurve();

		UpdateTooltip(pointDrawable);
		CurveChanged?.Invoke();
	}

	private void OnPointDragEnded(MsdCurvePointDrawable pointDrawable)
	{
		_draggingPoint = null;
		_isDragging = false;
		_tooltipText.FadeTo(0, 100);

		// Redraw to ensure proper point ordering after drag
		Redraw();
	}

	private void OnPointHovered(MsdCurvePointDrawable pointDrawable)
	{
		pointDrawable.FadeColour(_pointHoverColor, 100);
		UpdateTooltip(pointDrawable);
		_tooltipText.FadeTo(1, 100);
	}

	private void OnPointHoverLost(MsdCurvePointDrawable pointDrawable)
	{
		if (_draggingPoint != pointDrawable)
		{
			// Restore to the actual skillset color, not default
			pointDrawable.FadeColour(GetPointColor(pointDrawable.ControlPoint), 100);
			_tooltipText.FadeTo(0, 100);
		}
	}

	private void OnPointRightClicked(MsdCurvePointDrawable pointDrawable)
	{
		if (_config.Points.Count <= MsdCurveConfig.MinimumPoints)
			return;

		if (pointDrawable.IsEndpoint)
			return;

		if (_config.RemovePoint(pointDrawable.ControlPoint))
		{
			Redraw();
			CurveChanged?.Invoke();
		}
	}

	private void OnPointLeftClicked(MsdCurvePointDrawable pointDrawable)
	{
		// Cycle through skillsets on left click
		var point = pointDrawable.ControlPoint;
		var currentIndex = Array.IndexOf(_availableSkillsets, point.Skillset);
		var nextIndex = (currentIndex + 1) % _availableSkillsets.Length;
		point.Skillset = _availableSkillsets[nextIndex];

		// Update point color
		pointDrawable.FadeColour(GetPointColor(point), 100);

		// Redraw lines to update gradient colors
		DrawCurve();

		UpdateTooltip(pointDrawable);
		CurveChanged?.Invoke();
	}

	private void UpdateTooltip(MsdCurvePointDrawable pointDrawable)
	{
		var point = pointDrawable.ControlPoint;
		var skillsetText = point.Skillset ?? "any";
		_tooltipText.Text = $"T: {point.TimePercent:F0}% | MSD: {point.Msd:F1} | [{skillsetText}]";

		var chartPos = _chartArea.ToSpaceOfOtherDrawable(
			new Vector2(pointDrawable.Position.X * _chartArea.DrawWidth,
				pointDrawable.Position.Y * _chartArea.DrawHeight),
			this);
		_tooltipText.Position = chartPos + new Vector2(10, -15);
	}

	/// <summary>
	/// Handles right-click on empty space to add a new point on the curve.
	/// </summary>
	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button != osuTK.Input.MouseButton.Right)
			return base.OnMouseDown(e);

		var mousePos = e.MousePosition;

		// Check if click is within chart area
		var chartBounds = _chartArea.DrawRectangle;
		if (!chartBounds.Contains(mousePos))
			return base.OnMouseDown(e);

		var relative = ScreenToRelative(mousePos);
		var (timePercent, msd) = RelativeToPoint(relative);

		// Use the clicked position's MSD (not interpolated) so user can place point where they click
		var newPoint = _config.AddPoint(timePercent, msd);
		if (newPoint != null)
		{
			Redraw();
			CurveChanged?.Invoke();
		}

		return true;
	}

	/// <summary>
	/// Adds a new point at the specified time with interpolated MSD.
	/// </summary>
	public bool AddPointAtTime(double timePercent)
	{
		var msd = _config.GetMsdAtTime(timePercent);
		var newPoint = _config.AddPoint(timePercent, msd);
		if (newPoint != null)
		{
			Redraw();
			CurveChanged?.Invoke();
			return true;
		}

		return false;
	}

	/// <summary>
	/// Removes the last non-endpoint point.
	/// </summary>
	public bool RemoveLastMiddlePoint()
	{
		if (_config.Points.Count <= MsdCurveConfig.MinimumPoints)
			return false;

		// Find a middle point to remove (not first or last)
		for (var i = _config.Points.Count - 2; i >= 1; i--)
			if (_config.RemovePointAt(i))
			{
				Redraw();
				CurveChanged?.Invoke();
				return true;
			}

		return false;
	}

	/// <summary>
	/// Resets the curve to default.
	/// </summary>
	public void ResetToDefault()
	{
		_config.SetDefaultCurve();
		Redraw();
		CurveChanged?.Invoke();
	}
}

/// <summary>
/// Drawable representing a single control point on the MSD curve.
/// </summary>
public partial class MsdCurvePointDrawable : CompositeDrawable
{
	public MsdControlPoint ControlPoint { get; }
	public bool IsEndpoint { get; }

	private Circle _circle = null!;

	public event Action<MsdCurvePointDrawable>? DragStarted;
	public event Action<MsdCurvePointDrawable, Vector2>? Dragged;
	public event Action<MsdCurvePointDrawable>? DragEnded;
	public event Action<MsdCurvePointDrawable>? Hovered;
	public event Action<MsdCurvePointDrawable>? HoverLost;
	public event Action<MsdCurvePointDrawable>? RightClicked;
	public event Action<MsdCurvePointDrawable>? LeftClicked;

	public MsdCurvePointDrawable(MsdControlPoint controlPoint, bool isEndpoint)
	{
		ControlPoint = controlPoint;
		IsEndpoint = isEndpoint;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = _circle = new Circle
		{
			RelativeSizeAxes = Axes.Both,
			Anchor = Anchor.Centre,
			Origin = Anchor.Centre
		};
	}

	protected override bool OnHover(HoverEvent e)
	{
		Hovered?.Invoke(this);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		HoverLost?.Invoke(this);
		base.OnHoverLost(e);
	}

	protected override bool OnDragStart(DragStartEvent e)
	{
		if (e.Button == osuTK.Input.MouseButton.Left)
		{
			DragStarted?.Invoke(this);
			return true;
		}

		return false;
	}

	protected override void OnDrag(DragEvent e)
	{
		Dragged?.Invoke(this, e.Delta);
	}

	protected override void OnDragEnd(DragEndEvent e)
	{
		DragEnded?.Invoke(this);
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button == osuTK.Input.MouseButton.Right)
		{
			RightClicked?.Invoke(this);
			return true;
		}

		return base.OnMouseDown(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (e.Button == osuTK.Input.MouseButton.Left)
		{
			LeftClicked?.Invoke(this);
			return true;
		}

		return base.OnClick(e);
	}
}

/// <summary>
/// Line segment for the curve with gradient color, using relative positioning.
/// </summary>
public partial class GradientCurveLine : CompositeDrawable
{
	private readonly Vector2 _from;
	private readonly Vector2 _to;
	private readonly Color4 _fromColor;
	private readonly Color4 _toColor;
	private readonly float _thickness;
	private const int _segments = 10;

	public GradientCurveLine(Vector2 from, Vector2 to, Color4 fromColor, Color4 toColor, float thickness)
	{
		_from = from;
		_to = to;
		_fromColor = fromColor;
		_toColor = toColor;
		_thickness = thickness;
		RelativeSizeAxes = Axes.Both;
	}

	protected override void Update()
	{
		base.Update();

		ClearInternal();

		var actualFrom = new Vector2(_from.X * DrawWidth, _from.Y * DrawHeight);
		var actualTo = new Vector2(_to.X * DrawWidth, _to.Y * DrawHeight);

		var diff = actualTo - actualFrom;
		var angle = MathF.Atan2(diff.Y, diff.X);
		var segmentLength = diff.Length / _segments;

		// Draw multiple segments with interpolated colors for gradient effect
		for (var i = 0; i < _segments; i++)
		{
			var t = (float)i / _segments;
			var segmentStart = actualFrom + diff * t;
			var color = LerpColor(_fromColor, _toColor, t + 0.5f / _segments);

			AddInternal(new Box
			{
				Width = segmentLength + 1, // +1 to avoid gaps
				Height = _thickness,
				Position = segmentStart,
				Origin = Anchor.CentreLeft,
				Rotation = MathHelper.RadiansToDegrees(angle),
				Colour = color
			});
		}
	}

	private static Color4 LerpColor(Color4 from, Color4 to, float t)
	{
		t = Math.Clamp(t, 0, 1);
		return new Color4(
			from.R + (to.R - from.R) * t,
			from.G + (to.G - from.G) * t,
			from.B + (to.B - from.B) * t,
			from.A + (to.A - from.A) * t
		);
	}
}
