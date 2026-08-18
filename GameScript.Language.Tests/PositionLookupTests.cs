using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Visitors;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Simulates the LSP DefinitionHandler flow (FindNodeAtPosition + symbol lookup)
/// over 2.0 syntax: cursor on a node -> IdentifierNode -> GetSymbol by name.
/// </summary>
public class PositionLookupTests
{
	private const string Source = """
		// Returns a name
		func skill_name(int skill) returns string
		    return "attack"

		func main()
		    int localValue = 5
		    print(skill_name(localValue))
		    @hp = localValue + ^max_level
		    print("lvl {skill_name(localValue)}!")
		    print("{localValue}")
		""";

	private static (AstNode Root, GlobalSymbolTable Symbols, System.Collections.Generic.Dictionary<MethodDefinitionNode, LocalIndex> Locals) Setup()
	{
		var symbols = new GlobalSymbolTable();
		var types = new GlobalTypeIndex();
		System.Collections.Generic.Dictionary<MethodDefinitionNode, LocalIndex> locals = [];

		AstNode Add(string path, string text)
		{
			var parser = new AstParser(path, text);
			AstNode root = path.EndsWith(".context") ? parser.ParseContexts()
				: path.EndsWith(".const") ? parser.ParseConstants()
				: parser.ParseProgram();
			Assert.Empty(parser.Errors ?? []);
			var fileIndex = new FileIndex();
			var visitor = new IndexVisitor(fileIndex, new VisitorContext(types, symbols, path));
			root.Accept(visitor);
			symbols.AddFile(path, fileIndex.FileSymbols);
			foreach (var (method, index) in visitor.LocalIndexes)
				locals[method] = index;
			return root;
		}

		Add("core.gs", "// Prints\ncommand print(string value)");
		Add("player.context", "int @hp = 3");
		Add("skills.const", "int ^max_level = 99");
		var root = Add("test.gs", Source);
		return (root, symbols, locals);
	}

	[Theory]
	[InlineData(6, 12, "skill_name")]  // cursor on the bare func call name
	[InlineData(6, 5, "print")]        // cursor on the bare command call name
	[InlineData(6, 24, "localValue")]  // cursor on a bare local usage
	[InlineData(7, 6, "hp")]           // cursor on the @context usage
	[InlineData(7, 28, "max_level")]   // cursor on the ^constant usage
	[InlineData(8, 18, "skill_name")]  // cursor inside a string interpolation call
	[InlineData(8, 29, "localValue")]  // cursor on a local inside interpolation
	public void Definition_Lookup_Finds_Symbol_Under_Cursor(int line, int character, string expectedName)
	{
		var (root, symbols, locals) = Setup();

		var identifier = root.FindNodeAtPosition<IdentifierNode>(line, character);
		Assert.NotNull(identifier);
		Assert.Equal(expectedName, identifier!.Name);

		// mirror DefinitionHandler: innermost local scope first, then globals
		var localIndex = locals.Values.FirstOrDefault(x => x.FileRange.Contains(line, character));
		var symbol = localIndex?.GetSymbol(identifier.Name) ?? symbols.GetSymbol(identifier.Name);
		Assert.NotNull(symbol);
		Assert.Equal(expectedName, symbol!.Name);
	}

	// References/Rename/Highlight resolve the cursor with the UNTYPED lookup: a
	// synthetic zero-width node (interpolation '+') at the same position must not
	// win over the real identifier.
	[Theory]
	[InlineData(8, 16, "skill_name")]  // first char of an embedded call
	[InlineData(9, 12, "localValue")]  // first char of a "{x}"-style embedded local
	public void Untyped_Lookup_Skips_Synthetic_Zero_Width_Nodes(int line, int character, string expectedName)
	{
		var (root, _, _) = Setup();

		var node = root.FindNodeAtPosition(line, character);
		var identifier = Assert.IsType<IdentifierNode>(node);
		Assert.Equal(expectedName, identifier.Name);
	}

