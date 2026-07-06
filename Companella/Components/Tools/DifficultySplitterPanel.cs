using System.Globalization;
using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Tools;

/// <summary>
/// Full-area note preview for difficulty splitting. Each region is two same-named markers (start/end by time).
/// </summary>
public partial class DifficultySplitterPanel : CompositeDrawable
{
	private const double _analysisDebounceMs = 200;
	private const float _sidebarWidth = 272f;
	private const float _overlayPadding = 12f;
	private const float _inlineEditWidth = 260f;
	private const float _inlineEditHeight = 34f;

	[Resolved] private DanConfigurationService DanConfigService { get; set; } = null!;
	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;
	[Resolved] private ReadableKeyCombinationProvider KeyCombinationProvider { get; set; } = null!;

	private Container _chartColumn = null!;
	private DifficultySplitterNoteChart _noteChart = null!;
	private DifficultySplitterSidebar _sidebar = null!;
	private DifficultySplitterTimeline _timeline = null!;
	private DifficultySplitterContextMenu _contextMenu = null!;
	private DifficultySplitterToast _toast = null!;
	private Container _introContainer = null!;
	private ConfirmationDialog _clearConfirmDialog = null!;
	private Container _inlineEditContainer = null!;
	private StyledTextBox _inlineEditBox = null!;
	private Action<string>? _inlineEditCommit;

	private readonly List<DifficultyRegionMarker> _markers = new();
	private readonly List<DifficultyRegionPair> _pairs = new();
	private readonly Dictionary<Guid, DifficultyRegionAnalysisResult> _analysisCache = new();
	private readonly Dictionary<string, int> _regionColorIndices = new(StringComparer.OrdinalIgnoreCase);

	private OsuFile? _currentBeatmap;
	private List<HitObject> _hitObjects = new();
	private Guid? _selectedMarkerId;
	private Guid? _selectedPairId;
	private string? _pendingRegionName;
	private int _regionCounter;
	private bool _enabled = true;
	private CancellationTokenSource? _analysisCts;
	private int _analysisGeneration;
	private DifficultySplitterRegionAnalyzer? _regionAnalyzer;
	private bool _inlineEditActive;
	private bool _snapToNotes = true;
	private string? _statusHint;
	private double _lastTimelineViewCenterMs = double.NaN;
	private double _lastVisibleStartMs = double.NaN;
	private double _lastVisibleEndMs = double.NaN;

	public event Action<DifficultySplitterRequest>? SplitRequested;

	public override bool HandleNonPositionalInput => _enabled && _currentBeatmap != null && !_inlineEditActive;

	public DifficultySplitterPanel()
	{
		RelativeSizeAxes = Axes.X;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = new GridContainer
		{
			RelativeSizeAxes = Axes.Both,
			RowDimensions = new[]
			{
				new Dimension(GridSizeMode.Relative, 1f),
				new Dimension(GridSizeMode.Absolute, DifficultySplitterTimeline.PreferredHeight)
			},
			Content = new[]
			{
				new Drawable[]
				{
					new GridContainer
					{
						RelativeSizeAxes = Axes.Both,
						ColumnDimensions = new[]
						{
							new Dimension(),
							new Dimension(GridSizeMode.Absolute, _sidebarWidth)
						},
						Content = new[]
						{
							new Drawable[]
							{
								_chartColumn = new Container
								{
									RelativeSizeAxes = Axes.Both,
									Masking = true,
									Children = new Drawable[]
									{
										_noteChart = new DifficultySplitterNoteChart
										{
											RelativeSizeAxes = Axes.Both
										},
										new SplitterOverlayLayer
										{
											RelativeSizeAxes = Axes.Both,
											Padding = new MarginPadding(12),
											Children = new Drawable[]
											{
												_contextMenu = new DifficultySplitterContextMenu(),
												_toast = new DifficultySplitterToast
												{
													Position = new Vector2(0, 8)
												},
												_introContainer = new Container
												{
													Width = 380,
													Anchor = Anchor.Centre,
													Origin = Anchor.Centre,
													Alpha = 0,
													Children = new Drawable[]
													{
														new Box
														{
															RelativeSizeAxes = Axes.Both,
															Colour = new Color4(24, 24, 30, 245)
														},
														new FillFlowContainer
														{
															RelativeSizeAxes = Axes.X,
															AutoSizeAxes = Axes.Y,
															Direction = FillDirection.Vertical,
															Padding = new MarginPadding(14),
															Spacing = new Vector2(0, 10),
															Children = new Drawable[]
															{
																new SpriteText
																{
																	Text = "Difficulty Splitter",
																	Font = new FontUsage("", 15, "Bold"),
																	Colour = Color4.White
																},
																new SpriteText
																{
																	Text = "Click the chart twice to define a region (start, then end). Right-click for actions.",
																	Font = new FontUsage("", 12),
																	Colour = new Color4(190, 190, 190, 255)
																},
																new StyledButton("Got it", StyledButtonAppearance.Filled)
																{
																	Width = 90,
																	Height = 30,
																	Action = DismissIntro
																}
															}
														}
													}
												},
												_clearConfirmDialog = new ConfirmationDialog(),
												_inlineEditContainer = new Container
												{
													Width = 260,
													Height = 34,
													Anchor = Anchor.TopLeft,
													Origin = Anchor.TopLeft,
													Alpha = 0,
													Children = new Drawable[]
													{
														new Box
														{
															RelativeSizeAxes = Axes.Both,
															Colour = new Color4(32, 32, 38, 250)
														},
														_inlineEditBox = new StyledTextBox
														{
															RelativeSizeAxes = Axes.Both,
															Padding = new MarginPadding(6)
														}
													}
												}
											}
										}
									}
								},
								_sidebar = new DifficultySplitterSidebar()
							}
						}
					}
				},
				new Drawable[]
				{
					_timeline = new DifficultySplitterTimeline()
				}
			}
		};

		_noteChart.MarkerAddedAtTime += OnChartMarkerAdded;
		_noteChart.MarkerMoved += OnChartMarkerMoved;
		_noteChart.MarkerSelected += OnChartMarkerSelected;
		_noteChart.PairSelected += OnChartPairSelected;
		_noteChart.ContextMenuRequested += OnChartContextMenuRequested;
		_noteChart.InteractionBegan += () => GetContainingFocusManager()?.ChangeFocus(this);
		_sidebar.CreateClicked += OnCreateClicked;
		_sidebar.PairSelected += SelectRegion;
		_sidebar.IncompleteRegionSelected += SelectIncompleteRegion;
		_timeline.SeekRequested += OnTimelineSeekRequested;
		_timeline.PairSelected += SelectRegion;
		_timeline.MarkerSelected += OnTimelineMarkerSelected;
		_contextMenu.ItemSelected += OnContextMenuItemSelected;
		_clearConfirmDialog.Confirmed += OnClearAllConfirmed;

		_inlineEditBox.OnCommit += (sender, _) =>
		{
			_inlineEditCommit?.Invoke(sender.Text);
			HideInlineEdit();
		};

		UpdateSidebarState();
		UpdateSplitButtonState();
		UpdateContextBar();
		RebuildRegionList();
		UpdateSidebarAnalysis();
		SyncTimelineState();
	}

