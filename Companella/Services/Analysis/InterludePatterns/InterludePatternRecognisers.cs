namespace Companella.Services.Analysis.InterludePatterns;

/// <summary>
/// Pattern recogniser functions ported from YAVSRG Prelude Patterns.fs.
/// </summary>
internal static class InterludePatternRecognisers
{
	private static bool SameColumns(int[] a, int[] b)
	{
		if (a.Length != b.Length)
			return false;

		for (var i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
				return false;
		}

		return true;
	}

	private static InterludeRowInfo? At(IReadOnlyList<InterludeRowInfo> rows, int index)
		=> index >= 0 && index < rows.Count ? rows[index] : null;

	public static class Core
	{
		public static int Stream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			var r4 = At(rows, 4);

			if (r0 == null || r1 == null || r2 == null || r3 == null || r4 == null)
				return 0;

			if (r0.Notes == 1 && r0.Jacks == 0 &&
				r1.Notes == 1 && r1.Jacks == 0 &&
				r2.Notes == 1 && r2.Jacks == 0 &&
				r3.Notes == 1 && r3.Jacks == 0 &&
				r4.Notes == 1 && r4.Jacks == 0 &&
				r0.RawNotes[0] != r4.RawNotes[0])
				return 5;

			return 0;
		}

		public static int Jacks(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			if (r0 == null)
				return 0;

			return r0.Jacks > 1 && r0.MsPerBeat < 2000.0f ? 1 : 0;
		}

