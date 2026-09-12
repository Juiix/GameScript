using System.Linq;
using GameScript.Bytecode;
using GameScript.Language.Ast;
using GameScript.Language.Symbols;
using GameScript.Language.Tests.Harness;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Named types (2.5): 'type NAME : root' declarations, assignability, casts,
/// overload ranking, typed constants / contexts / columns, and erasure at codegen.
/// </summary>
public class NamedTypeTests
{
	// Declared in a file indexed AFTER the script under test (ErrorsFor appends extra
	// files), so every test also proves that a type declared in a later file resolves.
	private const string Types = """
		// The id of an item definition
		type item : int
		// The id of a menu component
		type menu : int
		// A display title
		type title : string
		item ^item_sword = 1
		item ^item_shield = 2
		menu ^menu_hud = 10
		title ^title_bob = "bob"
		""";

	private static TestCompilation Analyze(string script, params (string Path, string Source)[] extraFiles)
	{
		var compilation = new TestCompilation()
			.AddFile("core.gs", Fixtures.CoreGs)
			.AddFile("test.gs", script);
		foreach (var (path, source) in extraFiles)
			compilation.AddFile(path, source);
		return compilation.Analyze();
	}

	private static string[] ErrorsFor(string script) =>
		Analyze(script, ("types.gs", Types)).AllErrors.Select(e => e.Message).ToArray();

	private static (TestHost Host, BytecodeProgram Program) Build(string script)
	{
		var compilation = Analyze(script, ("types.gs", Types));
		Assert.True(!compilation.AllErrors.Any(),
			"Unexpected errors:\n" + string.Join("\n", compilation.AllErrors.Select(TestCompilation.FormatError)));
		return (new TestHost(), compilation.Compile().Program);
	}

	// ---------------------------------------------------------------
	// declarations
	// ---------------------------------------------------------------

	[Fact]
	public void Type_Declared_In_Earlier_File_Also_Resolves()
	{
		var compilation = new TestCompilation()
			.AddFile("types.gs", Types)
			.AddFile("core.gs", Fixtures.CoreGs)
			.AddFile("test.gs", """
				func main()
				    item x = ^item_sword
				    print(int_to_str(x))
				""")
			.Analyze();
		Assert.Empty(compilation.AllErrors);
	}

	[Fact]
	public void Typed_Constant_Symbol_Carries_The_Named_Type_And_Its_Root()
	{
		var compilation = Analyze("func main()\n    return", ("types.gs", Types));
		var sword = compilation.Symbols.GetSymbols("item_sword").Single();
		Assert.Equal(TypeKind.Named, sword.Type!.Kind);
		Assert.Equal("item", sword.Type.Name);
		Assert.Equal("item ^item_sword", sword.Signature);

		var item = compilation.Symbols.GetSymbols("item").Single();
		Assert.Equal(IdentifierType.Type, item.IdentifierType);
		Assert.Equal("type item : int", item.Signature);
		Assert.Equal("The id of an item definition", item.Summary);
		Assert.Equal("int", item.Type!.Underlying!.Name);
	}

	[Fact]
	public void Root_Must_Be_Int_Or_String()
	{
		var errors = ErrorsFor("""
			type flag : bool
			type alias : item
			""");
		Assert.Contains(errors, e => e.Contains("The root type of 'flag' must be 'int' or 'string'"));
		Assert.Contains(errors, e => e.Contains("The root type of 'alias' must be 'int' or 'string'"));
	}

	[Fact]
	public void Builtin_Type_Cannot_Be_Redeclared()
	{
		var errors = ErrorsFor("type int : string");
		Assert.Contains(errors, e => e.Contains("'int' is a built-in type and cannot be redeclared"));
	}

	[Fact]
	public void Type_Name_Conflicts_With_Funcs_And_Duplicates()
	{
		var errors = ErrorsFor("""
			type item : int

			func menu()
			    return
			""");
		Assert.Contains(errors, e => e.Contains("Type 'item' is already defined in this context"));
		Assert.Contains(errors, e => e.Contains("'menu' conflicts with type 'menu'"));
	}

	[Fact]
	public void Undefined_Named_Type_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    nothing x = 1
			""");
		Assert.Contains(errors, e => e.Contains("Undefined type 'nothing'"));
	}

	[Fact]
	public void Local_And_Parameter_May_Shadow_A_Type_Name()
	{
		var errors = ErrorsFor("""
			func f(int item) returns int
			    return item + 1

