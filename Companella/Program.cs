using System.Runtime.InteropServices;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework;
using osu.Framework.Platform;
using Squirrel;

namespace Companella;

public static class Program
{
	[DllImport("kernel32.dll")]
	private static extern bool FreeConsole();

	private static SplashForm? _splashForm;
	private static Thread? _splashThread;

	[STAThread]
	public static void Main(string[] args)
	{
		// Handle Squirrel lifecycle events (install, update, uninstall, etc.)
		// This must be called early, before any UI is shown
		SquirrelAwareApp.HandleEvents(
			OnAppInstall,
			OnAppUpdate,
			onAppUninstall: OnAppUninstall
		);

		if (args.Contains(OsuWinelloSetup.Flag, StringComparer.OrdinalIgnoreCase))
		{
			Environment.Exit(OsuWinelloSetup.Run());
		}

		// Show splash screen immediately on a separate thread
		ShowSplashScreen();

		// Migrate user data from old location to AppData if needed
		// This handles upgrades from pre-Squirrel versions
		DataPaths.MigrateUserDataIfNeeded();

		// Parse command line arguments
		var trainingMode = ParseTrainingMode(args);

		using GameHost host = Host.GetSuitableDesktopHost("Companella!");
		using var game = new CompanellaGame(trainingMode, CloseSplashScreen);
		host.Run(game);
	}

	/// <summary>
	/// Shows the splash screen on a separate STA thread.
	/// </summary>
	private static void ShowSplashScreen()
	{
		_splashThread = new Thread(() =>
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			_splashForm = new SplashForm();
			Application.Run(_splashForm);
		});

		_splashThread.SetApartmentState(ApartmentState.STA);
		_splashThread.IsBackground = true;
		_splashThread.Start();
	}

	/// <summary>
	/// Closes the splash screen with a fade out animation.
	/// Called from the game when it's ready.
	/// </summary>
	public static void CloseSplashScreen()
	{
		if (_splashForm != null && !_splashForm.IsDisposed)
			try
			{
				_splashForm.Invoke(() => _splashForm.FadeOutAndClose());
			}
			catch
			{
				// Form may already be closed
			}
	}

	/// <summary>
	/// Called when the application is first installed via Squirrel.
	/// Creates Start Menu and Desktop shortcuts.
	/// </summary>
	private static void OnAppInstall(SemanticVersion version, IAppTools tools)
	{
		Logger.Info($"[Squirrel] Installing version {version}");

		// Create shortcuts in Start Menu and Desktop
		tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);

		Logger.Info("[Squirrel] Shortcuts created");
	}

	/// <summary>
	/// Called when the application is updated via Squirrel.
	/// Updates shortcuts to point to the new version.
	/// </summary>
	private static void OnAppUpdate(SemanticVersion version, IAppTools tools)
	{
		Logger.Info($"[Squirrel] Updated to version {version}");

		// Update shortcuts to point to the new exe
		tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);

		Logger.Info("[Squirrel] Shortcuts updated");
	}

	/// <summary>
	/// Called when the application is uninstalled via Squirrel.
	/// Removes shortcuts and optionally cleans up user data.
	/// </summary>
	private static void OnAppUninstall(SemanticVersion version, IAppTools tools)
	{
		Logger.Info($"[Squirrel] Uninstalling version {version}");

		// Remove shortcuts
		tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);

		// Note: We intentionally do NOT delete user data in AppData
		// Users may want to keep their settings, sessions, and maps database
		// If they reinstall later, their data will still be there

		Logger.Info("[Squirrel] Shortcuts removed. User data in AppData preserved.");
	}

	/// <summary>
	/// Parses command line arguments for --training flag.
	/// </summary>
	private static bool ParseTrainingMode(string[] args)
	{
		if (args == null || args.Length == 0)
			return false;

		foreach (var arg in args)
			if (arg.Equals("--training", StringComparison.OrdinalIgnoreCase) ||
				arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
				return true;

		return false;
	}
}
