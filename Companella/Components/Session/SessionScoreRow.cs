using System.Globalization;
using Companella.Components.Misc;
using Companella.Models.Beatmap;
using Companella.Models.Session;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Companella.Components.Session;

/// <summary>
/// A row component displaying a single play from a session with map info, stats, and grade.
/// </summary>
public partial class SessionScoreRow : CompositeDrawable, IHasTooltip
{
	private readonly StoredSessionPlay _play;
	private readonly int _index;
	private readonly OsuFileParser? _fileParser;

	[Resolved] private IRenderer Renderer { get; set; } = null!;

	[Resolved] private UserSettingsService UserSettings { get; set; } = null!;
	[Resolved] private Services.Integrations.ManiaTracker.ManiaTrackerService ManiaTracker { get; set; } = null!;
	private string? _maniaTrackerStatus;

	private Box _background = null!;
	private Container _thumbnailContainer = null!;
	private Sprite? _thumbnailSprite;
	private Container _replayStatusContainer = null!;
	private SpriteText _replayStatusText = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _normalBg = new(40, 40, 45, 255);
	private readonly Color4 _hoverBg = new(50, 50, 55, 255);
	private readonly Color4 _failedBg = new(60, 40, 40, 255);
	private readonly Color4 _quitBg = new(50, 50, 60, 255);

	// Grade colors
	private static readonly Dictionary<string, Color4> _gradeColors = new()
	{
		{ "SS", new Color4(255, 215, 0, 255) }, // Gold
		{ "S", new Color4(255, 200, 50, 255) }, // Yellow-gold
		{ "A", new Color4(100, 200, 100, 255) }, // Green
		{ "B", new Color4(100, 150, 255, 255) }, // Blue
		{ "C", new Color4(200, 100, 255, 255) }, // Purple
		{ "D", new Color4(255, 100, 100, 255) }, // Red
		{ "F", new Color4(150, 50, 50, 255) }, // Dark red
		{ "Q", new Color4(100, 100, 120, 255) } // Gray
	};

	/// <summary>
	/// Event raised when the row is clicked and has a replay available.
	/// </summary>
	public event Action<StoredSessionPlay>? ReplayRequested;

	/// <summary>
	/// Event raised when the row is clicked.
	/// </summary>
	public event Action<StoredSessionPlay>? Clicked;

	/// <summary>
	/// Event raised when the row is right-clicked to load the beatmap.
	/// </summary>
	public event Action<StoredSessionPlay>? BeatmapRequested;

	/// <summary>
	/// Tooltip showing available actions.
	/// </summary>
	public LocalisableString TooltipText
	{
		get
		{
			var leftAction = _play.HasReplay ? "Left-click: Analyze replay" : "Left-click: No replay available";
			var rightAction = "Right-click: Analyze beatmap";
			return $"{leftAction}\n{rightAction}" + (_maniaTrackerStatus == null ? "" : "\n" + _maniaTrackerStatus);
		}
	}

