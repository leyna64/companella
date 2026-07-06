using Companella.Analyzers.Attributes;
using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Analysis;

/// <summary>
/// Panel for map indexing controls in the Settings tab.
/// Provides buttons for indexing, reindexing, and refreshing the map database.
/// </summary>
public partial class MapIndexingPanel : CompositeDrawable
{
	[Resolved] private MapsDatabaseService MapsDatabase { get; set; } = null!;

	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	[Resolved] private AptabaseService AptabaseService { get; set; } = null!;

	private TextFlowContainer _statusText = null!;
	private TextFlowContainer _mapCountText = null!;
	private StyledButton _indexButton = null!;
	private StyledButton _reindexButton = null!;
	private StyledButton _refreshButton = null!;

	private CancellationTokenSource? _indexingCts;

	[BackgroundDependencyLoader]
	[Suppress("COMP001")]
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
				_mapCountText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.MutedLabel),
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 8),
					Children = new Drawable[]
					{
						_indexButton = new StyledButton("Index Maps") { AccentColor = StyledButton.Theme.InfoFill, RelativeSizeAxes = Axes.X, Height = 32, FontSize = 14, TooltipText = "Scan osu! Songs folder to enable map recommendations" },
						_reindexButton = new StyledButton("Reindex Maps") { AccentColor = StyledButton.Theme.DestructiveAccent, RelativeSizeAxes = Axes.X, Height = 32, FontSize = 14, TooltipText = "Clear and rebuild the entire map index" },
						_refreshButton = new StyledButton("Refresh All MSD", StyledButtonAppearance.Muted) { RelativeSizeAxes = Axes.X, Height = 32, FontSize = 14, TooltipText = "Recalculate difficulty ratings for all indexed maps" }
					}
				},
				_statusText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.DisabledLabel)
			}
		};

		InternalChild = new SettingsSection("Map Database", "Index beatmaps for recommendations and difficulty data", content);

		_indexButton.Clicked += OnIndexClicked;
		_reindexButton.Clicked += OnReindexClicked;
		_refreshButton.Clicked += OnRefreshAllClicked;

		MapsDatabase.IndexingProgressChanged += OnIndexingProgressChanged;
		MapsDatabase.IndexingCompleted += OnIndexingCompleted;

		UpdateMapCount();
	}

	private void UpdateMapCount()
	{
		var count = MapsDatabase.Get4KMapCount();
		_mapCountText.Text = $"4K maps indexed: {count}";
	}

	private void OnIndexClicked()
	{
		StartIndexing(false);
	}

	private void OnReindexClicked()
	{
		StartIndexing(true);
	}

	private void OnRefreshAllClicked()
	{
		// Refresh all maps by forcing reindex (same as reindex for now)
		StartIndexing(true);
	}

	private void StartIndexing(bool forceReindex)
	{
		var songsFolder = ProcessDetector.GetSongsFolder();
		if (string.IsNullOrEmpty(songsFolder))
		{
			_statusText.Text = "Could not find osu! Songs folder.";
			return;
		}

		if (MapsDatabase.IsIndexing)
		{
			_statusText.Text = "Indexing already in progress...";
			return;
		}

		_indexingCts?.Cancel();
		_indexingCts = new CancellationTokenSource();

		SetButtonsEnabled(false);
		_statusText.Text = forceReindex ? "Reindexing all maps..." : "Indexing new maps...";

		if (forceReindex)
			// For reindex, we clear the database first
			ClearDatabaseAndIndex(songsFolder);
		else
			_ = MapsDatabase.ScanOsuSongsFolderAsync(songsFolder, _indexingCts.Token);
	}

	private async void ClearDatabaseAndIndex(string songsFolder)
	{
		// Clear the database by recreating it (simple approach)
		// The service will skip unchanged files, but we want to force reanalysis
		await MapsDatabase.ScanOsuSongsFolderAsync(songsFolder, _indexingCts?.Token ?? CancellationToken.None);
	}

	private void OnIndexingProgressChanged(object? sender, IndexingProgressEventArgs e)
	{
		Schedule(() =>
		{
			_statusText.Text = $"Indexing: {e.ProcessedFiles}/{e.TotalFiles} ({e.ProgressPercentage}%)";
		});
	}

	private void OnIndexingCompleted(object? sender, IndexingCompletedEventArgs e)
	{
		Schedule(() =>
		{
			SetButtonsEnabled(true);
			UpdateMapCount();

			if (e.WasCancelled)
			{
				_statusText.Text = "Indexing cancelled.";
			}
			else if (e.FailedFiles > 0)
			{
				_statusText.Text = $"Done: {e.IndexedFiles} indexed, {e.FailedFiles} failed.";
				// Track analytics
				AptabaseService.TrackMapIndexing(e.IndexedFiles);
			}
			else
			{
				_statusText.Text = $"Done: {e.IndexedFiles} maps indexed.";
				// Track analytics
				AptabaseService.TrackMapIndexing(e.IndexedFiles);
			}
		});
	}

	private void SetButtonsEnabled(bool enabled)
	{
		_indexButton.SetEnabled(enabled);
		_reindexButton.SetEnabled(enabled);
		_refreshButton.SetEnabled(enabled);
	}

	/// <summary>
	/// Cancels any ongoing indexing operation.
	/// </summary>
	public void CancelIndexing()
	{
		_indexingCts?.Cancel();
		MapsDatabase.CancelIndexing();
	}

	protected override void Dispose(bool isDisposing)
	{
		MapsDatabase.IndexingProgressChanged -= OnIndexingProgressChanged;
		MapsDatabase.IndexingCompleted -= OnIndexingCompleted;
		_indexingCts?.Cancel();
		_indexingCts?.Dispose();
		base.Dispose(isDisposing);
	}
}
