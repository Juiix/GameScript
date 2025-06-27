using System;

namespace GameScript.Language.File
{
	public ref struct LineReader
	{
		private readonly ReadOnlySpan<char> _source;
		private int _position;
		private int _line;

		public LineReader(ReadOnlySpan<char> source) : this()
		{
			_source = source;
		}

		/// <summary>
		///  Returns <c>true</c> and the next logical line (without newline) or
		///  <c>false</c> on EOF. <paramref name="lineStart"/> contains the absolute
		///  offset of the returned span.
		/// </summary>
		public bool TryGetNextLine(out ReadOnlySpan<char> text,
								   out FilePosition lineStart)
		{
			// Absolute offset where this line begins
			int start = _position;

			/*──────────── EOF ───────────*/
			if (start >= _source.Length)
			{
				text = default;
				lineStart = default;
				return false;
			}

			/*──────────── find newline char ───────────*/
			ReadOnlySpan<char> slice = _source[start..];
			int nl = slice.IndexOfAny('\r', '\n');

			if (nl < 0)                    // final line, no terminator
			{
				text = slice;
				lineStart = new FilePosition(start, _line++, 0);
				_position = _source.Length;
				return true;
			}

			/*──────────── emit line before newline ────*/
			text = slice[..nl];
			lineStart = new FilePosition(start, _line++, 0);

			/*──────────── skip newline sequence ───────*/
			_position = start + nl + 1;            // consume '\n'  or '\r'
			if (slice[nl] == '\r' &&
				_position < _source.Length &&
				_source[_position] == '\n')
			{
				_position++;                       // consume LF of CRLF
			}

			return true;
		}
	}
}