	public SessionScoreRow(StoredSessionPlay play, int index, OsuFileParser? fileParser = null)
	{
		_play = play;
		_index = index;
		_fileParser = fileParser;

		RelativeSizeAxes = Axes.X;
		Height = 60;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 6;

		// Determine background color based on status
		var bgColor = _play.Status switch
		{
			PlayStatus.Failed => _failedBg,
			PlayStatus.Quit => _quitBg,
			_ => _normalBg
		};

		// Try to parse beatmap for metadata
		OsuFile? osuFile = null;
		if (_fileParser != null && File.Exists(_play.BeatmapPath))
			try
			{
				osuFile = OsuFileParser.Parse(_play.BeatmapPath);
			}
			catch
			{
				/* Ignore parse errors */
			}

		var preferRomanized = UserSettings.Settings.PreferRomanizedMetadata;
		var title = osuFile?.GetTitle(preferRomanized) ?? Path.GetFileNameWithoutExtension(_play.BeatmapPath);
		var version = osuFile?.Version ?? "";
		var creator = osuFile?.Creator ?? "";
		var artist = osuFile?.GetArtist(preferRomanized) ?? "";

		InternalChildren = new Drawable[]
		{
			_background = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = bgColor
			},
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Padding = new MarginPadding { Left = 8, Right = 8, Top = 6, Bottom = 6 },
				Spacing = new Vector2(10, 0),
				Children = new Drawable[]
				{
					// Index number
					new Container
					{
						Size = new Vector2(24, 48),
						Child = new SpriteText
						{
							Text = $"{_index + 1}.",
							Font = new FontUsage("", 16, "Bold"),
							Colour = _accentColor,
							Anchor = Anchor.Centre,
							Origin = Anchor.Centre
						}
					},
					// Thumbnail
					_thumbnailContainer = new Container
					{
						Size = new Vector2(48, 48),
						Masking = true,
						CornerRadius = 4,
						Children = new Drawable[]
						{
							new Box
							{
								RelativeSizeAxes = Axes.Both,
								Colour = new Color4(30, 30, 35, 255)
							}
						}
					},
					// Map info
					new FillFlowContainer
					{
						AutoSizeAxes = Axes.Both,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 2),
						Children = new Drawable[]
						{
							new MarqueeText
							{
								Text = title,
								Font = new FontUsage("", 16, "Bold"),
								Colour = Color4.White,
								Width = 180,
								Height = 16
							},
							new FillFlowContainer
							{
								AutoSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(6, 0),
								Children = new Drawable[]
								{
									new MarqueeText
									{
										Text = string.IsNullOrEmpty(version) ? "" : $"[{version}]",
										Font = new FontUsage("", 14),
										Colour = new Color4(160, 160, 160, 255),
										Width = 80,
										Height = 14
									},
									new MarqueeText
									{
										Text = string.IsNullOrEmpty(creator) ? "" : $"by {creator}",
										Font = new FontUsage("", 14),
										Colour = new Color4(120, 120, 120, 255),
										Width = 80,
										Height = 14
									}
								}
							},
							// Stats row
							new FillFlowContainer
							{
								AutoSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(12, 0),
								Children = new Drawable[]
								{
									CreateStatItem("Acc", $"{_play.Accuracy:F2}%", _accentColor),
									CreateStatItem("Miss", _play.Misses.ToString(CultureInfo.InvariantCulture),
										_play.Misses > 0
											? new Color4(255, 100, 100, 255)
											: new Color4(150, 150, 150, 255)),
									CreateStatItem("Pause", _play.PauseCount.ToString(CultureInfo.InvariantCulture),
										_play.PauseCount > 0
											? new Color4(200, 150, 100, 255)
											: new Color4(150, 150, 150, 255))
								}
							}
						}
					}
				}
			},
			// Right side: Grade and replay status
			new Container
			{
				Anchor = Anchor.CentreRight,
				Origin = Anchor.CentreRight,
				AutoSizeAxes = Axes.Both,
				Margin = new MarginPadding { Right = 8 },
				Children = new Drawable[]
				{
					new FillFlowContainer
					{
						AutoSizeAxes = Axes.Both,
						Direction = FillDirection.Horizontal,
						Spacing = new Vector2(8, 0),
						Children = new Drawable[]
						{
							// Replay status indicator
							_replayStatusContainer = new Container
							{
								AutoSizeAxes = Axes.Both,
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft,
								Child = _replayStatusText = new SpriteText
								{
									Text = _play.HasReplay ? "[R]" : "...",
									Font = new FontUsage("", 13),
									Colour = _play.HasReplay
										? new Color4(100, 200, 100, 255)
										: new Color4(100, 100, 110, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								}
							},
							// Grade badge
							CreateGradeBadge(_play.Grade)
						}
					}
				}
			}
		};

		// Load thumbnail asynchronously
		LoadThumbnailAsync(osuFile);
		var trackerText = new MarqueeText
		{
			Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft,
			Position = new Vector2(42, -3), Width = 300, Height = 13,
			Font = new FontUsage("", 11), Colour = StyledButton.Theme.MutedLabel
		};
		AddInternal(trackerText);
		Scheduler.AddDelayed(() =>
		{
			_maniaTrackerStatus = ManiaTracker.DescribeSessionPlay(_play.BeatmapHash, _play.RecordedAt);
			trackerText.Text = _maniaTrackerStatus ?? "";
			trackerText.Alpha = _maniaTrackerStatus == null ? 0 : 1;
			Height = _maniaTrackerStatus == null ? 60 : 78;
		}, 1000, true);
	}

	/// <summary>
	/// Loads the beatmap thumbnail asynchronously.
	/// </summary>
	private void LoadThumbnailAsync(OsuFile? osuFile)
	{
		if (osuFile == null)
			return;

		var bgPath = osuFile.BackgroundFilePath;
		if (string.IsNullOrEmpty(bgPath) || !File.Exists(bgPath))
			return;

		Task.Run(() =>
		{
			try
			{
				// Load image using ImageSharp on background thread
				using var stream = File.OpenRead(bgPath);
				var image = Image.Load<Rgba32>(stream);

				Schedule(() =>
				{
					try
					{
						// Create texture on the main thread where Renderer is accessible
						var texture = Renderer.CreateTexture(image.Width, image.Height);
						texture.SetData(new TextureUpload(image));

						_thumbnailSprite = new Sprite
						{
							RelativeSizeAxes = Axes.Both,
							Texture = texture,
							FillMode = FillMode.Fill,
							Anchor = Anchor.Centre,
							Origin = Anchor.Centre
						};
						_thumbnailContainer.Add(_thumbnailSprite);
					}
					catch
					{
						/* Ignore texture creation errors */
					}
				});
			}
			catch
			{
				/* Ignore thumbnail load errors */
			}
		});
	}

	/// <summary>
	/// Creates a stat item (label + value).
	/// </summary>
	private static FillFlowContainer CreateStatItem(string label, string value, Color4 valueColor)
	{
		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(3, 0),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = $"{label}:",
					Font = new FontUsage("", 13),
					Colour = new Color4(120, 120, 120, 255)
				},
				new SpriteText
				{
					Text = value,
					Font = new FontUsage("", 13, "Bold"),
					Colour = valueColor
				}
			}
		};
	}

	/// <summary>
	/// Creates a grade badge.
	/// </summary>
	private static Container CreateGradeBadge(string grade)
	{
		var gradeColor = _gradeColors.GetValueOrDefault(grade, new Color4(150, 150, 150, 255));

		return new Container
		{
			Size = new Vector2(36, 36),
			Masking = true,
			CornerRadius = 6,
			Anchor = Anchor.CentreLeft,
			Origin = Anchor.CentreLeft,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(gradeColor.R, gradeColor.G, gradeColor.B, 0.3f)
				},
				new SpriteText
				{
					Text = grade,
					Font = new FontUsage("", 20, "Bold"),
					Colour = gradeColor,
					Anchor = Anchor.Centre,
					Origin = Anchor.Centre
				}
			}
		};
	}

	/// <summary>
	/// Updates the replay status indicator.
	/// </summary>
	public void UpdateReplayStatus(bool hasReplay)
	{
		_replayStatusText.Text = hasReplay ? "[R]" : "...";
		_replayStatusText.Colour = hasReplay ? new Color4(100, 200, 100, 255) : new Color4(100, 100, 110, 255);
	}

	protected override bool OnHover(HoverEvent e)
	{
		_background.FadeColour(_hoverBg, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		var bgColor = _play.Status switch
		{
			PlayStatus.Failed => _failedBg,
			PlayStatus.Quit => _quitBg,
			_ => _normalBg
		};
		_background.FadeColour(bgColor, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		Clicked?.Invoke(_play);

		if (_play.HasReplay) ReplayRequested?.Invoke(_play);

		return true;
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button == MouseButton.Right)
		{
			BeatmapRequested?.Invoke(_play);
			return true;
		}

		return base.OnMouseDown(e);
	}
}

/// <summary>
/// A loading spinner for replay status.
/// </summary>
public partial class ReplayLoadingSpinner : CompositeDrawable
{
	private Box _dot = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		Size = new Vector2(16, 16);

		InternalChild = _dot = new Box
		{
			Size = new Vector2(4, 4),
			Colour = new Color4(150, 150, 150, 255),
			Anchor = Anchor.Centre,
			Origin = Anchor.Centre
		};

		// Pulsing animation
		_dot.Loop(d => d
			.FadeTo(0.3f, 500)
			.Then()
			.FadeTo(1f, 500)
		);
	}
}
