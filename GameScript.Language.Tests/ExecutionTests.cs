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
	public void Modulo_Operator_And_Compound_Assign()
	{
		var (host, program) = Build("""
			func main()
			    int x = 17
			    print(int_to_str(x % 5))
			    x %= 5
			    print(int_to_str(x))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "2", "2" }, host.Context.Printed);
	}

	[Fact]
	public void String_Concatenation_Coerces()
	{
		var (host, program) = Build("""
			func main()
			    int level = 42
			    print("lvl " + level + "!")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "lvl 42!" }, host.Context.Printed);
	}

	[Fact]
	public void Func_Call_With_Return()
	{
		var (host, program) = Build("""
			func add(int a, int b) returns int
			    return a + b

			func main()
			    print(int_to_str(add(2, 3)))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "5" }, host.Context.Printed);
	}

	[Fact]
	public void Recursion()
	{
		var (host, program) = Build("""
			func factorial(int n) returns int
			    if n <= 1
			        return 1
			    return n * factorial(n - 1)

			func main()
			    print(int_to_str(factorial(5)))
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
			    if false and side()
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
			    if true or side()
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
			    int i = 0
			    int sum = 0
			    while i < 10
			        i++
			        if i == 3
			            continue
			        if i == 6
			            break
			        sum += i
			    print(int_to_str(sum))
			""");
		host.Start(program, "main");
		// 1 + 2 + 4 + 5 = 12 (3 skipped, loop exits at 6)
		Assert.Equal(new[] { "12" }, host.Context.Printed);
	}

	[Fact]
	public void Tuple_Return_And_Assignment()
	{
		var (host, program) = Build("""
			func pair() returns (int x, int y)
			    return (7, 9)

			func main()
			    int a
			    int b
			    (a, b) = pair()
			    print(int_to_str(a * 10 + b))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "79" }, host.Context.Printed);
	}

	[Fact]
	public void Declare_And_Destructure_In_One_Line()
	{
		var (host, program) = Build("""
			func pair() returns (int x, string y)
			    return (5, "ok")

			func main()
			    (int a, string b) = pair()
			    print(b + int_to_str(a))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "ok5" }, host.Context.Printed);
	}

	[Fact]
	public void Mixed_Destructure_With_Existing_Local()
	{
		var (host, program) = Build("""
			func pair() returns (int x, int y)
			    return (3, 4)

			func main()
			    int a
			    (a, int b) = pair()
			    print(int_to_str(a * 10 + b))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "34" }, host.Context.Printed);
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
	public void Tail_Calls_Do_Not_Grow_The_Stack()
	{
		var (host, program) = Build("""
			func ping(int n)
			    if n == 0
			        print("done")
			        return
			    if n == 100
			        wait()
			    pong(n - 1)

			func pong(int n)
			    ping(n)
			""");
		// 200 mutual transfers would overflow the 64-frame stack as plain calls
		var execution = host.Start(program, "ping", Value.FromInt(200));

		// suspended mid-chain: the frame stack must still be flat
		Assert.Equal(ScriptExecution.Paused, execution);
		Assert.Equal(0, host.State.FrameDepth);

		execution = host.Resume();
		Assert.Equal(ScriptExecution.Finished, execution);
		Assert.Equal(new[] { "done" }, host.Context.Printed);
	}

	[Fact]
	public void Return_Call_Is_A_Tail_Transfer()
	{
		var (host, program) = Build("""
			func countdown(int n) returns int
			    if n == 0
			        return 99
			    return countdown(n - 1)

			func main()
			    print(int_to_str(countdown(500)))
			""");
		// 500 self-recursions with a 64-frame budget only work as tail transfers
		host.Start(program, "main");
		Assert.Equal(new[] { "99" }, host.Context.Printed);
	}

	[Fact]
	public void Queue_Passes_Func_Reference_As_Method_Index()
	{
		var (host, program) = Build("""
			func deferred(int x)
			    print(int_to_str(x))

			func main()
			    queue_strong(deferred, 1)
			    queue_strong(deferred, 2, 42)
			""");
		host.Start(program, "main");
		Assert.Equal(2, host.Context.Queued.Count);

		// overloads routed to distinct engine ops by the '=' binding in core.gs
		Assert.Empty(host.Context.Queued[0].Args);
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
			    @hp = @hp + 5
			    print(int_to_str(@hp))
			""",
			("player.context", "int @hp = 3"));
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
			    int x = 5
			    int a = x++
			    int b = ++x
			    int c = x--
			    print(int_to_str(a * 100 + b * 10 + x))
			""");
		host.Start(program, "main");
		// a=5 (post), x becomes 7 after ++, b=7, c=7 (post), x=6
		Assert.Equal(new[] { "576" }, host.Context.Printed);
	}

	[Fact]
	public void Not_Keyword_Negates()
	{
		var (host, program) = Build("""
			func main()
			    bool flag = false
			    if not flag
			        print("negated")
			    if not (1 == 2) and not false
			        print("compound")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "negated", "compound" }, host.Context.Printed);
	}

	[Fact]
	public void Overloaded_Funcs_Dispatch_By_Argument_Type()
	{
		var (host, program) = Build("""
			func pick(int v) returns int
			    return 1

			func pick(string v) returns int
			    return 2

			func main()
			    print(int_to_str(pick(0) * 10 + pick("x")))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "12" }, host.Context.Printed);
	}

	[Fact]
	public void Command_Overloads_With_Op_Binding_Hit_Distinct_Engine_Ops()
	{
		var (host, program) = Build("""
			func deferred(int x)
			    print(int_to_str(x))

			func main()
			    enqueue(deferred, 1)
			    enqueue(deferred, 2, 42)
			""",
			("overloads.gs", """
			// One script name, two engine ops selected by '=' binding
			command enqueue(func method, int delay) = queue_strong
			command enqueue(func method, int delay, int arg0) = queue_strong_int
			"""));
		host.Start(program, "main");

		Assert.Equal(2, host.Context.Queued.Count);
		Assert.Empty(host.Context.Queued[0].Args);
		Assert.Single(host.Context.Queued[1].Args);
		Assert.Equal(42, host.Context.Queued[1].Args[0].Int);
	}

	[Fact]
	public void String_Interpolation_Compiles_To_Concat()
	{
		var (host, program) = Build("""
			func skill_name(int skill) returns string
			    return "attack"

			func main()
			    int after = 50
			    int skill = 0
			    print("Congratulations, your {skill_name(skill)} level is now {after}!")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "Congratulations, your attack level is now 50!" }, host.Context.Printed);
	}

	// ---------------------------------------------------------------
	// 2.1: for loops
	// ---------------------------------------------------------------

	[Fact]
	public void For_Sums_A_Half_Open_Range()
	{
		var (host, program) = Build("""
			func main()
			    int sum = 0
			    for i in 0..5
			        sum += i
			    print(int_to_str(sum))
			""");
		host.Start(program, "main");
		// 0+1+2+3+4 = 10 (END is exclusive)
		Assert.Equal(new[] { "10" }, host.Context.Printed);
	}

	[Fact]
	public void For_Empty_Range_Never_Runs()
	{
		var (host, program) = Build("""
			func main()
			    for i in 5..5
			        print("never")
			    print("done")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "done" }, host.Context.Printed);
	}

	[Fact]
	public void For_Continue_Still_Increments()
	{
		var (host, program) = Build("""
			func main()
			    int sum = 0
			    for i in 0..6
			        if i == 2
			            continue
			        if i == 4
			            break
			        sum += i
			    print(int_to_str(sum))
			""");
		host.Start(program, "main");
		// 0 + 1 + 3 = 4 (2 skipped, loop exits at 4)
		Assert.Equal(new[] { "4" }, host.Context.Printed);
	}

	[Fact]
	public void For_Bounds_Are_Evaluated_Once()
	{
		var (host, program) = Build("""
			func limit() returns int
			    print("eval")
			    return 3

			func main()
			    for i in 0..limit()
			        print(int_to_str(i))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "eval", "0", "1", "2" }, host.Context.Printed);
	}

	[Fact]
	public void For_Variable_Stays_Visible_After_The_Loop()
	{
		var (host, program) = Build("""
			func main()
			    int last = 0
			    for i in 0..3
			        last = i
			    print(int_to_str(i * 10 + last))
			""");
		host.Start(program, "main");
		// function-flat scoping: i == 3 after the loop, last == 2
		Assert.Equal(new[] { "32" }, host.Context.Printed);
	}

	[Fact]
	public void Nested_And_Sequential_For_Loops()
	{
		var (host, program) = Build("""
			func main()
			    int sum = 0
			    for i in 0..3
			        for j in 0..3
			            sum += i * 3 + j
			    for i in 0..2
			        sum += 100
			    print(int_to_str(sum))
			""");
		host.Start(program, "main");
		// 0..8 sums to 36, plus 200
		Assert.Equal(new[] { "236" }, host.Context.Printed);
	}

	// ---------------------------------------------------------------
	// 2.1: switch statements
	// ---------------------------------------------------------------

	[Fact]
	public void Switch_On_Int_With_Multi_Value_And_Default()
	{
		var (host, program) = Build("""
			func name(int skill) returns string
			    switch skill
			        case 0: return "Attack"
			        case 3, 4:
			            return "Gathering"
			        default: return "Unknown"

			func main()
			    print(name(0))
			    print(name(3))
			    print(name(4))
			    print(name(9))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "Attack", "Gathering", "Gathering", "Unknown" }, host.Context.Printed);
	}

	[Fact]
	public void Switch_On_String()
	{
		var (host, program) = Build("""
			func id(string name) returns int
			    switch name
			        case "attack": return 0
			        case "mining", "fishing": return 1
			        default: return -1

			func main()
			    print(int_to_str(id("attack")))
			    print(int_to_str(id("fishing")))
			    print(int_to_str(id("nope")))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "0", "1", "-1" }, host.Context.Printed);
	}

	[Fact]
	public void Switch_No_Match_Without_Default_Skips()
	{
		var (host, program) = Build("""
			func main()
			    int x = 9
			    switch x
			        case 1: print("one")
			    print("after")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "after" }, host.Context.Printed);
	}

	[Fact]
	public void Switch_Subject_Is_Evaluated_Once()
	{
		var (host, program) = Build("""
			func subject() returns int
			    print("eval")
			    return 2

			func main()
			    switch subject()
			        case 1: print("one")
			        case 2: print("two")
			        case 3: print("three")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "eval", "two" }, host.Context.Printed);
	}

	[Fact]
	public void Switch_On_Constants_Compiles_Cases()
	{
		var (host, program) = Build("""
			func main()
			    int skill = 1
			    switch skill
			        case ^skill_attack: print("attack")
			        case ^skill_mining: print("mining")
			""",
			("skills.const", "int ^skill_attack = 0\nint ^skill_mining = 1"));
		host.Start(program, "main");
		Assert.Equal(new[] { "mining" }, host.Context.Printed);
	}

	[Fact]
	public void Break_In_Switch_Case_Exits_The_Enclosing_Loop()
	{
		var (host, program) = Build("""
			func main()
			    int i = 0
			    while i < 10
			        i++
			        switch i
			            case 3: break
			        print(int_to_str(i))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "1", "2" }, host.Context.Printed);
	}

	// ---------------------------------------------------------------
	// 2.1: inline if/else bodies
	// ---------------------------------------------------------------

	[Fact]
	public void Inline_If_And_Else_Bodies_Run()
	{
		var (host, program) = Build("""
			func pick(bool flag) returns int
			    if flag: return 1
			    else: return 2

			func main()
			    print(int_to_str(pick(true) * 10 + pick(false)))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "12" }, host.Context.Printed);
	}

	// ---------------------------------------------------------------
	// 2.1: default parameter values
	// ---------------------------------------------------------------

	[Fact]
	public void Func_Defaults_Are_Baked_At_The_Call_Site()
	{
		var (host, program) = Build("""
			func greet(string who, string suffix = "!", int reps = 1)
			    for i in 0..reps
			        print(who + suffix)

			func main()
			    greet("a")
			    greet("b", "?")
			    greet("c", ".", 2)
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "a!", "b?", "c.", "c." }, host.Context.Printed);
	}

	[Fact]
	public void Constant_And_Negated_Defaults_Resolve()
	{
		var (host, program) = Build("""
			func report(int anim = ^anim_still, int off = -1)
			    print(int_to_str(anim * 100 + off))

			func main()
			    report()
			""",
			("anims.const", "int ^anim_still = 7"));
		host.Start(program, "main");
		Assert.Equal(new[] { "699" }, host.Context.Printed);
	}

	[Fact]
	public void Command_Defaults_Reach_The_Host()
	{
		var (host, program) = Build("""
			func deferred(int x)
			    print(int_to_str(x))

			func main()
			    enqueue(deferred)
			""",
			("overloads.gs", "command enqueue(func method, int delay = 9, int arg0 = 42) = queue_strong_int"));
		host.Start(program, "main");

		var (_, delay, args) = Assert.Single(host.Context.Queued);
		Assert.Equal(9, delay);
		Assert.Equal(42, args[0].Int);
	}

	[Fact]
	public void Tail_Call_With_Omitted_Defaults()
	{
		var (host, program) = Build("""
			func fallback(int n, int bonus = 7) returns int
			    return n + bonus

			func compute(int n) returns int
			    return fallback(n)

			func main()
			    print(int_to_str(compute(10)))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "17" }, host.Context.Printed);
	}

	[Fact]
	public void String_Interpolation_Edge_Shapes()
	{
		var (host, program) = Build("""
			func main()
			    int x = 7
			    print("{x}")
			    print("{{literal}} {x + 1}")
			    print("tail {x}")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "7", "{literal} 8", "tail 7" }, host.Context.Printed);
	}
}