	[Fact]
	public void References_Work_On_Local_Inside_Interpolation_RealWorldShape()
	{
		// mirror of the reported case: gain_xp's 'after' local, cursor inside {after}
		const string source = """
			command p_level(int skill) returns int
			command add_xp(int skill, int amount)
			command play_fx(int fx)
			command play_jingle(int jingle)
			command message(string text)
			func skill_jingle(int skill) returns int
			    return 0
			func skill_name(int skill) returns string
			    return "attack"

			func gain_xp(int skill, int amount)
			    int before = p_level(skill)
			    add_xp(skill, amount)
			    int after = p_level(skill)
			    if after > before
			        play_fx(^fx_level_up)
			        play_jingle(skill_jingle(skill))
			        message("Congratulations, your {skill_name(skill)} level is now {after}!")
			""";

		var symbols = new GlobalSymbolTable();
		var types = new GlobalTypeIndex();
		var parser = new AstParser("test.gs", source);
		var root = parser.ParseProgram();
		var fileIndex = new FileIndex();
		var indexVisitor = new IndexVisitor(fileIndex, new VisitorContext(types, symbols, "test.gs"));
		root.Accept(indexVisitor);
		symbols.AddFile("test.gs", fileIndex.FileSymbols);

		var constParser = new AstParser("fx.const", "int ^fx_level_up = 1");
		var constRoot = constParser.ParseConstants();
		var constIndex = new FileIndex();
		constRoot.Accept(new IndexVisitor(constIndex, new VisitorContext(types, symbols, "fx.const")));
		symbols.AddFile("fx.const", constIndex.FileSymbols);

		// line 17 = the message(...) line; '{after}' starts at col 74, 'after' at 75
		var messageLine = source.Split('\n')[17];
		var cursor = messageLine.IndexOf("{after}") + 1;

		// ReferencesHandler flow
		var node = root.FindNodeAtPosition(17, cursor);
		Assert.NotNull(node);
		var identifier = Assert.IsType<IdentifierNode>(node);
		Assert.Equal("after", identifier.Name);

		var localIndex = indexVisitor.LocalIndexes.Values
			.FirstOrDefault(x => x.FileRange.Contains(17, cursor));
		Assert.NotNull(localIndex);
		Assert.NotNull(localIndex!.GetSymbol("after"));

		var references = localIndex.GetReferences("after").ToList();
		// declaration usage sites: 'if after > before' + '{after}'
		Assert.Contains(references, r => r.FileRange.Start.Line == 17);
	}

	[Fact]
	public void Lookup_Resolves_Loop_Variables_And_Case_Values()
	{
		const string source = """
			func main(int skill)
			    for i in 0..5
			        print("{i}")
			    switch skill
			        case ^max_level: print("max")
			""";

		var symbols = new GlobalSymbolTable();
		var types = new GlobalTypeIndex();
		var parser = new AstParser("test.gs", source);
		var root = parser.ParseProgram();
		Assert.Empty(parser.Errors ?? []);
		var fileIndex = new FileIndex();
		var indexVisitor = new IndexVisitor(fileIndex, new VisitorContext(types, symbols, "test.gs"));
		root.Accept(indexVisitor);
		symbols.AddFile("test.gs", fileIndex.FileSymbols);

		// cursor on the loop-variable use inside the interpolated body
		var loopVar = root.FindNodeAtPosition<IdentifierNode>(2, 16);
		Assert.NotNull(loopVar);
		Assert.Equal("i", loopVar!.Name);
		var localIndex = indexVisitor.LocalIndexes.Values.First();
		Assert.NotNull(localIndex.GetSymbol("i"));

		// cursor on the constant inside a case value
		var caseValue = root.FindNodeAtPosition<IdentifierNode>(4, 16);
		Assert.NotNull(caseValue);
		Assert.Equal("max_level", caseValue!.Name);
	}

	[Fact]
	public void Local_References_Include_Interpolated_Usages()
	{
		var (_, _, locals) = Setup();

		// mirror ReferencesHandler: local symbol -> local reference list
		var mainIndex = locals.Values.First(x => x.GetSymbol("localValue") != null);
		var references = mainIndex.GetReferences("localValue").ToList();

		// usages: call arg (line 6), @hp assignment (line 7),
		// inside "lvl {skill_name(localValue)}!" (line 8), inside "{localValue}" (line 9)
		Assert.Contains(references, r => r.FileRange.Start.Line == 8);
		Assert.Contains(references, r => r.FileRange.Start.Line == 9);
		Assert.Equal(4, references.Count);
	}

	// ---------------------------------------------------------------
	// 2.4: constant tables
	// ---------------------------------------------------------------

	private const string TableSource = """
		// bar tier -> outputs
		table smith_tier(key int bar, int sword, int helm)
		    1, 2, 3
		    4, 5, 6

		func main(int bar)
		    int s = smith_tier[bar].sword
		    int h = smith_tier[bar: bar].helm
		    for r in smith_tier
		        int x = r.helm
		    int n = smith_tier.count
		""";