	protected override void Update()
	{
		base.Update();

		var scroll = FindScrollContainer();
		if (scroll == null)
			return;

		var targetHeight = scroll.DrawHeight - 56;
		if (targetHeight > 200 && Math.Abs(Height - targetHeight) > 1)
			Height = targetHeight;

		UpdateTimelineViewState();
	}

	private void UpdateTimelineViewState()
	{
		if (_currentBeatmap == null || _hitObjects.Count == 0)
			return;

		var center = _noteChart.ViewCenterMs;
		var (visibleStart, visibleEnd) = _noteChart.GetVisibleTimeRange();
		if (Math.Abs(center - _lastTimelineViewCenterMs) < 0.5 &&
			Math.Abs(visibleStart - _lastVisibleStartMs) < 0.5 &&
			Math.Abs(visibleEnd - _lastVisibleEndMs) < 0.5)
			return;

		_lastTimelineViewCenterMs = center;
		_lastVisibleStartMs = visibleStart;
		_lastVisibleEndMs = visibleEnd;
		_timeline.SetViewState(center, visibleStart, visibleEnd);
	}

	private void OnTimelineSeekRequested(double timeMs)
	{
		if (!_enabled || _currentBeatmap == null)
			return;

		GetContainingFocusManager()?.ChangeFocus(this);
		_noteChart.CenterOnTime(Math.Max(0, timeMs));
		_lastTimelineViewCenterMs = double.NaN;
		UpdateTimelineViewState();
	}

	private void OnTimelineMarkerSelected(Guid markerId) => OnChartMarkerSelected(markerId);

	private ChainedScrollContainer? FindScrollContainer()
	{
		for (var node = Parent; node != null; node = node.Parent)
		{
			if (node is ChainedScrollContainer scroll)
				return scroll;
		}

		return null;
	}

	public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
		_sidebar.ReceivePositionalInputAt(screenSpacePos) ||
		(_contextMenu.Alpha > 0 && _contextMenu.ReceivePositionalInputAt(screenSpacePos)) ||
		(_introContainer.Alpha > 0 && _introContainer.ReceivePositionalInputAt(screenSpacePos)) ||
		(_clearConfirmDialog.Alpha > 0 && _clearConfirmDialog.ReceivePositionalInputAt(screenSpacePos)) ||
		(_inlineEditContainer.Alpha > 0 && _inlineEditContainer.ReceivePositionalInputAt(screenSpacePos));

