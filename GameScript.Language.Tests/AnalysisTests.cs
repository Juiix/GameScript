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

	// ---------------------------------------------------------------
	// 2.1: declared triggers
	// ---------------------------------------------------------------

	[Fact]
	public void Handler_With_Declared_Kind_Is_Valid()
	{
		var errors = ErrorsFor("""
			obj_op_1 door_closed_r
			    print("open")
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Handler_With_Undeclared_Kind_Is_Reported()
	{
		var errors = ErrorsFor("""
			obj_po_1 door_closed_r
			    return
			""");
		Assert.Contains(errors, e => e.Contains("Unknown trigger kind 'obj_po_1'"));
	}

	[Fact]
	public void Handler_Subject_Is_Not_Validated()
	{
		var errors = ErrorsFor("""
			obj_op_1 anything_at_all
			    return

			mn_button hud:whatever
			    return
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Handler_Params_May_Be_A_Prefix()
	{
		var errors = ErrorsFor("""
			npc_queue_1 zero_args
			    return

			npc_queue_1 one_arg(int uid)
			    return

			npc_queue_1 two_args(int uid, int extra)
			    return
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Handler_With_Too_Many_Params_Is_Reported()
	{
		var errors = ErrorsFor("""
			npc_queue_1 broken(int a, int b, int c)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("must be a prefix"));
	}

	[Fact]
	public void Handler_With_Param_Type_Mismatch_Is_Reported()
	{
		var errors = ErrorsFor("""
			mn_text broken(int text)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("must be 'string'"));
	}

	[Fact]
	public void Trigger_Name_Colliding_With_Func_Is_Reported()
	{
		var errors = ErrorsFor("""
			func obj_op_1()
			    return
			""");
		Assert.Contains(errors, e => e.Contains("conflicts with trigger 'obj_op_1'"));
	}

	[Fact]
	public void Trigger_Declaration_Colliding_With_Func_Is_Reported()
	{
		var errors = ErrorsFor("""
			func my_thing()
			    return
			""",
			("extra.gs", "trigger my_thing"));
		Assert.Contains(errors, e => e.Contains("conflicts with"));
	}

	[Fact]
	public void Duplicate_Trigger_Declaration_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    return
			""",
			("extra.gs", "trigger obj_op_1"));
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Trigger_Declaration_With_Returns_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    return
			""",
			("extra.gs", "trigger custom_kind returns int"));
		Assert.Contains(errors, e => e.Contains("cannot declare return values"));
	}

	[Fact]
	public void Trigger_Declaration_With_Body_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    return
			""",
			("extra.gs", """
			trigger custom_kind
			    print("nope")
			"""));
		Assert.Contains(errors, e => e.Contains("cannot define a method body"));
	}

	[Fact]
	public void Trigger_Declaration_Cannot_Be_Called()
	{
		var errors = ErrorsFor("""
			func main()
			    obj_op_1()
			""");
		Assert.NotEmpty(errors);
	}

	// ---------------------------------------------------------------
	// 2.1: default parameter values
	// ---------------------------------------------------------------

	[Fact]
	public void Trailing_Defaults_Are_Valid()
	{
		var errors = ErrorsFor("""
			func choice(string text, string c3 = "", int anim = -1) returns int
			    return 0

			func main()
			    print(int_to_str(choice("hi")))
			    print(int_to_str(choice("hi", "x")))
			    print(int_to_str(choice("hi", "x", 5)))
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Non_Trailing_Default_Is_Reported()
	{
		var errors = ErrorsFor("""
			func broken(string a = "", string b)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("cannot follow defaulted parameters"));
	}

	[Fact]
	public void Non_Constant_Default_Is_Reported()
	{
		var errors = ErrorsFor("""
			func broken(int a, int b = a)
			    return
			""");
		Assert.Contains(errors, e => e.Contains("must be a literal or a '^' constant"));
	}

	[Fact]
	public void Type_Mismatched_Default_Is_Reported()
	{
		var errors = ErrorsFor("""
			func broken(int a = "nope")
			    return
			""");
		Assert.Contains(errors, e => e.Contains("Type mismatch"));
	}

	[Fact]
	public void Default_On_Trigger_Handler_Param_Is_Reported()
	{
		var errors = ErrorsFor("""
			mn_text broken(string text = "")
			    return
			""");
		Assert.Contains(errors, e => e.Contains("Trigger parameters cannot declare default values"));
	}

	[Fact]
	public void Call_Ambiguous_Through_Omitted_Defaults_Is_Reported()
	{
		var errors = ErrorsFor("""
			func pick(int a) returns int
			    return 1

			func pick(int a, int b = 0) returns int
			    return 2

			func main()
			    print(int_to_str(pick(5)))
			""");
		Assert.Contains(errors, e => e.Contains("Ambiguous call to 'pick'"));
	}

	[Fact]
	public void Call_Below_Required_Arity_Is_Reported()
	{
		var errors = ErrorsFor("""
			func choice(string text, int anim = -1) returns int
			    return 0

			func main()
			    print(int_to_str(choice()))
			""");
		Assert.NotEmpty(errors);
	}

	// ---------------------------------------------------------------
	// 2.1: switch statements
	// ---------------------------------------------------------------

	[Fact]
	public void Duplicate_Case_Values_Are_Reported()
	{
		var errors = ErrorsFor("""
			func main(int x)
			    switch x
			        case 1: return
			        case 2, 1: return
			""");
		Assert.Contains(errors, e => e.Contains("Duplicate case value '1'"));
	}

	[Fact]
	public void Duplicate_Case_Via_Constant_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main(int x)
			    switch x
			        case ^skill_attack: return
			        case 0: return
			""",
			("skills.const", "int ^skill_attack = 0"));
		Assert.Contains(errors, e => e.Contains("Duplicate case value '0'"));
	}

	[Fact]
	public void Non_Constant_Case_Value_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main(int x, int y)
			    switch x
			        case y: return
			""");
		Assert.Contains(errors, e => e.Contains("Case values must be constants"));
	}

	[Fact]
	public void Case_Type_Mismatch_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main(int x)
			    switch x
			        case "one": return
			""");
		Assert.Contains(errors, e => e.Contains("does not match the switch subject type"));
	}

	[Fact]
	public void Switch_With_Default_And_All_Returning_Arms_Counts_As_Return()
	{
		var errors = ErrorsFor("""
			func name(int x) returns string
			    switch x
			        case 0: return "a"
			        default: return "z"
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Switch_Without_Default_Does_Not_Count_As_Return()
	{
		var errors = ErrorsFor("""
			func name(int x) returns string
			    switch x
			        case 0: return "a"
			""");
		Assert.Contains(errors, e => e.Contains("not all paths return"));
	}

	[Fact]
	public void Switch_With_Non_Returning_Arm_Does_Not_Count_As_Return()
	{
		var errors = ErrorsFor("""
			func name(int x) returns string
			    switch x
			        case 0: print("no return")
			        default: return "z"
			""");
		Assert.Contains(errors, e => e.Contains("not all paths return"));
	}

	[Fact]
	public void Break_In_Switch_Without_Loop_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main(int x)
			    switch x
			        case 1: break
			""");
		Assert.Contains(errors, e => e.Contains("outside of a loop"));
	}

	// ---------------------------------------------------------------
	// 2.1: for loops
	// ---------------------------------------------------------------

	[Fact]
	public void For_Bounds_Must_Be_Int()
	{
		var errors = ErrorsFor("""
			func main(string s)
			    for i in 0..s
			        return
			""");
		Assert.Contains(errors, e => e.Contains("range bounds must be 'int'"));
	}

	[Fact]
	public void Break_And_Continue_In_For_Are_Valid()
	{
		var errors = ErrorsFor("""
			func main()
			    for i in 0..10
			        if i == 2
			            continue
			        if i == 5
			            break
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void Sequential_For_Loops_May_Reuse_The_Variable()
	{
		var errors = ErrorsFor("""
			func main()
			    for i in 0..3
			        print(int_to_str(i))
			    for i in 5..8
			        print(int_to_str(i))
			""");
		Assert.Empty(errors);
	}

	[Fact]
	public void For_Variable_Reusing_A_Plain_Local_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    int i = 0
			    for i in 0..3
			        return
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	[Fact]
	public void Plain_Local_Reusing_A_For_Variable_Is_Reported()
	{
		var errors = ErrorsFor("""
			func main()
			    for i in 0..3
			        return
			    int i = 0
			""");
		Assert.Contains(errors, e => e.Contains("already defined"));
	}

	// ---------------------------------------------------------------
	// 2.4: constant tables
	// ---------------------------------------------------------------

	private const string SkillsConst = """
		int ^skill_attack = 0
		int ^skill_mining = 2
		int ^jingle_melee = 100
		""";

	private const string SkillTable = """
		table skill(key int id, key string name, int jingle)
		    ^skill_attack, "Attack", ^jingle_melee
		    ^skill_mining, "Mining", ^jingle_melee

		""";

	[Fact]
	public void Negative_Constants_Are_Distinct_Compile_Time_Values()
	{
		// previously any non-literal initializer indexed as a boxed Value.Null, so
		// two negative constants compared equal in duplicate-case / key checks
		var errors = ErrorsFor("""
			table t(key int a, string b)
			    ^neg_one, "x"
			    ^neg_two, "y"

			func main(int x)
			    switch x
			        case ^neg_one: return
			        case ^neg_two: return
			""",
			("n.const", "int ^neg_one = -1\nint ^neg_two = -2"));
		Assert.Empty(errors);
	}

	[Fact]
	public void Valid_Table_Script_Has_No_Errors()
	{
		var errors = ErrorsFor(SkillTable + """
			func main(int s, string n)
			    print(skill[s].name)
			    print(skill[name: n].name)
			    print(skill[id: s].name)
			    if skill.has(s): print("y")
			    print(skill.at(skill.count - 1).name)
			    for r in skill
			        print(r.name + int_to_str(r.jingle))
			""",
			("skills.const", SkillsConst));
		Assert.Empty(errors);
	}

	[Fact]
	public void Table_Column_Type_Must_Be_Scalar()
	{
		var errors = ErrorsFor("""
			table t(func a, int b)
			    main, 1

			func main()
			    return
			""");
		Assert.Contains(errors, e => e.Contains("Table columns must be 'int', 'string' or 'bool'"));
	}

	[Fact]
	public void Table_Reserved_And_Duplicate_Column_Names_Are_Reported()
	{
		var errors = ErrorsFor("""
			table t(int count, int a, int a)
			    1, 2, 3
			""");
		Assert.Contains(errors, e => e.Contains("'count' is a reserved table member"));
		Assert.Contains(errors, e => e.Contains("Duplicate column 'a'"));
	}

	[Fact]
	public void Table_Ragged_Row_Is_Reported()
	{
		var errors = ErrorsFor("""
			table t(int a, int b)
			    1, 2
			    3
			    default: 0
			""");
		Assert.Contains(errors, e => e.Contains("Row 2 of table 't' has 1 cell(s) but the table has 2 column(s)"));
		Assert.Contains(errors, e => e.Contains("The default row of table 't' has 1 cell(s)"));
	}

	[Fact]
	public void Table_Cells_Must_Be_Constants_Of_The_Column_Type()
	{
		var errors = ErrorsFor("""
			table t(int a, string b)
			    1 + 1, "x"
			    2, 3
			""");
		Assert.Contains(errors, e => e.Contains("Table cells must be constants"));
		Assert.Contains(errors, e => e.Contains("Cell 2 of row 2 in table 't' is 'int' but column 'b' is 'string'"));
	}

	[Fact]
	public void Table_Duplicate_Key_Column_Value_Is_Reported()
	{
		var errors = ErrorsFor("""
			table t(key int a, key string b)
			    ^skill_attack, "x"
			    0, "y"
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("Duplicate key 0 in 'key' column 'a' of table 't'"));
	}

	[Fact]
	public void Table_Duplicate_Row_Is_Reported()
	{
		var errors = ErrorsFor("""
			table t(int a, string b)
			    1, "x"
			    2, "y"
			    1, "x"
			""");
		Assert.Contains(errors, e => e.Contains("Duplicate row: row 3 of table 't' repeats an earlier row"));
	}

	[Fact]
	public void Table_Lookup_Arity_Follows_The_Key_Width()
	{
		var errors = ErrorsFor("""
			table choice_ui(bool mobile, bool three, int menu)
			    false, false, 1
			    false, true,  2
			    true,  false, 3

			func main(bool m, bool three)
			    int a = choice_ui[m].menu
			    int b = choice_ui[m, three, 1, 2].menu
			    int c = choice_ui[m, three].menu
			""");
		Assert.Contains(errors, e => e.Contains("Lookup on 'choice_ui' needs at least 2 key(s)"));
		Assert.Contains(errors, e => e.Contains("Lookup on 'choice_ui' passes 4 key(s) but the table has only 3 column(s)"));
		Assert.Single(errors.Where(e => e.Contains("Lookup on 'choice_ui'")).Distinct().Where(e => e.Contains("needs")));
	}

	[Fact]
	public void Table_Key_Type_Mismatch_Is_Reported()
	{
		var errors = ErrorsFor(SkillTable + """
			func main(string s)
			    print(skill[s].name)
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("Key 1 of 'skill' must be 'int' (column 'id'), not 'string'"));
	}

	[Fact]
	public void Table_Named_Lookup_Requires_A_Key_Column()
	{
		var errors = ErrorsFor(SkillTable + """
			func main(int j)
			    print(skill[jingle: j].name)
			    print(skill[bogus: j].name)
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("'jingle' is not a key column of table 'skill'"));
		Assert.Contains(errors, e => e.Contains("Table 'skill' has no column 'bogus'"));
	}

	[Fact]
	public void Table_Unknown_Column_And_Missing_Column_Are_Reported()
	{
		var errors = ErrorsFor(SkillTable + """
			func main(int s)
			    print(skill[s].nope)
			    skill[s]
			    skill.at(0)
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("Table 'skill' has no column 'nope'"));
		Assert.Contains(errors, e => e.Contains("A table lookup yields a row; select a column"));
		Assert.Contains(errors, e => e.Contains("'at' yields a row; select a column"));
	}

	[Fact]
	public void Table_Builtin_Shapes_Are_Checked()
	{
		var errors = ErrorsFor(SkillTable + """
			func main(int s, string n)
			    int a = skill.count()
			    bool b = skill.has
			    string c = skill.at(n).name
			    string d = skill.name
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("'count' is a property; write skill.count without parentheses"));
		Assert.Contains(errors, e => e.Contains("'has' takes the key(s) to test"));
		Assert.Contains(errors, e => e.Contains("'at' takes an 'int' row index, not 'string'"));
		Assert.Contains(errors, e => e.Contains("Select a row before a column: skill[key].name"));
	}

	[Fact]
	public void Table_Cannot_Be_Used_As_A_Value()
	{
		var errors = ErrorsFor(SkillTable + """
			func main()
			    print(skill)
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("Table 'skill' cannot be used as a value"));
	}

	[Fact]
	public void Row_Cursor_Is_Only_Valid_As_A_Member_Target()
	{
		var errors = ErrorsFor(SkillTable + """
			func main()
			    for r in skill
			        int x = r
			        r = r
			        r.name = "x"
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("'r' is a table row cursor; read a column with r.column"));
		Assert.Contains(errors, e => e.Contains("Left-hand side of assignment must be an assignable variable"));
	}

	[Fact]
	public void Row_Cursor_Reuse_Must_Match_The_Table()
	{
		var errors = ErrorsFor(SkillTable + """
			table other(int a)
			    1

			func main()
			    for r in skill
			        print(r.name)
			    for r in other
			        print(int_to_str(r.a))
			    for r in skill
			        print(r.name)
			    for i in 0..2
			        return
			    for i in skill
			        return
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("'r' is already a row cursor of 'skill'; it cannot iterate 'other'"));
		Assert.Contains(errors, e => e.Contains("'i' is already an int loop variable; a row cursor cannot reuse it"));
	}

	[Fact]
	public void For_In_Non_Table_Is_Reported()
	{
		var errors = ErrorsFor("""
			func helper()
			    return

			func main(int n)
			    for r in n
			        return
			    for q in helper
			        return
			    for z in nothing
			        return
			""");
		Assert.Contains(errors, e => e.Contains("'n' is not a table"));
		Assert.Contains(errors, e => e.Contains("'helper' is not a table"));
		Assert.Contains(errors, e => e.Contains("'nothing' is not declared"));
	}

	[Fact]
	public void Non_Table_Cannot_Be_Indexed_Or_Dotted()
	{
		var errors = ErrorsFor("""
			func main(int n)
			    int a = n[1].x
			    int b = n.count
			    int c = main.x
			""");
		Assert.Contains(errors, e => e.Contains("'n' is not a table and cannot be indexed"));
		Assert.Contains(errors, e => e.Contains("'.count' is only valid on a table"));
		Assert.Contains(errors, e => e.Contains("Member access ('.name') is only valid on a table or a table row"));
	}

	[Fact]
	public void Table_Name_Shares_The_Method_Namespace()
	{
		var errors = ErrorsFor("""
			table print(int a)
			    1

			table dup(int a)
			    1

			func dup() returns int
			    return 1

			func main()
			    int dup = 0
			""");
		Assert.Contains(errors, e => e.Contains("Table 'print' conflicts with Command 'print'"));
		Assert.Contains(errors, e => e.Contains("conflicts with table 'dup'") || e.Contains("Table 'dup' conflicts with Func 'dup'"));
		Assert.Contains(errors, e => e.Contains("Local 'dup' conflicts with Table 'dup'"));
	}

	[Fact]
	public void Duplicate_Table_Across_Files_Is_Reported()
	{
		var errors = ErrorsFor("""
			table t(int a)
			    1
			""",
			("other.gs", "table t(int a)\n    2\n"));
		Assert.Contains(errors, e => e.Contains("Table 't' is already defined in this context"));
	}

	[Fact]
	public void Table_In_Another_File_With_Const_Cells_Resolves()
	{
		var errors = ErrorsFor("""
			func main(int s)
			    print(skill[s].name)
			""",
			("skills.const", SkillsConst),
			("tables.gs", SkillTable));
		Assert.Empty(errors);
	}

	[Fact]
	public void Large_Table_Warns()
	{
		var rows = string.Join("\n", Enumerable.Range(0, 65).Select(i => $"    {i}, {i}"));
		var errors = ErrorsFor("table big(int a, int b)\n" + rows + "\n");
		Assert.Contains(errors, e => e.Contains("Table 'big' has 65 rows"));
	}

	[Fact]
	public void Constant_Key_With_No_Row_Warns_Unless_Default_Row_Exists()
	{
		var errors = ErrorsFor(SkillTable + """
			table dm(int i, string text)
			    0, "a"
			    default: 0, "z"

			func main()
			    print(skill[42].name)
			    print(skill[name: "Nope"].name)
			    print(dm[42].text)
			""",
			("skills.const", SkillsConst));
		Assert.Contains(errors, e => e.Contains("No row of 'skill' has key (42)"));
		Assert.Contains(errors, e => e.Contains("No row of 'skill' has key (\"Nope\")"));
		Assert.DoesNotContain(errors, e => e.Contains("No row of 'dm'"));
	}
}
