using System.Globalization;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Platform;
using osuTK.Input;

namespace Companella.Services.Common;

/// <summary>
/// Formats keys for UI display using the active keyboard layout via osu-framework.
/// </summary>
public static class KeyboardDisplayHelper
{
	public static string FormatKey(ReadableKeyCombinationProvider provider, Key key) =>
		provider.GetReadableString(KeyCombination.FromKey(key));

	public static string FormatKeys(ReadableKeyCombinationProvider provider, params Key[] keys) =>
		string.Join(" / ", keys.Select(k => FormatKey(provider, k)));

	public static string FormatCombination(ReadableKeyCombinationProvider provider, params InputKey[] keys) =>
		provider.GetReadableString(new KeyCombination(keys));

	public static string FormatStoredKeybind(ReadableKeyCombinationProvider provider, string keybind)
	{
		if (string.IsNullOrWhiteSpace(keybind))
			return string.Empty;

		var keys = new List<InputKey>();
		foreach (var part in keybind.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (TryParseModifier(part, out var modifier))
			{
				keys.Add(modifier);
				continue;
			}

			if (TryParseKey(part, out var key))
				keys.Add(KeyCombination.FromKey(key));
		}

		return keys.Count == 0
			? keybind
			: provider.GetReadableString(new KeyCombination(keys));
	}

	private static bool TryParseModifier(string part, out InputKey modifier)
	{
		modifier = part.ToUpperInvariant() switch
		{
			"CTRL" or "CONTROL" => InputKey.Control,
			"ALT" => InputKey.Alt,
			"SHIFT" => InputKey.Shift,
			"WIN" or "WINDOWS" => InputKey.Super,
			_ => InputKey.None
		};

		return modifier != InputKey.None;
	}

	private static bool TryParseKey(string part, out Key key)
	{
		key = Key.Unknown;
		var upper = part.ToUpperInvariant();

		if (upper.Length == 1)
		{
			if (char.IsLetter(upper[0]))
			{
				key = Enum.Parse<Key>(upper, ignoreCase: true);
				return true;
			}

			if (char.IsDigit(upper[0]))
			{
				key = Enum.Parse<Key>($"Number{upper[0]}", ignoreCase: true);
				return true;
			}
		}

		if (upper.StartsWith('F') &&
			int.TryParse(upper[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fn) &&
			fn is >= 1 and <= 35)
		{
			key = Enum.Parse<Key>($"F{fn}", ignoreCase: true);
			return true;
		}

		key = upper switch
		{
			"OEMPLUS" or "=" or "EQUALS" => Key.Plus,
			"OEMMINUS" or "-" => Key.Minus,
			"OEMOPENBRACKETS" or "[" => Key.BracketLeft,
			"OEMCLOSEBRACKETS" or "]" => Key.BracketRight,
			"OEMSEMICOLON" or ";" => Key.Semicolon,
			"OEMQUOTES" or "'" => Key.Quote,
			"OEMCOMMA" or "," => Key.Comma,
			"OEMPERIOD" or "." => Key.Period,
			"OEMQUESTION" or "/" => Key.Slash,
			"OEMPIPE" or "\\" => Key.BackSlash,
			"OEMTILDE" or "~" => Key.Tilde,
			"SPACE" => Key.Space,
			"ENTER" => Key.Enter,
			"TAB" => Key.Tab,
			"ESC" or "ESCAPE" => Key.Escape,
			"BACKSPACE" => Key.BackSpace,
			"DELETE" => Key.Delete,
			"INSERT" => Key.Insert,
			"HOME" => Key.Home,
			"END" => Key.End,
			"PAGEUP" => Key.PageUp,
			"PAGEDOWN" => Key.PageDown,
			"UP" => Key.Up,
			"DOWN" => Key.Down,
			"LEFT" => Key.Left,
			"RIGHT" => Key.Right,
			_ => Enum.TryParse<Key>(part, true, out var parsed) ? parsed : Key.Unknown
		};

		return key != Key.Unknown;
	}
}
