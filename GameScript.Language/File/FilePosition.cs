using System;

namespace GameScript.Language.File
{
	public readonly struct FilePosition(int position, int line, int column) : IEquatable<FilePosition>
	{
		public int Position { get; } = position;
		public int Line { get; } = line;
		public int Column { get; } = column;

		public FilePosition AddColumn(int column) => new(Position + column, Line, Column + column);

		public override bool Equals(object? obj) => obj is FilePosition other && Equals(other);

		public bool Equals(FilePosition other) =>
			Position == other.Position &&
			Line == other.Line &&
			Column == other.Column;

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + Position;
				hash = hash * 31 + Line;
				hash = hash * 31 + Column;
				return hash;
			}
		}

		public static bool operator ==(FilePosition left, FilePosition right) => left.Equals(right);

		public static bool operator !=(FilePosition left, FilePosition right) => !left.Equals(right);
	}
}