		public static int Chordstream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);

			if (r0 == null || r1 == null || r2 == null || r3 == null)
				return 0;

			if (r0.Notes > 1 && r0.Jacks == 0 &&
				r1.Jacks == 0 &&
				r2.Jacks == 0 &&
				r3.Jacks == 0 &&
				(r1.Notes > 1 || r2.Notes > 1 || r3.Notes > 1))
				return 4;

			return 0;
		}
	}

	public static class Jacks
	{
		public static int Chordjacks(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			if (r0 == null || r1 == null)
				return 0;

			return r0.Notes > 2 && r1.Notes > 1 && r1.Jacks >= 1 && (r1.Notes < r0.Notes || r1.Jacks < r0.Notes) ? 2 : 0;
		}

		public static int Minijacks(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			if (r0 == null || r1 == null)
				return 0;

			return r0.Jacks > 0 && r1.Jacks == 0 ? 2 : 0;
		}

		public static int Longjacks(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			var r4 = At(rows, 4);
			if (r0 == null || r1 == null || r2 == null || r3 == null || r4 == null)
				return 0;

			if (r0.Jacks <= 0 || r1.Jacks <= 0 || r2.Jacks <= 0 || r3.Jacks <= 0 || r4.Jacks <= 0)
				return 0;

			foreach (var column in r0.RawNotes)
			{
				if (Array.IndexOf(r1.RawNotes, column) >= 0 &&
					Array.IndexOf(r2.RawNotes, column) >= 0 &&
					Array.IndexOf(r3.RawNotes, column) >= 0 &&
					Array.IndexOf(r4.RawNotes, column) >= 0)
					return 5;
			}

			return 0;
		}
	}

	public static class Jacks4K
	{
		public static int Quadstream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			if (r0 == null || r2 == null || r3 == null)
				return 0;

			return r0.Notes == 4 && r2.Jacks == 0 && r3.Jacks == 0 ? 4 : 0;
		}

		public static int Gluts(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			if (r0 == null || r1 == null || r2 == null)
				return 0;

			if (r1.Jacks != 1 || r2.Jacks != 1)
				return 0;

			foreach (var column in r0.RawNotes)
			{
				if (Array.IndexOf(r1.RawNotes, column) >= 0 && Array.IndexOf(r2.RawNotes, column) >= 0)
					return 0;
			}

			return 3;
		}
	}

	public static class Chordstream4K
	{
		public static int Handstream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			if (r0 == null || r1 == null || r2 == null || r3 == null)
				return 0;

			return r0.Notes == 3 && r0.Jacks == 0 &&
				   r1.Jacks == 0 && r2.Jacks == 0 && r3.Jacks == 0 ? 4 : 0;
		}

		public static int Jumpstream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			if (r0 == null || r1 == null || r2 == null || r3 == null)
				return 0;

			return r0.Notes == 2 && r0.Jacks == 0 &&
				   r1.Notes == 1 && r1.Jacks == 0 &&
				   r2.Notes < 3 && r2.Jacks == 0 &&
				   r3.Notes < 3 && r3.Jacks == 0 ? 4 : 0;
		}

		public static int Jumptrill(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			if (r0 == null || r1 == null || r2 == null || r3 == null)
				return 0;

			return r0.Notes == 2 &&
				   r1.Notes == 2 && r1.Roll &&
				   r2.Notes == 2 && r2.Roll &&
				   r3.Notes == 2 && r3.Roll ? 4 : 0;
		}

		public static int Splittrill(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			if (r0 == null || r1 == null || r2 == null)
				return 0;

			return r0.Notes == 2 &&
				   r1.Notes == 2 && r1.Jacks == 0 && !r1.Roll &&
				   r2.Notes == 2 && r2.Jacks == 0 && !r2.Roll ? 3 : 0;
		}
	}

	public static class Stream4K
	{
		public static int Roll(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			if (r0 == null || r1 == null || r2 == null)
				return 0;

			if (r0.Notes == 1 && r0.Direction == InterludeDirection.Left &&
				r1.Notes == 1 && r1.Direction == InterludeDirection.Left &&
				r2.Notes == 1 && r2.Direction == InterludeDirection.Left)
				return 3;

			if (r0.Notes == 1 && r0.Direction == InterludeDirection.Right &&
				r1.Notes == 1 && r1.Direction == InterludeDirection.Right &&
				r2.Notes == 1 && r2.Direction == InterludeDirection.Right)
				return 3;

			return 0;
		}

		public static int Trill(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			if (r0 == null || r1 == null || r2 == null || r3 == null)
				return 0;

			return SameColumns(r0.RawNotes, r2.RawNotes) &&
				   SameColumns(r1.RawNotes, r3.RawNotes) &&
				   r1.Jacks == 0 && r2.Jacks == 0 ? 4 : 0;
		}

		public static int Minitrill(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			var r3 = At(rows, 3);
			if (r0 == null || r1 == null || r2 == null || r3 == null)
				return 0;

			return SameColumns(r0.RawNotes, r2.RawNotes) &&
				   !SameColumns(r1.RawNotes, r3.RawNotes) &&
				   r1.Jacks == 0 && r2.Jacks == 0 ? 4 : 0;
		}
	}

	public static InterludeSpecificPatterns ForKeyCount(int keyCount)
	{
		if (keyCount == 4)
		{
			return new InterludeSpecificPatterns
			{
				Stream =
				[
					("Rolls", Stream4K.Roll),
					("Trills", Stream4K.Trill),
					("Minitrills", Stream4K.Minitrill)
				],
				Chordstream =
				[
					("Handstream", Chordstream4K.Handstream),
					("Split Trill", Chordstream4K.Splittrill),
					("Jumptrill", Chordstream4K.Jumptrill),
					("Jumpstream", Chordstream4K.Jumpstream)
				],
				Jack =
				[
					("Longjacks", Jacks.Longjacks),
					("Quadstream", Jacks4K.Quadstream),
					("Gluts", Jacks4K.Gluts),
					("Chordjacks", Jacks.Chordjacks),
					("Minijacks", Jacks.Minijacks)
				]
			};
		}

		if (keyCount == 7)
		{
			return new InterludeSpecificPatterns
			{
				Stream = [],
				Chordstream =
				[
					("Brackets", Chordstream7K.Brackets),
					("Double Stream", Chordstream7K.DoubleStreams),
					("Dense Chordstream", Chordstream7K.DenseChordstream),
					("Light Chordstream", Chordstream7K.LightChordstream)
				],
				Jack =
				[
					("Longjacks", Jacks.Longjacks),
					("Chordjacks", Jacks.Chordjacks),
					("Minijacks", Jacks.Minijacks)
				]
			};
		}

		return new InterludeSpecificPatterns
		{
			Stream = [],
			Chordstream =
			[
				("Double Stream", ChordstreamOther.DoubleStreams),
				("Dense Chordstream", ChordstreamOther.DenseChordstream),
				("Light Chordstream", ChordstreamOther.LightChordstream)
			],
			Jack =
			[
				("Longjacks", Jacks.Longjacks),
				("Chordjacks", Jacks.Chordjacks),
				("Minijacks", Jacks.Minijacks)
			]
		};
	}

	private static class Chordstream7K
	{
		public static int DoubleStreams(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			return r0?.Notes == 2 && r1 is { Notes: 2, Jacks: 0, Roll: false } ? 2 : 0;
		}

		public static int DenseChordstream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			return r0 is { Notes: var x } && r1 is { Notes: var y, Jacks: 0 } && x > 1 && y > 1 ? 2 : 0;
		}

		public static int LightChordstream(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			return r0 is { Notes: var x } && r1 is { Notes: 1, Jacks: 0 } && x > 1 ? 2 : 0;
		}

		public static int Brackets(IReadOnlyList<InterludeRowInfo> rows)
		{
			var r0 = At(rows, 0);
			var r1 = At(rows, 1);
			var r2 = At(rows, 2);
			if (r0 == null || r1 == null || r2 == null)
				return 0;

			return r0.Notes > 2 && r1.Notes > 2 && r2.Notes > 2 &&
				   r1 is { Roll: false, Jacks: 0 } &&
				   r2 is { Roll: false, Jacks: 0 } &&
				   r0.Notes + r1.Notes + r2.Notes > 9 ? 3 : 0;
		}
	}

	private static class ChordstreamOther
	{
		public static int DoubleStreams(IReadOnlyList<InterludeRowInfo> rows)
			=> Chordstream7K.DoubleStreams(rows);

		public static int DenseChordstream(IReadOnlyList<InterludeRowInfo> rows)
			=> Chordstream7K.DenseChordstream(rows);

		public static int LightChordstream(IReadOnlyList<InterludeRowInfo> rows)
			=> Chordstream7K.LightChordstream(rows);
	}
}