			func g() returns int
			    int menu = 2
			    return menu
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Calling_A_Local_That_Shadows_A_Type_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    int item = 1
			    int y = item(2)
			""");
		Assert.Contains(errors, e => e.Contains("'item' is a local variable here and cannot be called"));
	}

	// ---------------------------------------------------------------
	// assignability
	// ---------------------------------------------------------------

	[Fact]
	public void Named_Widens_To_Its_Root_Implicitly()
	{
		var errors = ErrorsFor("""
			func main()
			    int x = ^item_sword
			    string s = ^title_bob
			    print(int_to_str(^item_sword))
			    print(^title_bob)
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Root_To_Named_Requires_A_Cast()
	{
		var errors = ErrorsFor("""
			func main()
			    item x = 5
			    item y = item(5)
			    int n = 7
			    item z = item(n)
			""");
		var error = Assert.Single(errors);
		Assert.Contains("Type mismatch, cannot assign 'int' to 'item'", error);
	}

	[Fact]
	public void Named_To_Named_Requires_A_Cast()
	{
		var errors = ErrorsFor("""
			func main()
			    menu m = ^item_sword
			    menu n = menu(^item_sword)
			""");
		var error = Assert.Single(errors);
		Assert.Contains("Type mismatch, cannot assign 'item' to 'menu'", error);
	}

	[Fact]
	public void Zero_Literal_Is_Assignable_To_Every_Named_Type()
	{
		var errors = ErrorsFor("""
			func main()
			    item a = 0
			    item b = -0
			    item c = (0)
			    item d = 0x0
			    title t = ""
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Zero_Literal_Of_The_Wrong_Root_Is_Rejected()
	{
		var errors = ErrorsFor("""
			func main()
			    item a = ""
			    title t = 0
			    item b = 1
			""");
		Assert.Equal(3, errors.Length);
	}

	[Fact]
	public void Assignment_To_An_Existing_Variable_Follows_The_Same_Rules()
	{
		var errors = ErrorsFor("""
			func main()
			    item x = ^item_sword
			    x = ^item_shield
			    x = 0
			    x = 3
			    x = ^menu_hud
			""");
		Assert.Equal(2, errors.Length);
		Assert.All(errors, e => Assert.Contains("Type mismatch, cannot assign", e));
	}

	// ---------------------------------------------------------------
	// casts
	// ---------------------------------------------------------------

	[Fact]
	public void Cast_Root_Mismatch_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    item x = item("x")
			    title t = title(1)
			""");
		Assert.Contains(errors, e => e.Contains("Cannot cast 'string' to 'item'; 'item' is a named type over 'int'"));
		Assert.Contains(errors, e => e.Contains("Cannot cast 'int' to 'title'; 'title' is a named type over 'string'"));
	}

