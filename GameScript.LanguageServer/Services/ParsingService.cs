using GameScript.Language.Ast;
using GameScript.Language.File;
using GameScript.Language.Index;
using GameScript.LanguageServer.Parsing;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace GameScript.LanguageServer.Services;

/// <summary>
/// Parses a GameScript source file (or raw text) into an AST and returns
/// accompanying diagnostics, comments, and line-offset metadata.
/// </summary>
internal sealed class ParsingService
{
	private readonly GlobalSymbolTable _symbols;
	private readonly GlobalReferenceTable _references;
	private readonly GlobalTypeIndex _types;
	private readonly ILogger<ParsingService> _logger;

	/// <summary>
	/// Creates a new <see cref="ParsingService"/>.
	/// </summary>
	public ParsingService(
		GlobalSymbolTable symbols,
		GlobalReferenceTable references,
		GlobalTypeIndex types,
		ILogger<ParsingService> logger)
	{
		_symbols = symbols;
		_references = references;
		_types = types;
		_logger = logger;
	}

	/// <summary>
	/// Parses the specified file (or provided <paramref name="fullText"/> override)
	/// and produces a <see cref="ParseResult"/>.
	/// </summary>
	/// <param name="filePath">Absolute path of the file being parsed.</param>
	/// <param name="fullText">
	/// Optional raw text to parse instead of reading from disk; useful for unsaved buffers.
	/// </param>
	/// <returns>
	/// A populated <see cref="ParseResult"/>, or <c>null</c> if the file cannot be read
	/// or a fatal exception occurs (already logged).
	/// </returns>
	public ParseResult? Parse(string filePath, ReadOnlySpan<char> source, int? fileVersion)
	{
		char[]? chars = null;
		try
		{
			if (source.IsEmpty)
			{
				if (!LoadTemporaryFile(filePath, out chars, out var length))
				{
					return null;
				}

				source = chars.AsSpan(0, length);
			}

			var errors = new List<FileError>();
			var comments = new List<CommentNode>();
			AstNode ast;
			IReadOnlyList<int> lineOffsets;

			(ast, lineOffsets) = ParseAst(filePath, source, errors, comments);

			return new ParseResult(ast, errors, comments, lineOffsets, fileVersion);
		}
		catch (Exception e)
		{
			_logger.LogError(e, "An error occurred during parsing");
			return null;
		}
		finally
		{
			if (chars != null)
			{
				ArrayPool<char>.Shared.Return(chars);
			}
		}
	}

	/// <summary>
	/// Core parsing routine that builds the AST and collects diagnostics.
	/// Uses a rented buffer for efficient streaming of large files.
	/// </summary>
	private static (AstNode Ast, IReadOnlyList<int> LineOffsets) ParseAst(
		string filePath,
		ReadOnlySpan<char> source,
		List<FileError> errors,
		List<CommentNode> comments)
	{
		var parser = new AstParser(filePath, source);
		var extension = Path.GetExtension(filePath.AsSpan());
		AstNode node = extension switch
		{
			".context" => parser.ParseContexts(),
			".const" => parser.ParseConstants(),
			".gs" => parser.ParseProgram(),
			_ => throw new InvalidOperationException($"Unsupported extension: {extension}")
		};

		if (parser.Errors is { Count: > 0 })
			errors.AddRange(parser.Errors);

		if (parser.Comments is { Count: > 0 })
			comments.AddRange(parser.Comments);

		return (node, parser.LineOffsets);
	}

	/// <summary>
	/// Reads <paramref name="filePath"/> into a pooled <see cref="char"/> buffer
	/// that can be sliced without additional allocations.
	/// </summary>
	/// <param name="filePath">
	/// Fully-qualified path of the file to load. The file must exist and be smaller
	/// than <see cref="int.MaxValue"/> bytes.
	/// </param>
	/// <param name="chars">
	/// When the call succeeds, a rented array from <see cref="ArrayPool{T}.Shared"/>
	/// containing the file’s text. On failure this is set to <c>null</c>.
	/// The caller <b>must</b> return the array with
	/// <c>ArrayPool&lt;char&gt;.Shared.Return(chars)</c> when finished.
	/// </param>
	/// <param name="length">
	/// Number of valid characters read into <paramref name="chars"/>.
	/// </param>
	/// <returns>
	/// <c>true</c> if the file was loaded successfully (even if it is empty);
	/// <c>false</c> if the file does not exist, is too large, or an I/O error
	/// occurs. Errors are logged internally.
	/// </returns>
	private bool LoadTemporaryFile(string filePath, [MaybeNullWhen(false)] out char[] chars, out int length)
	{
		chars = null;
		length = 0;

		try
		{
			if (!File.Exists(filePath))
			{
				_logger.LogWarning("File not found: {Path}", filePath);
				return false;
			}

			long byteLength = new FileInfo(filePath).Length;
			if (byteLength == 0)
				return false;                 // empty file -> empty span

			if (byteLength > int.MaxValue)
			{
				_logger.LogError("File too large to parse (>2 GB): {Path}", filePath);
				return false;
			}

			// Rent a buffer exactly the file size (char count may be smaller for UTF-8,
			// but StreamReader does the right thing and stops when it hits EOF).
			chars = ArrayPool<char>.Shared.Rent((int)byteLength);

			using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
			length = sr.Read(chars, 0, chars.Length);

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unable to read file {Path}", filePath);
			if (chars != null)                             // return on failure
				ArrayPool<char>.Shared.Return(chars);
			chars = null;
			length = 0;
			return false;
		}
	}
}
