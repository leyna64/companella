using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;

namespace Companella.Components.Layout;

/// <summary>
/// Scroll container that handles wheel input when content overflows, and passes scroll
/// to parent containers when at the top/bottom edge (or when there is nothing to scroll).
/// </summary>
public partial class ChainedScrollContainer : BasicScrollContainer
{
	private const float _boundaryEpsilon = 1f;

	protected override bool OnScroll(ScrollEvent e)
	{
		var scrollDelta = e.ScrollDelta.Y;
		if (Math.Abs(scrollDelta) < float.Epsilon)
			return false;

		if (ScrollableExtent <= _boundaryEpsilon)
			return false;

		var atTop = Current <= _boundaryEpsilon;
		var atBottom = Current >= ScrollableExtent - _boundaryEpsilon;

		if ((atTop && scrollDelta > 0) || (atBottom && scrollDelta < 0))
			return false;

		return base.OnScroll(e);
	}
}
