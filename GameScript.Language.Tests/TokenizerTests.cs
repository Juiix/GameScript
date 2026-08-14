using System.Collections.Generic;
using GameScript.Language.Lexer;
using Xunit;

namespace GameScript.Language.Tests;

public class TokenizerTests
{
	private static List<(TokenType Type, string Value)> Lex(string source)
	{
		var tokens = new List<(TokenType, string)>();
		var tokenizer = new Tokenizer(source);
		while (true)
		{
			var token = tokenizer.NextToken();
			if (token.Type == TokenType.EndOfFile)
				break;
			tokens.Add((token.Type, token.Value.ToString()));
		}
		return tokens;
	}

	private static List<(TokenType Type, string Value)> LexNoLayout(string source)
	{
		var tokens = Lex(source);
		return tokens.FindAll(t => t.Type is not (TokenType.EndOfLine or TokenType.Indent or TokenType.Dedent));
	}

	[Fact]
	public void Numbers_Decimal_And_Hex()
	{
		var tokens = LexNoLayout("123 0xff0000 0");
		Assert.Equal(new[] { (TokenType.Number, "123"), (TokenType.Number, "0xff0000"), (TokenType.Number, "0") }, tokens);
	}

	[Fact]
	public void Strings_Keep_Quotes()
	{
		var tokens = LexNoLayout("\"hello world\"");
		Assert.Equal(new[] { (TokenType.String, "\"hello world\"") }, tokens);
	}

	[Fact]
	public void Line_Comment_Emitted_As_Comment_Token()
	{
		var tokens = LexNoLayout("// a comment");
		Assert.Equal(new[] { (TokenType.Comment, "// a comment") }, tokens);
	}

	[Fact]
	public void Block_Comment_Single_Line()
	{
		var tokens = LexNoLayout("/* block */ 5");
		Assert.Equal(new[] { (TokenType.Comment, "/* block */"), (TokenType.Number, "5") }, tokens);
	}

	[Fact]
	public void Keywords_Are_Keyword_Tokens()
	{
		foreach (var kw in new[] { "func", "label", "command", "return", "returns", "if", "else", "while", "break", "continue", "and", "or" })
		{
			var tokens = LexNoLayout(kw);
			Assert.Equal(TokenType.Keyword, tokens[0].Type);
		}
	}

	[Fact]
	public void Booleans_Are_Boolean_Tokens()
	{
		Assert.Equal(TokenType.Boolean, LexNoLayout("true")[0].Type);
		Assert.Equal(TokenType.Boolean, LexNoLayout("false")[0].Type);
	}

	[Fact]
	public void Sigiled_Identifiers_Are_Single_Tokens()
	{
		var tokens = LexNoLayout("$local ^const %ctx ~call @label");
		Assert.Equal(new[] {
			(TokenType.Identifier, "$local"),
			(TokenType.Identifier, "^const"),
			(TokenType.Identifier, "%ctx"),
			(TokenType.Identifier, "~call"),
			(TokenType.Identifier, "@label") }, tokens);
	}

	[Fact]
	public void Operators_Single_And_Multi_Char()
	{
		var tokens = LexNoLayout("+ - * / = == != <= >= ++ -- += -=");
		foreach (var (type, _) in tokens)
			Assert.Equal(TokenType.Operator, type);
		Assert.Equal(13, tokens.Count);
	}

	[Fact]
	public void Indentation_Produces_Indent_And_Dedent()
	{
		var tokens = Lex("a\n    b\nc");
		Assert.Contains((TokenType.Indent, ""), tokens);
		Assert.Contains((TokenType.Dedent, ""), tokens);
	}

	[Fact]
	public void Punctuation_Tokens()
	{
		var tokens = LexNoLayout("( ) , :");
		Assert.Equal(new[] {
			(TokenType.OpenParen, "("),
			(TokenType.CloseParen, ")"),
			(TokenType.Comma, ","),
			(TokenType.Colon, ":") }, tokens);
	}

	[Fact]
	public void Dot_Prefixed_Identifiers()
	{
		var tokens = LexNoLayout(".cmd ..cmd .%ctx");
		Assert.Equal(new[] {
			(TokenType.Identifier, ".cmd"),
			(TokenType.Identifier, "..cmd"),
			(TokenType.Identifier, ".%ctx") }, tokens);
	}
}
