using Companella.Components.Misc;
using Companella.Services.Integrations.ManiaTracker;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>Account management and optional automatic uploads for Mania Tracker.</summary>
public partial class ManiaTrackerSettingsPanel : CompositeDrawable
{
	[Resolved] private ManiaTrackerService Tracker { get; set; } = null!;
	private TextFlowContainer _account = null!;
	private TextFlowContainer _connection = null!;
	private TextFlowContainer _status = null!;
	private TextFlowContainer _queue = null!;
	private TextFlowContainer _capture = null!;
	private TextFlowContainer _confirmationText = null!;
	private Container _details = null!;
	private Container _confirmation = null!;
	private StyledButton _connect = null!;
	private StyledButton _cancel = null!;
	private StyledButton _disconnect = null!;
	private StyledButton _uploads = null!;
	private StyledButton _retry = null!;
	private StyledButton _clear = null!;
	private StyledButton _confirm = null!;
	private StyledButton _dismiss = null!;
	private StyledButton _detailsButton = null!;
	private bool _busy;
	private bool _signingIn;
	private bool _showDetails;
	private string? _error;
	private string? _pendingAction;
	private readonly Dictionary<TextFlowContainer, string> _displayedText = new();

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
		_connect = Button("Sign in", () => RunAsync(() => Tracker.ConnectAsync(false), true), StyledButtonAppearance.Filled);
		_cancel = Button("Cancel sign-in", Tracker.CancelConnection);
		_disconnect = Button("Disconnect", () => Confirm("disconnect"));
		_uploads = Button("Enable uploads", () =>
		{
			if (_busy || !Tracker.Connected) return;
			try { Tracker.SetEnabled(!Tracker.Enabled); _error = null; }
			catch (Exception) { _error = "Could not save your upload preference. Please try again."; }
			Refresh();
		}, StyledButtonAppearance.Toggle);
		_uploads.TooltipText = "Pause or resume capture and uploads. Pending plays are kept when paused.";
		_retry = Button("Retry uploads", () => RunAsync(Tracker.RetryPendingAsync));
		_clear = Button("Clear pending files", () => Confirm("clear"));
		_clear.AccentColor = StyledButton.Theme.DestructiveAccent;
		_confirm = Button("Confirm", () =>
		{
			var action = _pendingAction;
			_pendingAction = null;
			if (action == "clear") RunAsync(Tracker.ClearPendingAsync);
			else if (action == "disconnect") RunAsync(Tracker.DisconnectAsync);
		});
		_confirm.AccentColor = StyledButton.Theme.DestructiveAccent;
		_dismiss = Button("Keep as is", () => { _pendingAction = null; Refresh(); });
		_detailsButton = Button("Show upload details", () => { _showDetails = !_showDetails; Refresh(); });

