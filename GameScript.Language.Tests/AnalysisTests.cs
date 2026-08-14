using System.Linq;
using GameScript.Language.Tests.Harness;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Diagnostics produced by the analysis visitors (symbol, semantic, type).
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
			    print($undeclared)
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
	public void Label_Jump_Inside_Func_Is_Reported()
	{
		var errors = ErrorsFor("""
			label target()
			    return

			func main()
			    @target()
			""");
		Assert.NotEmpty(errors);
	}

	[Fact]
	public void Missing_Return_Path_With_Empty_Body_Is_Reported()
	{
		var errors = ErrorsFor("""
			func maybe(int $x) returns int
			    print("no return")
			""");
		Assert.NotEmpty(errors);
	}

	[Fact]
	public void Missing_Return_Path_Behind_If_Without_Else()
	{
		// KNOWN GAP: MustReturn treats `if` without `else` as guaranteed to return
		// when its then-block returns (SemanticAnalysisVisitor.MustReturn). This
		// documents current behavior; the 2.0 grammar-tightening pass fixes it to
		// report an error, at which point this assertion flips to NotEmpty.
		var errors = ErrorsFor("""
			func maybe(int $x) returns int
			    if $x > 0
			        return 1
			""");
		Assert.Empty(errors);
	}

	// ---------------------------------------------------------------
	// Overloading (2.0): same name, different parameter signatures
	// ---------------------------------------------------------------

	[Fact]
	public void Func_Overloads_By_Arity_Are_Allowed()
	{
		var errors = ErrorsFor("""
			func greet()
			    print("hi")

			func greet(string $name)
			    print("hi " + $name)

			func main()
			    ~greet()
			    ~greet("bob")
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Func_Overloads_By_Type_Are_Allowed()
	{
		var errors = ErrorsFor("""
			func show(int $value)
			    print(int_to_str($value))

			func show(string $value)
			    print($value)

			func main()
			    ~show(5)
			    ~show("five")
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
			command overloaded(int $a)
			command overloaded(int $a, int $b)
			"""));
		Assert.Empty(errors);
	}

	[Fact]
	public void Duplicate_Signature_Differing_Only_By_Return_Is_Reported()
	{
		var errors = ErrorsFor("""
			func same(int $a) returns int
			    return $a

			func same(int $a) returns string
			    return "x"
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Func_Duplicating_Command_Signature_Is_Reported()
	{
		var errors = ErrorsFor("""
			func print(string $text)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Call_Matching_No_Overload_Is_Reported()
	{
		var errors = ErrorsFor("""
			func show(int $value)
			    print(int_to_str($value))

			func show(string $value)
			    print($value)

			func main()
			    ~show(true)
			""");
		Assert.Contains(errors, e => e.Contains("No overload"));
	}

	[Fact]
	public void Use_Before_Declaration_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    print(int_to_str($late))
			    int $late = 1
			""");
		Assert.NotEmpty(errors);
	}
}
