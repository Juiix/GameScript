namespace GameScript.Language.Lexer
{
	public enum TokenType
	{
		None = 0,
		Identifier,
		Keyword,
		Number,
		String,
		Boolean,
		Operator,
		OpenParen,
		CloseParen,
		Comma,
		EndOfLine,
		EndOfFile,
		Indent,
		Dedent,
		Comment,
		Colon,
		Error,
		Range,
		OpenBracket,
		CloseBracket,
		Dot
	}
}
