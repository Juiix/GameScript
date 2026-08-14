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
}
