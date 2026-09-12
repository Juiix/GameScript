using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.Language.Symbols;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Handlers;

internal sealed class RenameHandler(
	OpenDocumentCache openDocumentCache,
	AstCache astCache,
	Services.ProjectRegistry projects) : IRenameHandler
{
	private readonly OpenDocumentCache _openDocumentCache = openDocumentCache;
	private readonly AstCache _astCache = astCache;
	private readonly Services.ProjectRegistry _projects = projects;

	public async Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.GetNormalizedFilePath();
		if (!_openDocumentCache.TryGet(filePath, out var text, out var fileVersion) ||
			!_astCache.TryGetRoot(filePath, out var rootData) ||
			rootData.Parse.FileVersion != fileVersion)
		{
			ExceptionHelper.ThrowFileVersionNotFound();
			return null;
		}


		var astNode = rootData.Root.FindNodeAtPosition(request.Position.Line, request.Position.Character);
		if (astNode == null)
		{
			return null;
		}

		var symbolName = astNode.GetSymbolName();
		if (symbolName == null)
		{
			return null;
		}

		var project = _projects.GetProject(filePath);
		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		// a named type is never local (a local may shadow its name) and is looked up by kind
		var isType = astNode.IsTypeReference();
		var localSymbol = isType ? null : localIndex?.GetSymbol(symbolName);
		var symbol = localSymbol ?? (isType ? project.Symbols.FindTypeSymbol(symbolName) : project.Symbols.GetSymbol(symbolName));
		if (symbol == null)
		{
			return null;
		}

		// validate new symbol name
		var newName = request.NewName;
		if (string.IsNullOrWhiteSpace(newName) ||
			newName.Equals(symbol.Name) ||
			!IsValidIdentifier(newName))
		{
			return null;
		}
		
		var changes = new Dictionary<string, List<TextEdit>>
		{
			{ symbol.FilePath, [ GetEdit(symbol, newName) ] }
		};

		var references = localSymbol != null ?
			localIndex?.GetReferences(symbolName) :
			project.References.GetReferences(symbolName);
		if (references != null)
		{
			foreach (var reference in references)
			{
				if (!changes.TryGetValue(reference.FilePath, out var fileList))
				{
					fileList = [];
					changes.Add(reference.FilePath, fileList);
				}

				fileList.Add(GetEdit(reference, newName));
			}
		}

		return new WorkspaceEdit
		{
			Changes = changes.ToDictionary(x => DocumentUri.FromFileSystemPath(x.Key), x => (IEnumerable<TextEdit>)x.Value)
		};
	}

	public RenameRegistrationOptions GetRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript"),
			PrepareProvider = true
		};
	}

	private static TextEdit GetEdit(SymbolInfo symbol, string newName) =>
		GetEdit(symbol.FileRange, symbol.Name, newName);

	private static TextEdit GetEdit(ReferenceInfo reference, string newName) =>
		GetEdit(reference.FileRange, reference.Name, newName);

	// The range of a '^const' / '@ctx' / '.@ctx' / '.cmd' occurrence includes its
	// mark(s) while the symbol name does not; a bare occurrence (func, local, table,
	// type) has no prefix at all. Whatever the prefix, it is exactly the range's
	// surplus over the name, and it is kept.
	private static TextEdit GetEdit(FileRange range, string name, string newName)
	{
		var prefixLength = Math.Max(0, range.End.Position - range.Start.Position - name.Length);
		var editRange = new FileRange(range.Start.AddColumn(prefixLength), range.End);

		return new TextEdit
		{
			NewText = newName,
			Range = editRange.ConvertRange()
		};
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="name"/>
	/// � is non-empty  
	/// � begins with a letter (A�Z / a�z) or '_'  
	/// � thereafter contains only letters, digits, or '_'  
	/// </summary>
	private static bool IsValidIdentifier(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return false;

		// first char: letter or '_'
		char c0 = name[0];
		if (!(char.IsLetter(c0) || c0 == '_'))
			return false;

		// remaining chars: letter, digit, or '_'
		for (int i = 1; i < name.Length; i++)
		{
			char c = name[i];
			if (!(char.IsLetterOrDigit(c) || c == '_'))
				return false;
		}
		return true;
	}
}