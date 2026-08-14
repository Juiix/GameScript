using System.Linq;
using GameScript.Language.Tests.Harness;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Diagnostics produced by the analysis visitors (name resolution, symbol, semantic, type).
/// </summary>
public class AnalysisTests
{
	private static string[] ErrorsFor(string script, params (string Path, string Source)[] extraFiles)
	{
		var compilation = new TestCompilation()
			.AddFile("core.gs", Fixtures.CoreGs)
			.AddFile("test.gs", script);
		foreach (var (path, source) in extraFiles)
			compilation.AddFile(path, source);
		compilation.Analyze();
		return compilation.AllErrors.Select(e => e.Message).ToArray();
	}

	[Fact]
	public void Valid_Script_Has_No_Errors()
	{
		var errors = ErrorsFor("""
			func main()
			    print("hello")
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Unknown_Symbol_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    print(int_to_str(undeclared))
			""");
		Assert.Contains(errors, e => e.Contains("undeclared"));
	}

	[Fact]
	public void Unknown_Command_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    nonexistent_command(1)
			""");
		Assert.NotEmpty(errors);
	}

	[Fact]
	public void Argument_Type_Mismatch_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    print(5)
			""");
		Assert.NotEmpty(errors);
	}

	[Fact]
	public void Duplicate_Symbol_Is_Reported()
	{
		var errors = ErrorsFor("""
			func twice()
			    return

			func twice()
			    return
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Condition_Must_Be_Bool()
	{
		var errors = ErrorsFor("""
			func main()
			    if 5
			        print("no")
			""");
		Assert.NotEmpty(errors);
	}

	[Fact]
	public void Missing_Return_Path_With_Empty_Body_Is_Reported()
	{
		var errors = ErrorsFor("""
			func maybe(int x) returns int
			    print("no return")
			""");
		Assert.NotEmpty(errors);
	}

	[Fact]
	public void Missing_Return_Path_Behind_If_Without_Else_Is_Reported()
	{
		// fixed in 2.0: an if without an else cannot guarantee a return
		var errors = ErrorsFor("""
			func maybe(int x) returns int
			    if x > 0
			        return 1
			""");
		Assert.Contains(errors, e => e.Contains("not all paths return"));
	}

	// ---------------------------------------------------------------
	// Overloading: same name, different parameter signatures
	// ---------------------------------------------------------------

	[Fact]
	public void Func_Overloads_By_Arity_Are_Allowed()
	{
		var errors = ErrorsFor("""
			func greet()
			    print("hi")

			func greet(string who)
			    print("hi " + who)

			func main()
			    greet()
			    greet("bob")
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Func_Overloads_By_Type_Are_Allowed()
	{
		var errors = ErrorsFor("""
			func show(int value)
			    print(int_to_str(value))

			func show(string value)
			    print(value)

			func main()
			    show(5)
			    show("five")
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Command_Overloads_Are_Allowed()
	{
		var errors = ErrorsFor("""
			func main()
			    return
			""",
			("extra.gs", """
			command overloaded(int a)
			command overloaded(int a, int b)
			"""));
		Assert.Empty(errors);
	}

	[Fact]
	public void Duplicate_Signature_Differing_Only_By_Return_Is_Reported()
	{
		var errors = ErrorsFor("""
			func same(int a) returns int
			    return a

			func same(int a) returns string
			    return "x"
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Func_Duplicating_Command_Signature_Is_Reported()
	{
		var errors = ErrorsFor("""
			func print(string text)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Call_Matching_No_Overload_Is_Reported()
	{
		var errors = ErrorsFor("""
			func show(int value)
			    print(int_to_str(value))

			func show(string value)
			    print(value)

			func main()
			    show(true)
			""");
		Assert.Contains(errors, e => e.Contains("No overload"));
	}

	// ---------------------------------------------------------------
	// 2.0 collision and mark rules
	// ---------------------------------------------------------------

	[Fact]
	public void Local_Colliding_With_Command_Is_Reported()
	{
		var errors = ErrorsFor("""
			func show_alert(string print)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("conflicts with"));
	}

	[Fact]
	public void Local_Colliding_With_Func_Is_Reported()
	{
		var errors = ErrorsFor("""
			func helper()
			    return

			func main()
			    int helper = 1
			""");
		Assert.Contains(errors, e => e.Contains("conflicts with"));
	}

	[Fact]
	public void Context_Var_Without_At_Mark_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    print(int_to_str(hp))
			""",
			("player.context", "int @hp = 3"));
		Assert.Contains(errors, e => e.Contains("'@' mark"));
	}

	[Fact]
	public void Constant_With_At_Mark_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    print(int_to_str(@max_level))
			""",
			("skills.const", "int ^max_level = 99"));
		Assert.Contains(errors, e => e.Contains("'^' mark"));
	}

	[Fact]
	public void Use_Before_Declaration_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    print(int_to_str(late))
			    int late = 1
			""");
		Assert.NotEmpty(errors);
	}
}
