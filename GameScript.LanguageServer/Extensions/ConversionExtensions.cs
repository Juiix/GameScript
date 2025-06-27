using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Symbols;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Runtime.InteropServices;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace GameScript.LanguageServer.Extensions
{
	/// <summary>
	/// Helper methods that adapt GameScript-native structures to their LSP equivalents
	/// and provide a reliable way to normalise file paths across platforms.
	/// </summary>
	internal static class ConversionExtensions
	{
		/// <summary>
		/// Converts an engine-side <see cref="FileRange"/> to an LSP <see cref="Range"/>.
		/// </summary>
		public static Range ConvertRange(this FileRange fileRange) =>
			new(
				fileRange.Start.Line, fileRange.Start.Column,
				fileRange.End.Line, fileRange.End.Column);

		/// <summary>
		/// Produces an LSP <see cref="Location"/> for the given AST node.
		/// </summary>
		public static Location GetLocation(this AstNode astNode) => new()
		{
			Uri = DocumentUri.FromFileSystemPath(astNode.FilePath),
			Range = astNode.FileRange.ConvertRange()
		};

		/// <summary>
		/// Produces an LSP <see cref="Location"/> for the symbol’s declaration.
		/// </summary>
		public static Location GetLocation(this SymbolInfo symbol) => new()
		{
			Uri = DocumentUri.FromFileSystemPath(symbol.FilePath),
			Range = symbol.FileRange.ConvertRange()
		};

		/// <summary>
		/// Normalises a raw path/URI into an absolute, canonical file-system path.
		/// Handles URI prefixes (<c>file:///…</c>) and Windows drive-letter quirks.
		/// </summary>
		/// <remarks>
		/// Example transformations:  
		/// <list type="bullet">
		///   <item><description><c>c:\dir\file.gs</c> → <c>C:\dir\file.gs</c></description></item>
		///   <item><description><c>/C:/dir/file.gs</c> → <c>C:\dir\file.gs</c></description></item>
		///   <item><description><c>file:///C:/dir/file.gs</c> → <c>C:\dir\file.gs</c></description></item>
		/// </list>
		/// </remarks>
		public static string NormalizePath(this string raw)
		{
			// 1) If it's a URI, drop the scheme and authority.
			if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.IsFile)
				raw = uri.LocalPath;

			// 2) Convert "/C:/…" (URI form) to "C:/…" on Windows.
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				raw.Length >= 3 && raw[0] == '/' && char.IsLetter(raw[1]) && raw[2] == ':')
			{
				raw = raw[1..];
			}

			// 3) Resolve relative segments and normalise slashes.
			var full = Path.GetFullPath(raw);

			// 4) Capitalise drive letter for consistency.
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				full.Length >= 2 && full[1] == ':')
			{
				full = char.ToUpperInvariant(full[0]) + full[1..];
			}

			return full;
		}

		/// <summary>
		/// Maps an <see cref="IdentifierType"/> to the closest LSP <see cref="SymbolKind"/>.
		/// </summary>
		public static SymbolKind GetSymbolKind(this IdentifierType identifierType) =>
			identifierType switch
			{
				IdentifierType.Local or
				IdentifierType.Context or
				IdentifierType.Constant => SymbolKind.Variable,

				IdentifierType.Func or
				IdentifierType.Label or
				IdentifierType.Trigger or
				IdentifierType.Command => SymbolKind.Function,

				_ => default
			};
	}
}