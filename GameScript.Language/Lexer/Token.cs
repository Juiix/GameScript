using System;
using GameScript.Language.File;

namespace GameScript.Language.Lexer
{
	public readonly ref struct Token
	{
		public TokenType Type { get; }
		public ReadOnlySpan<char> Value { get; }
		public FileRange Range { get; }

		public Token(TokenType type, ReadOnlySpan<char> value, FileRange range)
		{
			Type = type;
			Value = value;
			Range = range;
		}

		public FilePosition Start => Range.Start;
		public FilePosition End => Range.End;
	}
}
