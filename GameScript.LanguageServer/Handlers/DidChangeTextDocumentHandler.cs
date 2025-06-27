using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Extensions;
using GameScript.LanguageServer.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Text;

namespace GameScript.LanguageServer.Handlers;

internal sealed class DidChangeTextDocumentHandler(
	TextCache textCache,
	FileProcessingService fileProcessingService) : IDidChangeTextDocumentHandler
{
	private readonly TextCache _textCache = textCache;
	private readonly FileProcessingService _fileProcessingService = fileProcessingService;

	public Task<Unit> Handle(DidChangeTextDocumentParams req, CancellationToken ct)
	{
		var filePath = req.TextDocument.Uri.Path.NormalizePath();
		if (!_textCache.TryGetText(filePath, out var text))
		{
			return Unit.Task;
		}

		foreach (var change in req.ContentChanges)
		{
			// -- 1. Full-document replacement -------------------------------
			if (change.Range == null)
			{
				text = change.Text;
				continue;
			}

			// -- 2. Incremental range edit ----------------------------------
			// Convert (line,character) pairs to absolute byte offsets
			// in the *current* version of the buffer.
			//
			// -  Lines are 0-based.
			// -  Characters are UTF-16 code units (string indexing).
			//
			int GetOffset(Position pos)
			{
				// Walk the buffer until 'pos.Line' newlines have been seen.
				var offset = 0;
				for (int line = 0; line < pos.Line; line++)
				{
					offset = text.IndexOf('\n', offset) + 1;   // move past '\n'
					if (offset == 0)                           // line out of range
						return text.Length;
				}
				return offset + pos.Character;
			}

			var start = GetOffset(change.Range.Start);
			var end = GetOffset(change.Range.End);

			// Sanity-clip
			start = Math.Clamp(start, 0, text.Length);
			end = Math.Clamp(end, start, text.Length);

			var sb = new StringBuilder(text.Length + change.Text.Length - (end - start));
			sb.Append(text, 0, start)        // prefix
			  .Append(change.Text)           // replacement
			  .Append(text, end, text.Length - end); // suffix

			text = sb.ToString();
		}

		_textCache.Update(filePath, text);
		_fileProcessingService.Queue(filePath);
		return Unit.Task;
	}

	public TextDocumentChangeRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
	{
		return new()
		{
			DocumentSelector = TextDocumentSelector.ForLanguage("gamescript")
		};
	}
}