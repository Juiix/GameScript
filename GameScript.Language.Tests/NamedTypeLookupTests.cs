using System.Linq;
using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.Language.Visitors;
using GameScript.LanguageServer.Tools;
using Xunit;

namespace GameScript.Language.Tests;

/// <summary>
/// Simulates the LSP flows (FindNodeAtPosition + symbol lookup) over named types:
/// type positions, cast callees, type-name references, shadowing locals, and the
/// per-project type index the handlers resolve through.
/// </summary>
public class NamedTypeLookupTests
{
	private const string Source = """
		// An item id
		type item : int
		item ^sword = 1

		func f(item i) returns item
		    return item(i + 1)

		func g()
		    int item = 2
		    int other = item + 1
		""";

	private static (ProgramNode Root, GlobalSymbolTable Symbols, FileIndex FileIndex, System.Collections.Generic.Dictionary<MethodDefinitionNode, LocalIndex> Locals) Setup()
	{
		var symbols = new GlobalSymbolTable();
		var types = new GlobalTypeIndex();
		var parser = new AstParser("test.gs", Source);
		var root = parser.ParseProgram();
		Assert.Empty(parser.Errors ?? []);

		var fileIndex = new FileIndex();
		var context = new VisitorContext(types, symbols, "test.gs");
		var indexVisitor = new IndexVisitor(fileIndex, context);
		root.Accept(indexVisitor);
		symbols.AddFile("test.gs", fileIndex.FileSymbols);

		// the handlers see the AST after name resolution (cast callees are typed there)
		root.Accept(new NameResolutionVisitor(indexVisitor.LocalIndexes, context));
		return (root, symbols, fileIndex, indexVisitor.LocalIndexes);
	}

	[Theory]
	[InlineData(2, 1)]    // 'item' in 'item ^sword = 1'
	[InlineData(4, 8)]    // 'item' in 'func f(item i)'
	[InlineData(4, 24)]   // 'item' in 'returns item'
	public void Type_Position_Is_A_TypeNode_Resolving_To_The_Declaration(int line, int character)
	{
		var (root, symbols, _, _) = Setup();

		var typeNode = root.FindNodeAtPosition<TypeNode>(line, character);
		Assert.NotNull(typeNode);
		Assert.Equal("item", typeNode!.Name);

		var declaration = symbols.GetSymbols(typeNode.Name).Single(s => s.IdentifierType == IdentifierType.Type);
		Assert.Equal("type item : int", declaration.Signature);
		Assert.Equal("An item id", declaration.Summary);
		Assert.Equal(1, declaration.FileRange.Start.Line);
	}

	[Fact]
	public void Cast_Callee_Resolves_To_The_Type()
	{
		var (root, _, _, _) = Setup();

		var callee = root.FindNodeAtPosition<IdentifierNode>(5, 12);
		Assert.NotNull(callee);
		Assert.Equal("item", callee!.Name);
		Assert.Equal(IdentifierType.Type, callee.Type);
	}

	[Fact]
	public void Every_Use_Of_A_Named_Type_Is_A_Reference_To_Its_Declaration()
	{
		var (_, _, fileIndex, _) = Setup();

		var lines = fileIndex.GetReferences("item").Select(r => r.FileRange.Start.Line).OrderBy(x => x).ToArray();
		// constant type (2), parameter type + return type (4), cast callee (5); the
		// shadowing local's uses in g() are local references, not type references
		Assert.Equal(new[] { 2, 4, 4, 5 }, lines);
		Assert.Empty(fileIndex.GetReferences("int"));
	}

	[Fact]
	public void Local_Shadowing_A_Type_Name_Stays_Local()
	{
		var (root, symbols, _, locals) = Setup();

		var use = root.FindNodeAtPosition<IdentifierNode>(9, 17);
		Assert.NotNull(use);
		Assert.Equal("item", use!.Name);
		Assert.Equal(IdentifierType.Local, use.Type);

		var localIndex = locals.Values.Single(x => x.FileRange.Contains(9, 17));
		Assert.NotNull(localIndex.GetSymbol("item"));

		// while a type-position lookup of the same name still finds the type
		Assert.NotNull(symbols.GetSymbols("item").SingleOrDefault(s => s.IdentifierType == IdentifierType.Type));
	}

	[Fact]
	public void Typed_Constant_Symbol_Resolves_Its_Root_Through_The_Project_Type_Index()
	{
		var (_, symbols, _, _) = Setup();
		var types = new ProjectTypeIndex(new GlobalTypeIndex(), symbols);

		var sword = symbols.GetSymbols("sword").Single();
		Assert.Equal(TypeKind.Named, sword.Type!.Kind);
		var resolved = types.Resolve(sword.Type);
		Assert.NotNull(resolved);
		Assert.Equal("int", resolved!.Underlying!.Name);
		Assert.Equal(TypeKind.Int, resolved.RootKind);
	}

	[Fact]
	public void Named_Types_Are_Per_Symbol_Table()
	{
		var primitives = new GlobalTypeIndex();
		var server = Index("server/types.gs", "type item : int");
		var client = Index("client/types.gs", "type item : string");

		Assert.Equal("int", new ProjectTypeIndex(primitives, server).GetType("item")!.Underlying!.Name);
		Assert.Equal("string", new ProjectTypeIndex(primitives, client).GetType("item")!.Underlying!.Name);
		Assert.Same(primitives.GetType("int"), new ProjectTypeIndex(primitives, server).GetType("int"));

		static GlobalSymbolTable Index(string path, string source)
		{
			var symbols = new GlobalSymbolTable();
			var parser = new AstParser(path, source);
			var root = parser.ParseProgram();
			var fileIndex = new FileIndex();
			root.Accept(new IndexVisitor(fileIndex, new VisitorContext(new GlobalTypeIndex(), symbols, path)));
			symbols.AddFile(path, fileIndex.FileSymbols);
			return symbols;
		}
	}

	[Theory]
	[InlineData("scripts/core.gs", true)]
	[InlineData("gen/item.const", false)]
	[InlineData("gen/varp.context", false)]
	[InlineData("data/rocks.obj", false)]
	public void Only_Gs_Files_Are_GameScript_Sources(string path, bool expected)
	{
		Assert.Equal(expected, ExtensionFilter.IsGameScript(path));
	}
}
