using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Services.Session;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Panel for importing older scores from scores.db as Companella sessions.
/// Only imports scores that have corresponding .osr replay files.
/// </summary>
public partial class ScoreImportPanel : CompositeDrawable
{
	[Resolved] private ScoreImportService ImportService { get; set; } = null!;

	[Resolved] private ReplayFileWatcherService ReplayWatcherService { get; set; } = null!;

	private StyledButton _importButton = null!;
	private StyledButton _reimportReplaysButton = null!;
	private TextFlowContainer _statusText = null!;
	private TextFlowContainer _progressText = null!;
	private TextFlowContainer _resultText = null!;
	private bool _isWorking;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		var content = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 10),
			Children = new Drawable[]
			{
				SettingsLayout.CreateWrappingText(
					"Import osu!mania scores from scores.db as Companella sessions. Only scores with replay files are imported, grouped by calendar day.",
					14,
					StyledButton.Theme.MutedLabel),
				SettingsLayout.CreateHint("MSD calculation runs for each play and may take a while for large imports."),
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 6),
					Children = new Drawable[]
					{
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 8),
							Children = new Drawable[]
							{
								_importButton = new StyledButton("Import Scores as Sessions") { RelativeSizeAxes = Axes.X, Height = 32, Action = OnImportClicked },
								_reimportReplaysButton = new StyledButton("Find Missing Replays") { AccentColor = StyledButton.Theme.InfoFill, RelativeSizeAxes = Axes.X, Height = 32, Action = OnFindMissingReplaysClicked }
							}
						},
						_statusText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.MutedLabel, 0),
						_progressText = SettingsLayout.CreateStatusText(13, StyledButton.Theme.DisabledLabel, 0),
						_resultText = SettingsLayout.CreateStatusText(15, StyledButton.Theme.Accent, 0)
					}
				}
			}
		};

		InternalChild = new SettingsSection("Score Import", "Bring historical osu! scores into Companella sessions", content);
	}

	private void OnImportClicked()
	{
		if (_isWorking)
			return;

		_isWorking = true;
		_importButton.SetEnabled(false);
		_statusText.Alpha = 1;
		_statusText.Text = "Starting import...";
		_progressText.Alpha = 1;
		_progressText.Text = "";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var result = ImportService.ImportScoresAsSessions(progress =>
			{
				Schedule(() =>
				{
					_statusText.Text = progress.Stage;
					if (!string.IsNullOrEmpty(progress.CurrentItem))
						_progressText.Text = progress.CurrentItem;
					else if (progress.Total > 0) _progressText.Text = $"{progress.Current}/{progress.Total}";
				});
			});

			Schedule(() =>
			{
				_isWorking = false;
				_importButton.SetEnabled(true);
				_statusText.Alpha = 0;
				_progressText.Alpha = 0;

				if (result.Success)
				{
					if (result.SessionsCreated > 0)
					{
						_resultText.Text =
							$"Imported {result.PlaysImported} plays into {result.SessionsCreated} sessions";
						_resultText.Colour = new Color4(100, 200, 100, 255);
						_resultText.Alpha = 1;
					}
					else if (result.ManiaScoresFound == 0)
					{
						_resultText.Text = "No osu!mania scores found in scores.db";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else if (result.ScoresWithReplays == 0)
					{
						_resultText.Text = $"Found {result.ManiaScoresFound} mania scores, but none have replay files";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else if (result.ScoresWithBeatmaps == 0)
					{
						_resultText.Text =
							$"Found {result.ScoresWithReplays} scores with replays, but could not find beatmaps";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else
					{
						_resultText.Text = "Import completed with no sessions created";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
				}
				else
				{
					_resultText.Text = $"Import failed: {result.Error}";
					_resultText.Colour = new Color4(255, 100, 100, 255);
					_resultText.Alpha = 1;
				}
			});
		});
	}

	private void OnFindMissingReplaysClicked()
	{
		if (_isWorking)
			return;

		_isWorking = true;
		_importButton.SetEnabled(false);
		_reimportReplaysButton.SetEnabled(false);
		_statusText.Alpha = 1;
		_statusText.Text = "Finding missing replays...";
		_progressText.Alpha = 1;
		_progressText.Text = "";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var foundCount = ReplayWatcherService.FindAllMissingReplays((matched, total) =>
			{
				Schedule(() => { _progressText.Text = $"Found {matched} of {total} checked..."; });
			});

			Schedule(() =>
			{
				_isWorking = false;
				_importButton.SetEnabled(true);
				_reimportReplaysButton.SetEnabled(true);
				_statusText.Alpha = 0;
				_progressText.Alpha = 0;

				if (foundCount > 0)
				{
					_resultText.Text = $"Found {foundCount} missing replays";
					_resultText.Colour = new Color4(100, 200, 100, 255);
				}
				else
				{
					_resultText.Text = "No missing replays found";
					_resultText.Colour = new Color4(160, 160, 160, 255);
				}

				_resultText.Alpha = 1;
			});
		});
	}
}
