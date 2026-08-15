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
		foreach (var kw in new[] { "func", "label", "command", "return", "returns", "if", "else", "while", "break", "continue", "and", "or", "not", "trigger", "switch", "case", "default", "for", "in" })
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
	public void Marked_And_Bare_Identifiers_Are_Single_Tokens()
	{
		var tokens = LexNoLayout("^const @ctx plain_name");
		Assert.Equal(new[] {
			(TokenType.Identifier, "^const"),
			(TokenType.Identifier, "@ctx"),
			(TokenType.Identifier, "plain_name") }, tokens);
	}

	[Fact]
	public void Removed_Sigils_Are_Error_Tokens()
	{
		Assert.Equal(TokenType.Error, LexNoLayout("$old")[0].Type);
		Assert.Equal(TokenType.Error, LexNoLayout("~call")[0].Type);
	}

	[Fact]
	public void Percent_Is_An_Operator()
	{
		var tokens = LexNoLayout("a % b %= c");
		Assert.Equal(TokenType.Operator, tokens[1].Type);
		Assert.Equal("%", tokens[1].Value);
		Assert.Equal(TokenType.Operator, tokens[3].Type);
		Assert.Equal("%=", tokens[3].Value);
	}

	[Fact]
	public void Operators_Single_And_Multi_Char()
	{
		var tokens = LexNoLayout("+ - * / % = == != <= >= ++ -- += -= %=");
		foreach (var (type, _) in tokens)
			Assert.Equal(TokenType.Operator, type);
		Assert.Equal(15, tokens.Count);
	}

	[Fact]
	public void Indentation_Produces_Indent_And_Dedent()
	{
		var tokens = Lex("a\n    b\nc");
		Assert.Contains((TokenType.Indent, ""), tokens);
		Assert.Contains((TokenType.Dedent, ""), tokens);
	}

	[Fact]
	public void Tab_Indentation_Is_An_Error()
	{
		var tokens = Lex("a\n\tb");
		Assert.Contains(tokens, t => t.Type == TokenType.Error && t.Value == Tokenizer.TabIndentMessage);
	}

	[Fact]
	public void Indent_Must_Be_Multiple_Of_Four()
	{
		var tokens = Lex("a\n   b");
		Assert.Contains(tokens, t => t.Type == TokenType.Error && t.Value == Tokenizer.BadIndentMessage);
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
		var tokens = LexNoLayout(".cmd .@ctx");
		Assert.Equal(new[] {
			(TokenType.Identifier, ".cmd"),
			(TokenType.Identifier, ".@ctx") }, tokens);
	}

	[Fact]
	public void Range_Token_Between_Numbers()
	{
		var tokens = LexNoLayout("0..10");
		Assert.Equal(new[] {
			(TokenType.Number, "0"),
			(TokenType.Range, ".."),
			(TokenType.Number, "10") }, tokens);
	}

	[Fact]
	public void Range_Token_Between_Identifiers()
	{
		var tokens = LexNoLayout("x..y");
		Assert.Equal(new[] {
			(TokenType.Identifier, "x"),
			(TokenType.Range, ".."),
			(TokenType.Identifier, "y") }, tokens);
	}

	[Fact]
	public void Range_Before_Call_Does_Not_Capture_A_Dot_Identifier()
	{
		// regression: '..inv_size' previously lexed as one multi-dot identifier
		var tokens = LexNoLayout("0..inv_size(^inv)");
		Assert.Equal((TokenType.Number, "0"), tokens[0]);
		Assert.Equal((TokenType.Range, ".."), tokens[1]);
		Assert.Equal((TokenType.Identifier, "inv_size"), tokens[2]);
	}

	[Fact]
	public void Newlines_Inside_Parens_Are_Joined()
	{
		// implicit line joining: no EndOfLine/Indent/Dedent tokens inside '(...)'
		var tokens = Lex("f(1,\n        2)\nnext");
		Assert.Equal(new[] {
			(TokenType.Identifier, "f"),
			(TokenType.OpenParen, "("),
			(TokenType.Number, "1"),
			(TokenType.Comma, ","),
			(TokenType.Number, "2"),
			(TokenType.CloseParen, ")"),
			(TokenType.EndOfLine, "\n"),
			(TokenType.Identifier, "next") }, tokens);
	}

	[Fact]
	public void Indentation_Resumes_After_Parens_Close()
	{
		var tokens = Lex("func f(int a,\n        int b)\n    return");
		// the body line after the joined signature still indents normally
		Assert.Contains((TokenType.Indent, ""), tokens);
		Assert.DoesNotContain(tokens, t => t.Type == TokenType.Error);
	}

	[Fact]
	public void Multi_Dot_Prefix_Now_Lexes_As_Range()
	{
		// '..cmd' (2+ dots) was never a valid identifier form; it now lexes as Range + name
		var tokens = LexNoLayout("..cmd");
		Assert.Equal(new[] {
			(TokenType.Range, ".."),
			(TokenType.Identifier, "cmd") }, tokens);
	}
}
