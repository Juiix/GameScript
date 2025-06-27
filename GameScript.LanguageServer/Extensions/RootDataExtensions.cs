using GameScript.Language.Ast;
using GameScript.Language.Index;
using GameScript.LanguageServer.Parsing;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace GameScript.LanguageServer.Extensions
{
	/// <summary>
	/// Convenience extensions that operate on cached <see cref="RootFileData"/>
	/// and assorted AST nodes for quick look-ups during LSP requests.
	/// </summary>
	internal static class RootDataExtensions
	{
		/// <summary>
		/// Finds the innermost <see cref="LocalIndex"/> that contains the given
		/// zero-based line/column position.
		/// </summary>
		/// <param name="rootData">The file-level cache entry.</param>
		/// <param name="line">Zero-based line number.</param>
		/// <param name="column">Zero-based character offset within the line.</param>
		/// <returns>The matching <see cref="LocalIndex"/>, or <c>null</c> if none.</returns>
		public static LocalIndex? GetLocalIndex(this RootFileData rootData, int line, int column)
		{
			foreach (var localIndex in rootData.Index.LocalIndexes.Values)
			{
				if (localIndex.FileRange.Contains(line, column))
					return localIndex;
			}
			return null;
		}

		/// <summary>
		/// Converts a (line, column) pair into a single character offset within
		/// the file’s text buffer.
		/// </summary>
		/// <param name="rootData">The file-level cache entry.</param>
		/// <param name="line">Zero-based line number.</param>
		/// <param name="column">Zero-based character offset within the line.</param>
		/// <returns>The absolute offset, or –1 if the position is invalid.</returns>
		public static int GetOffset(this RootFileData rootData, int line, int column)
		{
			if (line < 0 || line >= rootData.Parse.LineOffsets.Count) return -1;
			return rootData.Parse.LineOffsets[line] + column;
		}

		/// <summary>
		/// Walks backwards from <paramref name="position"/> to capture the
		/// identifier prefix (letters, digits, underscore) that precedes the
		/// caret. Used for completion filtering.
		/// </summary>
		/// <param name="rootData">The file-level cache entry.</param>
		/// <param name="text">Full text of the document.</param>
		/// <param name="position">Caret position supplied by the LSP client.</param>
		/// <returns>The identifier prefix, or an empty string if none.</returns>
		public static string GetPrefix(this RootFileData rootData, string text, Position position, out IdentifierType identifierType)
		{
			var offset = rootData.GetOffset(position.Line, position.Character);
			if (offset < 0)
			{
				identifierType = IdentifierType.Unknown;
				return string.Empty;
			}

			var i = offset - 1;
			while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
				i--;

			identifierType = i >= 0 ? GetIdentifierType(text[i]) : IdentifierType.Unknown;
			return text[(i + 1)..offset];
		}

		/// <summary>
		/// Extracts the declared symbol name from common AST node kinds.
		/// Returns <c>null</c> for nodes that do not represent a named symbol.
		/// </summary>
		public static string? GetSymbolName(this AstNode astNode) => astNode switch
		{
			MethodDefinitionNode m => m.Name.Name,
			IdentifierNode i => i.Name,
			IdentifierDeclarationNode d => d.Name,
			_ => null
		};

		private static IdentifierType GetIdentifierType(char prefix)
		{
			return prefix switch
			{
				'$' => IdentifierType.Local,
				'^' => IdentifierType.Constant,
				'%' => IdentifierType.Context,
				'~' => IdentifierType.Func,
				'@' => IdentifierType.Label,
				_ => IdentifierType.Unknown
			};
		}
	}
}
