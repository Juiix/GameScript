using GameScript.Language.Index;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace GameScript.LanguageServer.Handlers
{
	internal sealed class WorkspaceSymbolResolveHandler(
		ISymbolIndex symbols) : IWorkspaceSymbolResolveHandler
	{
		private readonly ISymbolIndex _symbols = symbols;

		public async Task<WorkspaceSymbol> Handle(WorkspaceSymbol request, CancellationToken cancellationToken)
		{
			var symbol = _symbols.GetSymbol(request.Name);
			if (symbol == null)
			{
				return request;
			}

			// TODO pack client-specific data

			return request;
		}

		public void SetCapability(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities)
		{

		}
	}
}
