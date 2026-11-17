using Companella.Components.Misc;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring the toggle visibility keybind.
/// </summary>
public partial class KeybindConfigPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	[Resolved] private GlobalHotkeyService HotkeyService { get; set; } = null!;

	[Resolved] private ReadableKeyCombinationProvider KeyCombinationProvider { get; set; } = null!;

	private StyledButton _keybindButton = null!;
	private bool _isRecording;
	private List<Key> _pressedKeys = new();
	private List<Key> _capturedKeys = new();

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
				_keybindButton = new StyledButton(FormatKeybind(SettingsService.Settings.ToggleVisibilityKeybind))
				{
					RelativeSizeAxes = Axes.X,
					Height = 34,
					AcceptsKeyboardFocus = true,
					RecordingFillColor = StyledButton.Theme.DestructiveAccent,
					Action = OnKeybindButtonClicked,
					TooltipText = "Click to set a new hotkey for toggling overlay visibility",
					OnKeyDownCallback = OnKeybindKeyDown,
					OnKeyUpCallback = OnKeybindKeyUp
				},
				SettingsLayout.CreateHint("Click the field above, press your key combination, then release any key to save.")
			}
		};

		InternalChild = new SettingsSection("Hotkeys", "Global shortcuts that work while osu! is focused", content);
	}

	private bool OnKeybindKeyDown(KeyDownEvent e)
	{
		if (!_isRecording)
			return false;

		if (!_pressedKeys.Contains(e.Key))
			_pressedKeys.Add(e.Key);

		_capturedKeys = new List<Key>(_pressedKeys);
		UpdateKeybindDisplay();
		return true;
	}

	private void OnKeybindKeyUp(KeyUpEvent e)
	{
		if (!_isRecording)
			return;

		_pressedKeys.Remove(e.Key);

		if (_capturedKeys.Count > 0)
			FinishRecording(_capturedKeys);
	}

	private void OnKeybindButtonClicked()
	{
		if (_isRecording)
			CancelRecording();
		else
			StartRecording();
	}

	private void StartRecording()
	{
		_isRecording = true;
		_pressedKeys.Clear();
		_capturedKeys.Clear();
		_keybindButton.SetRecording(true);
		_keybindButton.SetKeybindText("Press keys...");

		GetContainingFocusManager()?.ChangeFocus(_keybindButton);
	}

	private void CancelRecording()
	{
		_isRecording = false;
		_keybindButton.SetKeybindText(FormatKeybind(SettingsService.Settings.ToggleVisibilityKeybind));
		_keybindButton.SetRecording(false);
		_pressedKeys.Clear();
		_capturedKeys.Clear();
	}

	private void FinishRecording(List<Key> keys)
	{
		_isRecording = false;

		if (keys.Count > 0)
		{
			var keybind = BuildKeybindString(keys.ToList());
			SettingsService.Settings.ToggleVisibilityKeybind = keybind;
			HotkeyService.RegisterHotkey(keybind);
			Task.Run(async () => await SettingsService.SaveAsync());
			_keybindButton.SetKeybindText(FormatKeybind(keybind));
		}
		else
		{
			_keybindButton.SetKeybindText(FormatKeybind(SettingsService.Settings.ToggleVisibilityKeybind));
		}

		_keybindButton.SetRecording(false);
		_pressedKeys.Clear();
		_capturedKeys.Clear();
	}

	private void UpdateKeybindDisplay()
	{
		if (_pressedKeys.Count > 0)
			_keybindButton.SetKeybindText(FormatLiveKeybind(_pressedKeys));
	}

	private string FormatKeybind(string keybind) =>
		KeyboardDisplayHelper.FormatStoredKeybind(KeyCombinationProvider, keybind);

	private string FormatLiveKeybind(IEnumerable<Key> keys)
	{
		var inputKeys = new List<InputKey>();
		foreach (var key in keys)
			inputKeys.Add(KeyCombination.FromKey(key));

		return inputKeys.Count == 0
			? string.Empty
			: KeyCombinationProvider.GetReadableString(new KeyCombination(inputKeys));
	}

	private static string BuildKeybindString(List<Key> keys)
	{
		var parts = new List<string>();
		var hasShift = keys.Contains(Key.LShift) || keys.Contains(Key.RShift);

		// Add the main key (first non-modifier)
		var mainKey = keys.FirstOrDefault(k => k != Key.LControl && k != Key.RControl &&
											   k != Key.LAlt && k != Key.RAlt &&
											   k != Key.LShift && k != Key.RShift &&
											   k != Key.LWin && k != Key.RWin);

		// Check if Shift is part of the key (e.g., Shift+Plus = =) or a modifier
		var shiftIsModifier = hasShift && (mainKey == Key.Unknown || mainKey != Key.Plus);

		// Add modifiers first
		if (keys.Contains(Key.LControl) || keys.Contains(Key.RControl))
			parts.Add("Ctrl");
		if (keys.Contains(Key.LAlt) || keys.Contains(Key.RAlt))
			parts.Add("Alt");
		if (shiftIsModifier)
			parts.Add("Shift");
		if (keys.Contains(Key.LWin) || keys.Contains(Key.RWin))
			parts.Add("Win");

		// Add the main key
		if (mainKey != Key.Unknown) parts.Add(KeyToKeybindString(mainKey));

		return string.Join("+", parts);
	}

	private static string KeyToKeybindString(Key key)
	{
		// Handle special keys
		return key switch
		{
			Key.Plus => "OemPlus", // = key (when Shift is held) or numpad +
			Key.Minus => "OemMinus", // - key
			Key.BracketLeft => "OemOpenBrackets",
			Key.BracketRight => "OemCloseBrackets",
			Key.Semicolon => "OemSemicolon",
			Key.Quote => "OemQuotes",
			Key.Comma => "OemComma",
			Key.Period => "OemPeriod",
			Key.Slash => "OemQuestion",
			Key.BackSlash => "OemPipe",
			Key.Tilde => "OemTilde",
			Key.Space => "Space",
			Key.Enter => "Enter",
			Key.Tab => "Tab",
			Key.Escape => "Escape",
			Key.BackSpace => "Backspace",
			Key.Delete => "Delete",
			Key.Insert => "Insert",
			Key.Home => "Home",
			Key.End => "End",
			Key.PageUp => "PageUp",
			Key.PageDown => "PageDown",
			Key.Up => "Up",
			Key.Down => "Down",
			Key.Left => "Left",
			Key.Right => "Right",
			_ => key.ToString()
		};
	}
}
