// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using System.Runtime.CompilerServices;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// Builds pass 1 notes by reflecting short maps until at least 60s, then cropping to exactly 60s.
/// Maps that are already >= 60s are returned unchanged.
/// </summary>
internal static class DanielPass1NoteBuilder
{
	internal const int TargetDurationMs = 60_000;

	internal readonly record struct Pass1BuildResult(
		DanielNote[] Notes,
		int MapEndTime,
		bool Changed,
		int OriginalDurationMs,
		int ExtendedDurationMs,
		int TrimEachSideMs);

	internal static Pass1BuildResult Build(IReadOnlyList<DanielNote> original)
	{
		if (original.Count == 0)
		{
			return new Pass1BuildResult(
				Array.Empty<DanielNote>(),
				1,
				false,
				0,
				0,
				0);
		}

		var center = original.ToArray();
		var (origMin, origMax) = GetNoteSpan(center);
		var originalDuration = origMax - origMin + 1;

		if (originalDuration >= TargetDurationMs)
		{
			return new Pass1BuildResult(
				center,
				origMax + 1,
				false,
				originalDuration,
				originalDuration,
				0);
		}

		var timeline = center;
		var extendedDuration = originalDuration;
		while (extendedDuration < TargetDurationMs)
		{
			timeline = SymmetricExtend(timeline);
			var (extMin, extMax) = GetNoteSpan(timeline);
			extendedDuration = extMax - extMin + 1;
		}

		var (timelineMin, timelineMax) = GetNoteSpan(timeline);
		var trimEachSide = (extendedDuration - TargetDurationMs) / 2;
		var windowMin = timelineMin + trimEachSide;
		var windowMax = windowMin + TargetDurationMs - 1;

		var cropped = new List<DanielNote>(timeline.Length);
		foreach (var note in timeline)
		{
			if (note.Time < windowMin || note.Time > windowMax)
				continue;

			cropped.Add(new DanielNote(note.Column, note.Time - windowMin));
		}

		return new Pass1BuildResult(
			cropped.ToArray(),
			TargetDurationMs,
			true,
			originalDuration,
			extendedDuration,
			trimEachSide);
	}

	private static DanielNote[] SymmetricExtend(DanielNote[] center)
	{
		var withLeft = PrependMirroredBlock(center);
		var rightMirror = MirrorReverseBlock(center);
		return AppendBlock(withLeft, rightMirror);
	}

	private static (int Min, int Max) GetNoteSpan(DanielNote[] notes)
	{
		if (notes.Length == 0)
			return (0, -1);

		var min = notes[0].Time;
		var max = notes[0].Time;
		for (var i = 1; i < notes.Length; i++)
		{
			var time = notes[i].Time;
			if (time < min)
				min = time;
			if (time > max)
				max = time;
		}

		return (min, max);
	}

	private static DanielNote MirrorNoteInSpan(DanielNote note, int minT, int maxT)
		=> new(note.Column, minT + maxT - note.Time);

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static DanielNote[] MirrorReverseBlock(DanielNote[] block)
	{
		var (minT, maxT) = GetNoteSpan(block);
		var mirrored = GC.AllocateUninitializedArray<DanielNote>(block.Length);
		for (var i = 0; i < block.Length; i++)
			mirrored[i] = MirrorNoteInSpan(block[block.Length - 1 - i], minT, maxT);
		return mirrored;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static DanielNote[] PrependMirroredBlock(DanielNote[] block)
	{
		var mirrored = MirrorReverseBlock(block);
		var (_, beforeMax) = GetNoteSpan(mirrored);
		var (afterMin, _) = GetNoteSpan(block);
		ShiftNotesInPlace(mirrored, afterMin - beforeMax - 1);
		return ConcatBlocks(mirrored, block);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static DanielNote[] AppendBlock(DanielNote[] before, DanielNote[] after)
	{
		if (after.Length == 0)
			return before;
		if (before.Length == 0)
			return after;

		var (_, beforeMax) = GetNoteSpan(before);
		var (afterMin, _) = GetNoteSpan(after);
		var result = GC.AllocateUninitializedArray<DanielNote>(before.Length + after.Length);
		before.AsSpan().CopyTo(result);
		var afterSpan = result.AsSpan(before.Length);
		after.AsSpan().CopyTo(afterSpan);
		ShiftNotesInPlace(afterSpan, beforeMax + 1 - afterMin);
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static DanielNote[] ConcatBlocks(DanielNote[] a, DanielNote[] b)
	{
		if (b.Length == 0)
			return a;
		if (a.Length == 0)
			return b;

		var result = GC.AllocateUninitializedArray<DanielNote>(a.Length + b.Length);
		a.AsSpan().CopyTo(result);
		b.AsSpan().CopyTo(result.AsSpan(a.Length));
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void ShiftNotesInPlace(Span<DanielNote> notes, int delta)
	{
		if (delta == 0)
			return;

		for (var i = 0; i < notes.Length; i++)
		{
			ref var n = ref notes[i];
			n = new DanielNote(n.Column, n.Time + delta);
		}
	}
}
