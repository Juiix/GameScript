using System;
using System.Collections.Generic;
using System.Linq;
using GameScript.Language.File;

namespace GameScript.Language.Lexer
{
	public ref struct Tokenizer
	{
		private readonly static HashSet<long> s_keywords = new(Constants.Keywords.Select(x => EncodeSpan(x.AsSpan())));
		private readonly static HashSet<long> s_bools = new(Constants.Bools.Select(x => EncodeSpan(x.AsSpan())));

		private LineReader _reader;
		private ReadOnlySpan<char> _text;
		private int _column;
		private FilePosition _lineStart;
		private int _indent;
		private bool _inMultilineComment;
		private readonly List<int> _lineOffsets = [];

		public Tokenizer(ReadOnlySpan<char> source)
		{
			_reader = new LineReader(source);
			_text = [];
			_column = 0;
			_lineStart = default;
			_indent = 0;
			_inMultilineComment = false;
		}

		public IReadOnlyList<int> LineOffsets => _lineOffsets;
		private FilePosition CurrentFilePosition => new(_lineStart.Position + _column, _lineStart.Line, _lineStart.Column + _column);
		private FileRange CurrentRange(FilePosition start) => new(start, CurrentFilePosition);

		/// <summary>
		/// Retrieves the next token from the source.
		/// </summary>
		public Token NextToken()
		{
			/*──────────────────────────────────────────────────────────────
			 * 1.  Refill line buffer — emit EndOfFile / EndOfLine if needed
			 *──────────────────────────────────────────────────────────────*/
			if (_column >= _text.Length)
			{
				// EOF?
				if (!_reader.TryGetNextLine(out _text, out var newPos))
					return new Token(TokenType.EndOfFile, [], CurrentRange(CurrentFilePosition));

				// End-of-line token for the line we just finished.
				_lineOffsets.Add(newPos.Position);
				var start = CurrentFilePosition;
				_column = 0;
				_lineStart = newPos;

				if (newPos.Line > 0)
					return new Token(TokenType.EndOfLine, "\n".AsSpan(), new FileRange(start, newPos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 2.  Indentation / whitespace
			 *──────────────────────────────────────────────────────────────*/
			if (_column == 0 && ProcessIndentation(out var indentTok))
				return indentTok;

			SkipWhitespace();
			if (_column >= _text.Length)
				return NextToken();

			/*──────────────────────────────────────────────────────────────
			 * 3.  Single-char punctuation and newline
			 *──────────────────────────────────────────────────────────────*/
			char ch = _text[_column];
			var pos = CurrentFilePosition;          // capture BEFORE we advance

			switch (ch)
			{
				case '\n': _column++; return new Token(TokenType.EndOfLine, "\n".AsSpan(), CurrentRange(pos));
				case '(': _column++; return new Token(TokenType.OpenParen, "(".AsSpan(), CurrentRange(pos));
				case ')': _column++; return new Token(TokenType.CloseParen, ")".AsSpan(), CurrentRange(pos));
				case ',': _column++; return new Token(TokenType.Comma, ",".AsSpan(), CurrentRange(pos));
				case ':': _column++; return new Token(TokenType.Colon, ":".AsSpan(), CurrentRange(pos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 4.  Comment handling
			 *──────────────────────────────────────────────────────────────*/
			if (ch == '/' && _column + 1 < _text.Length)
			{
				ReadOnlySpan<char> two = _text.Slice(_column, 2);

				// block comment  /* ... */
				if (two.SequenceEqual("/*".AsSpan()))
				{
					int idx = _text.Slice(_column + 2).IndexOf("*/".AsSpan());
					ReadOnlySpan<char> span;

					if (idx >= 0)
					{
						span = _text.Slice(_column, idx + 4);
						_column += span.Length;
						_inMultilineComment = false;
					}
					else
					{
						span = _text.Slice(_column);
						_column = _text.Length;
						_inMultilineComment = true;
					}
					return new Token(TokenType.Comment, span, CurrentRange(pos));
				}

				// line comment  //
				if (two.SequenceEqual("//".AsSpan()))
				{
					var span = _text.Slice(_column);
					_column = _text.Length;
					return new Token(TokenType.Comment, span, CurrentRange(pos));
				}
			}

			// continue unfinished block-comment
			if (_inMultilineComment)
			{
				int idx = _text.Slice(_column).IndexOf("*/".AsSpan());
				ReadOnlySpan<char> span;

				if (idx >= 0)
				{
					span = _text.Slice(_column, idx + 2);
					_column += span.Length;
					_inMultilineComment = false;
				}
				else
				{
					span = _text.Slice(_column);
					_column = _text.Length;
				}
				return new Token(TokenType.Comment, span, CurrentRange(pos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 5.  Operator token
			 *──────────────────────────────────────────────────────────────*/
			if (IsOperatorChar(ch))
			{
				int start = _column++;
				if (_column < _text.Length &&
					IsMultiCharOperator(_text.Slice(start, 2)))
					_column++;                         // consume the second char

				return new Token(TokenType.Operator,
								 _text.Slice(start, _column - start),
								 CurrentRange(pos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 6.  String literal
			 *──────────────────────────────────────────────────────────────*/
			if (ch == '"')
			{
				int start = _column++;
				bool esc = false;

				while (_column < _text.Length && (_text[_column] != '"' || esc))
				{
					esc = _text[_column] == '\\';
					_column++;
				}
				if (_column < _text.Length) _column++;   // consume closing quote

				return new Token(TokenType.String,
								 _text.Slice(start, _column - start),
								 CurrentRange(pos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 7.  Number literal
			 *──────────────────────────────────────────────────────────────*/
			if (char.IsDigit(ch))
			{
				int start = _column;
				while (_column < _text.Length && char.IsDigit(_text[_column]))
					_column++;

				return new Token(TokenType.Number,
								 _text.Slice(start, _column - start),
								 CurrentRange(pos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 8.  Identifier / keyword / boolean
			 *──────────────────────────────────────────────────────────────*/
			if (char.IsLetter(ch) || IsVariableAccessor(ch) || IsFunctionCall(ch))
			{
				int start = _column;
				while (_column < _text.Length &&
					   (char.IsLetterOrDigit(_text[_column]) ||
						_text[_column] == '_' ||
						(_column == start &&
						 (IsVariableAccessor(_text[_column]) || IsFunctionCall(_text[_column])))))
				{
					_column++;
				}

				ReadOnlySpan<char> span = _text.Slice(start, _column - start);

				if (IsKeyword(span))
					return new Token(TokenType.Keyword, span, CurrentRange(pos));
				if (IsBoolean(span))
					return new Token(TokenType.Boolean, span, CurrentRange(pos));

				return new Token(TokenType.Identifier, span, CurrentRange(pos));
			}

			/*──────────────────────────────────────────────────────────────
			 * 9.  Unknown – skip char and retry
			 *──────────────────────────────────────────────────────────────*/
			_column++;
			return NextToken();
		}

		private bool ProcessIndentation(out Token token)
		{
			// Save current position.
			var startPos = CurrentFilePosition;
			int pos = _column;
			int indent = 0;
			// Count spaces (or expand tabs as needed).
			while (pos < _text.Length)
			{
				char c = _text[pos];
				if (c == ' ')
				{
					indent++;
				}
				else if (c == '\t')
				{
					// Convert tab to spaces (for example, assume 4 spaces per tab).
					indent += 4;
				}
				else
				{
					break;
				}
				pos++;
			}

			var tabs = (indent + 2) / 4;

			if (tabs > _indent)
			{
				_indent += 1;
				token = new Token(TokenType.Indent, ReadOnlySpan<char>.Empty, CurrentRange(startPos));
				return true;
			}
			else if (tabs < _indent)
			{
				_indent -= 1;
				token = new Token(TokenType.Dedent, ReadOnlySpan<char>.Empty, CurrentRange(startPos));
				return true;
			}

			// indent change done
			// Update _position so that it starts at the first non-space character.
			_column = pos;

			token = default;
			return false;
		}

		private void SkipWhitespace()
		{
			while (_column < _text.Length &&
				   char.IsWhiteSpace(_text[_column]) &&
				   _text[_column] != '\n')
			{
				_column++;
			}
		}

		private static bool IsOperatorChar(char c)
		{
			return c == '+' || c == '-' || c == '*' || c == '/' ||
				   c == '=' || c == '<' || c == '>' || c == '!';
		}

		private static bool IsMultiCharOperator(ReadOnlySpan<char> op)
		{
			// Compare the span with known multi-character operators.
			return op.SequenceEqual("==".AsSpan()) ||
				   op.SequenceEqual("<=".AsSpan()) ||
				   op.SequenceEqual(">=".AsSpan()) ||
				   op.SequenceEqual("!=".AsSpan()) ||
				   op.SequenceEqual("++".AsSpan()) ||
				   op.SequenceEqual("--".AsSpan()) ||
				   op.SequenceEqual("+=".AsSpan()) ||
				   op.SequenceEqual("-=".AsSpan()) ||
				   op.SequenceEqual("*=".AsSpan()) ||
				   op.SequenceEqual("/=".AsSpan());
		}

		private static bool IsKeyword(ReadOnlySpan<char> value)
		{
			// Define a set of DSL keywords.
			if (value.Length > 12) return false;
			var enc = EncodeSpan(value);
			return s_keywords.Contains(enc);
		}

		private static bool IsBoolean(ReadOnlySpan<char> value)
		{
			// Define a set of DSL keywords.
			if (value.Length > 12) return false;
			var enc = EncodeSpan(value);
			return s_bools.Contains(enc);
		}

		private static bool IsVariableAccessor(char c)
		{
			return c == '^' || c == '$' || c == '%';
		}

		private static bool IsFunctionCall(char c)
		{
			return c == '@' || c == '~';
		}

		private static long EncodeSpan(ReadOnlySpan<char> span)
		{
			// Limit to 12 characters
			int length = Math.Min(span.Length, 12);
			long result = 0;

			for (int i = length - 1; i >= 0; i--)
			{
				char c = span[i];
				result *= 37;

				if (c >= 'a' && c <= 'z')
				{
					// Map 'a' to 1, 'b' to 2, ... 'z' to 26.
					result += c - 'a' + 1;
				}
				else if (c >= 'A' && c <= 'Z')
				{
					// Map 'A' to 1, 'B' to 2, ... 'Z' to 26.
					result += c - 'A' + 1;
				}
				else if (c >= '0' && c <= '9')
				{
					// Map '0' to 27, '1' to 28, ... '9' to 36.
					result += c - '0' + 27;
				}
				else
				{
					// Characters not in the mapping are treated as 0 (often a space).
					result += 0;
				}
			}

			return result;
		}
	}
}
