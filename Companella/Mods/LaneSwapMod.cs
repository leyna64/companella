using Companella.Mods.Parameters;

namespace Companella.Mods;

/// <summary>
/// Swaps the lane within the selected beatmap. Currently supports 4-10 keys
/// </summary>
public class LaneSwapMod : BaseMod
{
	private readonly StringModParameter _laneOrder;

	public override string Name => "Lane Swap";
	public override string Description => "Swap the lane order for the beatmap selected";
	public override string Category => "General";
	public override string Icon => "LS";

	public LaneSwapMod()
	{
		_laneOrder = new StringModParameter(
			"Lane Order",
			"The order of the lanes. eg. 4321 for mirrored lanes",
			"");
		AddParameter(_laneOrder);
	}

	/// <summary>
	/// Gets the current lane order value
	/// </summary>
	public string GetLaneOrder() => _laneOrder.Value.Trim();

	protected override string? ValidateContext(ModContext context)
	{
		base.ValidateContext(context);

		var laneOrder = _laneOrder.Value.Trim();

		if (string.IsNullOrEmpty(laneOrder))
			return "Lane order cannot be empty";

		if (laneOrder.Length != context.KeyCount)
			return $"Lane order length ({laneOrder.Length}) must match key count ({context.KeyCount})";

		var usedLanes = new HashSet<int>();
		foreach (var ch in laneOrder)
		{
			if (!int.TryParse(ch.ToString(), out var lane))
				return $"Invalid character '{ch}'. Must be 1 to {context.KeyCount}";

			if (lane < 1 || lane > context.KeyCount)
				return $"Invalid lane number {lane}. Must be 1 to {context.KeyCount}";

			// do we wanna allow this though
			if (usedLanes.Contains(lane))
				return $"Lane {lane} appears multiple times in lane order!";

			usedLanes.Add(lane);
		}

		return null;
	}

	protected override ModResult ApplyInternal(ModContext context)
	{
		var laneOrder = _laneOrder.Value.Trim();
		var keyCount = context.KeyCount;

		var laneMapping = new int[keyCount];
		for (var i = 0; i < laneOrder.Length; i++)
		{
			var lane = int.Parse(laneOrder[i].ToString()) - 1; // 0 index
			laneMapping[lane] = i;
		}

		var hitObjects = CloneHitObjects(context.HitObjects);
		foreach (var ho in hitObjects)
		{
			ho.Column = laneMapping[ho.Column];
		}

		hitObjects = hitObjects
			.OrderBy(h => h.Time)
			.ThenBy(h => h.Column)
			.ToList();

		var stats = CalculateStatistics(context.HitObjects, hitObjects);
		stats.CustomStats["Lane"] = laneOrder;

		return ModResult.Succeeded(hitObjects, stats);
	}
}
