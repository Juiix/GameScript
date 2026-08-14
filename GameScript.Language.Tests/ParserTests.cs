using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.File;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// AST-shape and parse-diagnostic tests driven directly through AstParser.
/// </summary>
public class ParserTests
{
	private static (ProgramNode Root, FileError[] Errors) ParseProgram(string source)
	{
		var parser = new AstParser("test.gs", source);
		var root = parser.ParseProgram();
		return ((ProgramNode)root, parser.Errors?.ToArray() ?? []);
	}

	[Fact]
	public void Parses_Func_With_Params_And_Returns()
	{
		var (root, errors) = ParseProgram("""
			func add(int a, int b) returns int
			    return a + b
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(2, method.Parameters!.Count);
		Assert.Single(method.ReturnTypes!);
	}

	[Fact]
	public void Label_Declaration_Is_An_Error()
	{
		var (root, errors) = ParseProgram("""
			label close_gate(int coord)
			    return
			""");
		Assert.Contains(errors, e => e.Message.Contains("'label' declarations are removed"));
		// still parses as a func for downstream tooling
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.Func, method.Name.Type);
	}

	[Fact]
	public void Parses_Command_Declaration_Without_Body()
	{
		var (root, errors) = ParseProgram("command clamp(int value, int minValue, int maxValue) returns int");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.Command, method.Name.Type);
		Assert.Equal(3, method.Parameters!.Count);
	}

	[Fact]
	public void Parses_Func_Type_In_Command_Params()
	{
		var (root, errors) = ParseProgram("command queue_strong(func method, int delay)");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal("func", method.Parameters![0].Type.Name);
	}

	[Fact]
	public void Label_Type_In_Params_Is_An_Error()
	{
		var (_, errors) = ParseProgram("command queue_strong(label method, int delay)");
		Assert.Contains(errors, e => e.Message.Contains("'label' type is removed"));
	}

	[Fact]
	public void Parses_Trigger_With_Component()
	{
		var (root, errors) = ParseProgram("""
			mn_button login:google
			    return
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.Trigger, method.Name.Type);
		Assert.StartsWith("mn_button", method.SymbolName);
	}

