using Companella.Components.Misc;
using Companella.Components.Settings;
using Companella.Services.Integrations.ManiaTracker;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Localisation;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Layout;

/// <summary>Global account shortcut with a dismissible Mania Tracker menu.</summary>
public partial class ManiaTrackerAccountMenu : CompositeDrawable
{
	[Resolved] private ManiaTrackerService Tracker { get; set; } = null!;
	public Action? ManageAccountRequested { get; set; }
	private AccountControl _account = null!;
	private MenuAction _disconnect = null!;
	private MenuAction _cancel = null!;
	private MenuAction _uploads = null!;
	private TextFlowContainer _identity = null!;
	private TextFlowContainer _status = null!;
	private Container _menu = null!;
	private ClickableContainer _dismiss = null!;
	private bool _open;
	private bool _busy;
	private bool _confirmDisconnect;
	private string _lastStatus = "";
	private string? _error;
	private string? _operationResult;
	private bool? _lastUploadsEnabled;

	public ManiaTrackerAccountMenu()
	{
		RelativeSizeAxes = Axes.Both;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			_dismiss = new ClickableContainer
			{
				RelativeSizeAxes = Axes.Both,
				Action = CloseMenu,
				Alpha = 0
			},
			_account = new AccountControl
			{
				Anchor = Anchor.TopRight,
				Origin = Anchor.TopRight,
				Position = new Vector2(-88, 3),
				Size = new Vector2(168, 25),
				TooltipText = "Mania Tracker account",
				Action = () =>
				{
					if (Tracker.Connected || Tracker.Connecting || _busy) SetOpen(!_open);
					else { SetOpen(true); RunAsync(() => Tracker.ConnectAsync(false)); }
				}
			},
			_menu = new ClickableContainer
			{
				Action = () => { },
				Anchor = Anchor.TopRight,
				Origin = Anchor.TopRight,
				Position = new Vector2(-88, 38),
				Width = 272,
				AutoSizeAxes = Axes.Y,
				Masking = true,
				CornerRadius = 8,
				BorderThickness = 1,
				BorderColour = new Color4(66, 59, 70, 255),
				Alpha = 0,
				Children = new Drawable[]
				{
					new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(27, 24, 31, 255) },
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 8),
						Padding = new MarginPadding(14),
						Children = new Drawable[]
						{
							SettingsLayout.CreateWrappingText("MANIA TRACKER", 10, StyledButton.Theme.Accent, "Bold"),
							_identity = SettingsLayout.CreateStatusText(19, Color4.White),
							_status = SettingsLayout.CreateStatusText(13, StyledButton.Theme.MutedLabel),
							new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = new Color4(53, 48, 59, 255), Margin = new MarginPadding { Vertical = 4 } },
							_uploads = Button("Auto uploads", ToggleUploads),
							Button("Manage account", () => { CloseMenu(); ManageAccountRequested?.Invoke(); }),
							_disconnect = Button("Disconnect", () =>
							{
								if (!_confirmDisconnect) { _confirmDisconnect = true; Refresh(); }
								else { _confirmDisconnect = false; RunAsync(Tracker.DisconnectAsync); }
							}),
							_cancel = Button("Cancel", () =>
							{
								if (Tracker.Connecting) Tracker.CancelConnection();
								_confirmDisconnect = false;
								Refresh();
							})
						}
					}
				}
			}
		};
		_disconnect.Colour = new Color4(232, 151, 164, 255);
		_uploads.TooltipText = "Automatically share new completed osu!stable mania replays (including player name and inputs) and beatmaps with Mania Tracker. Turning this off pauses capture and uploads; pending files are kept.";
		Scheduler.AddDelayed(Refresh, 250, true);
		Refresh();
	}

	private static MenuAction Button(string text, Action action) => new(text)
	{
		RelativeSizeAxes = Axes.X,
		Height = 34,
		Action = action
	};

	private void ToggleUploads()
	{
		if (_busy || Tracker.Connecting || !Tracker.Connected || _confirmDisconnect) return;
		try
		{
			Tracker.SetEnabled(!Tracker.Enabled);
			_error = null;
			_operationResult = null;
		}
		catch (Exception) { _error = "Could not save your upload preference. Please try again."; }
		Refresh();
	}

	public void CloseMenu()
	{
		if (_open) SetOpen(false);
	}

	private void SetOpen(bool open)
	{
		_open = open;
		_confirmDisconnect = false;
		_menu.Alpha = _dismiss.Alpha = open ? 1 : 0;
		_account.SetExpanded(open);
		Refresh();
	}

	private void RunAsync(Func<Task> action)
	{
		if (_busy) return;
		_busy = true;
		_error = null;
		_operationResult = null;
		Refresh();
		_ = Task.Run(async () =>
		{
			try { await action(); }
			catch (Exception) { Schedule(() => _error = "Could not complete this operation. Please try again in Settings."); }
			finally { Schedule(() => { _busy = false; _operationResult = Tracker.Status; Refresh(); }); }
		});
	}

	private void Refresh()
	{
		var connected = Tracker.Connected;
		var connecting = Tracker.Connecting;
		if (_lastUploadsEnabled != Tracker.Enabled) _operationResult = null;
		_lastUploadsEnabled = Tracker.Enabled;
		_uploads.Alpha = connected ? 1 : 0;
		_uploads.Enabled = connected && !_busy && !connecting && !_confirmDisconnect;
		_uploads.ToggleState = Tracker.Enabled;
		_account.SetState(connected ? Tracker.Account : connecting || _busy ? "Signing in…" : "Tracker sign in");
		_account.TooltipText = connected ? "Mania Tracker: " + Tracker.Account : "Sign in to Mania Tracker";
		var identity = connected ? Tracker.Account : connecting ? "Connect your account" : "Welcome to Mania Tracker";
		if (_identityText != identity) { _identity.Text = identity; _identityText = identity; }
		var status = _error ?? (_busy && !connecting && connected ? "Disconnecting…" : _operationResult) ?? (connecting ? "Finish signing in in your browser." : connected
			? (Tracker.Enabled ? "Automatic uploads are on" : "Automatic uploads are paused")
			: Tracker.Status);
		if (_confirmDisconnect)
			status = "Disconnect this account? Uploads will pause. Pending files stay on this device, but cannot be sent through a new connection.";
		if (status != _lastStatus) { _status.Text = status; _lastStatus = status; }
		_disconnect.Alpha = connected ? 1 : 0;
		_disconnect.Enabled = !_busy && !connecting;
		_disconnect.Text = _confirmDisconnect ? "Confirm disconnect" : "Disconnect";
		_cancel.Alpha = connecting || _confirmDisconnect ? 1 : 0;
		_cancel.Text = connecting ? "Cancel sign-in" : "Keep connected";
	}

	private string _identityText = "";

	private partial class MenuAction : ClickableContainer, IHasTooltip
	{
		public LocalisableString TooltipText { get; set; }
		private readonly SpriteText _label;
		private readonly SpriteText _state;
		private readonly Box _hover;
		private bool _enabled = true;
		public bool Enabled
		{
			get => _enabled;
			set { _enabled = value; _label.Alpha = _state.Alpha = value ? 1 : 0.4f; }
		}
		public string Text { set => _label.Text = value; }
		public bool ToggleState
		{
			set
			{
				_state.Text = value ? "ON" : "OFF";
				_state.Colour = value ? StyledButton.Theme.Accent : StyledButton.Theme.MutedLabel;
			}
		}

		public MenuAction(string text)
		{
			Masking = true;
			CornerRadius = 4;
			Children = new Drawable[]
			{
				_hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0 },
				_label = new SpriteText
				{
					Text = text, Font = new FontUsage("", 13),
					Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft,
					X = 8
				},
				_state = new SpriteText
				{
					Font = new FontUsage("", 11, "Bold"),
					Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight,
					X = -8
				}
			};
		}

		protected override bool OnClick(ClickEvent e) => Enabled && base.OnClick(e);
		protected override bool OnHover(HoverEvent e)
		{
			_hover.FadeTo(Enabled ? 0.06f : 0, 100);
			return base.OnHover(e);
		}
		protected override void OnHoverLost(HoverLostEvent e)
		{
			_hover.FadeOut(100);
			base.OnHoverLost(e);
		}
	}

	private partial class AccountControl : ClickableContainer, IHasTooltip
	{
		public LocalisableString TooltipText { get; set; }
		private readonly Box _hover;
		private readonly SpriteText _label;
		private readonly Container _chevron;
		private bool _expanded;

		public AccountControl()
		{
			Masking = true;
			CornerRadius = 4;
			Children = new Drawable[]
			{
				_hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0 },
				new Container
				{
					RelativeSizeAxes = Axes.Both,
					Padding = new MarginPadding { Left = 10, Right = 26 },
					Child = _label = new SpriteText
					{
						RelativeSizeAxes = Axes.X, Truncate = true,
						Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft,
						Font = new FontUsage("", 13), Colour = new Color4(216, 209, 220, 255)
					}
				},
				_chevron = new Container
				{
					Anchor = Anchor.CentreRight, Origin = Anchor.Centre,
					X = -13, Size = new Vector2(8), Colour = StyledButton.Theme.MutedLabel,
					Children = new Drawable[]
					{
						new Box { Size = new Vector2(5, 1), Position = new Vector2(1, 3), Rotation = 45 },
						new Box { Size = new Vector2(5, 1), Position = new Vector2(4, 6.5f), Rotation = -45 }
					}
				}
			};
		}

		public void SetState(string name) { _label.Text = name; }
		protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left || base.OnMouseDown(e);
		public void SetExpanded(bool expanded)
		{
			_expanded = expanded;
			_hover.FadeTo(expanded ? 0.08f : 0, 120);
			_chevron.RotateTo(expanded ? 180 : 0, 120);
		}
		protected override bool OnHover(HoverEvent e)
		{
			_hover.FadeTo(0.08f, 100);
			return base.OnHover(e);
		}
		protected override void OnHoverLost(HoverLostEvent e)
		{
			if (!_expanded) _hover.FadeOut(100);
			base.OnHoverLost(e);
		}
	}

	protected override bool OnKeyDown(KeyDownEvent e)
	{
		if (_open && e.Key == Key.Escape) { CloseMenu(); return true; }
		return base.OnKeyDown(e);
	}
}
