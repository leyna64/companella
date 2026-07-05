using System.Globalization;
using System.Windows.Forms;
using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Components.Settings;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Services.Common;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Panel for creating and editing map packs (multi-difficulty beatmapsets from cross-map sources).
/// </summary>
public partial class MapPackManagerPanel : CompositeDrawable
{
	private FillFlowContainer _listContainer = null!;
	private StyledButton _addButton = null!;
	private StyledButton _buildButton = null!;
	private StyledButton _clearButton = null!;
	private StyledButton _msdButton = null!;
	private StyledButton _newPackButton = null!;
	private StyledButton _loadFolderButton = null!;
	private StyledButton _loadCurrentMapButton = null!;
	private StyledTextBox _titleTextBox = null!;
	private StyledTextBox _ownerTextBox = null!;
	private StyledTextBox _tagsTextBox = null!;
	private StyledTextBox _outputFolderTextBox = null!;
	private SpriteText _summaryText = null!;
	private SpriteText _artistDisplayText = null!;
	private StyledTextBox _bulkRateTextBox = null!;
	private StyledTextBox _bulkHpTextBox = null!;
	private StyledTextBox _bulkOdTextBox = null!;
	private SettingsCheckbox _bulkPitchCheckbox = null!;
	private StyledButton _applyBulkButton = null!;

	private readonly List<MapPackEntry> _entries = new();
	private OsuFile? _currentBeatmap;
	private string? _loadedFolderPath;
	private bool _resetOnlineIdsOnExport = true;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	public event Action<MapPackDefinition>? BuildMapPackRequested;
	public event Action<List<MapPackEntry>>? RecalculateMsdRequested;

