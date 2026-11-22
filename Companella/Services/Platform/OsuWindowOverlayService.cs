using System.Diagnostics;
using System.Runtime.InteropServices;
using Companella.Services.Common;

namespace Companella.Services.Platform;

/// <summary>
/// Service for managing overlay window positioning relative to osu! window.
/// Tracks osu! window position and size, and positions overlay window accordingly.
/// </summary>
public class OsuWindowOverlayService : IDisposable
{
	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	private Process? _osuProcess;
	private Rectangle _lastOsuWindowRect;
	private bool _isOverlayMode;
	private Point _overlayOffset = new(0, 0);

	/// <summary>
	/// Event raised when osu! window position/size changes.
	/// </summary>
	public event EventHandler<Rectangle>? OsuWindowChanged;

	/// <summary>
	/// Event raised when overlay mode change is requested from UI.
	/// </summary>
	public event EventHandler<bool>? OverlayModeChangeRequested;

	/// <summary>
	/// Whether an osu! process is attached and still running.
	/// </summary>
	public bool IsAttached => _osuProcess is { HasExited: false };

	/// <summary>
	/// Whether overlay mode is currently enabled.
	/// </summary>
	public bool IsOverlayMode
	{
		get => _isOverlayMode;
		set
		{
			if (_isOverlayMode != value)
			{
				_isOverlayMode = value;
				if (value)
					StartTracking();
				else
					StopTracking();
			}
		}
	}

	/// <summary>
	/// Offset from osu! window top-left corner for overlay positioning.
	/// </summary>
	public Point OverlayOffset
	{
		get => _overlayOffset;
		set => _overlayOffset = value;
	}

	/// <summary>
	/// Checks if osu! window or the overlay window is currently in focus.
	/// This prevents the overlay from hiding when clicked.
	/// </summary>
	public bool IsOsuOrOverlayInFocus(IntPtr overlayWindowHandle)
	{
		if (_osuProcess == null || _osuProcess.HasExited) return false;

		try
		{
			var foregroundWindow = GetForegroundWindow();
			var osuWindow = _osuProcess.MainWindowHandle;

			if (foregroundWindow == IntPtr.Zero)
				return false;

			// Check if foreground window is either osu! or the overlay window
			return (osuWindow != IntPtr.Zero && foregroundWindow == osuWindow) ||
				   (overlayWindowHandle != IntPtr.Zero && foregroundWindow == overlayWindowHandle);
		}
		catch (Exception ex)
		{
			Logger.Info($"[Overlay] Error checking focus: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Gets the current osu! window rectangle, or null if not found.
	/// </summary>
	public Rectangle? GetOsuWindowRect()
	{
		if (_osuProcess == null || _osuProcess.HasExited) return null;

		try
		{
			var hWnd = _osuProcess.MainWindowHandle;
			if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
			{
				// Process caches MainWindowHandle: attached while osu! was starting, it stays zero until refreshed
				_osuProcess.Refresh();
				hWnd = _osuProcess.MainWindowHandle;
			}

			if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd)) return null;

			if (GetWindowRect(hWnd, out var rect))
				return new Rectangle(
					rect.Left,
					rect.Top,
					rect.Right - rect.Left,
					rect.Bottom - rect.Top
				);
		}
		catch (Exception ex)
		{
			Logger.Info($"[Overlay] Error getting osu! window rect: {ex.Message}");
		}

		return null;
	}

	/// <summary>
	/// Calculates the overlay window position based on osu! window position.
	/// Anchors to the left middle of the osu! window.
	/// </summary>
	public Point? CalculateOverlayPosition(int overlayWidth, int overlayHeight)
	{
		var osuRect = GetOsuWindowRect();
		if (!osuRect.HasValue) return null;

		// Position overlay at left middle of osu! window
		// Center vertically, align to left edge
		// Can be adjusted via OverlayOffset
		var centerY = osuRect.Value.Top + osuRect.Value.Height / 2 - overlayHeight / 2;

		return new Point(
			osuRect.Value.Left + _overlayOffset.X,
			centerY + _overlayOffset.Y
		);
	}

	/// <summary>
	/// Requests a change in overlay mode from UI. Raises OverlayModeChangeRequested event.
	/// </summary>
	public void RequestOverlayModeChange(bool enabled)
	{
		OverlayModeChangeRequested?.Invoke(this, enabled);
	}

	/// <summary>
	/// Attaches to the osu! process.
	/// </summary>
	public bool AttachToOsu(Process? osuProcess)
	{
		_osuProcess = osuProcess;

		if (_osuProcess != null && !_osuProcess.HasExited)
		{
			Logger.Info($"[Overlay] Attached to osu! process: PID {_osuProcess.Id}");
			if (_isOverlayMode) StartTracking();

			return true;
		}

		return false;
	}

	/// <summary>
	/// Starts tracking osu! window position.
	/// </summary>
	private void StartTracking()
	{
		// Tracking will be done via Update() calls
		_lastOsuWindowRect = GetOsuWindowRect() ?? Rectangle.Empty;
	}

	/// <summary>
	/// Stops tracking osu! window position.
	/// </summary>
	private void StopTracking()
	{
		_lastOsuWindowRect = Rectangle.Empty;
	}

	/// <summary>
	/// Updates the overlay service. Call this every frame when overlay mode is enabled.
	/// </summary>
	public void Update()
	{
		if (!_isOverlayMode || _osuProcess == null) return;

		var currentRect = GetOsuWindowRect();
		if (currentRect.HasValue)
			if (_lastOsuWindowRect != currentRect.Value)
			{
				_lastOsuWindowRect = currentRect.Value;
				OsuWindowChanged?.Invoke(this, currentRect.Value);
			}
	}

	public void Dispose()
	{
		StopTracking();
		_osuProcess = null;

		GC.SuppressFinalize(this);
	}
}