	private static (AstNode Root, GlobalSymbolTable Symbols, System.Collections.Generic.Dictionary<MethodDefinitionNode, LocalIndex> Locals) SetupTables()
	{
		var symbols = new GlobalSymbolTable();
		var types = new GlobalTypeIndex();
		System.Collections.Generic.Dictionary<MethodDefinitionNode, LocalIndex> locals = [];

		var parser = new AstParser("tables.gs", TableSource);
		var root = parser.ParseProgram();
		Assert.Empty(parser.Errors ?? []);
		var fileIndex = new FileIndex();
		var visitor = new IndexVisitor(fileIndex, new VisitorContext(types, symbols, "tables.gs"));
		root.Accept(visitor);
		symbols.AddFile("tables.gs", fileIndex.FileSymbols);
		foreach (var (method, index) in visitor.LocalIndexes)
			locals[method] = index;

		// name resolution classifies the bare 'smith_tier' references as Table
		root.Accept(new NameResolutionVisitor(locals, new VisitorContext(types, symbols, "tables.gs")));
		return (root, symbols, locals);
	}

	[Theory]
	[InlineData(6, 14, "smith_tier")]   // 't' in 't[bar].sword'
	[InlineData(8, 14, "smith_tier")]   // 't' in 'for r in t'
	[InlineData(10, 14, "smith_tier")]  // 't' in 't.count'
	public void Definition_Lookup_Finds_Table_Under_Cursor(int line, int character, string expectedName)
	{
		var (root, symbols, _) = SetupTables();

		var identifier = root.FindNodeAtPosition<IdentifierNode>(line, character);
		Assert.NotNull(identifier);
		Assert.Equal(expectedName, identifier!.Name);
		Assert.Equal(IdentifierType.Table, identifier.Type);

		var symbol = symbols.GetSymbol(identifier.Name);
		Assert.NotNull(symbol);
		Assert.True(symbol!.IsTable);
		Assert.Equal("table smith_tier(key int bar, int sword, int helm)", symbol.Signature);
		Assert.Equal("bar tier -> outputs", symbol.Summary);
		Assert.Equal(3, symbol.Columns!.Count);
		Assert.Equal(2, symbol.Rows!.Count);
	}

	[Theory]
	[InlineData(6, 30, "sword")]   // '.sword' member of a keyed lookup
	[InlineData(7, 25, "bar")]     // 'bar' key selector in '[bar: bar]'
	[InlineData(9, 20, "helm")]    // 'r.helm' on a row cursor
	public void Column_Under_Cursor_Resolves_To_The_Table_Column(int line, int character, string expectedColumn)
	{
		var (root, symbols, locals) = SetupTables();

		// mirror DefinitionHandler / HoverHandler: a Column identifier resolves
		// through its parent expression's target to the owning table symbol
		var (node, parent) = root.FindNodeAndParentAtPosition(line, character);
		var identifier = Assert.IsType<IdentifierNode>(node);
		Assert.Equal(expectedColumn, identifier.Name);
		Assert.Equal(IdentifierType.Column, identifier.Type);

		var localIndex = locals.Values.FirstOrDefault(x => x.FileRange.Contains(line, character));
		var target = parent switch
		{
			MemberExpressionNode m => m.Target,
			IndexExpressionNode i => i.Target,
			_ => null,
		};
		Assert.NotNull(target);
		var table = Symbols.TableAccess.ResolveTable(target!, localIndex, symbols);
		Assert.NotNull(table);
		var column = table!.Columns!.Single(c => c.Name == expectedColumn);
		Assert.Equal(1, column.Range.Start.Line);   // declared in the header line
	}

	[Fact]
	public void Column_Identifiers_Are_Not_Recorded_As_References()
	{
		var (_, _, locals) = SetupTables();
		var mainIndex = locals.Values.Single();

		// the cursor 'r' is a local with the table's row type
		var cursor = mainIndex.GetSymbol("r");
		Assert.NotNull(cursor);
		Assert.Equal("smith_tier", Symbols.TableRowType.TryGetTableName(cursor!.Type));

		// '.sword' / '.helm' / 'bar:' never become references named like a symbol
		Assert.Empty(mainIndex.GetReferences("sword"));
		Assert.Empty(mainIndex.GetReferences("helm"));
	}
}