	public MapPackManagerPanel()
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
				Spacing = new Vector2(0, 12),
				Children = new Drawable[]
				{
					CreateSection("Pack Actions", new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(8, 0),
							Children = new Drawable[]
							{
								_newPackButton = new StyledButton("New Pack")
								{
									Size = new Vector2(90, 32),
									TooltipText = "Clear the editor and start a new map pack"
								},
								_loadFolderButton = new StyledButton("Load from Folder")
								{
									Size = new Vector2(130, 32),
									TooltipText =
										"Load an existing pack folder (uses .companella.mappack.json sidecar when present)"
								},
								_loadCurrentMapButton = new StyledButton("Load from Current Map")
								{
									Size = new Vector2(160, 32),
									Enabled = false,
									TooltipText =
										"Load the beatmapset folder of the currently selected map (traditional or Companella packs)"
								}
							}
						}
					}),
					CreateSection("Pack Metadata", new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 8),
							Children = new Drawable[]
							{
								CreateLabeledInput("Title", out _titleTextBox, "Map Pack"),
								new Container
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Child = new FillFlowContainer
									{
										RelativeSizeAxes = Axes.X,
										AutoSizeAxes = Axes.Y,
										Direction = FillDirection.Vertical,
										Spacing = new Vector2(0, 2),
										Children = new Drawable[]
										{
											new SpriteText
											{
												Text = "Artist",
												Font = new FontUsage("Roboto-Regular", 13),
												Colour = new Color4(100, 100, 100, 255)
											},
											_artistDisplayText = new SpriteText
											{
												Text = MapPackDefinition.DefaultArtist,
												Font = new FontUsage("Roboto-Regular", 14),
												Colour = new Color4(180, 180, 180, 255)
											}
										}
									}
								},
								CreateLabeledInput("Owner", out _ownerTextBox, "Companella"),
								CreateLabeledInput("Tags", out _tagsTextBox, ""),
								CreateLabeledInput("Output Folder Name", out _outputFolderTextBox, "")
							}
						}
					}),
					CreateSection("Difficulties", new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 4),
							Children = new Drawable[]
							{
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(8, 0),
									Children = new Drawable[]
									{
										_addButton = new StyledButton("Add Current Map")
										{
											Size = new Vector2(140, 32),
											Enabled = false,
											TooltipText = "Add the currently loaded beatmap as a pack difficulty"
										},
										_clearButton = new StyledButton("Clear All")
								{
									Size = new Vector2(80, 32),
									Enabled = false,
									AccentColor = StyledButton.Theme.DestructiveAccent
								},
										_msdButton = new StyledButton("MSD")
										{
											Size = new Vector2(60, 32),
											Enabled = false,
											TooltipText = "Recalculate MSD for all entries at their configured rates"
										}
									}
								},
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(6, 0),
									Children = new Drawable[]
									{
										CreateBulkField("Rate", out _bulkRateTextBox, 52),
										CreateBulkField("HP", out _bulkHpTextBox, 44),
										CreateBulkField("OD", out _bulkOdTextBox, 44),
										new Container
										{
											AutoSizeAxes = Axes.Both,
											Padding = new MarginPadding { Top = 18 },
											Child = _bulkPitchCheckbox = new SettingsCheckbox
											{
												LabelText = "Pitch",
												LabelFontSize = 11,
												LabelColour = new Color4(120, 120, 120, 255),
												IsChecked = true,
												TooltipText = "When applying bulk rate, also set pitch shift on all entries"
											}
										},
										_applyBulkButton = new StyledButton("Apply to All")
										{
											Size = new Vector2(90, 32),
											Margin = new MarginPadding { Top = 10 },
											Enabled = false,
											TooltipText =
												"Apply filled Rate/HP/OD values to every difficulty (empty fields are skipped)"
										}
									}
								},
								new Container
								{
									RelativeSizeAxes = Axes.X,
									Height = 280,
									Masking = true,
									CornerRadius = 6,
									Children = new Drawable[]
									{
										new Box
										{
											RelativeSizeAxes = Axes.Both,
											Colour = new Color4(30, 30, 35, 255)
										},
										new ChainedScrollContainer
										{
											RelativeSizeAxes = Axes.Both,
											ClampExtension = 100,
											ScrollbarVisible = true,
											Child = _listContainer = new FillFlowContainer
											{
												RelativeSizeAxes = Axes.X,
												AutoSizeAxes = Axes.Y,
												Direction = FillDirection.Vertical,
												Spacing = new Vector2(0, 4),
												Padding = new MarginPadding(8)
											}
										}
									}
								},
								_summaryText = new SpriteText
								{
									Text = "0 difficulties",
									Font = new FontUsage("", 13),
									Colour = new Color4(140, 140, 140, 255)
								}
							}
						}
					}),
					_buildButton = new StyledButton("Build Pack", StyledButton.Theme.SuccessFill)
					{
						RelativeSizeAxes = Axes.X,
						Height = 40,
						Enabled = false,
						TooltipText = "Export the pack to your osu! Songs folder"
					}
				}
			}
		};

		_addButton.Clicked += OnAddClicked;
		_clearButton.Clicked += OnClearClicked;
		_msdButton.Clicked += OnMsdClicked;
		_buildButton.Clicked += OnBuildClicked;
		_newPackButton.Clicked += OnNewPackClicked;
		_loadFolderButton.Clicked += OnLoadFolderClicked;
		_loadCurrentMapButton.Clicked += OnLoadCurrentMapClicked;
		_applyBulkButton.Clicked += OnApplyBulkClicked;
	}

	public void SetCurrentBeatmap(OsuFile? osuFile)
	{
		_currentBeatmap = osuFile;
		_addButton.Enabled = osuFile != null;
		_loadCurrentMapButton.Enabled = osuFile != null;
	}

	public void SetEnabled(bool enabled)
	{
		_buildButton.Enabled = enabled && _entries.Count > 0;
		_addButton.Enabled = enabled && _currentBeatmap != null;
		_loadCurrentMapButton.Enabled = enabled && _currentBeatmap != null;
		_clearButton.Enabled = enabled && _entries.Count > 0;
		_msdButton.Enabled = enabled && _entries.Count > 0;
		_applyBulkButton.Enabled = enabled && _entries.Count > 0;
	}

	public void RefreshList()
	{
		_listContainer.Clear();

		for (var i = 0; i < _entries.Count; i++)
		{
			var entry = _entries[i];
			var row = new MapPackEntryRow(entry, i, _accentColor)
			{
				RelativeSizeAxes = Axes.X,
				Height = 68
			};
			row.DeleteRequested += OnEntryDeleteRequested;
			row.MoveUpRequested += OnEntryMoveUpRequested;
			row.MoveDownRequested += OnEntryMoveDownRequested;
			row.RateChanged += OnEntryRateChanged;
			row.RatePitchAdjustChanged += OnEntryRatePitchAdjustChanged;
			row.VersionOverrideChanged += OnEntryVersionOverrideChanged;
			row.HpChanged += OnEntryHpChanged;
			row.OdChanged += OnEntryOdChanged;
			_listContainer.Add(row);
		}

		UpdateSummary();
		SetEnabled(true);
	}

	public void LoadDefinition(MapPackDefinition definition)
	{
		_entries.Clear();
		_entries.AddRange(definition.Entries);
		_titleTextBox.Text = definition.Title;
		_ownerTextBox.Text = definition.Creator;
		_tagsTextBox.Text = definition.Tags;
		_outputFolderTextBox.Text = string.IsNullOrWhiteSpace(definition.OutputFolderName)
			? definition.GetEffectiveOutputFolderName()
			: definition.OutputFolderName;
		_loadedFolderPath = definition.LoadedFolderPath;
		_resetOnlineIdsOnExport = definition.ResetOnlineIdsOnExport;
		_artistDisplayText.Text = string.IsNullOrWhiteSpace(definition.Artist)
			? MapPackDefinition.DefaultArtist
			: definition.Artist;
		RefreshList();
	}

	private MapPackDefinition BuildDefinitionFromUi()
	{
		return new MapPackDefinition
		{
			Title = _titleTextBox.Text.Trim(),
			Artist = MapPackDefinition.DefaultArtist,
			Creator = _ownerTextBox.Text.Trim(),
			Tags = _tagsTextBox.Text.Trim(),
			Source = string.Empty,
			OutputFolderName = _outputFolderTextBox.Text.Trim(),
			LoadedFolderPath = _loadedFolderPath,
			ResetOnlineIdsOnExport = _resetOnlineIdsOnExport,
			Entries = _entries.Select((e, i) =>
			{
				e.SortOrder = i;
				return e;
			}).ToList()
		};
	}

	private void OnAddClicked()
	{
		if (_currentBeatmap == null)
			return;

		if (_entries.Any(e => e.SourceFilePath == _currentBeatmap.FilePath))
			return;

		_entries.Add(MapPackEntry.FromOsuFile(_currentBeatmap));
		RefreshList();
	}

	private void OnClearClicked()
	{
		_entries.Clear();
		RefreshList();
	}

	private void OnMsdClicked()
	{
		if (_entries.Count == 0)
			return;
		RecalculateMsdRequested?.Invoke(new List<MapPackEntry>(_entries));
	}

	private void OnApplyBulkClicked()
	{
		if (_entries.Count == 0)
			return;

		var rate = TryParseOptionalRate(_bulkRateTextBox.Text);
		var hp = TryParseOptionalHpOd(_bulkHpTextBox.Text);
		var od = TryParseOptionalHpOd(_bulkOdTextBox.Text);

		if (!rate.HasValue && !hp.HasValue && !od.HasValue)
			return;

		foreach (var entry in _entries)
		{
			if (rate.HasValue)
			{
				entry.Rate = rate.Value;
				entry.RatePitchAdjust = _bulkPitchCheckbox.IsChecked;
			}

			if (hp.HasValue)
				entry.HP = double.IsNaN(hp.Value) ? null : hp;

			if (od.HasValue)
				entry.OD = double.IsNaN(od.Value) ? null : od;
		}

		RefreshList();
	}

	private static double? TryParseOptionalRate(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			return null;

		return Math.Clamp(value, 0.1, 5.0);
	}

	private static double? TryParseOptionalHpOd(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		var trimmed = text.Trim();
		if (trimmed.Equals("orig", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("-", StringComparison.Ordinal))
			return double.NaN;

		if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			return null;

		return Math.Clamp(value, 0, 10);
	}

	private void OnBuildClicked()
	{
		if (_entries.Count == 0)
			return;
		BuildMapPackRequested?.Invoke(BuildDefinitionFromUi());
	}

	private void OnNewPackClicked()
	{
		_entries.Clear();
		_loadedFolderPath = null;
		_resetOnlineIdsOnExport = true;
		_titleTextBox.Text = "Map Pack";
		_ownerTextBox.Text = "Companella";
		_tagsTextBox.Text = "";
		_outputFolderTextBox.Text = "";
		_artistDisplayText.Text = MapPackDefinition.DefaultArtist;
		RefreshList();
	}

	private void OnLoadFolderClicked()
	{
		var thread = new Thread(() =>
		{
			string? selected = null;
			try
			{
				using var dialog = new FolderBrowserDialog
				{
					Description = "Select a map pack folder (beatmapset folder in Songs)",
					UseDescriptionForTitle = true
				};

				if (dialog.ShowDialog() == DialogResult.OK)
					selected = dialog.SelectedPath?.Trim();
			}
			catch (Exception ex)
			{
				Logger.Info($"[MapPack] Folder browser failed: {ex.Message}");
			}

			var path = selected;
			Schedule(() => LoadFolderFromPath(path));
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Name = "MapPackFolderBrowser STA";
		thread.Start();
	}

	private void OnLoadCurrentMapClicked()
	{
		if (_currentBeatmap == null)
			return;

		LoadFolderFromPath(_currentBeatmap.DirectoryPath);
	}

	private void LoadFolderFromPath(string? folderPath)
	{
		if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
			return;

		var service = new MapPackManagerService();
		var definition = MapPackManagerService.LoadFromSidecar(folderPath) ?? MapPackManagerService.LoadFromFolder(folderPath);
		definition.ResetOnlineIdsOnExport = false;
		LoadDefinition(definition);
	}

	private void OnEntryDeleteRequested(MapPackEntry entry)
	{
		_entries.Remove(entry);
		RefreshList();
	}

	private void OnEntryMoveUpRequested(MapPackEntry entry)
	{
		var index = _entries.IndexOf(entry);
		if (index <= 0)
			return;
		(_entries[index - 1], _entries[index]) = (_entries[index], _entries[index - 1]);
		RefreshList();
	}

	private void OnEntryMoveDownRequested(MapPackEntry entry)
	{
		var index = _entries.IndexOf(entry);
		if (index < 0 || index >= _entries.Count - 1)
			return;
		(_entries[index], _entries[index + 1]) = (_entries[index + 1], _entries[index]);
		RefreshList();
	}

	private void OnEntryRateChanged(MapPackEntry entry, double newRate)
	{
		entry.Rate = newRate;
		UpdateSummary();
	}

	private void OnEntryRatePitchAdjustChanged(MapPackEntry entry, bool pitchAdjust)
	{
		entry.RatePitchAdjust = pitchAdjust;
	}

	private void OnEntryVersionOverrideChanged(MapPackEntry entry, string version)
	{
		entry.VersionOverride = version;
	}

	private void OnEntryHpChanged(MapPackEntry entry, double? hp)
	{
		entry.HP = hp;
	}

	private void OnEntryOdChanged(MapPackEntry entry, double? od)
	{
		entry.OD = od;
	}

	private void UpdateSummary()
	{
		var missing = _entries.Count(e => e.SourceMissing);
		var baseText = _entries.Count == 1 ? "1 difficulty" : $"{_entries.Count} difficulties";
		_summaryText.Text = missing > 0 ? $"{baseText} ({missing} missing source)" : baseText;
		_clearButton.Enabled = _entries.Count > 0;
		_msdButton.Enabled = _entries.Count > 0;
		_buildButton.Enabled = _entries.Count > 0;
		_applyBulkButton.Enabled = _entries.Count > 0;
	}

	private static Container CreateBulkField(string label, out StyledTextBox textBox, float width)
	{
		textBox = new StyledTextBox
		{
			Size = new Vector2(width, 28),
			PlaceholderText = label is "HP" or "OD" ? "orig" : "all"
		};

		return new Container
		{
			AutoSizeAxes = Axes.Both,
			Child = new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("Roboto-Regular", 11),
						Colour = new Color4(100, 100, 100, 255)
					},
					textBox
				}
			}
		};
	}

	private static SettingsSection CreateSection(string title, Drawable[] content) =>
		SettingsLayout.CreateSection(title, content);

	private static Container CreateLabeledInput(string label, out StyledTextBox textBox, string defaultValue)
	{
		textBox = new StyledTextBox
		{
			RelativeSizeAxes = Axes.X,
			Height = 32,
			Text = defaultValue
		};

		return new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Child = new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 2),
				Children = new Drawable[]
				{
					new SpriteText
					{
						Text = label,
						Font = new FontUsage("Roboto-Regular", 13),
						Colour = new Color4(100, 100, 100, 255)
					},
					textBox
				}
			}
		};
	}
}

