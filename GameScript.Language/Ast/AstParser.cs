using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GameScript.Language.File;
using GameScript.Language.Lexer;

namespace GameScript.Language.Ast;

public ref struct AstParser
{
	private readonly string _filePath;
	private Tokenizer _tokenizer;
	private Token _current;
	private Token _previous;
	private Token _peek;
	private readonly List<FileError> _errors = [];
	private readonly List<CommentNode> _comments = [];
	private StringBuilder? _summaryBuilder;
	private FilePosition _lastSummaryPosition;
	private int _newLineCommentCount;

	public AstParser(string filePath, ReadOnlySpan<char> source) : this()
	{
		_filePath = filePath;
		_tokenizer = new Tokenizer(source);
	}

	/// <summary>
	/// Parser over a single-line fragment embedded at <paramref name="origin"/>
	/// within a larger file (string interpolation expressions).
	/// </summary>
	internal AstParser(string filePath, ReadOnlySpan<char> source, FilePosition origin) : this()
	{
		_filePath = filePath;
		_tokenizer = new Tokenizer(source, origin);
	}

	/// <summary>Parses the entire source as one expression (used for interpolation fragments).</summary>
	internal ExpressionNode ParseExpressionFragment()
	{
		Advance(); // prime tokenizer
		var expr = ParseExpression();
		if (_current.Type is not (TokenType.EndOfFile or TokenType.EndOfLine))
			Error($"Unexpected token in interpolation expression: {_current.Value.ToString()}", _current.Range);
		return expr;
	}

	public IReadOnlyList<FileError> Errors => _errors;
	public IReadOnlyList<CommentNode> Comments => _comments;
	public IReadOnlyList<int> LineOffsets => _tokenizer.LineOffsets;
	private FileRange CurrentRange => _current.Range;
	private FileRange PreviousRange => _previous.Range;

	// Parses an entire program file.
	public ProgramNode ParseProgram()
	{
		var defs = new List<MethodDefinitionNode>();

		Advance(); // prime tokenizer
		var start = _current.Start;

		while (true)
		{
			SkipEndOfLineTokens();

			if (_current.Type == TokenType.EndOfFile)
				break;

			var lineStart = _current.Start;
			defs.Add(ParseDefinition());

			// Only one definition per physical line
			if (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile) &&
				_current.Start.Line == lineStart.Line)
			{
				Error("Only one statement per line is allowed.", _current.Range);
			}
		}

		return new ProgramNode(
			defs, _filePath,
			new FileRange(start, _previous.End));
	}

	public ConstantsNode ParseConstants()
	{
		var defs = new List<ConstantDefinitionNode>();

		Advance(); // prime the tokenizer
		var start = _current.Start;

		while (true)
		{
			SkipEndOfLineTokens();

			if (_current.Type == TokenType.EndOfFile)
				break;

			var lineStart = _current.Start;
			defs.Add(ParseConstantDefinition());

			// Only one statement per physical line
			if (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile) &&
				_current.Start.Line == lineStart.Line)
			{
				Error("Only one statement per line is allowed.", _current.Range);
			}
		}

		return new ConstantsNode(defs, _filePath,
								 new FileRange(start, _previous.End));
	}

	public ContextsNode ParseContexts()
	{
		var defs = new List<ContextDefinitionNode>();

		Advance(); // prime the tokenizer
		var start = _current.Start;

		while (true)
		{
			SkipEndOfLineTokens();

			if (_current.Type == TokenType.EndOfFile)
				break;

			var lineStart = _current.Start;
			defs.Add(ParseContextDefinition());

			// Only one statement per physical line
			if (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile) &&
				_current.Start.Line == lineStart.Line)
			{
				Error("Only one statement per line is allowed.", _current.Range);
			}
		}

		return new ContextsNode(defs, _filePath,
								 new FileRange(start, _previous.End));
	}

	// Parses definition
	private MethodDefinitionNode ParseDefinition()
	{
		if (CurrentIsKeyword("label"))
			Error("'label' declarations are removed; declare a 'func' instead (calls tail-transfer automatically)", CurrentRange);

		var method = _current switch
		{
			{ Type: TokenType.Keyword } when CurrentIsKeyword("func") => ParseMethodDefinition(IdentifierType.Func),
			{ Type: TokenType.Keyword } when CurrentIsKeyword("label") => ParseMethodDefinition(IdentifierType.Func),
			{ Type: TokenType.Keyword } when CurrentIsKeyword("command") => ParseMethodDefinition(IdentifierType.Command),
			{ Type: TokenType.Keyword } when CurrentIsKeyword("trigger") => ParseMethodDefinition(IdentifierType.TriggerDeclaration),
			{ Type: TokenType.Identifier } => ParseMethodDefinition(IdentifierType.Trigger),
			_ => null
		};

		if (method == null)
		{
			Error($"Unexpected token in method definition: {_current.Value.ToString()}", _current.Range);
		}

		return method ?? ParseMethodDefinition(IdentifierType.Unknown);
	}

	// Parses a statement. This would dispatch to various kinds of statements.
	private AstNode ParseStatement()
	{
		return _current.Type switch
		{
			TokenType.Keyword => _current.Value switch
			{
				"if" => ParseIfStatement(),
				"while" => ParseWhileStatement(),
				"for" => ParseForStatement(),
				"switch" => ParseSwitchStatement(),
				"return" => ParseReturnStatement(),
				"break" => ParseBreakStatement(),
				"continue" => ParseContinueStatement(),
				"func" when PeekIsIdentifier() => ParseVariableDefinition(),
				_ => ParseExpression()
			},
			TokenType.Identifier when PeekIsIdentifier() => ParseVariableDefinition(),
			_ => ParseExpression()
		};
	}

	private MethodDefinitionNode ParseMethodDefinition(IdentifierType idType)
	{
		var summary = GetSummary();
		var start = _current.Start;

		// leading keyword (func / label / command / trigger)
		var kwTok = _current;
		var kwNode = new KeywordNode(_current.Value.ToString(), _filePath, kwTok.Range);
		Advance();

		// method name
		var nameTok = Expect(TokenType.Identifier, "Expected method name", "?".AsSpan());
		var nameStart = _previous.Start;
		IdentifierDeclarationNode nameNode;
		if (idType == IdentifierType.Trigger && Match(TokenType.Colon))
		{
			var comTok = Expect(TokenType.Identifier, "Expected component identifier", "?".AsSpan());
			var combined = $"{nameTok.Value}:{comTok.Value}";
			nameNode = new IdentifierDeclarationNode(
							   combined, idType, summary,
							   _filePath, new FileRange(nameStart, _previous.End));
		}
		else
		{
			nameNode = new IdentifierDeclarationNode(
							   nameTok.Value.ToString(),
							   idType, summary, _filePath, PreviousRange);

			// trigger declarations name the trigger kind only — no ':component' subjects
			if (idType == IdentifierType.TriggerDeclaration && _current.Type == TokenType.Colon)
			{
				Error("Trigger declarations name the trigger kind only; subjects belong on handlers", CurrentRange);
				Advance();                       // ':'
				Match(TokenType.Identifier);     // skip the component name, if present
			}
		}

		/* ───── parameters ───── */
		List<ParameterNode>? parameters = null;
		if (Match(TokenType.OpenParen))
		{
			parameters = ParseParameterList();
			Expect(TokenType.CloseParen, "Expected ')' after parameters", ")".AsSpan());

			// trigger headers must omit '()' entirely when there are no parameters
			if (idType == IdentifierType.Trigger && parameters == null)
				Error("Trigger handlers with no parameters must omit the '()'", PreviousRange);
		}
		else if (Match(TokenType.CloseParen))                    // stray ')'
			Error("Expected '(' before parameters", PreviousRange);

		/* ───── returns clause ───── */
		List<ReturnTypeNode>? returns = null;
		KeywordNode? returnsKw = null;

		if (Match(TokenType.Keyword, "returns"))
		{
			returnsKw = new KeywordNode(_previous.Value.ToString(), _filePath, PreviousRange);

			if (Match(TokenType.OpenParen))
			{
				returns = ParseReturnTypes();
				Expect(TokenType.CloseParen, "Expected ')' after return types", ")".AsSpan());
			}
			else if (Match(TokenType.CloseParen))                // stray ')'
				Error("Expected '(' before return types", PreviousRange);
			else
				returns = [ParseReturnType()];
		}

		/* ───── '=' internal-name binding (commands only) ───── */
		OperatorNode? bindingOp = null;
		IdentifierDeclarationNode? bindingName = null;
		if (Match(TokenType.Operator, "="))
		{
			bindingOp = new OperatorNode("=", _filePath, PreviousRange);
			var internalTok = Expect(TokenType.Identifier, "Expected an engine-op name after '='", "?".AsSpan());
			bindingName = new IdentifierDeclarationNode(internalTok.Value.ToString(),
														IdentifierType.EngineOp, null, _filePath, internalTok.Range);
			if (idType != IdentifierType.Command)
				Error("'=' op binding is only allowed on command declarations", bindingOp.FileRange);
		}

		/* ───── body ───── */
		var body = ParseBlock();     // may be null if no nested indent
		AstNode? lastNode = (AstNode?)body
			?? (AstNode?)bindingName
			?? (AstNode?)returns?.LastOrDefault()
			?? (AstNode?)returnsKw
			?? (AstNode?)parameters?.LastOrDefault()
			?? (AstNode?)nameNode;

		return new MethodDefinitionNode(
			kwNode, returnsKw, returns, nameNode, parameters, body, _filePath,
			new FileRange(start, lastNode?.FileRange.End ?? start),
			bindingOp, bindingName);
	}

	private ConstantDefinitionNode ParseConstantDefinition()
	{
		var summary = GetSummary();
		var start = _current.Start;

		// constant type
		var typeTok = ExpectTypeIdentifier("Expected a type for constant declaration");
		var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

		// constant name (must start with '^')
		var nameTok = ExpectStartsWith(TokenType.Identifier, "^", "Expected constant name (must start with '^')", "^?".AsSpan());
		var nameNode = new IdentifierDeclarationNode(nameTok.Value.TrimStart('^').ToString(),
													 IdentifierType.Constant, summary, _filePath, PreviousRange);

		// '=' operator
		var opTok = Expect(TokenType.Operator, "Expected '=' in constant declaration", "=".AsSpan());
		if (!opTok.Value.SequenceEqual("=".AsSpan()))
			Error("Expected '=' operator for constant declaration", opTok.Range);

		var opNode = new OperatorNode(opTok.Value.ToString(), _filePath, opTok.Range);

		// initializer
		var initializer = ParseExpression();

		return new ConstantDefinitionNode(
			typeNode, nameNode, opNode, initializer, _filePath,
			new FileRange(start, _previous.End));
	}

	private ContextDefinitionNode ParseContextDefinition()
	{
		var summary = GetSummary();
		var start = _current.Start;

		// constant type
		var typeTok = ExpectTypeIdentifier("Expected a type for context variable declaration");
		var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

		// context variable name (must start with '@')
		var nameTok = ExpectStartsWith(TokenType.Identifier, "@", "Expected context variable name (must start with '@')", "@?".AsSpan());
		var nameNode = new IdentifierDeclarationNode(nameTok.Value.TrimStart('@').ToString(),
													 IdentifierType.Context, summary, _filePath, PreviousRange);

		// '=' operator
		var opTok = Expect(TokenType.Operator, "Expected '=' in context declaration", "=".AsSpan());
		if (!opTok.Value.SequenceEqual("=".AsSpan()))
			Error("Expected '=' operator for context declaration", opTok.Range);

		var opNode = new OperatorNode(opTok.Value.ToString(), _filePath, opTok.Range);

		// initializer
		var initializer = ParseExpression();

		return new ContextDefinitionNode(
			typeNode, nameNode, opNode, initializer, _filePath,
			new FileRange(start, _previous.End));
	}

	private IfStatementNode ParseIfStatement()
	{
		var start = _current.Start;

		// 'if' keyword
		var ifTok = Expect(TokenType.Keyword, "Expected 'if' keyword");
		if (!ifTok.Value.SequenceEqual("if".AsSpan()))
			Error("Expected 'if' keyword", ifTok.Range);

		var ifKeyword = new KeywordNode(ifTok.Value.ToString(), _filePath, PreviousRange);

		// condition and main block
		var condition = ParseConditionExpression();
		var ifBlock = ParseIfBody();

		// optional else-if / else chains
		List<ElseIfStatementNode>? elseIfs = null;
		BlockNode? elseBlk = null;
		KeywordNode? elseKey = null;

		SkipEndOfLineTokens();

		while (CurrentIsKeyword("else"))
		{
			var elseStart = _current.Start;

			var elseTok = Expect(TokenType.Keyword, "Expected 'else'");
			var elseKeyword = new KeywordNode(elseTok.Value.ToString(), _filePath, PreviousRange);

			if (CurrentIsKeyword("if"))                                // else if …
			{
				var elseIfTok = Expect(TokenType.Keyword, "Expected 'if' after 'else'");
				var elseIfKey = new KeywordNode(elseIfTok.Value.ToString(), _filePath, PreviousRange);

				var elseIfCond = ParseConditionExpression();
				var elseIfBlock = ParseIfBody();

				(elseIfs ??= []).Add(
					new ElseIfStatementNode(elseKeyword, elseIfKey, elseIfCond, elseIfBlock,
											_filePath, new FileRange(elseStart, _previous.End)));
			}
			else                                                       // plain else
			{
				elseBlk = ParseIfBody();
				elseKey = elseKeyword;
				break;                                                 // only one final else allowed
			}

			SkipEndOfLineTokens();
		}

		return new IfStatementNode(
			ifKeyword, condition, ifBlock, elseIfs, elseKey, elseBlk,
			_filePath, new FileRange(start, _previous.End));
	}

	private WhileStatementNode ParseWhileStatement()
	{
		var start = _current.Start;

		// 'while' keyword
		var kwTok = Expect(TokenType.Keyword, "Expected 'while' keyword");
		if (!kwTok.Value.SequenceEqual("while".AsSpan()))
			Error("Expected 'while' keyword", kwTok.Range);

		var kwNode = new KeywordNode(kwTok.Value.ToString(), _filePath, PreviousRange);

		// Condition and body
		var condition = ParseConditionExpression();
		var body = ParseBlock();   // may be null if no nested block

		return new WhileStatementNode(
			kwNode, condition, body, _filePath,
			new FileRange(start, _previous.End));
	}

	private ForStatementNode ParseForStatement()
	{
		var start = _current.Start;

		// 'for' keyword
		var kwTok = Expect(TokenType.Keyword, "Expected 'for' keyword");
		var kwNode = new KeywordNode(kwTok.Value.ToString(), _filePath, PreviousRange);

		// loop variable — always a plain int local
		var nameTok = Expect(TokenType.Identifier, "Expected a loop variable name after 'for'", "?".AsSpan());
		var varName = nameTok.Value.ToString();
		var varNode = new IdentifierDeclarationNode(varName, IdentifierType.Local, null,
													_filePath, PreviousRange);
		if (varName.Length > 0 && !char.IsLetter(varName[0]) && varName[0] != '_')
			Error("Loop variable must be a plain local name", varNode.FileRange);

		// 'in' keyword
		var inTok = Expect(TokenType.Keyword, "Expected 'in' after the loop variable", "in".AsSpan());
		if (!inTok.Value.SequenceEqual("in".AsSpan()))
			Error("Expected 'in' after the loop variable", inTok.Range);
		var inNode = new KeywordNode(inTok.Value.ToString(), _filePath, inTok.Range);

		// START .. END (half-open range)
		var startExpr = ParseExpression();
		var rangeTok = Expect(TokenType.Range, "Expected '..' between the range start and end", "..".AsSpan());
		var rangeNode = new OperatorNode(rangeTok.Value.ToString(), _filePath, rangeTok.Range);
		var endExpr = ParseExpression();

		var body = ParseBlock();

		return new ForStatementNode(
			kwNode, varNode, inNode, startExpr, rangeNode, endExpr, body,
			_filePath, new FileRange(start, _previous.End));
	}

	private SwitchStatementNode ParseSwitchStatement()
	{
		var start = _current.Start;

		// 'switch' keyword
		var kwTok = Expect(TokenType.Keyword, "Expected 'switch' keyword");
		var kwNode = new KeywordNode(kwTok.Value.ToString(), _filePath, PreviousRange);

		var subject = ParseConditionExpression();

		SkipEndOfLineTokens();
		if (!Match(TokenType.Indent))
		{
			Error("A switch requires at least one 'case' or 'default'", new FileRange(start, _previous.End));
			return new SwitchStatementNode(kwNode, subject, null, _filePath,
										   new FileRange(start, _previous.End));
		}

		SkipEndOfLineTokens();

		List<SwitchCaseNode>? cases = null;
		SwitchCaseNode? defaultCase = null;
		var defaultNotLastReported = false;

		while (_current.Type is not (TokenType.Dedent or TokenType.EndOfFile))
		{
			if (CurrentIsKeyword("case") || CurrentIsKeyword("default"))
			{
				var isDefault = CurrentIsKeyword("default");
				var caseNode = ParseSwitchCase(isDefault);
				(cases ??= []).Add(caseNode);

				if (isDefault)
				{
					if (defaultCase != null)
						Error("Only one 'default' case is allowed", caseNode.Keyword.FileRange);
					else
						defaultCase = caseNode;
				}
				else if (defaultCase != null && !defaultNotLastReported)
				{
					Error("'default' must be the last case in a switch", defaultCase.Keyword.FileRange);
					defaultNotLastReported = true;
				}
			}
			else
			{
				Error("Expected 'case' or 'default' inside switch", _current.Range);
				while (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile))
					Advance();
			}

			SkipEndOfLineTokens();
		}

		Match(TokenType.Dedent);     // consume the dedent, if present

		return new SwitchStatementNode(
			kwNode, subject, cases, _filePath,
			new FileRange(start, _previous.End));
	}

	private SwitchCaseNode ParseSwitchCase(bool isDefault)
	{
		var start = _current.Start;

		// 'case' / 'default' keyword
		var kwTok = Expect(TokenType.Keyword, isDefault ? "Expected 'default' keyword" : "Expected 'case' keyword");
		var kwNode = new KeywordNode(kwTok.Value.ToString(), _filePath, PreviousRange);

		// case values: <expr> (',' <expr>)*
		List<ExpressionNode>? values = null;
		if (!isDefault)
		{
			values = [];
			do
			{
				values.Add(ParseExpression());
			}
			while (Match(TokenType.Comma));
		}

		Expect(TokenType.Colon,
			   isDefault ? "Expected ':' after 'default'" : "Expected ':' after the case value(s)",
			   ":".AsSpan());

		var (body, isInline) = ParseColonBody();

		return new SwitchCaseNode(
			kwNode, values, body, isInline, _filePath,
			new FileRange(start, _previous.End));
	}

	// Parses the body following a block-opening ':' — either a single inline
	// statement on the same line, or an indented block on the following lines.
	// Declaring both is an error (inline XOR block).
	private (BlockNode? body, bool isInline) ParseColonBody()
	{
		if (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile))
		{
			// inline form: a single statement on the same line
			var lineStart = _current.Start;
			var stmt = ParseStatement();

			if (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile) &&
				_current.Start.Line == lineStart.Line)
			{
				Error("Only one statement per line is allowed.", _current.Range);
			}

			var inlineBlock = new BlockNode([stmt], _filePath, stmt.FileRange);

			// a stray indented block after an inline statement is an error
			var stray = ParseBlock();
			if (stray != null)
				Error("Cannot combine an inline statement with an indented block", stray.FileRange);

			return (inlineBlock, true);
		}

		// block form: indented statements on the following lines
		return (ParseBlock(), false);
	}

	// Parses an if/else body: an indented block, or ': stmt' inline form.
	private BlockNode? ParseIfBody()
	{
		if (!Match(TokenType.Colon))
			return ParseBlock();

		var colonRange = PreviousRange;
		var (body, isInline) = ParseColonBody();
		if (!isInline)
			Error("Expected a statement after ':' (omit the ':' to open an indented block)", colonRange);
		return body;
	}

	// Parses an if/while condition, rejecting full-wrap parentheses ('if (x)').
	// Inner grouping ('if (a or b) and c') is untouched — only a condition that IS
	// a grouping expression is flagged.
	private ExpressionNode ParseConditionExpression()
	{
		var condition = ParseExpression();
		if (condition is ParenthesizedExpressionNode)
			Error("Remove the parentheses around the condition", condition.FileRange);
		return condition;
	}

	private ReturnStatementNode ParseReturnStatement()
	{
		var start = _current.Start;

		// 'return' keyword
		var kwTok = Expect(TokenType.Keyword, "Expected 'return' keyword");
		var kwNode = new KeywordNode(kwTok.Value.ToString(), _filePath, PreviousRange);

		// Optional return value (not EOL / Dedent / EOF)
		ExpressionNode? value =
			_current.Type is TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile
				? null
				: ParseExpression();

		return new ReturnStatementNode(
			kwNode, value, _filePath,
			new FileRange(start, _previous.End));
	}

	private BreakStatementNode ParseBreakStatement()
	{
		// Consume "break"
		var token = Expect(TokenType.Keyword, "Expected 'break' keyword");
		var keyword = new KeywordNode(token.Value.ToString(), _filePath, PreviousRange);

		return new BreakStatementNode(keyword, _filePath, PreviousRange);
	}

	private ContinueStatementNode ParseContinueStatement()
	{
		// Consume "continue"
		var token = Expect(TokenType.Keyword, "Expected 'continue' keyword");
		var keyword = new KeywordNode(token.Value.ToString(), _filePath, PreviousRange);

		return new ContinueStatementNode(keyword, _filePath, PreviousRange);
	}

	private VariableDefinitionNode ParseVariableDefinition()
	{
		var summary = GetSummary();
		var start = _current.Start;

		// variable **type**
		var typeTok = ExpectTypeIdentifier("Expected a type for variable declaration");
		var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

		var vars = new List<(IdentifierDeclarationNode, ExpressionNode?)>();

		// <name> [ '=' <expr> ] (',' <name> [ '=' <expr> ])*
		do
		{
			var nameTok = Expect(TokenType.Identifier, "Expected variable name", "?".AsSpan());
			var nameNode = new IdentifierDeclarationNode(nameTok.Value.ToString(),
														 IdentifierType.Local, summary, _filePath, PreviousRange);

			// optional initializer
			ExpressionNode? init = null;
			if (Match(TokenType.Operator))
			{
				var opTok = _previous;                       // just consumed
				if (!opTok.Value.SequenceEqual("=".AsSpan()))
					Error("Expected '=' operator in variable declaration",
						  new FileRange(start, _current.Start));

				init = ParseExpression();
			}

			vars.Add((nameNode, init));
		}
		while (Match(TokenType.Comma));

		return new VariableDefinitionNode(
			typeNode, vars, _filePath,
			new FileRange(start, _previous.End));
	}

	// Parses parameter list: parameters are (Type Identifier [, ...])
	private List<ParameterNode>? ParseParameterList()
	{
		// Empty parameter list: "()"
		if (_current.Type == TokenType.CloseParen)
			return null;

		var parameters = new List<ParameterNode>();

		// <type> <name> (',' <type> <name>)*
		do
		{
			var summary = GetSummary();
			var start = _current.Start;

			var typeTok = ExpectTypeIdentifier("Expected parameter type");
			var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

			var nameTok = Expect(TokenType.Identifier, "Expected parameter name", "?".AsSpan());
			var nameNode = new IdentifierDeclarationNode(nameTok.Value.ToString(),
														 IdentifierType.Local, summary, _filePath, PreviousRange);

			// optional default value: <type> <name> = <expr>
			ExpressionNode? defaultValue = null;
			if (Match(TokenType.Operator, "="))
				defaultValue = ParseExpression();

			parameters.Add(new ParameterNode(typeNode, nameNode, defaultValue, _filePath,
											 new FileRange(start, _previous.End)));
		}
		while (Match(TokenType.Comma));

		return parameters;
	}

	// Parses return type list: types are (Type [, ...])
	private List<ReturnTypeNode>? ParseReturnTypes()
	{
		// No return types at all: `()`
		if (_current.Type == TokenType.CloseParen)
			return null;

		var types = new List<ReturnTypeNode>();

		// Parse first and subsequent types: <type> (',' <type>)*
		do
		{
			types.Add(ParseReturnType());
		}
		while (Match(TokenType.Comma));

		return types;
	}

	private ReturnTypeNode ParseReturnType()
	{
		var start = _current.Start;

		// required type
		var typeTok = ExpectTypeIdentifier("Expected return type");
		var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

		IdentifierDeclarationNode? nameNode = null;
		if (Match(TokenType.Identifier))
		{
			nameNode = new IdentifierDeclarationNode(_previous.Value.ToString(),
													 IdentifierType.Local, null, _filePath, PreviousRange);
		}

		return new ReturnTypeNode(typeNode, nameNode, _filePath,
								  new FileRange(start, _previous.End));
	}

	// Parses a block, for example by reading indented statements or until an EndOfBlock marker.
	private BlockNode? ParseBlock()
	{
		var start = _current.Start;

		// Skip any leading blank lines
		SkipEndOfLineTokens();

		// If the next token isn’t an indent, there’s no nested block
		if (!Match(TokenType.Indent))
			return null;

		SkipEndOfLineTokens();

		List<AstNode>? statements = null;

		// Parse statements until we hit a dedent or EOF
		var end = start;
		while (_current.Type is not (TokenType.Dedent or TokenType.EndOfFile))
		{
			var lineStart = _current.Start;
			var stmt = ParseStatement();
			(statements ??= []).Add(stmt);
			end = stmt.FileRange.End;

			// Only one statement per physical line
			if (_current.Type is not (TokenType.EndOfLine or TokenType.Dedent or TokenType.EndOfFile) &&
				_current.Start.Line == lineStart.Line)
			{
				Error("Only one statement per line is allowed.", _current.Range);
			}

			SkipEndOfLineTokens();
		}

		Match(TokenType.Dedent);     // consume the dedent, if present

		return new BlockNode(
			statements,
			_filePath,
			new FileRange(start, end));
	}

	// Entry point: parses an expression.
	private ExpressionNode ParseExpression() => ParseAssignmentExpression();

	// Parses equality.
	private ExpressionNode ParseAssignmentExpression()
	{
		var start = _current.Start;
		var target = ParseOrExpression();                  // left-hand side

		// Single assignment op (=, +=, …) — right-associative
		if (_current.Type == TokenType.Operator &&
			TryParseAssignmentOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();                                     // consume operator

			var value = ParseAssignmentExpression();       // recurse

			return new AssignmentExpressionNode(
				target, op, opNode, value,
				_filePath, new FileRange(start, _previous.End));
		}

		return target;
	}

	// Parses 'or' (left-associative, lowest logical precedence).
	private ExpressionNode ParseOrExpression()
	{
		var start = _current.Start;
		var expr = ParseAndExpression();

		while (_current.Type == TokenType.Keyword &&
			   _current.Value.SequenceEqual("or".AsSpan()))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();

			expr = new BinaryExpressionNode(
				expr, BinaryOperator.Or, opNode, ParseAndExpression(),
				_filePath, new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses 'and' (left-associative, binds tighter than 'or').
	private ExpressionNode ParseAndExpression()
	{
		var start = _current.Start;
		var expr = ParseEqualityExpression();

		while (_current.Type == TokenType.Keyword &&
			   _current.Value.SequenceEqual("and".AsSpan()))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();

			expr = new BinaryExpressionNode(
				expr, BinaryOperator.And, opNode, ParseEqualityExpression(),
				_filePath, new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses equality.
	private ExpressionNode ParseEqualityExpression()
	{
		var start = _current.Start;
		var expr = ParseRelationalExpression();

		// '==' / '!=' (left-associative chain)
		while (_current.Type == TokenType.Operator &&
			   TryParseEqualityOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();                                      // consume operator

			expr = new BinaryExpressionNode(
				expr, op, opNode, ParseRelationalExpression(),
				_filePath, new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses relational.
	private ExpressionNode ParseRelationalExpression()
	{
		var start = _current.Start;
		var expr = ParseAdditiveExpression();

		// '<', '>', '<=', '>=' … (left-associative)
		while (_current.Type == TokenType.Operator &&
			   TryParseRelationalOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();                                     // consume operator

			expr = new BinaryExpressionNode(
				expr, op, opNode, ParseAdditiveExpression(),
				_filePath, new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses addition and subtraction.
	private ExpressionNode ParseAdditiveExpression()
	{
		var start = _current.Start;
		var expr = ParseMultiplicativeExpression();

		// '+', '-' (left-associative)
		while (_current.Type == TokenType.Operator &&
			   TryParseAdditiveOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();                                   // consume operator

			expr = new BinaryExpressionNode(
				expr, op, opNode, ParseMultiplicativeExpression(),
				_filePath, new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses multiplication and division.
	private ExpressionNode ParseMultiplicativeExpression()
	{
		var start = _current.Start;
		var expr = ParseUnaryExpression();

		// *, /, … (left-associative)
		while (_current.Type == TokenType.Operator &&
			   TryParseMultiplicativeOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();                        // consume operator

			expr = new BinaryExpressionNode(
				expr, op, opNode, ParseUnaryExpression(),
				_filePath, new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses unary expressions
	private ExpressionNode ParseUnaryExpression()
	{
		var start = _current.Start;

		// '!' prefix is removed — recover as Not with a targeted error
		if (_current.Type == TokenType.Operator && _current.Value.SequenceEqual("!".AsSpan()))
		{
			Error("Use 'not' instead of '!'", CurrentRange);
			var bangNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();
			return new UnaryExpressionNode(
				UnaryOperator.Not, bangNode, ParseUnaryExpression(), _filePath,
				new FileRange(start, _previous.End));
		}

		// 'not' keyword — word form of logical negation
		if (CurrentIsKeyword("not"))
		{
			var notNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();
			var notOperand = ParseUnaryExpression();

			return new UnaryExpressionNode(
				UnaryOperator.Not, notNode, notOperand, _filePath,
				new FileRange(start, _previous.End));
		}

		// Prefix operator?
		if (_current.Type == TokenType.Operator &&
			TryParseUnaryOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();                           // consume operator
			var operand = ParseUnaryExpression(); // recurse right-associatively

			return new UnaryExpressionNode(
				op, opNode, operand, _filePath,
				new FileRange(start, _previous.End));
		}

		// No prefix → parse postfix / primary chain
		return ParsePostfixExpression();
	}

	// Parses postfix expressions
	private ExpressionNode ParsePostfixExpression()
	{
		var start = _current.Start;
		var expr = ParsePrimaryExpression();

		// Chain any number of postfix ops (++, --, etc.)
		while (_current.Type == TokenType.Operator &&
			   TryParsePostfixOperator(_current, out var op))
		{
			var opNode = new OperatorNode(_current.Value.ToString(), _filePath, CurrentRange);
			Advance();   // consume the operator

			expr = new PostfixExpressionNode(
				expr, op, opNode, _filePath,
				new FileRange(start, _previous.End));
		}

		return expr;
	}

	// Parses primary expressions: numbers, identifiers, parenthesized expressions, and function calls.
	private ExpressionNode ParsePrimaryExpression()
	{
		var start = _current.Start;

		// ── 1. Literals ────────────────────────────────────────────────
		if (TryParseLiteralType(_current.Type, out var lit))
		{
			var token = _current;
			Advance();

			if (lit == LiteralType.String)
				return ParseStringLiteral(token, start);

			return new LiteralNode(lit, token.Value.ToString(),
								   _filePath, new FileRange(start, _previous.End));
		}

		// ── 2. Identifiers (var ref, func ref, or call) ────────────────
		if (_current.Type == TokenType.Identifier)
		{
			var ident = _current;
			var idType = ParseIdentifierType(ident.Value);
			Advance();

			// bare or dot-prefixed name followed by '(' is a call; the callee kind
			// (func vs command) is resolved during analysis
			if (idType is IdentifierType.Unknown or IdentifierType.Command &&
				_current.Type == TokenType.OpenParen)
			{
				return ParseCallExpression(ident, idType, start);
			}

			var raw = ident.Value.TrimStart(".".AsSpan());
			int dotPrefix = ident.Value.Length - raw.Length;
			var name = raw.TrimStart("^@".AsSpan()).ToString();
			return new IdentifierNode(name, idType, dotPrefix,
									  _filePath, new FileRange(start, _previous.End));
		}

		// ── 3. Parenthesised / tuple literal ───────────────────────────
		if (_current.Type == TokenType.OpenParen)
			return ParseTupleOrGroupingExpression();

		// ── 4. Error recovery ──────────────────────────────────────────
		while (_current.Type is not (TokenType.EndOfLine or TokenType.EndOfFile))
			Advance();

		var badRange = new FileRange(start, _current.Start);
		Error($"Unexpected token in expression: {_current.Value.ToString()}", badRange);
		return new UnparsableExpressionNode(_filePath, in badRange);
	}

	// Parses a string literal, expanding "{expr}" interpolation into the equivalent
	// '+' concatenation chain. "{{" and "}}" produce literal braces.
	//
	// Every synthesized part carries its REAL sub-range within the string token —
	// semantic tokens are emitted per AST node, so a full-token range on the literal
	// parts would paint 'string' over the embedded expressions and clobber the
	// grammar's interpolation highlighting.
	private ExpressionNode ParseStringLiteral(Token token, FilePosition start)
	{
		var raw = token.Value; // includes surrounding quotes
		var range = new FileRange(start, _previous.End);

		// Fast path: no braces, no interpolation machinery
		if (raw.IndexOfAny('{', '}') < 0)
			return new LiteralNode(LiteralType.String, raw.ToString(), _filePath, range);

		// inner text bounds (tolerate an unterminated string missing its closing quote)
		int innerEnd = raw.Length >= 2 && raw[raw.Length - 1] == '"' ? raw.Length - 1 : raw.Length;

		var tokenStart = token.Start;
		var filePath = _filePath; // local copy: local functions in a struct can't touch 'this'
		FilePosition PositionAt(int index) => new(
			tokenStart.Position + index,
			tokenStart.Line,
			tokenStart.Column + index);

		var parts = new List<ExpressionNode>();
		var sb = new StringBuilder();
		int segStart = 0; // source index of the pending literal segment (0 = opening quote)

		void FlushLiteral(int endIndex)
		{
			// the first segment starts at the opening quote; later segments at their
			// first character after the closing '}' of the previous interpolation
			parts.Add(new LiteralNode(
				LiteralType.String, $"\"{sb}\"", filePath,
				new FileRange(PositionAt(segStart), PositionAt(endIndex))));
			sb.Clear();
		}

		for (int i = 1; i < innerEnd; i++)
		{
			char c = raw[i];
			if (c == '{')
			{
				if (i + 1 < innerEnd && raw[i + 1] == '{')
				{
					sb.Append('{');
					i++;
					continue;
				}

				int close = -1;
				for (int j = i + 1; j < innerEnd; j++)
				{
					if (raw[j] == '}') { close = j; break; }
				}
				if (close < 0)
				{
					Error("Unmatched '{' in string interpolation (use '{{' for a literal brace)", range);
					sb.Append(c);
					continue;
				}
				if (close == i + 1)
				{
					Error("Empty interpolation expression '{}'", range);
					i = close;
					continue;
				}

				if (sb.Length > 0 || parts.Count == 0)
				{
					// pending literal text (through the '{'); an empty leading literal
					// also anchors the chain so "{x}" still produces a string result
					FlushLiteral(i);
				}

				// parse the embedded expression at its true file position
				var exprSpan = raw.Slice(i + 1, close - i - 1);
				var fragmentParser = new AstParser(_filePath, exprSpan, PositionAt(i + 1));
				parts.Add(fragmentParser.ParseExpressionFragment());
				foreach (var fragmentError in fragmentParser.Errors)
					_errors.Add(fragmentError);

				i = close;
				segStart = close + 1;
			}
			else if (c == '}')
			{
				if (i + 1 < innerEnd && raw[i + 1] == '}')
				{
					sb.Append('}');
					i++;
					continue;
				}
				Error("Unexpected '}' in string (use '}}' for a literal brace)", range);
				sb.Append(c);
			}
			else
			{
				sb.Append(c);
			}
		}

		if (sb.Length > 0 || parts.Count == 0)
			FlushLiteral(raw.Length); // include the closing quote

		// left-associative '+' chain — identical shape (and bytecode) to manual concat.
		// Operator nodes get zero-width ranges so they emit no visible semantic token,
		// and each chain node spans exactly its own parts: a whole-token range here
		// would swallow position lookups for parts to its right (the untyped search
		// returns the first node that contains the cursor when no child does).
		var result = parts[0];
		for (int i = 1; i < parts.Count; i++)
		{
			var opPosition = parts[i].FileRange.Start;
			result = new BinaryExpressionNode(
				result, BinaryOperator.Add,
				new OperatorNode("+", _filePath, new FileRange(opPosition, opPosition)),
				parts[i], _filePath, FileRange.Combine(result.FileRange, parts[i].FileRange));
		}
		return result;
	}

	// Parses a call expression when an identifier is followed by an argument list.
	private CallExpressionNode ParseCallExpression(Token ident, IdentifierType identifierType, FilePosition funcStart)
	{
		var raw = ident.Value;
		int dotPrefix = 0;
		while (dotPrefix < raw.Length && raw[dotPrefix] == '.')
			dotPrefix++;
		if (dotPrefix > 1)
			Error("Commands only support a single '.' prefix.", ident.Range);
		var name = raw.Slice(dotPrefix).ToString();
		var nameNode = new IdentifierNode(name, identifierType, 0, _filePath, ident.Range);

		// '(' already expected next
		Expect(TokenType.OpenParen, "Expected '(' after method identifier", "(".AsSpan());

		List<ExpressionNode>? args = null;

		// Fast-path: empty argument list
		if (!Match(TokenType.CloseParen))
		{
			// Parse first and subsequent arguments: <expr> (',' <expr>)*
			do
			{
				(args ??= []).Add(ParseExpression());
			}
			while (Match(TokenType.Comma));

			Expect(TokenType.CloseParen, "Expected ')' after method call arguments", ")".AsSpan());
		}

		var fileRange = new FileRange(funcStart, _previous.End);
		return new CallExpressionNode(nameNode, args, dotPrefix, _filePath, in fileRange);
	}

	private ExpressionNode ParseTupleOrGroupingExpression()
	{
		var start = _current.Start;
		Expect(TokenType.OpenParen, "Expected '(' at start of expression");

		var first = ParseTupleElement();

		// No comma → just a parenthesised expression
		if (!Match(TokenType.Comma))
		{
			Expect(TokenType.CloseParen, "Expected ')' after expression", ")".AsSpan());

			// a lone declaration '(bool ok)' is a 1-element destructure target
			if (first is DeclarationExpressionNode)
			{
				var declRange = new FileRange(start, _previous.End);
				return new TupleExpressionNode([first], _filePath, in declRange);
			}

			var groupRange = new FileRange(start, _previous.End);
			return new ParenthesizedExpressionNode(first, _filePath, in groupRange);
		}

		// Comma found → parse tuple elements
		var elements = new List<ExpressionNode> { first };
		do
		{
			elements.Add(ParseTupleElement());
		}
		while (Match(TokenType.Comma));

		Expect(TokenType.CloseParen, "Expected ')' after tuple elements", ")".AsSpan());

		var fileRange = new FileRange(start, _previous.End);
		return new TupleExpressionNode(elements, _filePath, in fileRange);
	}

	// A tuple element is either an inline declaration ('bool ok') for destructuring,
	// or an ordinary expression.
	private ExpressionNode ParseTupleElement()
	{
		var isDeclaration =
			(_current.Type == TokenType.Identifier || CurrentIsKeyword("func")) &&
			PeekIsIdentifier();

		if (!isDeclaration)
			return ParseExpression();

		var start = _current.Start;
		var typeTok = ExpectTypeIdentifier("Expected a type for inline declaration");
		var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

		var nameTok = Expect(TokenType.Identifier, "Expected variable name", "?".AsSpan());
		var nameNode = new IdentifierDeclarationNode(nameTok.Value.ToString(),
													 IdentifierType.Local, null, _filePath, PreviousRange);

		return new DeclarationExpressionNode(typeNode, nameNode, _filePath,
											 new FileRange(start, _previous.End));
	}

	// Advances to the next token.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void Advance()
	{
		_previous = _current;
		_current = _peek.Type != TokenType.None ?
			_peek : NextAstToken();
		_peek = default;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Token NextAstToken()
	{
		while (true)
		{
			if (_current.Type is TokenType.Identifier)
				_summaryBuilder?.Clear();

			var token = _tokenizer.NextToken();
			if (token.Type == TokenType.Error)
			{
				// single-char Error tokens carry the offending character;
				// longer values are complete messages (indentation errors)
				Error(token.Value.Length == 1
					? $"Unexpected character '{token.Value.ToString()}'"
					: token.Value.ToString(), token.Range);
				continue;
			}
			if (token.Type != TokenType.Comment)
			{
				if (token.Type == TokenType.EndOfLine
					&& ++_newLineCommentCount > 1)
					_summaryBuilder?.Clear();
				return token;
			}
			_summaryBuilder ??= new();
			if (_summaryBuilder.Length > 0) _summaryBuilder.Append('\n');
			_summaryBuilder.Append(token.Value.TrimStart('/').TrimStart('*').TrimEnd('/').TrimEnd('*').Trim());
			_lastSummaryPosition = token.End;
			_comments?.Add(new CommentNode(token.Value.ToString(), _filePath, token.Range));
			_newLineCommentCount = 0;
		}
	}

	// --------------------------------- Tokens ----------------------------------

	/// <summary>
	/// Returns true if the current token is a keyword matching the provided text.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool CurrentIsKeyword(string keyword)
	{
		return _current.Type == TokenType.Keyword && _current.Value.SequenceEqual(keyword.AsSpan());
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void Error(string message, in FileRange fileRange)
	{
		_errors?.Add(new FileError(message, in fileRange));
	}

	// Expects that the current token is of a given type and advances.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Token Expect(TokenType type, string errorMessage)
	{
		if (_current.Type != type)
		{
			Error(errorMessage, _current.Range);

			throw new Exception(errorMessage);
		}
		Token token = _current;
		Advance();
		return token;
	}

	// Expects that the current token is of a given type and advances.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Token Expect(TokenType type, string errorMessage, ReadOnlySpan<char> patchToken)
	{
		if (_current.Type != type)
		{
			Error(errorMessage, _current.Range);

			return new Token(type, patchToken, _current.Range.AddLength(1));
		}
		Token token = _current;
		Advance();
		return token;
	}

	// Expects that the current token is of a given type and advances.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Token ExpectStartsWith(TokenType type, string startsWith, string errorMessage, ReadOnlySpan<char> patchToken)
	{
		if (_current.Type != type)
		{
			Error(errorMessage, _current.Range);
			return new Token(type, patchToken, _current.Range.AddLength(1));
		}

		if (_current.Value.Length <= startsWith.Length ||
			!_current.Value.StartsWith(startsWith))
		{
			Error(errorMessage, _current.Range);
			Advance();
			return new Token(type, patchToken, _current.Range.AddLength(1));
		}

		Token token = _current;
		Advance();
		return token;
	}

	// Expects a type identifier. Accepts a plain Identifier token, or the 'func' keyword
	// (a keyword in the lexer but a valid type name in type positions).
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Token ExpectTypeIdentifier(string errorMessage)
	{
		if (_current.Type == TokenType.Keyword && _current.Value.SequenceEqual("func".AsSpan()))
		{
			Token tok = _current;
			Advance();
			return tok;
		}
		if (_current.Type == TokenType.Keyword && _current.Value.SequenceEqual("label".AsSpan()))
		{
			Error("The 'label' type is removed; use 'func'", CurrentRange);
			Token tok = _current;
			Advance();
			return tok;
		}
		return Expect(TokenType.Identifier, errorMessage, "varType".AsSpan());
	}

	// Checks if the current token matches the given type.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool Match(TokenType type)
	{
		if (_current.Type == type)
		{
			Advance();
			return true;
		}
		return false;
	}

	// Checks if the current token matches the given type.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool Match(TokenType type, string value)
	{
		if (_current.Type == type &&
			_current.Value.SequenceEqual(value.AsSpan()))
		{
			Advance();
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool PeekIsIdentifier()
	{
		if (_peek.Type != TokenType.None) throw new InvalidOperationException("Peek must be consumed before peeking again");
		_peek = _tokenizer.NextToken();
		return _peek.Type == TokenType.Identifier;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SkipEndOfLineTokens()
	{
		while (_current.Type == TokenType.EndOfLine)
			Advance();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private string? GetSummary()
	{
		if (_summaryBuilder == null || _summaryBuilder.Length == 0 ||
			_lastSummaryPosition.Line < _current.Start.Line - 1)
			return null;
		var summary = _summaryBuilder.ToString();
		_summaryBuilder.Clear();
		return summary;
	}

	// --------------------------------- Parse Helpers ----------------------------------

	private static bool TryParseEqualityOperator(Token token, out BinaryOperator op)
	{
		op = token.Value.ToString() switch
		{
			"==" => BinaryOperator.EqualTo,
			"!=" => BinaryOperator.NotEqualTo,
			_ => BinaryOperator.Unknown
		};
		return op != BinaryOperator.Unknown;
	}

	private static bool TryParseRelationalOperator(Token token, out BinaryOperator op)
	{
		op = token.Value.ToString() switch
		{
			">" => BinaryOperator.GreaterThan,
			"<" => BinaryOperator.LessThan,
			">=" => BinaryOperator.GreaterThanOrEqual,
			"<=" => BinaryOperator.LessThanOrEqual,
			_ => BinaryOperator.Unknown
		};
		return op != BinaryOperator.Unknown;
	}

	private static bool TryParseAdditiveOperator(Token token, out BinaryOperator op)
	{
		op = token.Value.ToString() switch
		{
			"+" => BinaryOperator.Add,
			"-" => BinaryOperator.Subtract,
			_ => BinaryOperator.Unknown
		};
		return op != BinaryOperator.Unknown;
	}

	private static bool TryParseMultiplicativeOperator(Token token, out BinaryOperator op)
	{
		op = token.Value.ToString() switch
		{
			"*" => BinaryOperator.Multiply,
			"/" => BinaryOperator.Divide,
			"%" => BinaryOperator.Modulo,
			_ => BinaryOperator.Unknown
		};
		return op != BinaryOperator.Unknown;
	}

	private static bool TryParseUnaryOperator(Token token, out UnaryOperator op)
	{
		op = token.Value.ToString() switch
		{
			"-" => UnaryOperator.Negate,
			"++" => UnaryOperator.Increment,
			"--" => UnaryOperator.Decrement,
			_ => UnaryOperator.Unknown
		};
		return op != UnaryOperator.Unknown;
	}

	private static bool TryParseAssignmentOperator(Token token, out AssignmentOperator op)
	{
		op = token.Value.ToString() switch
		{
			"=" => AssignmentOperator.Assign,
			"+=" => AssignmentOperator.Add,
			"-=" => AssignmentOperator.Subtract,
			"*=" => AssignmentOperator.Multiply,
			"/=" => AssignmentOperator.Divide,
			"%=" => AssignmentOperator.Modulo,
			_ => AssignmentOperator.Unknown
		};
		return op != AssignmentOperator.Unknown;
	}

	private static bool TryParsePostfixOperator(Token token, out UnaryOperator op)
	{
		op = token.Value.ToString() switch
		{
			"++" => UnaryOperator.Increment,
			"--" => UnaryOperator.Decrement,
			_ => UnaryOperator.Unknown
		};
		return op != UnaryOperator.Unknown;
	}

	private static bool TryParseLiteralType(TokenType token, out LiteralType type)
	{
		type = token switch
		{
			TokenType.Number => LiteralType.Number,
			TokenType.String => LiteralType.String,
			TokenType.Boolean => LiteralType.Boolean,
			_ => LiteralType.Unknown
		};
		return type != LiteralType.Unknown;
	}

	private static IdentifierType ParseIdentifierType(ReadOnlySpan<char> name)
	{
		if (name.IsEmpty) return IdentifierType.Unknown;
		// dot-prefixed context variable (e.g. .@foo)
		if (name[0] == '.')
		{
			int i = 0;
			while (i < name.Length && name[i] == '.') i++;
			if (i < name.Length && name[i] == '@') return IdentifierType.Context;
			// dot-prefixed bare name is necessarily a command
			return IdentifierType.Command;
		}
		return name[0] switch
		{
			'^' => IdentifierType.Constant,
			'@' => IdentifierType.Context,
			// bare names resolve to Local/Func/Command during analysis
			_ => IdentifierType.Unknown
		};
	}
}
