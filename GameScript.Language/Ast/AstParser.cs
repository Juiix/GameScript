using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using GameScript.Language.File;
using GameScript.Language.Lexer;

namespace GameScript.Language.Ast
{
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
			var method = _current switch
			{
				{ Type: TokenType.Keyword } when CurrentIsKeyword("func") => ParseMethodDefinition(IdentifierType.Func),
				{ Type: TokenType.Keyword } when CurrentIsKeyword("label") => ParseMethodDefinition(IdentifierType.Label),
				{ Type: TokenType.Keyword } when CurrentIsKeyword("command") => ParseMethodDefinition(IdentifierType.Command),
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
					"return" => ParseReturnStatement(),
					"break" => ParseBreakStatement(),
					"continue" => ParseContinueStatement(),
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
			}

			/* ───── parameters ───── */
			List<ParameterNode>? parameters = null;
			if (Match(TokenType.OpenParen))
			{
				parameters = ParseParameterList();
				Expect(TokenType.CloseParen, "Expected ')' after parameters", ")".AsSpan());
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

			/* ───── body ───── */
			var body = ParseBlock();     // may be null if no nested indent

			return new MethodDefinitionNode(
				kwNode, returnsKw, returns, nameNode, parameters, body, _filePath,
				new FileRange(start, _previous.End));
		}

