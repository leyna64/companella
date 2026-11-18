namespace Companella.Mods;

/// <summary>
/// Swaps the lanes within the selected beatmap. Supports 2-18 keys.
/// </summary>
public class LaneSwapMod : BaseMod
{
	public override string Name => "Lane Swap";
	public override string Description => "Swap the lane order for the beatmap selected";
	public override string Category => "General";
	public override string Icon => "LS";

	public const int MinKeyCount = 2, MaxKeyCount = 18;

	/// <summary>
	/// The lane order value, edited by dragging the notes in the lane order field
	/// </summary>
	public string LaneOrder { get; set; } = "";

	/// <summary>
	/// Gets the current lane order value
	/// </summary>
	public string GetLaneOrder() => LaneOrder.Trim();

	/// <summary>
	/// Gets a compact lane order for file names and version tags, e.g. "3124"
	///
	/// </summary>
	public string GetLaneOrderTag() => GetLaneOrder().Replace(",", "");

	/// <summary>
	///  Parses a lane order such as "3,1,2,4" and validates it for the key count
	/// </summary>
	/// <param name="value">Comma separated lane order</param>
	/// <param name="keyCount">Key count of the beatmap</param>
	/// <param name="lanes">Lane number playing in each column from left to right</param>
	/// <returns>True if the value is a valid lane order for the key count</returns>
	public static bool TryParseOrder(string? value, int keyCount, out int[] lanes)
	{
		lanes = Array.Empty<int>();

		if (string.IsNullOrWhiteSpace(value) || keyCount < 1)
			return false;

		var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if (parts.Length != keyCount)
			return false;

		var seen = new bool[keyCount];
		var parsed = new int[keyCount];

		for (var i = 0; i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], out var lane) || lane < 1 || lane > keyCount || seen[lane - 1])
				return false;

			seen[lane - 1] = true;
			parsed[i] = lane;
		}

		lanes = parsed;
		return true;
	}

	protected override string? ValidateContext(ModContext context)
	{
		var baseError = base.ValidateContext(context);

		if (baseError != null)
			return baseError;

		var laneOrder = GetLaneOrder();

		if (string.IsNullOrEmpty(laneOrder))
			return "Lane order cannot be empty";

		// Don't think this is supposed to happen, so just in case
		if (!TryParseOrder(laneOrder, context.KeyCount, out _))
			return $"Lane order out of bounds!";

		return null;
	}

	protected override ModResult ApplyInternal(ModContext context)
	{
		var keyCount = context.KeyCount;

		if (!TryParseOrder(GetLaneOrder(), keyCount, out var lanes))
			return ModResult.Failed("Invalid lane order");

		var laneMapping = new int[keyCount];
		for (var i = 0; i < keyCount; i++)
			laneMapping[lanes[i] - 1] = i;

		var hitObjects = CloneHitObjects(context.HitObjects);
		foreach (var ho in hitObjects) ho.Column = laneMapping[ho.Column];

		hitObjects = hitObjects
			.OrderBy(h => h.Time)
			.ThenBy(h => h.Column)
			.ToList();

		var stats = CalculateStatistics(context.HitObjects, hitObjects);
		stats.CustomStats["Lane"] = string.Join(",", lanes);

		return ModResult.Succeeded(hitObjects, stats);
	}
}
