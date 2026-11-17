using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Services.Beatmap;
using Companella.Services.Platform;
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
/// Panel for migrating scores from Companella session copies to their original maps.
/// </summary>
public partial class ScoreMigrationPanel : CompositeDrawable
{
	[Resolved] private ScoreMigrationService MigrationService { get; set; } = null!;

	[Resolved] private OsuCollectionService CollectionService { get; set; } = null!;

	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	private ConfirmationDialog? _confirmationDialog;

	private StyledButton _migrateButton = null!;
	private StyledButton _cleanupButton = null!;
	private TextFlowContainer _statusText = null!;
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
					"Move scores from session practice copies back to their original beatmaps so your real maps keep the plays.",
					14,
					StyledButton.Theme.MutedLabel),
				SettingsLayout.CreateHint("osu! will restart to save scores, close for migration, then reopen. A backup is created."),
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
								_migrateButton = new StyledButton("Migrate Scores") { RelativeSizeAxes = Axes.X, Height = 32, Action = OnMigrateClicked },
								_cleanupButton = new StyledButton("Delete Session Maps") { AccentColor = StyledButton.Theme.DestructiveAccent, RelativeSizeAxes = Axes.X, Height = 32, Action = OnCleanupClicked }
							}
						},
						_statusText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.MutedLabel, 0),
						_resultText = SettingsLayout.CreateStatusText(15, StyledButton.Theme.Accent, 0)
					}
				}
			}
		};

		InternalChild = new SettingsSection("Score Migration", "Move practice scores to original beatmaps", content);
	}

	private void SetButtonsEnabled(bool enabled)
	{
		_migrateButton.SetEnabled(enabled);
		_cleanupButton.SetEnabled(enabled);
	}

	private void OnMigrateClicked()
	{
		if (_isWorking)
			return;

		// Check if osu! is running and show confirmation
		if (ProcessDetector.IsOsuRunning)
			ShowConfirmationDialog(
				"Force Close osu!?",
				"osu! is currently running. It will be force closed to migrate scores. Continue?",
				() => PerformMigration()
			);
		else
			PerformMigration();
	}

	private void PerformMigration()
	{
		_isWorking = true;
		SetButtonsEnabled(false);
		_statusText.Alpha = 1;
		_statusText.Text = "Restarting osu! to save scores, then migrating...";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var result = MigrationService.MigrateSessionScores();

			Schedule(() =>
			{
				_isWorking = false;
				SetButtonsEnabled(true);
				_statusText.Alpha = 0;

				if (result.Success)
				{
					if (result.ScoresMigrated > 0)
					{
						_resultText.Text =
							$"Migrated {result.ScoresMigrated} scores from {result.MigratedMaps.Count} maps (osu! restarted)";
						_resultText.Colour = new Color4(100, 200, 100, 255);
						_resultText.Alpha = 1;
					}
					else if (result.SessionMapsFound == 0)
					{
						_resultText.Text = "No session copy beatmaps found";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else
					{
						_resultText.Text = $"Found {result.SessionMapsFound} session maps, but no scores to migrate";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
				}
				else
				{
					_resultText.Text = $"Migration failed: {result.Error}";
					_resultText.Colour = new Color4(255, 100, 100, 255);
					_resultText.Alpha = 1;
				}
			});
		});
	}

	private void OnCleanupClicked()
	{
		if (_isWorking)
			return;

		// Check if osu! is running and show confirmation
		if (ProcessDetector.IsOsuRunning)
			ShowConfirmationDialog(
				"Force Close osu!?",
				"osu! is currently running. It will be force closed to cleanup session maps. Continue?",
				() => PerformCleanup()
			);
		else
			PerformCleanup();
	}

	private void PerformCleanup()
	{
		_isWorking = true;
		SetButtonsEnabled(false);
		_statusText.Alpha = 1;
		_statusText.Text = "Restarting osu! to save scores, migrating, then deleting...";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var result = MigrationService.CleanupSessionMaps();

			Schedule(() =>
			{
				_isWorking = false;
				SetButtonsEnabled(true);
				_statusText.Alpha = 0;

				if (result.Success)
				{
					if (result.FilesDeleted > 0)
					{
						var migrationInfo = result.MigrationResult?.ScoresMigrated > 0
							? $" ({result.MigrationResult.ScoresMigrated} scores migrated)"
							: "";
						var message = $"Deleted {result.FilesDeleted} session maps{migrationInfo} - osu! restarted";
						if (result.FilesFailed > 0)
							message += $" ({result.FilesFailed} failed)";

						_resultText.Text = message;
						_resultText.Colour = new Color4(100, 200, 100, 255);
						_resultText.Alpha = 1;
					}
					else
					{
						_resultText.Text = "No session maps found to delete";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
				}
				else
				{
					_resultText.Text = $"Cleanup failed: {result.Error}";
					_resultText.Colour = new Color4(255, 100, 100, 255);
					_resultText.Alpha = 1;
				}
			});
		});
	}

	private void ShowConfirmationDialog(string title, string message, Action onConfirm)
	{
		if (_confirmationDialog == null)
		{
			_confirmationDialog = new ConfirmationDialog();
			AddInternal(_confirmationDialog);
		}

		_confirmationDialog.Confirmed -= onConfirm;
		_confirmationDialog.Confirmed += onConfirm;
		_confirmationDialog.Show(title, message, true);
	}
}
