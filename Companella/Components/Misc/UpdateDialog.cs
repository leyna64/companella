using Companella.Components.Layout;
using Companella.Models.Application;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// A dialog for displaying update information and progress.
/// </summary>
public partial class UpdateDialog : CompositeDrawable
{
	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private SpriteText _versionText = null!;
	private TextFlowContainer _descriptionText = null!;
	private SpriteText _statusText = null!;
	private Container _progressBarContainer = null!;
	private Box _progressBar = null!;
	private StyledButton _updateButton = null!;
	private StyledButton _cancelButton = null!;
	private StyledButton _laterButton = null!;

	private UpdateInfo? _updateInfo;
	private SquirrelUpdaterService? _updaterService;
	private bool _isDownloading;

	/// <summary>
	/// Event raised when the dialog is closed.
	/// </summary>
	public event Action? Closed;

	/// <summary>
	/// Event raised when the user requests to apply the update and restart.
	/// </summary>
	public event Action? UpdateRequested;

	public UpdateDialog()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		var descriptionSection = StyledDialog.CreateInsetSection(100);
		descriptionSection.Add(new ChainedScrollContainer
		{
			RelativeSizeAxes = Axes.Both,
			Padding = new MarginPadding(10),
			Child = _descriptionText = new TextFlowContainer
			{
				AutoSizeAxes = Axes.Y,
				RelativeSizeAxes = Axes.X,
				TextAnchor = Anchor.TopLeft
			}
		});

		InternalChildren = new Drawable[]
		{
			StyledDialog.CreateDimBackground(),
			_dialogContainer = StyledDialog.CreateShell(new Vector2(420, 320), out var content)
		};