	[Fact]
	public void Trigger_With_Empty_Parens_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			obj_op_1 furnace()
			    return
			""");
		Assert.Contains(errors, e => e.Message.Contains("omit the '()'"));
	}

	[Fact]
	public void Trigger_With_Params_Keeps_Parens()
	{
		var (root, errors) = ParseProgram("""
			mn_text username:input(string text)
			    return
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Single(method.Parameters!);
	}

	[Fact]
	public void Parses_Tuple_Returns_Declaration()
	{
		var (root, errors) = ParseProgram("""
			func try_login() returns (bool success, string error)
			    return (true, "")
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(2, method.ReturnTypes!.Count);
	}

	[Fact]
	public void Parses_Multi_Variable_Declaration()
	{
		var (root, errors) = ParseProgram("""
			func main()
			    int x, y, plane
			    return
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Parses_If_ElseIf_Else_Chain()
	{
		var (root, errors) = ParseProgram("""
			func main(int x)
			    if x == 1
			        return
			    else if x == 2
			        return
			    else
			        return
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		var ifNode = Assert.IsType<IfStatementNode>(method.Body!.Statements![0]);
		Assert.NotNull(ifNode.ElseIfNodes);
		Assert.NotNull(ifNode.ElseBlock);
	}

	[Fact]
	public void Full_Wrap_Condition_Parens_Are_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(int x)
			    if (x == 1)
			        return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Remove the parentheses"));
	}

	[Fact]
	public void Inner_Grouping_In_Conditions_Is_Allowed()
	{
		var (_, errors) = ParseProgram("""
			func main(bool a, bool b, bool c)
			    if (a or b) and c
			        return
			    while not (a and b)
			        return
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Bang_Prefix_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(bool flag)
			    if !flag
			        return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Use 'not' instead of '!'"));
	}

	[Fact]
	public void Parses_Constants_File()
	{
		var parser = new AstParser("skills.const", "int ^skill_attack = 0\nint ^skill_mining = 1");
		var root = (ConstantsNode)parser.ParseConstants();
		Assert.Empty(parser.Errors ?? Enumerable.Empty<FileError>().ToList());
		Assert.Equal(2, root.Definitions!.Count);
	}

	[Fact]
	public void Parses_Contexts_File_With_At_Mark()
	{
		var parser = new AstParser("player.context", "int @platform = 4");
		var root = (ContextsNode)parser.ParseContexts();
		Assert.Empty(parser.Errors ?? Enumerable.Empty<FileError>().ToList());
		var def = Assert.Single(root.Definitions!);
		Assert.Equal("platform", def.Name.Name);
	}

	[Fact]
	public void Context_File_With_Old_Percent_Mark_Is_An_Error()
	{
		var parser = new AstParser("player.context", "int %platform = 4");
		parser.ParseContexts();
		Assert.NotEmpty(parser.Errors!);
	}

	[Fact]
	public void Parses_Command_Op_Binding()
	{
		var (root, errors) = ParseProgram("command enqueue(func method, int delay) = queue_strong");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal("queue_strong", method.InternalName);
	}

	[Fact]
	public void Op_Binding_On_Func_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func broken() = queue_strong
			    return
			""");
		Assert.Contains(errors, e => e.Message.Contains("command declarations"));
	}

	[Fact]
	public void Semicolon_Is_An_Unexpected_Character()
	{
		var (_, errors) = ParseProgram("""
			func main()
			    return;
			""");
		Assert.Contains(errors, e => e.Message.Contains("Unexpected character ';'"));
	}

	[Fact]
	public void Unmatched_Interpolation_Brace_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main()
			    return "oops {unclosed"
			""");
		Assert.Contains(errors, e => e.Message.Contains("Unmatched '{'"));
	}

	[Fact]
	public void Empty_Interpolation_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main()
			    return "oops {}"
			""");
		Assert.Contains(errors, e => e.Message.Contains("Empty interpolation"));
	}

	[Fact]
	public void Interpolation_Parts_Carry_Sub_Token_Ranges()
	{
		// semantic tokens are emitted per AST node: string parts must cover only
		// their own text so embedded expressions keep their own highlighting
		var (root, errors) = ParseProgram("""
			func main(int x)
			    return "lvl {x} up"
			""");
		Assert.Empty(errors);

		//                   0123456789012345678
		// line 1 source:    return "lvl {x} up"  (indent stripped: col 4 = 'r')
		var body = root.Methods![0].Body!.Statements!;
		var ret = Assert.IsType<ReturnStatementNode>(body[0]);

		// chain: ("lvl " + x) + " up"
		var outer = Assert.IsType<BinaryExpressionNode>(ret.Expression);
		var inner = Assert.IsType<BinaryExpressionNode>(outer.Left);
		var lead = Assert.IsType<LiteralNode>(inner.Left);
		var expr = Assert.IsType<IdentifierNode>(inner.Right);
		var tail = Assert.IsType<LiteralNode>(outer.Right);

		// leading literal covers only '"lvl ' + the '{' boundary, not the whole string
		Assert.Equal(11, lead.FileRange.Start.Column);            // opening quote
		Assert.Equal(16, lead.FileRange.End.Column);              // stops at '{'

		// embedded identifier sits exactly on its source character
		Assert.Equal("x", expr.Name);
		Assert.Equal(17, expr.FileRange.Start.Column);

		// trailing literal starts after '}' and includes the closing quote
		Assert.Equal(19, tail.FileRange.Start.Column);
		Assert.Equal(23, tail.FileRange.End.Column);

		// zero-width operators: no visible semantic token
		Assert.Equal(inner.OperatorNode.FileRange.Start, inner.OperatorNode.FileRange.End);
	}

	[Fact]
	public void Missing_Paren_Produces_Error()
	{
		var (_, errors) = ParseProgram("""
			func broken(int a
			    return
			""");
		Assert.NotEmpty(errors);
	}
}
