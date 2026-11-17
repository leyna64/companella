using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuTextBox = osu.Framework.Graphics.UserInterface.TextBox;
using osuTK;
using osuTK.Graphics;
using System.Threading;
using System.Windows.Forms;

namespace Companella.Components.Settings;

/// <summary>
/// Settings for automatic osu! directory detection vs manual path (Songs folder parent).
/// </summary>
public partial class OsuDirectorySettingsPanel : CompositeDrawable
{
	private const double _autoPathRefreshSeconds = 1.0;

	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	private SettingsCheckbox _autoDetectCheckbox = null!;
	private TextFlowContainer _readOnlyPathText = null!;
	private BasicTextBox _manualPathTextBox = null!;
	private StyledButton _browseButton = null!;
	private FillFlowContainer _autoPathRow = null!;
	private FillFlowContainer _manualPathRow = null!;

	private double _lastAutoPathRefresh;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		var settings = SettingsService.Settings;

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				_autoDetectCheckbox = new SettingsCheckbox
				{
					LabelText = "Automatically detect osu! songs directory",
					IsChecked = settings.AutoDetectOsuDirectory,
					TooltipText = "Find the osu! folder from the running game, cache, or default install location"
				},
				SettingsLayout.CreateHint("osu! directory (contains Songs folder):"),
				_autoPathRow = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 4),
					Children = new Drawable[]
					{
						_readOnlyPathText = SettingsLayout.CreateWrappingText(
							GetAutoPathDisplayText(),
							14,
							StyledButton.Theme.MutedLabel)
					}
				},
				_manualPathRow = new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8),
					Children = new Drawable[]
					{
						new Container
						{
							Height = 32,
							RelativeSizeAxes = Axes.X,
							Masking = true,
							CornerRadius = StyledDialog.CornerRadius,
							Children = new Drawable[]
							{
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = StyledButton.Theme.DialogInsetBg
								},
								_manualPathTextBox = new BasicTextBox
								{
									RelativeSizeAxes = Axes.Both,
									Text = settings.CachedOsuDirectory ?? string.Empty,
									PlaceholderText = "Path to osu! installation folder",
									CommitOnFocusLost = true
								}
							}
						},
						_browseButton = new StyledButton("Browse")
						{
							Width = 90,
							Height = 32,
							TooltipText = "Choose the folder that contains the Songs subfolder"
						}
					}
				}
			}
		};

		InternalChild = new SettingsSection("osu! Directory", "Where Companella looks for your beatmaps and replays", content);

		_autoDetectCheckbox.CheckedChanged += OnAutoDetectChanged;
		_manualPathTextBox.OnCommit += OnManualPathCommit;
		_browseButton.Clicked += OnBrowseClicked;
		UpdatePathRowVisibility();
	}

	private string GetAutoPathDisplayText()
	{
		var path = ProcessDetector.GetOsuDirectory();
		return string.IsNullOrEmpty(path) ? "(not detected)" : path;
	}

	private void OnAutoDetectChanged(bool isChecked)
	{
		if (!isChecked && string.IsNullOrWhiteSpace(SettingsService.Settings.CachedOsuDirectory))
		{
			var detected = ProcessDetector.GetOsuDirectory();
			if (!string.IsNullOrEmpty(detected))
				SettingsService.Settings.CachedOsuDirectory = detected;
		}

		SettingsService.Settings.AutoDetectOsuDirectory = isChecked;
		SaveSettings();

		if (!isChecked)
			_manualPathTextBox.Text = SettingsService.Settings.CachedOsuDirectory ?? string.Empty;

		UpdatePathRowVisibility();
		if (isChecked)
			_readOnlyPathText.Text = GetAutoPathDisplayText();
	}

	private void OnManualPathCommit(OsuTextBox sender, bool newText)
	{
		if (SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		var trimmed = (_manualPathTextBox.Text ?? string.Empty).Trim();
		SettingsService.Settings.CachedOsuDirectory = string.IsNullOrEmpty(trimmed) ? null : trimmed;
		SaveSettings();
	}

	private void OnBrowseClicked()
	{
		if (SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		var initialDir = SettingsService.Settings.CachedOsuDirectory;
		if (string.IsNullOrEmpty(initialDir) || !Directory.Exists(initialDir))
			initialDir = null;

		var thread = new Thread(() =>
		{
			string? selected = null;
			try
			{
				using var dialog = new FolderBrowserDialog
				{
					Description = "Select your osu! installation folder (the folder that contains Songs)",
					UseDescriptionForTitle = true
				};

				if (initialDir != null)
					dialog.InitialDirectory = initialDir;

				if (dialog.ShowDialog() == DialogResult.OK)
					selected = dialog.SelectedPath?.Trim();
			}
			catch (Exception ex)
			{
				Logger.Info($"[Settings] Folder browser failed: {ex.Message}");
			}

			var path = selected;
			Schedule(() => ApplyBrowseSelection(path));
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Name = "FolderBrowserDialog STA";
		thread.Start();
	}

	private void ApplyBrowseSelection(string? selected)
	{
		if (SettingsService.Settings.AutoDetectOsuDirectory || string.IsNullOrEmpty(selected))
			return;

		var songsPath = Path.Combine(selected, "Songs");
		if (!Directory.Exists(songsPath))
			Logger.Info("[Settings] Selected osu! folder has no Songs subfolder; path saved anyway.");

		SettingsService.Settings.CachedOsuDirectory = selected;
		_manualPathTextBox.Text = selected;
		SaveSettings();
	}

	private void UpdatePathRowVisibility()
	{
		var auto = SettingsService.Settings.AutoDetectOsuDirectory;
		_autoPathRow.Alpha = auto ? 1 : 0;
		_manualPathRow.Alpha = auto ? 0 : 1;

		if (auto)
			_readOnlyPathText.Text = GetAutoPathDisplayText();
		else
			_manualPathTextBox.Text = SettingsService.Settings.CachedOsuDirectory ?? string.Empty;
	}

	protected override void Update()
	{
		base.Update();

		if (!SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		var now = Clock.CurrentTime;
		if (now - _lastAutoPathRefresh < _autoPathRefreshSeconds)
			return;

		_lastAutoPathRefresh = now;
		_readOnlyPathText.Text = GetAutoPathDisplayText();
	}

	private void SaveSettings() => Task.Run(async () => await SettingsService.SaveAsync());
}
