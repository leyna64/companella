using System.Text;

namespace Companella.Services.Integrations.ManiaTracker;

/// <summary>Bounded reading of original stable replay headers, without decompressing untrusted input.</summary>
public sealed record ManiaTrackerReplay(string BeatmapHash, string PlayerName, int Mods, int Score, int[] Judgements, DateTimeOffset Timestamp)
{
	public static ManiaTrackerReplay Read(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes, false);
		using var reader = new BinaryReader(stream, Encoding.UTF8);
		if (reader.ReadByte() != 3) throw new InvalidDataException("Not a mania replay.");
		var version = reader.ReadInt32();
		// ppy/osu LegacyScoreEncoder.FIRST_LAZER_VERSION; lazer is deliberately unsupported.
		if (version <= 0 || version >= 30000000) throw new InvalidDataException("Only osu!stable replays are supported.");
		var hash = ReadString(reader);
		var name = ReadString(reader);
		ReadString(reader);
		var counts = Enumerable.Range(0, 6).Select(_ => (int)reader.ReadUInt16()).ToArray();
		var score = reader.ReadInt32();
		reader.ReadUInt16();
		reader.ReadBoolean();
		var mods = reader.ReadInt32();
		ReadString(reader);
		var timestamp = new DateTimeOffset(new DateTime(reader.ReadInt64(), DateTimeKind.Utc));
		var length = reader.ReadInt32();
		if (stream.Position > 16384 || length <= 13 || length > stream.Length - stream.Position - (version >= 20140721 ? 8 : 0))
			throw new InvalidDataException("Replay is incomplete or has no input data.");
		return new ManiaTrackerReplay(hash, name, mods, score, counts, timestamp);
	}

	private static string ReadString(BinaryReader reader)
	{
		var marker = reader.ReadByte();
		if (marker == 0) return "";
		if (marker != 11) throw new InvalidDataException("Invalid replay string.");
		var length = reader.Read7BitEncodedInt();
		if (length < 0 || length > 16384 || reader.BaseStream.Position + length > 16384) throw new InvalidDataException("Replay header is too long.");
		var bytes = reader.ReadBytes(length);
		if (bytes.Length != length) throw new EndOfStreamException();
		return Encoding.UTF8.GetString(bytes);
	}

	public bool Matches(ManiaTrackerPlay play) =>
		BeatmapHash.Equals(play.ChartMd5, StringComparison.OrdinalIgnoreCase) &&
		PlayerName.Equals(play.PlayerName, StringComparison.OrdinalIgnoreCase) &&
		Mods == play.Mods && Score == play.Score && Judgements.SequenceEqual(play.Judgements) &&
		Timestamp >= play.StartedAt.AddSeconds(-2) && Timestamp <= play.FinishedAt.AddSeconds(5);
}
