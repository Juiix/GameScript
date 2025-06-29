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
	AstCache astCache,
	ISymbolIndex symbols,
	IReferenceIndex references) : IRenameHandler
{
	private readonly AstCache _astCache = astCache;
	private readonly ISymbolIndex _symbols = symbols;
	private readonly IReferenceIndex _references = references;

	public async Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
	{
		var filePath = request.TextDocument.Uri.Path.NormalizePath();
		if (!_astCache.TryGetRoot(filePath, out var rootData))
		{
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

		var localIndex = rootData.GetLocalIndex(request.Position.Line, request.Position.Character);
		var symbol = localIndex?.GetSymbol(symbolName) ?? _symbols.GetSymbol(symbolName);
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

		var references = _references.GetReferences(symbolName);
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

	private static TextEdit GetEdit(SymbolInfo symbol, string newName)
	{
		var range = symbol.FileRange;
		if ((symbol.IdentifierType & IdentifierType.Variable) != IdentifierType.Unknown)
		{
			var newStart = range.Start.AddColumn(1); // ignore prefix
			range = new FileRange(newStart, range.End);
		}

		return new TextEdit
		{
			NewText = newName,
			Range = range.ConvertRange()
		};
	}

	private static TextEdit GetEdit(ReferenceInfo reference, string newName)
	{
		var range = reference.FileRange;
		var newStart = range.Start.AddColumn(1); // ignore prefix
		range = new FileRange(newStart, range.End);

		return new TextEdit
		{
			NewText = newName,
			Range = range.ConvertRange()
		};
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="name"/>
	/// • is non-empty  
	/// • begins with a letter (A–Z / a–z) or '_'  
	/// • thereafter contains only letters, digits, or '_'  
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