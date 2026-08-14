using System.Linq;
using GameScript.Bytecode;
using GameScript.Language.Tests.Harness;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// End-to-end tests: parse -> analyze -> compile -> execute against the TestOp host.
/// </summary>
public class ExecutionTests
{
	private static (TestHost Host, BytecodeProgram Program) Build(string script, params (string Path, string Source)[] extraFiles)
	{
		var compilation = new TestCompilation()
			.AddFile("core.gs", Fixtures.CoreGs)
			.AddFile("test.gs", script);
		foreach (var (path, source) in extraFiles)
			compilation.AddFile(path, source);
		compilation.Analyze();
		Assert.True(!compilation.AllErrors.Any(),
			"Unexpected errors:\n" + string.Join("\n", compilation.AllErrors.Select(TestCompilation.FormatError)));
		var result = compilation.Compile();
		return (new TestHost(), result.Program);
	}

	[Fact]
	public void Arithmetic_And_Print()
	{
		var (host, program) = Build("""
			func main()
			    print(int_to_str(1 + 2 * 3 - 4 / 2))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "5" }, host.Context.Printed);
	}

	[Fact]
	public void String_Concatenation_Coerces()
	{
		var (host, program) = Build("""
			func main()
			    int $level = 42
			    print("lvl " + $level + "!")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "lvl 42!" }, host.Context.Printed);
	}

	[Fact]
	public void Func_Call_With_Return()
	{
		var (host, program) = Build("""
			func add(int $a, int $b) returns int
			    return $a + $b

			func main()
			    print(int_to_str(~add(2, 3)))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "5" }, host.Context.Printed);
	}

	[Fact]
	public void Recursion()
	{
		var (host, program) = Build("""
			func factorial(int $n) returns int
			    if $n <= 1
			        return 1
			    return $n * ~factorial($n - 1)

			func main()
			    print(int_to_str(~factorial(5)))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "120" }, host.Context.Printed);
	}

	[Fact]
	public void ShortCircuit_And_Skips_Rhs()
	{
		var (host, program) = Build("""
			func side() returns bool
			    print("side")
			    return true

			func main()
			    if false and ~side()
			        print("then")
			    print("done")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "done" }, host.Context.Printed);
	}

	[Fact]
	public void ShortCircuit_Or_Skips_Rhs()
	{
		var (host, program) = Build("""
			func side() returns bool
			    print("side")
			    return false

			func main()
			    if true or ~side()
			        print("then")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "then" }, host.Context.Printed);
	}

	[Fact]
	public void While_With_Break_And_Continue()
	{
		var (host, program) = Build("""
			func main()
			    int $i = 0
			    int $sum = 0
			    while $i < 10
			        $i++
			        if $i == 3
			            continue
			        if $i == 6
			            break
			        $sum += $i
			    print(int_to_str($sum))
			""");
		host.Start(program, "main");
		// 1 + 2 + 4 + 5 = 12 (3 skipped, loop exits at 6)
		Assert.Equal(new[] { "12" }, host.Context.Printed);
	}

	[Fact]
	public void Tuple_Return_And_Assignment()
	{
		var (host, program) = Build("""
			func pair() returns (int $x, int $y)
			    return (7, 9)

			func main()
			    int $a
			    int $b
			    ($a, $b) = ~pair()
			    print(int_to_str($a * 10 + $b))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "79" }, host.Context.Printed);
	}

	[Fact]
	public void Suspend_And_Resume()
	{
		var (host, program) = Build("""
			func main()
			    print("before")
			    wait()
			    print("after")
			""");
		var execution = host.Start(program, "main");
		Assert.Equal(ScriptExecution.Paused, execution);
		Assert.Equal(new[] { "before" }, host.Context.Printed);

		execution = host.Resume();
		Assert.Equal(ScriptExecution.Finished, execution);
		Assert.Equal(new[] { "before", "after" }, host.Context.Printed);
	}

	[Fact]
	public void Label_Jump_Transfers_Without_Stack_Growth()
	{
		var (host, program) = Build("""
			label ping(int $n)
			    if $n == 0
			        print("done")
			        return
			    if $n == 100
			        wait()
			    @pong($n - 1)

			label pong(int $n)
			    @ping($n)
			""");
		// 200 mutual transfers would overflow the 64-frame stack if these were calls
		var execution = host.Start(program, "ping", Value.FromInt(200));

		// suspended mid-chain: the frame stack must still be flat
		Assert.Equal(ScriptExecution.Paused, execution);
		Assert.Equal(0, host.State.FrameDepth);

		execution = host.Resume();
		Assert.Equal(ScriptExecution.Finished, execution);
		Assert.Equal(new[] { "done" }, host.Context.Printed);
	}

	[Fact]
	public void Queue_Passes_Label_Reference_As_Method_Index()
	{
		var (host, program) = Build("""
			label deferred(int $x)
			    print(int_to_str($x))

			func main()
			    queue_strong(@deferred, 1)
			    queue_strong_int(@deferred, 2, 42)
			""");
		host.Start(program, "main");
		Assert.Equal(2, host.Context.Queued.Count);

		// the recorded index must point at the deferred method
		var (methodIndex, delay, args) = host.Context.Queued[1];
		Assert.Equal(2, delay);
		Assert.Equal(42, args[0].Int);
		Assert.Equal("deferred", program.Methods[methodIndex].Name);

		// invoking the queued method by index works (the host-side contract)
		host.Start(program, program.Methods[methodIndex].Name, Value.FromInt(7));
		Assert.Equal(new[] { "7" }, host.Context.Printed);
	}

	[Fact]
	public void Constants_Fold_Into_Bytecode()
	{
		var (host, program) = Build("""
			func main()
			    print(int_to_str(^max_level + 1))
			""",
			("skills.const", "int ^max_level = 99"));
		host.Start(program, "main");
		Assert.Equal(new[] { "100" }, host.Context.Printed);
	}

	[Fact]
	public void Context_Vars_Read_And_Write()
	{
		var (host, program) = Build("""
			func main()
			    %hp = %hp + 5
			    print(int_to_str(%hp))
			""",
			("player.context", "int %hp = 3"));
		host.Start(program, "main");
		// context slot 3 starts at default (0) in the test context; +5 = 5
		Assert.Equal(new[] { "5" }, host.Context.Printed);
	}

	[Fact]
	public void Trigger_Compiles_And_Runs()
	{
		var (host, program) = Build("""
			login main
			    print("logged in")
			""");
		host.Start(program, "login main");
		Assert.Equal(new[] { "logged in" }, host.Context.Printed);
	}

	[Fact]
	public void Increment_Decrement_Prefix_And_Postfix()
	{
		var (host, program) = Build("""
			func main()
			    int $x = 5
			    int $a = $x++
			    int $b = ++$x
			    int $c = $x--
			    print(int_to_str($a * 100 + $b * 10 + $x))
			""");
		host.Start(program, "main");
		// a=5 (post), x becomes 7 after ++, b=7, c=7 (post), x=6
		Assert.Equal(new[] { "576" }, host.Context.Printed);
	}

	[Fact]
	public void Negation_Operator()
	{
		var (host, program) = Build("""
			func main()
			    bool $flag = false
			    if !$flag
			        print("negated")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "negated" }, host.Context.Printed);
	}
}