/// <summary>
/// A single row in the map pack difficulty list.
/// </summary>
public partial class MapPackEntryRow : CompositeDrawable
{
	private readonly MapPackEntry _entry;
	private readonly int _index;
	private readonly Color4 _accentColor;

	private Box _background = null!;
	private StyledTextBox _rateTextBox = null!;
	private StyledTextBox _versionTextBox = null!;
	private StyledTextBox _hpTextBox = null!;
	private StyledTextBox _odTextBox = null!;
	private SettingsCheckbox? _pitchCheckbox;

	public event Action<MapPackEntry>? DeleteRequested;
	public event Action<MapPackEntry>? MoveUpRequested;
	public event Action<MapPackEntry>? MoveDownRequested;
	public event Action<MapPackEntry, double>? RateChanged;
	public event Action<MapPackEntry, bool>? RatePitchAdjustChanged;
	public event Action<MapPackEntry, string>? VersionOverrideChanged;
	public event Action<MapPackEntry, double?>? HpChanged;
	public event Action<MapPackEntry, double?>? OdChanged;

	private readonly Color4 _normalBg = new(40, 40, 45, 255);
	private readonly Color4 _hoverBg = new(50, 50, 55, 255);
	private readonly Color4 _missingBg = new(60, 35, 35, 255);
	private readonly Color4 _deleteBg = new(180, 60, 60, 255);
	private readonly Color4 _deleteHoverBg = new(200, 80, 80, 255);

