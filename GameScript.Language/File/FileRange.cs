using System;
using System.Collections.Generic;

namespace GameScript.Language.File
{
	public readonly struct FileRange(FilePosition start, FilePosition end) : IEquatable<FileRange>
	{
		public FilePosition Start { get; } = start;
		public FilePosition End { get; } = end;

		public static FileRange Combine(FileRange rangeA, FileRange rangeB)
		{
			return new FileRange(rangeA.Start, rangeB.End);
		}

		public static FileRange Combine(IEnumerable<FileRange> ranges)
		{
			int minPosition = int.MaxValue, minLine = int.MaxValue, minColumn = int.MaxValue;
			int maxPosition = 0, maxLine = 0, maxColumn = 0;

			int count = 0;
			foreach (var range in ranges)
			{
				count++;
				minPosition = Math.Min(minPosition, range.Start.Position);
				minLine = Math.Min(minLine, range.Start.Line);
				minColumn = Math.Min(minColumn, range.Start.Column);

				maxPosition = Math.Max(maxPosition, range.End.Position);
				maxLine = Math.Max(maxLine, range.End.Line);
				maxColumn = Math.Max(maxColumn, range.End.Column);
			}

			if (count == 0)
			{
				return default;
			}

			return new FileRange(
				new FilePosition(minPosition, minLine, minColumn),
				new FilePosition(maxPosition, maxLine, maxColumn)
			);
		}

		public FileRange AddLength(int length)
		{
			return new FileRange(Start, End.AddColumn(length));
		}

		public bool Contains(int position)
		{
			return position >= Start.Position && position <= End.Position;
		}

		public bool Contains(int line, int character)
		{
			// 1. Reject lines that are completely out of range
			if (line < Start.Line || line > End.Line)
				return false;

			// 2. Reject positions before the start column on the first line
			if (line == Start.Line && character < Start.Column)
				return false;

			// 3. Reject positions after the end column on the last line
			if (line == End.Line && character > End.Column)
				return false;

			// Everything else is inside the range
			return true;
		}

		public override bool Equals(object? obj) => obj is FileRange other && Equals(other);

		public bool Equals(FileRange other) =>
			Start.Equals(other.Start) &&
			End.Equals(other.End);

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + Start.GetHashCode();
				hash = hash * 31 + End.GetHashCode();
				return hash;
			}
		}

		public static bool operator ==(FileRange left, FileRange right) => left.Equals(right);

		public static bool operator !=(FileRange left, FileRange right) => !left.Equals(right);
	}
}
