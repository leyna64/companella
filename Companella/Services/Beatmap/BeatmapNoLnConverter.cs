using Companella.Models.Beatmap;

namespace Companella.Services.Beatmap;

/// <summary>
/// Converts beatmap hit objects to a no-LN version by turning holds into tap notes.
/// </summary>
public static class BeatmapNoLnConverter
{
	/// <summary>
	/// Returns a copy of the hit objects with all long notes converted to tap notes at their head time.
	/// </summary>
	public static List<HitObject> StripLongNotes(IEnumerable<HitObject> hitObjects)
	{
		return hitObjects.Select(ho =>
		{
			if (!ho.IsHold)
				return ho;

			var clone = ho.Clone();
			clone.Type = HitObjectType.Circle;
			clone.EndTime = clone.Time;
			return clone;
		}).ToList();
	}

	/// <summary>
	/// Returns true when the beatmap contains at least one long note.
	/// </summary>
	public static bool HasLongNotes(IEnumerable<HitObject> hitObjects)
	{
		return hitObjects.Any(ho => ho.IsHold);
	}
}