		InternalChild = new SettingsSection("Mania Tracker", "Your osu!mania plays, automatically in sync.", Stack(
			Card(
				_connection = SettingsLayout.CreateWrappingText("", 12, StyledButton.Theme.Accent, "Bold"),
				_account = SettingsLayout.CreateWrappingText("", 20, Color4.White, "Bold"),
				SettingsLayout.CreateWrappingText("Connect securely in your browser. Companella never asks for your password.", 13, StyledButton.Theme.MutedLabel),
				Actions(_connect, _cancel, _disconnect,
					Button("Visit Mania Tracker", () => RunAsync(() =>
					{
						ManiaTrackerApiClient.OpenPage(ManiaTrackerApiClient.Origin + "/companella");
						return Task.CompletedTask;
					})))),
			Card(
				SettingsLayout.CreateSubHeading("AUTOMATIC UPLOADS"),
				SettingsLayout.CreateWrappingText("Send new completed osu!stable mania plays without starting a practice session.", 14, StyledButton.Theme.MutedLabel),
				Actions(_uploads),
				SettingsLayout.CreateWrappingText("Enabling uploads shares the original replay, including your player name and inputs, and its beatmap with mania-tracker.com. Partner ratings are experimental.", 12, StyledButton.Theme.MutedLabel),
				_status = SettingsLayout.CreateStatusText(13, StyledButton.Theme.MutedLabel),
				Actions(_detailsButton)),
			_details = Card(
				SettingsLayout.CreateSubHeading("UPLOAD ACTIVITY"),
				_capture = SettingsLayout.CreateStatusText(13, StyledButton.Theme.MutedLabel),
				_queue = SettingsLayout.CreateStatusText(13, StyledButton.Theme.MutedLabel),
				SettingsLayout.CreateWrappingText("If a play is waiting for its replay, save the replay from the osu! results screen.", 12, StyledButton.Theme.MutedLabel),
				Actions(_retry, _clear)),
			_confirmation = Card(
				_confirmationText = SettingsLayout.CreateStatusText(14, StyledButton.Theme.MutedLabel),
				Actions(_confirm, _dismiss))
		));
		Scheduler.AddDelayed(Refresh, 500, true);
		Refresh();
	}

	private static FillFlowContainer Stack(params Drawable[] children) => new()
	{
		RelativeSizeAxes = Axes.X,
		AutoSizeAxes = Axes.Y,
		Direction = FillDirection.Vertical,
		Spacing = new Vector2(0, 12),
		Children = children
	};

	private static Container Card(params Drawable[] children) => new()
	{
		RelativeSizeAxes = Axes.X,
		AutoSizeAxes = Axes.Y,
		Masking = true,
		CornerRadius = 6,
		Children = new Drawable[]
		{
			new Box { RelativeSizeAxes = Axes.Both, Colour = StyledButton.Theme.DialogInsetBg },
			new Container
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Padding = new MarginPadding(14),
				Child = Stack(children)
			}
		}
	};

	// Wrap actions onto another line when the settings column is narrow.
	private static FillFlowContainer Actions(params Drawable[] children) => new()
	{
		RelativeSizeAxes = Axes.X,
		AutoSizeAxes = Axes.Y,
		Direction = FillDirection.Full,
		Spacing = new Vector2(8),
		Children = children
	};

	private static StyledButton Button(string text, Action action, StyledButtonAppearance appearance = StyledButtonAppearance.Muted) =>
		new(text, appearance)
		{
			Size = new Vector2(180, 36),
			FontSize = 13,
			Action = action
		};

	private void Confirm(string action)
	{
		if (_busy) return;
		_pendingAction = action;
		Refresh();
	}

	private void RunAsync(Func<Task> action, bool signingIn = false)
	{
		if (_busy) return;
		_busy = true;
		_signingIn = signingIn;
		_pendingAction = null;
		_error = null;
		Refresh();
		// File work and network operations never block the drawable update thread.
		_ = Task.Run(() => ExecuteAsync(action));
	}

	private async Task ExecuteAsync(Func<Task> action)
	{
		try { await action(); }
		catch (Exception) { Schedule(() => _error = "Could not complete this operation. Please try again; pending plays are retained."); }
		finally { Schedule(() => { _busy = false; _signingIn = false; Refresh(); }); }
	}

	private void SetText(TextFlowContainer target, string value)
	{
		if (_displayedText.TryGetValue(target, out var previous) && previous == value) return;
		target.Text = value;
		_displayedText[target] = value;
	}

	private void Refresh()
	{
		var connected = Tracker.Connected;
		var signingIn = _signingIn || Tracker.Connecting;
		SetText(_connection, signingIn ? "SIGNING IN" : connected ? "ACCOUNT CONNECTED" : "NOT CONNECTED");
		SetText(_account, connected ? Tracker.Account : signingIn ? "Continue in your browser" : "Connect your account");
		SetText(_status, _error ?? (signingIn ? "Finish signing in in your browser. You can cancel below the account name." : Tracker.Status));
		_status.Colour = _error == null ? Color4.White : StyledButton.Theme.DangerFill;
		SetText(_capture, Tracker.CaptureStatus);
		SetText(_queue, Tracker.Summary);
		_connect.Alpha = !connected && !signingIn ? 1 : 0;
		_connect.Enabled = !_busy;
		_cancel.Alpha = signingIn ? 1 : 0;
		_disconnect.Alpha = connected ? 1 : 0;
		_disconnect.Enabled = !_busy;
		_uploads.Enabled = connected && !_busy && _pendingAction == null;
		_uploads.Selected = Tracker.Enabled;
		_uploads.Text = Tracker.Enabled ? "Uploads on · Pause" : "Enable uploads";
		_uploads.TooltipText = connected
			? "Pause or resume capture and uploads. Pending plays are kept when paused."
			: "Sign in to enable automatic uploads.";
		_retry.Enabled = connected && Tracker.Enabled && !_busy && _pendingAction == null;
		_clear.Enabled = !_busy && _pendingAction == null;
		_details.Alpha = _showDetails ? 1 : 0;
		_detailsButton.Text = _showDetails ? "Hide upload details" : "Show upload details";
		_confirmation.Alpha = _pendingAction != null ? 1 : 0;
		_confirm.Enabled = _dismiss.Enabled = !_busy;
		if (_pendingAction != null)
		{
			var clear = _pendingAction == "clear";
			SetText(_confirmationText, clear
				? "Clear pending uploads? This removes local replay and beatmap copies from the queue and pauses uploads. Plays already sent to Mania Tracker may remain there."
				: "Disconnect this account? Uploads will pause. Pending files stay on this device, but cannot be sent through a new connection.");
			_confirm.Text = clear ? "Clear pending files" : "Disconnect account";
		}
	}
}