	public MapPackEntryRow(MapPackEntry entry, int index, Color4 accentColor)
	{
		_entry = entry;
		_index = index;
		_accentColor = accentColor;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 4;

		var bgColor = _entry.SourceMissing ? _missingBg : _normalBg;

		_background = new Box
		{
			RelativeSizeAxes = Axes.Both,
			Colour = bgColor
		};

		InternalChildren = new Drawable[]
		{
			_background,
			new GridContainer
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding { Horizontal = 8, Vertical = 6 },
				ColumnDimensions = new[]
				{
					new Dimension(GridSizeMode.Absolute, 22),
					new Dimension(),
					new Dimension(GridSizeMode.Absolute, 218),
					new Dimension(GridSizeMode.Absolute, 96)
				},
				Content = new[]
				{
					new Drawable[]
					{
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Child = new SpriteText
							{
								Text = $"{_index + 1}.",
								Font = new FontUsage("", 15, "Bold"),
								Colour = _entry.SourceMissing ? new Color4(255, 120, 120, 255) : _accentColor,
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							}
						},
						CreateInfoSection(),
						CreateSettingsSection(),
						CreateActionButtonsSection()
					}
				}
			}
		};

		_rateTextBox.Current.BindValueChanged(e => OnRateChanged(e.NewValue));
		_versionTextBox.Current.BindValueChanged(e => VersionOverrideChanged?.Invoke(_entry, e.NewValue));
		_hpTextBox.Current.BindValueChanged(e => HpChanged?.Invoke(_entry, ParseOptionalDouble(e.NewValue)));
		_odTextBox.Current.BindValueChanged(e => OdChanged?.Invoke(_entry, ParseOptionalDouble(e.NewValue)));
		if (_pitchCheckbox != null)
			_pitchCheckbox.CheckedChanged += pitch => RatePitchAdjustChanged?.Invoke(_entry, pitch);
	}

	private void OnRateChanged(string text)
	{
		if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			RateChanged?.Invoke(_entry, Math.Clamp(value, 0.1, 5.0));
	}

	private static double? ParseOptionalDouble(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
			? Math.Clamp(value, 0, 10)
			: null;
	}

	private FillFlowContainer CreateInfoSection()
	{
		var title = _entry.SourceMissing ? "Missing source file" : _entry.SourceTitle;
		var version = _entry.EffectiveVersion;

		return new FillFlowContainer
		{
			RelativeSizeAxes = Axes.Both,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 2),
			Padding = new MarginPadding { Right = 4 },
			Children = new Drawable[]
			{
				new MarqueeText
				{
					Text = title,
					Font = new FontUsage("", 13, "Bold"),
					Colour = Color4.White,
					RelativeSizeAxes = Axes.X,
					Height = 15
				},
				new MarqueeText
				{
					Text = _entry.SourceMissing ? _entry.SourceFilePath : $"[{version}]",
					Font = new FontUsage("", 11),
					Colour = new Color4(140, 140, 140, 255),
					RelativeSizeAxes = Axes.X,
					Height = 13
				},
				new SpriteText
				{
					Text = _entry.SourceMissing ? "" : _entry.RateBpmDisplay,
					Font = new FontUsage("", 11),
					Colour = new Color4(120, 120, 120, 255)
				}
			}
		};
	}

	private FillFlowContainer CreateSettingsSection()
	{
		return new FillFlowContainer
		{
			RelativeSizeAxes = Axes.Y,
			AutoSizeAxes = Axes.X,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(4, 0),
			Anchor = Anchor.CentreLeft,
			Origin = Anchor.CentreLeft,
			Children = new Drawable[]
			{
				CreateMiniInput("Ver", out _versionTextBox, _entry.EffectiveVersion, 58),
				CreateMiniInput("Rate", out _rateTextBox,
					_entry.Rate.ToString("0.0#", CultureInfo.InvariantCulture), 34),
				CreateMiniInput("HP", out _hpTextBox, FormatOptional(_entry.HP), 30),
				CreateMiniInput("OD", out _odTextBox, FormatOptional(_entry.OD), 30),
				new Container
				{
					AutoSizeAxes = Axes.Both,
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Padding = new MarginPadding { Top = 12 },
					Child = _pitchCheckbox = new SettingsCheckbox
					{
						LabelText = "Pitch",
						LabelFontSize = 10,
						LabelColour = new Color4(120, 120, 120, 255),
						IsChecked = _entry.RatePitchAdjust,
						TooltipText = "Pitch follows rate (DT/HT style) when enabled"
					}
				}
			}
		};
	}

	private static string FormatOptional(double? value) =>
		value.HasValue ? value.Value.ToString("0.0#", CultureInfo.InvariantCulture) : "";

	private static FillFlowContainer CreateMiniInput(string label, out StyledTextBox textBox, string value,
		float width)
	{
		textBox = new StyledTextBox
		{
			Size = new Vector2(width, 22),
			Text = value,
			PlaceholderText = label is "HP" or "OD" ? "orig" : ""
		};

		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 1),
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = label,
					Font = new FontUsage("", 10),
					Colour = new Color4(100, 100, 100, 255)
				},
				textBox
			}
		};
	}

	private FillFlowContainer CreateActionButtonsSection()
	{
		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(3, 0),
			Anchor = Anchor.CentreRight,
			Origin = Anchor.CentreRight,
			Children = new Drawable[]
			{
				CreateIconButton("\u25B2", () => MoveUpRequested?.Invoke(_entry)),
				CreateIconButton("\u25BC", () => MoveDownRequested?.Invoke(_entry)),
				CreateIconButton("X", () => DeleteRequested?.Invoke(_entry), _deleteBg, _deleteHoverBg)
			}
		};
	}

	private static StyledButton CreateIconButton(string text, Action onClick, Color4? normal = null,
		Color4? hover = null)
	{
		var button = new StyledButton(text, StyledButtonAppearance.Custom)
		{
			Size = new Vector2(28, 28),
			FontSize = 12,
			UseUnicodeFont = true,
			CustomNormalBg = normal ?? new Color4(55, 55, 60, 255),
			CustomHoverBg = hover ?? new Color4(70, 70, 75, 255),
			ShowAccentBar = false
		};
		button.Clicked += onClick;
		return button;
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (!_entry.SourceMissing)
			_background.FadeColour(_hoverBg, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_background.FadeColour(_entry.SourceMissing ? _missingBg : _normalBg, 100);
		base.OnHoverLost(e);
	}
}
