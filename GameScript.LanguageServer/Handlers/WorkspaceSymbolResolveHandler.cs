using GameScript.Language.Index;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace GameScript.LanguageServer.Handlers
{
	internal sealed class WorkspaceSymbolResolveHandler(
		Services.ProjectRegistry projects) : IWorkspaceSymbolResolveHandler
	{
		private readonly Services.ProjectRegistry _projects = projects;

		public async Task<WorkspaceSymbol> Handle(WorkspaceSymbol request, CancellationToken cancellationToken)
		{
			var symbol = _projects.Projects
				.Select(p => p.Symbols.GetSymbol(request.Name))
				.FirstOrDefault(x => x != null);
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
