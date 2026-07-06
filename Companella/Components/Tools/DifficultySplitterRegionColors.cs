using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Distinct accent colors for difficulty splitter regions.
/// </summary>
public static class DifficultySplitterRegionColors
{
	public static readonly Color4[] Palette =
	{
		new(255, 102, 170, 255),
		new(100, 180, 255, 255),
		new(255, 200, 80, 255),
		new(120, 220, 140, 255),
		new(200, 140, 255, 255),
		new(255, 140, 100, 255),
		new(100, 220, 220, 255),
		new(255, 120, 120, 255)
	};

	public static Color4 GetAccent(int colorIndex) => Palette[Math.Abs(colorIndex) % Palette.Length];

	public static Color4 GetBandFill(Color4 accent, bool selected, bool anySelected) =>
		new Color4(accent.R, accent.G, accent.B, (byte)(selected ? 55 : anySelected ? 22 : 32));

	public static Color4 GetPreviewFill(Color4 accent) =>
		new Color4(accent.R, accent.G, accent.B, 45);

	public static Color4 GetMarkerColor(Color4 accent, bool selected) =>
		selected ? Color4.White : accent;
}
