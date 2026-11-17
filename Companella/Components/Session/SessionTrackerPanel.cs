using Companella.Components.Charts;
using Companella.Components.Misc;
using Companella.Models.Session;
using Companella.Services.Session;
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

namespace Companella.Components.Session;

/// <summary>
/// Panel for tracking and visualizing gameplay sessions.
/// Shows a start/stop button, session statistics, and a chart of MSD/accuracy over time.
/// </summary>
public partial class SessionTrackerPanel : CompositeDrawable
{
	[Resolved] private SessionTrackerService TrackerService { get; set; } = null!;

	private StyledButton _toggleButton = null!;
	private SessionChart _sessionChart = null!;
	private SpriteText _statsText = null!;
	private SpriteText _durationText = null!;
	private Container _chartContainer = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	public SessionTrackerPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 10),
				Children = new Drawable[]
				{
					// Header with title
					new SpriteText
					{
						Text = "Session Tracker",
						Font = new FontUsage("", 17, "Bold"),
						Colour = new Color4(180, 180, 180, 255)
					},
					// Controls row
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Horizontal,
						Spacing = new Vector2(12, 0),
						Children = new Drawable[]
						{
							// Start/Stop button
							_toggleButton = new StyledButton("Start Session")
							{
								Size = new Vector2(120, 36),
								TooltipText = "Track your plays and view progress over time"
							},
							// Stats display
							new FillFlowContainer
							{
								AutoSizeAxes = Axes.Both,
								Direction = FillDirection.Vertical,
								Spacing = new Vector2(0, 2),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft,
								Children = new Drawable[]
								{
									_statsText = new SpriteText
									{
										Text = "Plays: 0",
										Font = new FontUsage("", 15),
										Colour = new Color4(160, 160, 160, 255)
									},
									_durationText = new SpriteText
									{
										Text = "Duration: 00:00:00",
										Font = new FontUsage("", 15),
										Colour = new Color4(120, 120, 120, 255)
									}
								}
							}
						}
					},
					// Chart container
					_chartContainer = new Container
					{
						RelativeSizeAxes = Axes.X,
						Height = 200,
						Masking = true,
						CornerRadius = 6,
						Child = _sessionChart = new SessionChart
						{
							RelativeSizeAxes = Axes.Both
						}
					}
				}
			}
		};

		// Wire up events
		_toggleButton.Clicked += OnToggleClicked;

		TrackerService.PlayRecorded += OnPlayRecorded;
		TrackerService.SessionStarted += OnSessionStarted;
		TrackerService.SessionStopped += OnSessionStopped;

		// Initialize state
		UpdateButtonState();
		UpdateStats();
	}

	protected override void Update()
	{
		base.Update();

		// Update duration display if tracking
		if (TrackerService.IsTracking) _durationText.Text = $"Duration: {TrackerService.SessionDuration:hh\\:mm\\:ss}";
	}

	private void OnToggleClicked()
	{
		if (TrackerService.IsTracking)
			TrackerService.StopSession();
		else
			TrackerService.StartSession();

		UpdateButtonState();
	}

	private void OnPlayRecorded(object? sender, SessionPlayResult play)
	{
		// Update UI on the main thread
		Schedule(() =>
		{
			_sessionChart.AddPlay(play);
			UpdateStats();
		});
	}

	private void OnSessionStarted(object? sender, EventArgs e)
	{
		Schedule(() =>
		{
			_sessionChart.Clear();
			UpdateButtonState();
			UpdateStats();
		});
	}

	private void OnSessionStopped(object? sender, EventArgs e)
	{
		Schedule(() => { UpdateButtonState(); });
	}

	private void UpdateButtonState()
	{
		_toggleButton.SetTrackingState(TrackerService.IsTracking);
	}

	private void UpdateStats()
	{
		var playCount = TrackerService.PlayCount;
		_statsText.Text = $"Plays: {playCount}";

		if (TrackerService.IsTracking)
			_durationText.Text = $"Duration: {TrackerService.SessionDuration:hh\\:mm\\:ss}";
		else
			_durationText.Text = "Duration: 00:00:00";
	}

	protected override void Dispose(bool isDisposing)
	{
		if (TrackerService != null)
		{
			TrackerService.PlayRecorded -= OnPlayRecorded;
			TrackerService.SessionStarted -= OnSessionStarted;
			TrackerService.SessionStopped -= OnSessionStopped;
		}

		base.Dispose(isDisposing);
	}
}