		private ConstantDefinitionNode ParseConstantDefinition()
		{
			var summary = GetSummary();
			var start = _current.Start;

			// constant type
			var typeTok = Expect(TokenType.Identifier, "Expected a type for constant declaration", "varType".AsSpan());
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
			var typeTok = Expect(TokenType.Identifier, "Expected a type for context variable declaration", "varType".AsSpan());
			var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

			// constant name (must start with '%')
			var nameTok = ExpectStartsWith(TokenType.Identifier, "%", "Expected context variable name (must start with '%')", "%?".AsSpan());
			var nameNode = new IdentifierDeclarationNode(nameTok.Value.TrimStart('%').ToString(),
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
			var condition = ParseExpression();
			var ifBlock = ParseBlock();

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

					var elseIfCond = ParseExpression();
					var elseIfBlock = ParseBlock();

					(elseIfs ??= []).Add(
						new ElseIfStatementNode(elseKeyword, elseIfKey, elseIfCond, elseIfBlock,
												_filePath, new FileRange(elseStart, _previous.End)));
				}
				else                                                       // plain else
				{
					elseBlk = ParseBlock();
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
			var condition = ParseExpression();
			var body = ParseBlock();   // may be null if no nested block

			return new WhileStatementNode(
				kwNode, condition, body, _filePath,
				new FileRange(start, _previous.End));
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
			var typeTok = Expect(TokenType.Identifier, "Expected a type for variable declaration", "varType".AsSpan());
			var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

			var vars = new List<(IdentifierDeclarationNode, ExpressionNode?)>();

			// <name> [ '=' <expr> ] (',' <name> [ '=' <expr> ])*
			do
			{
				// name must start with '$'
				var nameTok = ExpectStartsWith(TokenType.Identifier, "$", "Expected variable name (must start with '$')", "$?".AsSpan());
				var nameNode = new IdentifierDeclarationNode(nameTok.Value.TrimStart('$').ToString(),
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

				var typeTok = Expect(TokenType.Identifier, "Expected parameter type", "paramType".AsSpan());
				var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

				var nameTok = ExpectStartsWith(TokenType.Identifier, "$", "Expected parameter name", "$?".AsSpan());
				var nameNode = new IdentifierDeclarationNode(nameTok.Value.TrimStart('$').ToString(),
															 IdentifierType.Local, summary, _filePath, PreviousRange);

				parameters.Add(new ParameterNode(typeNode, nameNode, _filePath,
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
			var typeTok = Expect(TokenType.Identifier, "Expected return type", "returnType".AsSpan());
			var typeNode = new TypeNode(typeTok.Value.ToString(), _filePath, PreviousRange);

			IdentifierDeclarationNode? nameNode = null;
			if (Match(TokenType.Identifier))
			{
				nameNode = new IdentifierDeclarationNode(_previous.Value.TrimStart('$').ToString(),
														 IdentifierType.Local, null, _filePath, PreviousRange);

				if (_previous.Value[0] != '$')
					Error($"Return name must start with '$'", PreviousRange);
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
			while (_current.Type is not (TokenType.Dedent or TokenType.EndOfFile))
			{
				var lineStart = _current.Start;
				(statements ??= []).Add(ParseStatement());

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
				new FileRange(start, _previous.End));
		}

		// Entry point: parses an expression.
		private ExpressionNode ParseExpression() => ParseAssignmentExpression();

		// Parses equality.
		private ExpressionNode ParseAssignmentExpression()
		{
			var start = _current.Start;
			var target = ParseEqualityExpression();           // left-hand side

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
				return new LiteralNode(lit, token.Value.ToString(),
									   _filePath, new FileRange(start, _previous.End));
			}

			// ── 2. Identifiers (var ref or call) ───────────────────────────
			if (_current.Type == TokenType.Identifier)
			{
				var ident = _current;
				var idType = ParseIdentifierType(ident.Value);
				Advance();

				// Func / command / label followed by '(' → call expression
				if (idType is IdentifierType.Func or IdentifierType.Command or IdentifierType.Label)
					return ParseCallExpression(ident, idType, start);

				var name = ident.Value.TrimStart("^$%".AsSpan()).ToString();
				return new IdentifierNode(name, idType,
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

		// Parses a call expression when an identifier is followed by an argument list.
		private CallExpressionNode ParseCallExpression(Token ident, IdentifierType identifierType, FilePosition funcStart)
		{
			var name = ident.Value.TrimStart("~@".AsSpan()).ToString();
			var nameNode = new IdentifierNode(name, identifierType, _filePath, ident.Range);

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
			return new CallExpressionNode(nameNode, args, _filePath, in fileRange);
		}

		private ExpressionNode ParseTupleOrGroupingExpression()
		{
			var start = _current.Start;
			Expect(TokenType.OpenParen, "Expected '(' at start of expression");

			var first = ParseExpression();

			// No comma → just a parenthesised expression
			if (!Match(TokenType.Comma))
			{
				Expect(TokenType.CloseParen, "Expected ')' after expression", ")".AsSpan());
				return first;
			}

			// Comma found → parse tuple elements
			var elements = new List<ExpressionNode> { first };
			do
			{
				elements.Add(ParseExpression());
			}
			while (Match(TokenType.Comma));

			Expect(TokenType.CloseParen, "Expected ')' after tuple elements", ")".AsSpan());

			var fileRange = new FileRange(start, _previous.End);
			return new TupleExpressionNode(elements, _filePath, in fileRange);
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
				if (token.Type != TokenType.Comment)
				{
					if (token.Type == TokenType.EndOfLine
						&& ++_newLineCommentCount > 1)
						_summaryBuilder?.Clear();
					return token;
				}
				_summaryBuilder ??= new();
				if (_summaryBuilder.Length > 0) _summaryBuilder.Append('\n');
				_summaryBuilder.Append(token.Value.TrimStart("/*").TrimEnd("*/").Trim());
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
				_ => BinaryOperator.Unknown
			};
			return op != BinaryOperator.Unknown;
		}

		private static bool TryParseUnaryOperator(Token token, out UnaryOperator op)
		{
			op = token.Value.ToString() switch
			{
				"!" => UnaryOperator.Not,
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
			return name[0] switch
			{
				'$' => IdentifierType.Local,
				'^' => IdentifierType.Constant,
				'%' => IdentifierType.Context,
				'~' => IdentifierType.Func,
				'@' => IdentifierType.Label,
				_ => char.IsLetter(name[0]) ? IdentifierType.Command : IdentifierType.Unknown
			};
		}
	}
}