	protected override bool OnClick(ClickEvent e)
	{
		if (_sidebar.ReceivePositionalInputAt(e.ScreenSpaceMousePosition) ||
			(_contextMenu.Alpha > 0 && _contextMenu.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)) ||
			(_introContainer.Alpha > 0 && _introContainer.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)) ||
			(_clearConfirmDialog.Alpha > 0 && _clearConfirmDialog.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)) ||
			(_inlineEditContainer.Alpha > 0 && _inlineEditContainer.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)))
		{
			GetContainingFocusManager()?.ChangeFocus(this);
			return base.OnClick(e);
		}

		return false;
	}

	public void SetCurrentBeatmap(OsuFile? osuFile)
	{
		_currentBeatmap = osuFile;
		_hitObjects = osuFile != null ? HitObjectSerializer.Parse(osuFile) : new List<HitObject>();
		_regionAnalyzer = new DifficultySplitterRegionAnalyzer(DanConfigService);

		_markers.Clear();
		_pairs.Clear();
		_analysisCache.Clear();
		_regionColorIndices.Clear();
		_selectedMarkerId = null;
		_selectedPairId = null;
		_pendingRegionName = null;
		_regionCounter = 0;

		if (osuFile != null)
		{
			var keyCount = (int)osuFile.CircleSize;
			_sidebar.SetMapTitle($"{osuFile.Artist} - {osuFile.Title} [{osuFile.Version}] ({keyCount}K)");
		}
		else
		{
			_sidebar.SetMapTitle("No beatmap loaded");
		}

		_noteChart.SetBeatmap(osuFile, _hitObjects);
		UpdateSidebarState();
		UpdateSplitButtonState();
		UpdateContextBar();
		RebuildRegionList();
		SyncChartState();
		UpdateSidebarAnalysis();
		MaybeShowIntro();
	}

	public void ShowExportResult(DifficultySplitterResult result)
	{
		if (result.PairResults.Count == 0)
		{
			ShowToast(result.ErrorMessage ?? "Export failed", success: false);
			return;
		}

		var successes = result.PairResults.Where(r => r.Success).ToList();
		var failures = result.PairResults.Where(r => !r.Success).ToList();

		if (successes.Count > 0)
		{
			var names = string.Join(", ", successes.Select(r => r.VersionName));
			ShowToast($"Wrote {successes.Count} file{(successes.Count == 1 ? string.Empty : "s")}: {names}");
		}

		if (failures.Count > 0)
		{
			var message = failures.Count == 1
				? $"{failures[0].VersionName}: {failures[0].ErrorMessage ?? "failed"}"
				: $"{failures.Count} regions failed to export";
			ShowToast(message, success: false, durationMs: 6000);
		}
		else if (!result.Success)
		{
			ShowToast(result.ErrorMessage ?? "Export failed", success: false);
		}
	}

	public void SetEnabled(bool enabled)
	{
		_enabled = enabled;
		UpdateSplitButtonState();
	}

	protected override bool OnKeyDown(KeyDownEvent e)
	{
		if (!_enabled || _currentBeatmap == null)
			return base.OnKeyDown(e);

		if (_inlineEditContainer.Alpha > 0)
			return base.OnKeyDown(e);

		if (e.Key == Key.Escape)
		{
			if (_contextMenu.Alpha > 0)
			{
				_contextMenu.CloseMenu();
				return true;
			}

			CancelPendingRegion();
			return true;
		}

		if (e.Key == Key.M && !e.ControlPressed)
		{
			AddMarkerAtTime(_noteChart.ViewCenterMs);
			return true;
		}

		if (e.Key == Key.Tab)
		{
			CycleRegion(e.ShiftPressed);
			return true;
		}

		if (e.Key == Key.Delete || e.Key == Key.BackSpace)
		{
			DeleteSelectedRegion();
			return true;
		}

		if (e.Key == Key.Comma || e.Key == Key.Period)
		{
			CycleRegion(e.Key == Key.Comma);
			return true;
		}

		if (e.Key == Key.G && !e.ControlPressed)
		{
			BeginJumpToTimestamp();
			return true;
		}

		if (e.Key == Key.F2)
		{
			BeginInlineRename();
			return true;
		}

		if (e.Key == Key.F && !e.ControlPressed)
		{
			ZoomToSelectedRegion();
			return true;
		}

		if (e.Key == Key.N && !e.ControlPressed)
		{
			_snapToNotes = !_snapToNotes;
			UpdateContextBar();
			ShowToast(_snapToNotes ? "Snap to notes enabled" : "Snap to notes disabled");
			return true;
		}

		if (e.Key == Key.R && !e.ControlPressed)
		{
			_noteChart.ResetView();
			return true;
		}

		if (e.Key == Key.C && e.ControlPressed)
		{
			RequestClearAll();
			return true;
		}

		if (e.Key == Key.Up)
		{
			_noteChart.PanTime(500);
			return true;
		}

		if (e.Key == Key.Down)
		{
			_noteChart.PanTime(-500);
			return true;
		}

		if (e.Key == Key.Plus || e.Key == Key.KeypadPlus)
		{
			_noteChart.ZoomTime(1.15f);
			return true;
		}

		if (e.Key == Key.Minus || e.Key == Key.KeypadMinus)
		{
			_noteChart.ZoomTime(1f / 1.15f);
			return true;
		}

		return base.OnKeyDown(e);
	}

	private void OnChartMarkerAdded(double timeMs)
	{
		if (!_enabled || _currentBeatmap == null)
			return;

		AddMarkerAtTime(timeMs);
	}

	private void OnChartMarkerMoved(Guid markerId, double timeMs)
	{
		var marker = _markers.FirstOrDefault(m => m.Id == markerId);
		if (marker == null)
			return;

		marker.TimeMs = ApplySnap(Math.Max(0, timeMs));
		RebuildPairsFromMarkers();
		SyncChartState();
		ScheduleDebouncedAnalysis();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
	}

	private void OnChartMarkerSelected(Guid markerId)
	{
		_selectedMarkerId = markerId;
		SelectRegionForMarker(markerId);
		if (_selectedPairId.HasValue)
		{
			var pair = _pairs.First(p => p.Id == _selectedPairId.Value);
			ShowSidebarAnalysisForPair(pair);
		}
		else
			UpdateSidebarAnalysis();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
	}

	private void OnChartPairSelected(Guid pairId) => SelectRegion(pairId);

	private void AddMarkerAtTime(double timeMs)
	{
		timeMs = ApplySnap(timeMs);
		timeMs = Math.Max(0, timeMs);
		DifficultyRegionMarker marker;

		if (_pendingRegionName != null)
		{
			marker = new DifficultyRegionMarker
			{
				Name = _pendingRegionName,
				TimeMs = timeMs
			};
			_markers.Add(marker);
			_pendingRegionName = null;
		}
		else
		{
			_regionCounter++;
			var name = $"Region {_regionCounter}";
			_regionColorIndices[name] = _regionCounter - 1;
			marker = new DifficultyRegionMarker
			{
				Name = name,
				TimeMs = timeMs
			};
			_markers.Add(marker);
			_pendingRegionName = name;
		}

		_markers.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
		_selectedMarkerId = marker.Id;
		RebuildPairsFromMarkers();
		if (_selectedPairId.HasValue)
		{
			var pair = _pairs.FirstOrDefault(p => p.Id == _selectedPairId.Value);
			if (pair != null)
				ShowSidebarAnalysisForPair(pair);
		}
		else
			UpdateSidebarAnalysis();

		SyncChartState();
		ScheduleDebouncedAnalysis();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
		UpdateSplitButtonState();
	}

	private void SelectRegion(Guid pairId)
	{
		if (!_pairs.Any(p => p.Id == pairId))
			return;

		_selectedPairId = pairId;
		var pair = _pairs.First(p => p.Id == pairId);
		_selectedMarkerId = pair.StartMarkerId;
		CenterViewOnPair(pair);
		ShowSidebarAnalysisForPair(pair);
		SyncChartState();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
	}

	private void SelectIncompleteRegion(string regionName)
	{
		var marker = _markers.FirstOrDefault(m =>
			string.Equals(m.Name, regionName, StringComparison.OrdinalIgnoreCase));
		if (marker == null)
			return;

		_selectedMarkerId = marker.Id;
		_selectedPairId = null;
		_noteChart.CenterOnTime(marker.TimeMs);
		UpdateSidebarAnalysis();
		SyncChartState();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
	}

	private void CenterViewOnPair(DifficultyRegionPair pair)
	{
		var markerLookup = _markers.ToDictionary(m => m.Id);
		var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
		if (bounds == null)
			return;

		_noteChart.CenterOnTime((bounds.Value.StartMs + bounds.Value.EndMs) / 2.0);
	}

	private void RebuildPairsFromMarkers()
	{
		var previous = _pairs.ToList();
		_pairs.Clear();
		_pairs.AddRange(DifficultySplitterService.BuildRegionsFromMarkers(_markers, previous));

		if (_selectedMarkerId.HasValue)
			SelectRegionForMarker(_selectedMarkerId.Value);
		else if (_selectedPairId.HasValue && !_pairs.Any(p => p.Id == _selectedPairId))
			_selectedPairId = _pairs.FirstOrDefault()?.Id;
	}

	private void SelectRegionForMarker(Guid markerId)
	{
		var marker = _markers.FirstOrDefault(m => m.Id == markerId);
		if (marker == null)
			return;

		var pair = _pairs.FirstOrDefault(p =>
			p.StartMarkerId == markerId ||
			p.EndMarkerId == markerId ||
			string.Equals(p.VersionName, marker.Name, StringComparison.OrdinalIgnoreCase));

		_selectedPairId = pair?.Id;
	}

	private void CancelPendingRegion()
	{
		if (_pendingRegionName == null)
			return;

		_markers.RemoveAll(m => string.Equals(m.Name, _pendingRegionName, StringComparison.OrdinalIgnoreCase));
		_pendingRegionName = null;
		RebuildPairsFromMarkers();
		SyncChartState();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
		UpdateSplitButtonState();
	}

	private void CycleRegion(bool reverse)
	{
		if (_pairs.Count == 0)
			return;

		if (!_selectedPairId.HasValue)
		{
			_selectedPairId = _pairs[0].Id;
		}
		else
		{
			var index = _pairs.FindIndex(p => p.Id == _selectedPairId.Value);
			if (index < 0)
				index = 0;
			else
				index = reverse
					? (index - 1 + _pairs.Count) % _pairs.Count
					: (index + 1) % _pairs.Count;
			_selectedPairId = _pairs[index].Id;
		}

		var pair = _pairs.First(p => p.Id == _selectedPairId.Value);
		_selectedMarkerId = pair.StartMarkerId;
		CenterViewOnPair(pair);
		ShowSidebarAnalysisForPair(pair);
		SyncChartState();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
	}

	private void DeleteSelectedRegion()
	{
		if (!_selectedMarkerId.HasValue)
			return;

		var marker = _markers.FirstOrDefault(m => m.Id == _selectedMarkerId.Value);
		if (marker == null)
			return;

		var regionName = marker.Name;
		_markers.RemoveAll(m => string.Equals(m.Name, regionName, StringComparison.OrdinalIgnoreCase));

		if (string.Equals(_pendingRegionName, regionName, StringComparison.OrdinalIgnoreCase))
			_pendingRegionName = null;

		_selectedMarkerId = _markers.OrderBy(m => m.TimeMs).FirstOrDefault()?.Id;
		RebuildPairsFromMarkers();
		SyncChartState();
		ScheduleDebouncedAnalysis();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
		UpdateSplitButtonState();
	}

	private void BeginJumpToTimestamp()
	{
		var centerMs = _noteChart.ViewCenterMs;
		var totalSeconds = centerMs / 1000.0;
		var minutes = (int)(totalSeconds / 60);
		var seconds = totalSeconds - minutes * 60;
		var initial = $"{minutes}:{seconds:00}";

		ShowInlineEdit(initial, text =>
		{
			if (TryParseTimestamp(text, out var timeMs))
			{
				_noteChart.CenterOnTime(timeMs);
				_statusHint = null;
			}
			else
			{
				_statusHint = "Invalid timestamp — use mm:ss (e.g. 1:30)";
			}

			UpdateSidebarState();
		});
	}

	private static bool TryParseTimestamp(string input, out double timeMs)
	{
		timeMs = 0;
		input = input.Trim();
		if (input.Length == 0)
			return false;

		var colonIndex = input.IndexOf(':');
		if (colonIndex < 0)
			return false;

		var minutePart = input[..colonIndex].Trim();
		var secondPart = input[(colonIndex + 1)..].Trim();
		if (!int.TryParse(minutePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 0)
			return false;

		if (!double.TryParse(secondPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
			return false;

		timeMs = minutes * 60_000 + seconds * 1000;
		return true;
	}

	private void BeginInlineRename()
	{
		string? regionName = null;
		if (_selectedPairId.HasValue)
			regionName = _pairs.FirstOrDefault(p => p.Id == _selectedPairId.Value)?.VersionName;
		else if (_selectedMarkerId.HasValue)
			regionName = _markers.FirstOrDefault(m => m.Id == _selectedMarkerId.Value)?.Name;
		else if (_pendingRegionName != null)
			regionName = _pendingRegionName;

		if (string.IsNullOrWhiteSpace(regionName))
			return;

		var currentName = regionName;
		var anchorTime = GetRenameAnchorTimeMs();
		ShowInlineEdit(currentName, text =>
		{
			var newName = text.Trim();
			if (string.IsNullOrWhiteSpace(newName))
				return;

			if (_markers.Any(m =>
					string.Equals(m.Name, newName, StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(m.Name, currentName, StringComparison.OrdinalIgnoreCase)))
			{
				_statusHint = "Region name already in use";
				UpdateSidebarState();
				return;
			}

			foreach (var marker in _markers.Where(m =>
				string.Equals(m.Name, currentName, StringComparison.OrdinalIgnoreCase)))
				marker.Name = newName;

			if (_regionColorIndices.TryGetValue(currentName, out var colorIndex))
			{
				_regionColorIndices.Remove(currentName);
				_regionColorIndices[newName] = colorIndex;
			}

			if (string.Equals(_pendingRegionName, currentName, StringComparison.OrdinalIgnoreCase))
				_pendingRegionName = newName;

			RebuildPairsFromMarkers();
			SyncChartState();
			ScheduleDebouncedAnalysis();
			UpdateSidebarState();
			UpdateContextBar();
			RebuildRegionList();
			UpdateSplitButtonState();
		}, anchorTime);
	}

	private double? GetRenameAnchorTimeMs()
	{
		if (_selectedMarkerId.HasValue)
		{
			var marker = _markers.FirstOrDefault(m => m.Id == _selectedMarkerId.Value);
			if (marker != null)
				return marker.TimeMs;
		}

		if (_selectedPairId.HasValue)
		{
			var pair = _pairs.FirstOrDefault(p => p.Id == _selectedPairId.Value);
			if (pair != null &&
				_markers.FirstOrDefault(m => m.Id == pair.StartMarkerId) is { } startMarker)
				return startMarker.TimeMs;
		}

		return null;
	}

	private void ShowInlineEdit(string initial, Action<string> onCommit, double? anchorTimeMs = null)
	{
		_inlineEditActive = true;
		_inlineEditCommit = onCommit;
		_inlineEditBox.Text = initial;
		_inlineEditBox.SelectAll();

		var chartPos = anchorTimeMs.HasValue
			? _noteChart.GetInlineEditPositionForTime(anchorTimeMs.Value)
			: GetInlineEditTopCenterChartPosition();
		chartPos = ClampInlineEditChartPosition(chartPos);

		_inlineEditContainer.Anchor = Anchor.TopLeft;
		_inlineEditContainer.Origin = Anchor.TopLeft;
		_inlineEditContainer.Position = chartPos - new Vector2(_overlayPadding, _overlayPadding);
		_inlineEditContainer.FadeIn(150);
		GetContainingFocusManager()?.ChangeFocus(_inlineEditBox);
	}

	private Vector2 GetInlineEditTopCenterChartPosition()
	{
		const float topOffset = 48f;
		var x = (_chartColumn.DrawWidth - _inlineEditWidth) * 0.5f;
		return new Vector2(x, topOffset);
	}

	private Vector2 ClampInlineEditChartPosition(Vector2 chartPos)
	{
		const float margin = 8f;
		var maxX = Math.Max(margin, _chartColumn.DrawWidth - _inlineEditWidth - margin);
		var maxY = Math.Max(margin, _chartColumn.DrawHeight - _inlineEditHeight - margin);
		return new Vector2(
			Math.Clamp(chartPos.X, margin, maxX),
			Math.Clamp(chartPos.Y, margin, maxY));
	}

	private void HideInlineEdit()
	{
		_inlineEditActive = false;
		_inlineEditContainer.FadeOut(100);
		_inlineEditCommit = null;
		GetContainingFocusManager()?.ChangeFocus(this);
	}

	private void ClearAll()
	{
		_markers.Clear();
		_pairs.Clear();
		_analysisCache.Clear();
		_regionColorIndices.Clear();
		_selectedMarkerId = null;
		_selectedPairId = null;
		_pendingRegionName = null;
		_regionCounter = 0;
		UpdateSidebarAnalysis();
		SyncChartState();
		UpdateSidebarState();
		UpdateContextBar();
		RebuildRegionList();
		UpdateSplitButtonState();
	}

	private void SyncChartState()
	{
		_noteChart.SetMarkers(_markers);
		_noteChart.SetPairs(_pairs, _selectedPairId);
		_noteChart.SetRegionAnalysis(_analysisCache);
		_noteChart.SetSelectedMarkerId(_selectedMarkerId);
		_noteChart.SetRegionColorIndices(_regionColorIndices);

		double? pendingStart = null;
		var pendingColor = 0;
		if (_pendingRegionName != null)
		{
			var startMarker = _markers.FirstOrDefault(m =>
				string.Equals(m.Name, _pendingRegionName, StringComparison.OrdinalIgnoreCase));
			if (startMarker != null)
			{
				pendingStart = startMarker.TimeMs;
				pendingColor = _regionColorIndices.GetValueOrDefault(_pendingRegionName, 0);
			}
		}

		_noteChart.SetPendingPlacement(_pendingRegionName, pendingStart, pendingColor);
		SyncTimelineState();
	}

	private void SyncTimelineState()
	{
		if (_hitObjects.Count == 0)
		{
			_timeline.SetEmpty();
			_lastTimelineViewCenterMs = double.NaN;
			_lastVisibleStartMs = double.NaN;
			_lastVisibleEndMs = double.NaN;
			return;
		}

		var minTime = _hitObjects.Min(h => h.Time);
		var maxTime = _hitObjects.Max(h => Math.Max(h.Time, h.EndTime));
		var padding = Math.Max(1000, (maxTime - minTime) * 0.03);
		var mapMin = Math.Max(0, minTime - padding);
		var mapMax = maxTime + padding;

		var markerLookup = _markers.ToDictionary(m => m.Id);
		var regions = new List<DifficultySplitterTimelineRegion>();

		foreach (var pair in _pairs)
		{
			var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
			if (!bounds.HasValue)
				continue;

			var colorIndex = _regionColorIndices.GetValueOrDefault(pair.VersionName, 0);
			regions.Add(new DifficultySplitterTimelineRegion(
				pair.Id,
				bounds.Value.StartMs,
				bounds.Value.EndMs,
				DifficultySplitterRegionColors.GetAccent(colorIndex),
				pair.Id == _selectedPairId));
		}

		var selectedPair = _selectedPairId.HasValue
			? _pairs.FirstOrDefault(p => p.Id == _selectedPairId.Value)
			: null;

		var timelineMarkers = new List<DifficultySplitterTimelineMarker>();
		foreach (var marker in _markers)
		{
			var colorIndex = _regionColorIndices.GetValueOrDefault(marker.Name.Trim(), 0);
			var isPairMarker = selectedPair != null &&
							   (marker.Id == selectedPair.StartMarkerId || marker.Id == selectedPair.EndMarkerId);
			timelineMarkers.Add(new DifficultySplitterTimelineMarker(
				marker.Id,
				marker.TimeMs,
				DifficultySplitterRegionColors.GetAccent(colorIndex),
				marker.Id == _selectedMarkerId || isPairMarker));
		}

		_timeline.SetMapState(mapMin, mapMax, regions, timelineMarkers);
		_lastTimelineViewCenterMs = double.NaN;
		_lastVisibleStartMs = double.NaN;
		_lastVisibleEndMs = double.NaN;
		UpdateTimelineViewState();
	}

	private void UpdateSidebarState()
	{
		if (_pendingRegionName != null)
		{
			_sidebar.SetStateText($"Placing end for {_pendingRegionName}");
			return;
		}

		if (!string.IsNullOrEmpty(_statusHint))
		{
			_sidebar.SetStateText(_statusHint);
			return;
		}

		if (_pairs.Count > 0)
		{
			_sidebar.SetStateText($"{_pairs.Count} region{(_pairs.Count == 1 ? string.Empty : "s")} ready");
			return;
		}

		_sidebar.SetStateText(_markers.Count == 0 ? "Click chart twice to add a region" : string.Empty);
	}

	private void ScheduleDebouncedAnalysis()
	{
		_analysisCts?.Cancel();
		_analysisCts = new CancellationTokenSource();
		var token = _analysisCts.Token;
		var generation = ++_analysisGeneration;

		Task.Run(async () =>
		{
			try
			{
				await Task.Delay((int)_analysisDebounceMs, token);
				if (token.IsCancellationRequested)
					return;

				RunAnalysis(generation, token);
			}
			catch (TaskCanceledException)
			{
			}
		}, token);
	}

	private void RunAnalysis(int generation, CancellationToken token)
	{
		Dictionary<Guid, DifficultyRegionAnalysisResult> results;

		try
		{
			if (_currentBeatmap == null || _regionAnalyzer == null)
				return;

			var markerLookup = _markers.ToDictionary(m => m.Id);
			var calculatorMode = UserSettingsService.Settings.RiceDanCalculator;
			results = new Dictionary<Guid, DifficultyRegionAnalysisResult>();

			foreach (var pair in _pairs)
			{
				if (token.IsCancellationRequested)
					return;

				try
				{
					var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
					if (bounds == null)
					{
						results[pair.Id] = DifficultyRegionAnalysisResult.Invalid(
							pair.Id,
							DifficultySplitterService.ValidatePair(pair, markerLookup) ?? "Invalid pair");
						continue;
					}

					results[pair.Id] = _regionAnalyzer.AnalyzePair(
						pair.Id,
						_hitObjects,
						_currentBeatmap,
						bounds.Value.StartMs,
						bounds.Value.EndMs,
						calculatorMode);
				}
				catch (Exception ex)
				{
					results[pair.Id] = DifficultyRegionAnalysisResult.Invalid(
						pair.Id,
						$"Analysis failed: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[DifficultySplitter] Region analysis failed: {ex}");
			return;
		}

		if (token.IsCancellationRequested || generation != _analysisGeneration)
			return;

		Schedule(() =>
		{
			if (generation != _analysisGeneration)
				return;

			foreach (var (pairId, result) in results)
				_analysisCache[pairId] = result;

			UpdateSidebarState();
			UpdateContextBar();
			RebuildRegionList();
			UpdateSidebarAnalysis();
			SyncChartState();
		});
	}

	private string GetPairBadgeText(Guid pairId)
	{
		if (!_analysisCache.TryGetValue(pairId, out var result))
			return "analyzing...";

		if (!result.IsValid)
			return result.ErrorMessage ?? "invalid";

		var dan = result.Dan?.DisplayName ?? "?";
		return $"{result.NoteCount} notes, {dan}";
	}

	private bool HasValidPairs() => CountValidPairs() > 0;

	private int CountValidPairs()
	{
		if (_currentBeatmap == null || _pairs.Count == 0)
			return 0;

		var markerLookup = _markers.ToDictionary(m => m.Id);
		var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var count = 0;

		foreach (var pair in _pairs)
		{
			if (DifficultySplitterService.ValidatePair(pair, markerLookup, usedNames) != null)
				continue;

			usedNames.Add(pair.VersionName.Trim());
			var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
			if (bounds == null)
				continue;

			var notes = DifficultySplitterService.FilterRegionNotes(
				_hitObjects, bounds.Value.StartMs, bounds.Value.EndMs);
			if (notes.Count == 0)
				continue;

			count++;
		}

		return count;
	}

	private void UpdateSplitButtonState()
	{
		var validCount = CountValidPairs();
		_sidebar.SetCreateButton(
			validCount > 0 ? $"Create {validCount} Difficult{(validCount == 1 ? "y" : "ies")}" : "Create Difficulties",
			_enabled && validCount > 0);
	}

	private void OnCreateClicked()
	{
		if (_currentBeatmap == null)
			return;

		var validCount = CountValidPairs();
		if (validCount == 0)
		{
			var issue = GetFirstExportIssue();
			ShowToast(issue ?? "No valid regions to export", success: false);
			return;
		}

		SplitRequested?.Invoke(new DifficultySplitterRequest
		{
			Source = _currentBeatmap,
			Markers = _markers.ToList(),
			Pairs = _pairs.ToList()
		});
	}

	private void UpdateContextBar()
	{
		string K(Key key) => KeyboardDisplayHelper.FormatKey(KeyCombinationProvider, key);

		if (_pendingRegionName != null)
		{
			_sidebar.SetContextText($"Click again or {K(Key.M)} to finish · {K(Key.Escape)} cancel");
			return;
		}

		if (_selectedPairId.HasValue)
		{
			_sidebar.SetContextText($"{K(Key.F2)} rename · {K(Key.Delete)} delete · {K(Key.F)} zoom · {K(Key.N)} snap {(_snapToNotes ? "on" : "off")}");
			return;
		}

		_sidebar.SetContextText($"Click twice, double-click, or {K(Key.M)} to add region · Right-click menu · Scroll to navigate");
	}

	private void RebuildRegionList()
	{
		var markerLookup = _markers.ToDictionary(m => m.Id);
		var items = new List<DifficultySplitterRegionListItem>();
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var pair in _pairs.OrderBy(p =>
		{
			var bounds = DifficultySplitterService.ResolvePairBounds(p, markerLookup);
			return bounds?.StartMs ?? double.MaxValue;
		}))
		{
			seenNames.Add(pair.VersionName);
			var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
			var timeRange = bounds != null
				? $"{FormatTimestamp(bounds.Value.StartMs)}–{FormatTimestamp(bounds.Value.EndMs)}"
				: "—";

			items.Add(new DifficultySplitterRegionListItem(
				pair.Id,
				pair.VersionName,
				timeRange,
				GetRegionListBadge(pair),
				true,
				_selectedPairId == pair.Id,
				_regionColorIndices.GetValueOrDefault(pair.VersionName, 0)));
		}

		if (_pendingRegionName != null && !seenNames.Contains(_pendingRegionName))
		{
			var startMarker = _markers.FirstOrDefault(m =>
				string.Equals(m.Name, _pendingRegionName, StringComparison.OrdinalIgnoreCase));
			var timeRange = startMarker != null ? $"{FormatTimestamp(startMarker.TimeMs)}–…" : "—";

			items.Add(new DifficultySplitterRegionListItem(
				null,
				_pendingRegionName,
				timeRange,
				string.Empty,
				false,
				true,
				_regionColorIndices.GetValueOrDefault(_pendingRegionName, 0)));
		}

		_sidebar.SetRegions(items);
	}

	private static string FormatTimestamp(double timeMs)
	{
		var totalSeconds = (int)(timeMs / 1000);
		var minutes = totalSeconds / 60;
		var seconds = totalSeconds % 60;
		return $"{minutes}:{seconds:D2}";
	}

	private double ApplySnap(double timeMs) =>
		_snapToNotes
			? DifficultySplitterService.SnapToNearestNote(timeMs, _hitObjects)
			: timeMs;

	private void ShowToast(string message, bool success = true, int durationMs = 4000) =>
		_toast.Show(message, success, durationMs);

	private DifficultySplitterChartContext _lastChartContext;

	private void OnChartContextMenuRequested(DifficultySplitterChartContext context)
	{
		if (!_enabled || _currentBeatmap == null)
			return;

		_lastChartContext = context;
		_contextMenu.CloseMenu();

		if (context.PairId.HasValue)
			SelectRegion(context.PairId.Value);
		else if (context.MarkerId.HasValue)
			OnChartMarkerSelected(context.MarkerId.Value);

		var mouse = GetContainingInputManager()?.CurrentState.Mouse.Position ?? Vector2.Zero;
		_contextMenu.Show(ToLocalSpace(mouse), BuildContextMenuItems(context));
	}

	private List<DifficultySplitterContextMenuItem> BuildContextMenuItems(DifficultySplitterChartContext context)
	{
		var items = new List<DifficultySplitterContextMenuItem>
		{
			new("jump", "Jump here"),
			new("marker", _pendingRegionName != null ? "Place end here" : "Add marker here")
		};

		if (_pendingRegionName != null)
			items.Add(new("cancel", "Cancel placement"));

		if (_selectedPairId.HasValue || context.PairId.HasValue)
		{
			items.Add(new("rename", "Rename region"));
			items.Add(new("zoom", "Zoom to region"));
			items.Add(new("delete", "Delete region"));
		}

		return items;
	}

	private void OnContextMenuItemSelected(string itemId)
	{
		switch (itemId)
		{
			case "jump":
				_noteChart.CenterOnTime(_lastChartContext.TimeMs);
				ShowToast($"Jumped to {FormatTimestamp(_lastChartContext.TimeMs)}");
				break;
			case "marker":
				AddMarkerAtTime(_lastChartContext.TimeMs);
				break;
			case "cancel":
				CancelPendingRegion();
				break;
			case "rename":
				BeginInlineRename();
				break;
			case "zoom":
				ZoomToSelectedRegion();
				break;
			case "delete":
				DeleteSelectedRegion();
				break;
		}
	}

	private void ShowSidebarAnalysisForPair(DifficultyRegionPair pair)
	{
		_sidebar.SetAnalysisRegion(
			pair.VersionName,
			DifficultySplitterRegionColors.GetAccent(_regionColorIndices.GetValueOrDefault(pair.VersionName, 0)));
		UpdateSidebarAnalysis();
	}

	private void UpdateSidebarAnalysis()
	{
		if (_pendingRegionName != null)
		{
			_sidebar.SetAnalysisPlaceholder("Finish placing the end marker to analyze this region.");
			return;
		}

		if (!_selectedPairId.HasValue)
		{
			_sidebar.SetAnalysisPlaceholder("Select a region to see DAN and patterns.");
			return;
		}

		if (!_analysisCache.TryGetValue(_selectedPairId.Value, out var result))
		{
			_sidebar.SetAnalysisAnalyzing();
			return;
		}

		_sidebar.SetAnalysisResult(result);
	}

	private void ZoomToSelectedRegion()
	{
		if (!_selectedPairId.HasValue)
			return;

		var pair = _pairs.FirstOrDefault(p => p.Id == _selectedPairId.Value);
		if (pair == null)
			return;

		var markerLookup = _markers.ToDictionary(m => m.Id);
		var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
		if (bounds == null)
			return;

		_noteChart.ZoomToRegion(bounds.Value.StartMs, bounds.Value.EndMs);
	}

	private void RequestClearAll()
	{
		if (_markers.Count == 0)
			return;

		_clearConfirmDialog.Show(
			"Clear all regions?",
			"This removes every marker and region from the chart.",
			isDangerous: true);
	}

	private void OnClearAllConfirmed() => ClearAll();

	private void MaybeShowIntro()
	{
		if (_currentBeatmap == null ||
			UserSettingsService.Settings.HasSeenDifficultySplitterIntro ||
			_markers.Count > 0)
			return;

		_introContainer.FadeIn(200);
	}

	private void DismissIntro()
	{
		UserSettingsService.Settings.HasSeenDifficultySplitterIntro = true;
		_ = UserSettingsService.SaveAsync();
		_introContainer.FadeOut(150);
	}

	private string GetRegionListBadge(DifficultyRegionPair pair)
	{
		var issue = GetRegionValidationIssue(pair);
		if (issue != null)
			return issue;

		return GetPairBadgeText(pair.Id);
	}

	private string? GetRegionValidationIssue(DifficultyRegionPair pair)
	{
		var markerLookup = _markers.ToDictionary(m => m.Id);
		var usedNames = new HashSet<string>(
			_pairs.Where(p => p.Id != pair.Id).Select(p => p.VersionName.Trim()),
			StringComparer.OrdinalIgnoreCase);

		var validation = DifficultySplitterService.ValidatePair(pair, markerLookup, usedNames);
		if (validation != null)
			return validation;

		var bounds = DifficultySplitterService.ResolvePairBounds(pair, markerLookup);
		if (bounds == null)
			return "invalid";

		var notes = DifficultySplitterService.FilterRegionNotes(
			_hitObjects, bounds.Value.StartMs, bounds.Value.EndMs);
		return notes.Count == 0 ? "empty region" : null;
	}

	private string? GetFirstExportIssue()
	{
		if (_pairs.Count == 0)
			return _pendingRegionName != null ? "Finish placing the current region first" : "Add at least one region";

		foreach (var pair in _pairs)
		{
			var issue = GetRegionValidationIssue(pair);
			if (issue != null)
				return $"{pair.VersionName}: {issue}";
		}

		return "No exportable regions";
	}
}


/// <summary>
/// Overlay layer that passes mouse input through except on interactive children.
/// </summary>
internal partial class SplitterOverlayLayer : Container
{
	public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
	{
		foreach (var child in Children)
		{
			if (child.ReceivePositionalInputAt(screenSpacePos))
				return true;
		}

		return false;
	}
}