	[Fact]
	public void Cast_Arity_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    item x = item()
			    item y = item(1, 2)
			""");
		Assert.Equal(2, errors.Count(e => e.Contains("Cast to 'item' takes exactly one argument")));
	}

	[Fact]
	public void Bare_Type_Name_As_A_Value_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    int x = item
			""");
		Assert.Contains(errors, e => e.Contains("'item' is a type, not a value; write item(expr) to cast"));
	}

	[Fact]
	public void Casts_Are_Not_Constant_Expressions()
	{
		var errors = ErrorsFor("""
			func f(item i = item(5))
			    return

			func main(item x)
			    switch x
			        case item(5): return
			""");
		Assert.Contains(errors, e => e.Contains("Parameter defaults must be a literal or a '^' constant"));
		Assert.Contains(errors, e => e.Contains("Case values must be constants"));
	}

	// ---------------------------------------------------------------
	// operators
	// ---------------------------------------------------------------

	[Fact]
	public void Equality_Against_Root_Or_Same_Type_Is_Allowed()
	{
		var errors = ErrorsFor("""
			func main(item x, int n)
			    if x == 0: return
			    if x != n: return
			    if x == ^item_sword: return
			    if n == ^item_sword: return
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Equality_Between_Different_Named_Types_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    if ^item_sword == ^menu_hud
			        return
			""");
		Assert.Contains(errors, e => e.Contains("Type mismatch, cannot compare 'item' and 'menu'"));
	}

	[Fact]
	public void Arithmetic_Widens_To_The_Root()
	{
		var errors = ErrorsFor("""
			func main()
			    int a = ^item_sword + 1
			    int b = ^item_sword * ^item_shield
			    bool c = ^item_sword < ^menu_hud
			    string s = ^title_bob + "!"
			    item d = ^item_sword + 1
			""");
		var error = Assert.Single(errors);
		Assert.Contains("Type mismatch, cannot assign 'int' to 'item'", error);
	}

	[Fact]
	public void Compound_Assignment_And_Increment_On_A_Named_Type_Are_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    item x = ^item_sword
			    x += 1
			    x++
			    ++x
			    x = item(x + 1)
			""");
		Assert.Equal(3, errors.Count(e => e.Contains("cannot be stored back into 'item'")));
	}

	[Fact]
	public void Interpolation_Prints_The_Root_Value()
	{
		var errors = ErrorsFor("""
			func main()
			    print("id {^item_sword} {^title_bob}")
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Switch_Cases_Follow_Assignability_To_The_Subject()
	{
		var errors = ErrorsFor("""
			func main(item x, int n)
			    switch x
			        case ^item_sword: return
			        case 0: return
			        case 5: return
			    switch n
			        case ^item_sword: return
			        case 7: return
			""");
		var error = Assert.Single(errors);
		Assert.Contains("Case value type 'int' does not match the switch subject type 'item'", error);
	}

	// ---------------------------------------------------------------
	// returns, tuples, defaults, params
	// ---------------------------------------------------------------

	[Fact]
	public void Typed_Returns_And_Tuple_Destructuring_Follow_Assignability()
	{
		var errors = ErrorsFor("""
			func f() returns item
			    return ^item_sword

			func g() returns (item, int)
			    return (0, 1)

			func h() returns item
			    return 5

			func main()
			    (item a, int b) = g()
			    (menu c, int d) = g()
			""");
		Assert.Equal(2, errors.Length);
		Assert.Contains(errors, e => e.Contains("Cannot return 'int', expected 'item'"));
		Assert.Contains(errors, e => e.Contains("Type mismatch, cannot assign '(item,int)' to '(menu,int)'"));
	}

	[Fact]
	public void Default_Parameter_Values_Follow_Assignability()
	{
		var errors = ErrorsFor("""
			func a(item i = ^item_sword)
			    return

			func b(item i = 0)
			    return

			func c(item i = 5)
			    return
			""");
		var error = Assert.Single(errors);
		Assert.Contains("cannot assign 'int' default to 'item' parameter 'i'", error);
	}

	[Fact]
	public void Variadic_Handler_Parameters_Stay_Primitive()
	{
		var errors = ErrorsFor("""
			slash give(item i)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("may only declare int/string/bool parameters"));
	}

	// ---------------------------------------------------------------
	// constants, contexts, tables
	// ---------------------------------------------------------------

	[Fact]
	public void Typed_Constants_Take_Root_Literals_And_Contexts_Take_Slot_Ids()
	{
		var errors = ErrorsFor("""
			item ^bad = "a"
			item @held = 1030
			item @broken = "x"
			""");
		Assert.Equal(2, errors.Length);
		Assert.Contains(errors, e => e.Contains("Type mismatch, cannot assign 'string' to 'item'"));
		Assert.Contains(errors, e => e.Contains("Context variable declaration expects an ID number"));
	}

	[Fact]
	public void Typed_Table_Columns_Check_Cells_Keys_And_Cursors()
	{
		var errors = ErrorsFor("""
			table t(item id, int v)
			    ^item_sword, 1
			    0, 2
			    5, 3

			func main(int n)
			    int a = t[^item_sword].v
			    int b = t[5].v
			    int c = t[n].v
			    for r in t
			        int d = r.id
			        menu m = r.id
			""");
		Assert.Equal(4, errors.Length);
		Assert.Contains(errors, e => e.Contains("Cell 1 of row 3 in table 't' is 'int' but column 'id' is 'item'"));
		Assert.Equal(2, errors.Count(e => e.Contains("Key 1 of 't' must be 'item'")));
		Assert.Contains(errors, e => e.Contains("Type mismatch, cannot assign 'item' to 'menu'"));
	}

	// ---------------------------------------------------------------
	// overloads
	// ---------------------------------------------------------------

	[Fact]
	public void Exact_Named_Match_Outranks_Widening()
	{
		var compilation = Analyze("""
			func f(int v) returns int
			    return 1

			func f(item v) returns int
			    return 2

			func main()
			    int a = f(^item_sword)
			    int b = f(0)
			    int c = f(7)
			""", ("types.gs", Types));
		Assert.Empty(compilation.AllErrors);
		var signatures = compilation.ResolvedCalls
			.Where(kv => kv.Key.FunctionName.Name == "f")
			.OrderBy(kv => kv.Key.FileRange.Start.Line)
			.Select(kv => kv.Value.ParamSignature)
			.ToArray();
		Assert.Equal(new[] { "(item)", "(int)", "(int)" }, signatures);
	}

	[Fact]
	public void Typed_Argument_Widens_Onto_An_Int_Only_Overload()
	{
		var errors = ErrorsFor("""
			func f(int v) returns int
			    return v

			func main()
			    int a = f(^item_sword)
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Zero_Literal_Across_Two_Named_Overloads_Is_Ambiguous()
	{
		var errors = ErrorsFor("""
			func g(item v)
			    return

			func g(menu v)
			    return

			func main()
			    g(0)
			""");
		Assert.Contains(errors, e => e.Contains("Ambiguous call to 'g'") && e.Contains("cast it"));
	}

	[Fact]
	public void Wrong_Kind_Argument_Is_Reported()
	{
		var errors = ErrorsFor("""
			func open(menu m)
			    return

			func main()
			    open(^item_sword)
			    open(5)
			""");
		Assert.Equal(2, errors.Count(e => e.Contains("Type mismatch, cannot call 'open(menu)'")));
	}

	[Fact]
	public void Call_Result_Type_Follows_The_Resolved_Overload()
	{
		var errors = ErrorsFor("""
			func g(int v) returns string
			    return "s"

			func g(item v) returns int
			    return 1

			func main()
			    int a = g(^item_sword)
			    int b = g(1)
			""");
		var error = Assert.Single(errors);
		Assert.Contains("Type mismatch, cannot assign 'string' to 'int'", error);
	}

	// ---------------------------------------------------------------
	// execution: erasure
	// ---------------------------------------------------------------

	[Fact]
	public void Named_Types_Erase_To_Their_Root()
	{
		var (host, program) = Build("""
			func main()
			    item x = item(5)
			    print(int_to_str(x + ^item_sword))
			    title t = ^title_bob
			    print(t + "!")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "6", "bob!" }, host.Context.Printed);
	}

	[Fact]
	public void Typed_Overloads_Route_By_Kind()
	{
		var (host, program) = Build("""
			func f(int v) returns string
			    return "int"

			func f(item v) returns string
			    return "item"

			func main()
			    print(f(1))
			    print(f(^item_sword))
			    print(f(0))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "int", "item", "int" }, host.Context.Printed);
	}

	[Fact]
	public void Switch_And_Table_Lookup_On_Named_Types_Run()
	{
		var (host, program) = Build("""
			table names(item id, title text)
			    ^item_sword,  ^title_bob
			    ^item_shield, ""

			func describe(item x)
			    switch x
			        case ^item_sword: print("sword")
			        case 0: print("none")
			        default: print("other")

			func main()
			    describe(^item_sword)
			    describe(0)
			    describe(^item_shield)
			    item k = ^item_sword
			    print(names[k].text)
			    k = item(99)
			    print(names[k].text + "|")
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "sword", "none", "other", "bob", "|" }, host.Context.Printed);
	}

	[Fact]
	public void Cast_Emits_No_Ops()
	{
		var (_, plain) = Build("""
			func main()
			    int y = 1
			    int x = y
			    print(int_to_str(x))
			""");
		var (_, cast) = Build("""
			func main()
			    int y = 1
			    item x = item(y)
			    print(int_to_str(x))
			""");
		Assert.Equal(
			plain.Methods.Single(m => m.Name == "main").Ops.Length,
			cast.Methods.Single(m => m.Name == "main").Ops.Length);
	}

	[Fact]
	public void Typed_Parameters_Report_Their_Root_Slot_Type()
	{
		var (_, program) = Build("""
			func f(item a, title b, int c)
			    return
			""");
		var f = program.Methods.Single(m => m.Name == "f");
		Assert.Equal(new[] { ValueType.Int, ValueType.String, ValueType.Int }, f.ParamTypes);
	}

	[Fact]
	public void Constants_Contexts_Types_And_Tables_Co_Locate_In_One_File()
	{
		var (host, program) = Build("""
			// A kind of hit splat
			type hit : int
			hit ^hit_damage = 0
			hit ^hit_heal = 3
			int @last_hit = 1040

			table hit_color(hit kind, int color)
			    ^hit_damage, 0xff0000
			    ^hit_heal,   0x00ff00

			func main()
			    print(int_to_str(hit_color[^hit_heal].color))
			""");
		host.Start(program, "main");
		Assert.Equal(new[] { "65280" }, host.Context.Printed);
	}
}
