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
			func add(int $a, int $b) returns int
			    return $a + $b
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(2, method.Parameters!.Count);
		Assert.Single(method.ReturnTypes!);
	}

	[Fact]
	public void Parses_Label_Declaration()
	{
		var (root, errors) = ParseProgram("""
			label close_gate(int $coord)
			    return
			""");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.Label, method.Name.Type);
	}

	[Fact]
	public void Parses_Command_Declaration_Without_Body()
	{
		var (root, errors) = ParseProgram("command clamp(int $value, int $min, int $max) returns int");
		Assert.Empty(errors);
		var method = Assert.Single(root.Methods!);
		Assert.Equal(IdentifierType.Command, method.Name.Type);
		Assert.Equal(3, method.Parameters!.Count);
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
	public void Parses_Tuple_Returns_Declaration()
	{
		var (root, errors) = ParseProgram("""
			func try_login() returns (bool $success, string $error)
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
			    int $x, $y, $plane
			    return
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Parses_If_ElseIf_Else_Chain()
	{
		var (root, errors) = ParseProgram("""
			func main(int $x)
			    if $x == 1
			        return
			    else if $x == 2
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
	public void Parses_Constants_File()
	{
		var parser = new AstParser("skills.const", "int ^skill_attack = 0\nint ^skill_mining = 1");
		var root = (ConstantsNode)parser.ParseConstants();
		Assert.Empty(parser.Errors ?? Enumerable.Empty<FileError>().ToList());
		Assert.Equal(2, root.Definitions!.Count);
	}

	[Fact]
	public void Parses_Contexts_File()
	{
		var parser = new AstParser("player.context", "int %platform = 4");
		var root = (ContextsNode)parser.ParseContexts();
		Assert.Empty(parser.Errors ?? Enumerable.Empty<FileError>().ToList());
		Assert.Single(root.Definitions!);
	}

	[Fact]
	public void Bare_Label_Reference_Is_Not_A_Call()
	{
		var (root, errors) = ParseProgram("""
			label deferred()
			    return

			func main()
			    queue_strong(@deferred, 1)
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Parses_Command_Op_Binding()
	{
		var (root, errors) = ParseProgram("command enqueue(label $method, int $delay) = queue_strong");
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
	public void Missing_Paren_Produces_Error()
	{
		var (_, errors) = ParseProgram("""
			func broken(int $a
			    return
			""");
		Assert.NotEmpty(errors);
	}
}