		content.Child = new FillFlowContainer
		{
			RelativeSizeAxes = Axes.Both,
			Direction = FillDirection.Vertical,
			Spacing = new Vector2(0, 12),
			Children = new Drawable[]
			{
				_titleText = StyledDialog.CreateTitle("Update Available"),
				_versionText = StyledDialog.CreateBodyText("New version available"),
				new Container
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					Size = new Vector2(380, 100),
					Child = descriptionSection
				},
				_statusText = new SpriteText
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					Text = string.Empty,
					Font = new FontUsage("", 14),
					Colour = StyledButton.Theme.MutedLabel
				},
				_progressBarContainer = new Container
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					Size = new Vector2(380, 6),
					Masking = true,
					CornerRadius = StyledDialog.CornerRadius,
					Alpha = 0,
					Children = new Drawable[]
					{
						new Box
						{
							RelativeSizeAxes = Axes.Both,
							Colour = StyledDialog.InsetBg
						},
						_progressBar = new Box
						{
							RelativeSizeAxes = Axes.Y,
							Width = 0,
							Colour = StyledButton.Theme.Accent
						}
					}
				},
				new FillFlowContainer
				{
					Anchor = Anchor.TopCentre,
					Origin = Anchor.TopCentre,
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(10, 0),
					Margin = new MarginPadding { Top = 8 },
					Children = new Drawable[]
					{
						_laterButton = StyledDialog.CreateSecondaryButton("Later"),
						_cancelButton = StyledDialog.CreateCancelButton(),
						_updateButton = new StyledButton("Update Now") { Size = new Vector2(120, 36) }
					}
				}
			}
		};

		_laterButton.Clicked += OnLaterClicked;
		_cancelButton.Clicked += OnCancelClicked;
		_updateButton.Clicked += OnUpdateClicked;
		_cancelButton.Alpha = 0;
	}

	/// <summary>
	/// Shows the update dialog with the specified update information.
	/// </summary>
	public void Show(UpdateInfo updateInfo, SquirrelUpdaterService updaterService)
	{
		_updateInfo = updateInfo;
		_updaterService = updaterService;
		_isDownloading = false;

		_versionText.Text = $"Version {updateInfo.TagName} is available (Current: {updaterService.CurrentVersion})";

		_descriptionText.Clear();
		if (!string.IsNullOrWhiteSpace(updateInfo.Body))
			_descriptionText.AddText(updateInfo.Body, t =>
			{
				t.Font = new FontUsage("", 15);
				t.Colour = StyledButton.Theme.MutedLabel;
			});
		else
			_descriptionText.AddText("No release notes available.", t =>
			{
				t.Font = new FontUsage("", 15);
				t.Colour = StyledButton.Theme.DisabledLabel;
			});

		_statusText.Text = $"Download size: {FormatBytes(updateInfo.DownloadSize)}";

		_progressBarContainer.Alpha = 0;
		_progressBar.Width = 0;
		_laterButton.Alpha = 1;
		_cancelButton.Alpha = 0;
		_updateButton.Alpha = 1;
		_updateButton.Enabled = true;
		_updateButton.Text = "Update Now";

		if (_updaterService != null)
			_updaterService.DownloadProgressChanged += OnDownloadProgress;

		StyledDialog.PlayShowAnimation(this, _dialogContainer);
	}

	/// <summary>
	/// Hides the update dialog.
	/// </summary>
	public new void Hide()
	{
		if (_updaterService != null)
			_updaterService.DownloadProgressChanged -= OnDownloadProgress;

		StyledDialog.PlayHideAnimation(this);
		Closed?.Invoke();
	}

	private void OnLaterClicked()
	{
		if (!_isDownloading)
			Hide();
	}

	private void OnCancelClicked()
	{
		if (!_isDownloading)
			return;

		SquirrelUpdaterService.CancelDownload();
		_isDownloading = false;

		_statusText.Text = "Download cancelled";
		_progressBarContainer.FadeOut(200);
		_laterButton.FadeIn(200);
		_cancelButton.FadeOut(200);
		_updateButton.FadeIn(200);
		_updateButton.Enabled = true;
		_updateButton.Text = "Update Now";
	}

	private async void OnUpdateClicked()
	{
		if (_updateInfo == null || _updaterService == null)
			return;

		if (_updateButton.Text == "Restart Now")
		{
			UpdateRequested?.Invoke();
			_updaterService.StartUpdateAndRestart(_updateInfo);
			return;
		}

		_isDownloading = true;

		_laterButton.FadeOut(200);
		_cancelButton.FadeIn(200);
		_updateButton.Enabled = false;
		_progressBarContainer.FadeIn(200);
		_statusText.Text = "Starting download...";

		var progress = new Progress<DownloadProgressEventArgs>(args =>
		{
			Schedule(() =>
			{
				_progressBar.ResizeWidthTo(args.ProgressPercentage * 3.8f, 100);
				_statusText.Text = args.Status;
			});
		});

		var success = await _updaterService.DownloadAndApplyUpdateAsync(_updateInfo, progress);

		Schedule(() =>
		{
			_isDownloading = false;

			if (success)
			{
				_statusText.Text = "Update ready! Restart to apply.";
				_progressBar.ResizeWidthTo(380, 100);
				_cancelButton.FadeOut(200);
				_updateButton.FadeIn(200);
				_updateButton.Enabled = true;
				_updateButton.Text = "Restart Now";
			}
			else
			{
				_statusText.Text = "Download failed. Please try again.";
				_progressBarContainer.FadeOut(200);
				_laterButton.FadeIn(200);
				_cancelButton.FadeOut(200);
				_updateButton.FadeIn(200);
				_updateButton.Enabled = true;
				_updateButton.Text = "Retry";
			}
		});
	}

	private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
	{
		Schedule(() =>
		{
			_progressBar.ResizeWidthTo(e.ProgressPercentage * 3.8f, 50);
			_statusText.Text = e.Status;
		});
	}

	private static string FormatBytes(long bytes)
	{
		string[] sizes = { "B", "KB", "MB", "GB" };
		var order = 0;
		double size = bytes;

		while (size >= 1024 && order < sizes.Length - 1)
		{
			order++;
			size /= 1024;
		}

		return $"{size:0.##} {sizes[order]}";
	}

	protected override bool OnClick(ClickEvent e) => true;
}
