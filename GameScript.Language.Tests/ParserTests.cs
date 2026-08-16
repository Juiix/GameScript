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

		// the binding is a real AST node (semantic tokens / hover need its range),
		// typed EngineOp so it is never mistaken for a script symbol
		//              0         1         2         3         4
		//              0123456789012345678901234567890123456789012345678
		// source:      command enqueue(func method, int delay) = queue_strong
		Assert.NotNull(method.BindingOperator);
		Assert.Equal(40, method.BindingOperator!.FileRange.Start.Column);
		var bindingName = Assert.IsType<IdentifierDeclarationNode>(method.BindingName);
		Assert.Equal(IdentifierType.EngineOp, bindingName.Type);
		Assert.Equal(42, bindingName.FileRange.Start.Column);
		Assert.Equal(54, bindingName.FileRange.End.Column);
		Assert.Contains(bindingName, method.Children);

		// the method's own range extends over the binding
		Assert.Equal(54, method.FileRange.End.Column);
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

	// ---------------------------------------------------------------
	// 2.1: trigger declarations
	// ---------------------------------------------------------------

	[Fact]
	public void Parses_Trigger_Declaration()
	{
		var (root, errors) = ParseProgram("trigger obj_op_1");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.TriggerDeclaration, method.Name.Type);
		Assert.Equal("obj_op_1", method.SymbolName);
		Assert.Null(method.Parameters);
	}

	[Fact]
	public void Parses_Trigger_Declaration_With_Params()
	{
		var (root, errors) = ParseProgram("trigger mn_text(string text)");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.TriggerDeclaration, method.Name.Type);
		Assert.Single(method.Parameters!);
	}

	[Fact]
	public void Trigger_Declaration_With_Component_Is_An_Error()
	{
		var (_, errors) = ParseProgram("trigger mn_button:google");
		Assert.Contains(errors, e => e.Message.Contains("subjects belong on handlers"));
	}

	// ---------------------------------------------------------------
	// 2.1: default parameter values
	// ---------------------------------------------------------------

	[Fact]
	public void Parses_Parameter_Defaults()
	{
		var (root, errors) = ParseProgram("""
			func choice(string text, string c3 = "", int anim = ^anim_still, int off = -1) returns int
			    return 0
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Null(method.Parameters![0].Default);
		Assert.IsType<LiteralNode>(method.Parameters[1].Default);
		Assert.IsType<IdentifierNode>(method.Parameters[2].Default);
		Assert.IsType<UnaryExpressionNode>(method.Parameters[3].Default);
	}

	[Fact]
	public void Command_Default_Params_Coexist_With_Op_Binding()
	{
		var (root, errors) = ParseProgram("command enqueue(func method, int delay = 5) = queue_strong");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal("queue_strong", method.InternalName);
		Assert.NotNull(method.Parameters![1].Default);
	}

	// ---------------------------------------------------------------
	// 2.1: for loops
	// ---------------------------------------------------------------

	[Fact]
	public void Parses_For_Statement()
	{
		var (root, errors) = ParseProgram("""
			func main(int size)
			    for i in 0..size
			        return
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		var forNode = Assert.IsType<ForStatementNode>(method.Body!.Statements![0]);
		Assert.Equal("i", forNode.Variable.Name);
		Assert.Equal(IdentifierType.Local, forNode.Variable.Type);
		Assert.NotNull(forNode.Body);
	}

	[Fact]
	public void For_Without_In_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main()
			    for i 0..5
			        return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Expected 'in'"));
	}

	[Fact]
	public void For_Without_Range_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main()
			    for i in 5
			        return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Expected '..'"));
	}

	// ---------------------------------------------------------------
	// 2.1: switch statements
	// ---------------------------------------------------------------

	[Fact]
	public void Parses_Switch_With_Inline_Block_And_Default_Cases()
	{
		var (root, errors) = ParseProgram("""
			func skill_name(int skill) returns string
			    switch skill
			        case 0: return "Attack"
			        case 1, 2:
			            print("gathering")
			            return "Gathering"
			        default: return "Unknown"
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		var switchNode = Assert.IsType<SwitchStatementNode>(method.Body!.Statements![0]);
		Assert.Equal(3, switchNode.Cases!.Count);

		var inline = switchNode.Cases[0];
		Assert.True(inline.IsInline);
		Assert.Single(inline.Values!);
		Assert.Single(inline.Body!.Statements!);

		var block = switchNode.Cases[1];
		Assert.False(block.IsInline);
		Assert.Equal(2, block.Values!.Count);
		Assert.Equal(2, block.Body!.Statements!.Count);

		Assert.True(switchNode.Cases[2].IsDefault);
		Assert.NotNull(switchNode.DefaultCase);
	}

	[Fact]
	public void Case_With_Inline_Statement_And_Block_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(int x)
			    switch x
			        case 1: return
			            return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Cannot combine an inline statement with an indented block"));
	}

	[Fact]
	public void Duplicate_Default_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(int x)
			    switch x
			        default: return
			        default: return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Only one 'default' case"));
	}

	[Fact]
	public void Default_Not_Last_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(int x)
			    switch x
			        default: return
			        case 1: return
			""");
		Assert.Contains(errors, e => e.Message.Contains("'default' must be the last case"));
	}

	[Fact]
	public void Statement_Inside_Switch_Body_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(int x)
			    switch x
			        print("nope")
			""");
		Assert.Contains(errors, e => e.Message.Contains("Expected 'case' or 'default'"));
	}

	[Fact]
	public void Switch_Without_Body_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(int x)
			    switch x
			    return
			""");
		Assert.Contains(errors, e => e.Message.Contains("at least one 'case' or 'default'"));
	}

	// ---------------------------------------------------------------
	// 2.1: inline if/else bodies
	// ---------------------------------------------------------------

	[Fact]
	public void Parses_Inline_If_And_Else_Bodies()
	{
		var (root, errors) = ParseProgram("""
			func pick(bool flag) returns int
			    if flag: return 1
			    else: return 2
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		var ifNode = Assert.IsType<IfStatementNode>(method.Body!.Statements![0]);
		Assert.Single(ifNode.IfBlock!.Statements!);
		Assert.Single(ifNode.ElseBlock!.Statements!);
	}

	[Fact]
	public void Inline_If_With_Indented_Block_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(bool flag)
			    if flag: return
			        return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Cannot combine an inline statement with an indented block"));
	}

	[Fact]
	public void Parses_Multi_Line_Method_Signature()
	{
		var (root, errors) = ParseProgram("""
			func input_choice_npc(string text, string c1, string c2, string c3 = "",
			        int anim = -1) returns int
			    return 0
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(5, method.Parameters!.Count);
		Assert.Single(method.ReturnTypes!);
		Assert.NotNull(method.Body);
	}

	[Fact]
	public void Parses_Multi_Line_Call_And_Condition()
	{
		var (root, errors) = ParseProgram("""
			func add(int a, int b, int c) returns int
			    return a + b + c

			func main(bool x,
			        bool y)
			    if (x or
			            y) and true
			        add(1,
			            2,
			            3)
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void If_Colon_Without_Statement_Is_An_Error()
	{
		var (_, errors) = ParseProgram("""
			func main(bool flag)
			    if flag:
			        return
			""");
		Assert.Contains(errors, e => e.Message.Contains("Expected a statement after ':'"));
	}
}
