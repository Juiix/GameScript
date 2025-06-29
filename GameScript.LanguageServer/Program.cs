// See https://aka.ms/new-console-template for more information

using GameScript.Language.Index;
using GameScript.LanguageServer;
using GameScript.LanguageServer.Caches;
using GameScript.LanguageServer.Handlers;
using GameScript.LanguageServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;
using System.Diagnostics;

var flags = args.Where(x => x.StartsWith("--"))
				.Select(x => Enum.TryParse<ProgramFlags>(x.TrimStart('-'), true, out var flag) ? flag : ProgramFlags.None)
				.Aggregate(ProgramFlags.None, (a, v) => a | v);

var server = await LanguageServer.From(options =>
{
	options.WithInput(Console.OpenStandardInput());
	options.WithOutput(Console.OpenStandardOutput());
	options.ConfigureLogging(logging =>
	{
		logging.ClearProviders();
		logging.AddDebug();
		logging.AddLanguageProtocolLogging();
		logging.SetMinimumLevel(LogLevel.Trace);
	});
	options.WithServices(services =>
	{
		services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));

		// caches
		services.AddSingleton<AstCache>();
		services.AddSingleton<OpenDocumentCache>();
		services.AddSingleton<TextCache>();

		// services
		services.AddSingleton<AnalysisService>();
		services.AddSingleton<DiagnosticsService>();
		services.AddSingleton<FileProcessingService>();
		services.AddSingleton<IndexingService>();
		services.AddSingleton<ParsingService>();
		services.AddSingleton<WorkspaceService>();

		// index
		services.AddSingleton<GlobalReferenceTable>();
		services.AddSingleton<GlobalSymbolTable>();
		services.AddSingleton<GlobalTypeIndex>();
		services.AddSingleton<IReferenceIndex, GlobalReferenceTable>(x => x.GetRequiredService<GlobalReferenceTable>());
		services.AddSingleton<ISymbolIndex, GlobalSymbolTable>(x => x.GetRequiredService<GlobalSymbolTable>());
		services.AddSingleton<ITypeIndex, GlobalTypeIndex>(x => x.GetRequiredService<GlobalTypeIndex>());

		// handlers
		services.AddSingleton<CompletionHandler>();
		services.AddSingleton<DefinitionHandler>();
		services.AddSingleton<DidChangeTextDocumentHandler>();
		services.AddSingleton<DidCloseTextDocumentHandler>();
		services.AddSingleton<DidOpenTextDocumentHandler>();
		services.AddSingleton<DidSaveTextDocumentHandler>();
		services.AddSingleton<DocumentHighlightHandler>();
		services.AddSingleton<DocumentSymbolHandler>();
		services.AddSingleton<HoverHandler>();
		services.AddSingleton<PrepareRenameHandler>();
		services.AddSingleton<ReferencesHandler>();
		services.AddSingleton<RenameHandler>();
		services.AddSingleton<SemanticTokensHandler>();
		services.AddSingleton<WorkspaceSymbolResolveHandler>();
		services.AddSingleton<WorkspaceSymbolsHandler>();
	});
	options.OnUnhandledException = e => Console.Error.WriteLine(e);
	options.OnInitialize((server, request, _) =>
	{
		ArgumentNullException.ThrowIfNull(request.RootPath);

		var workspaceService = server.Services.GetRequiredService<WorkspaceService>();
		workspaceService.SetRoot(request.RootPath);

		return Task.CompletedTask;
	});
	options.OnInitialized(async (server, p, r, token) =>
	{
		RegisterCapabilities(server.ServerSettings.Capabilities, flags);
		RegisterCapabilities(r.Capabilities, flags);

		var workspaceService = server.Services.GetRequiredService<WorkspaceService>();
		await workspaceService.InitialScanAsync(token);
	});
	options.OnStarted(async (server, token) =>
	{
		var workspaceService = server.Services.GetRequiredService<WorkspaceService>();
		await workspaceService.SendInitialDiagnosticsAsync(token);

		var fileProcessingService = server.Services.GetRequiredService<FileProcessingService>();
		fileProcessingService.Start();
	});
});

await Task.WhenAny(
	Task.Run(async () =>
	{
		while (true)
		{
			await Task.Delay(1_000);
			if (server.ClientSettings.ProcessId.HasValue && Process.GetProcessById((int)server.ClientSettings.ProcessId.Value).HasExited)
			{
				await Console.Error.WriteLineAsync("Client disappeared, shutting down...");
				server.ForcefulShutdown();
				return;
			}
		}
	}),
	server.WaitForExit
);

// dynamic capability registration wasn't working with VisualStudio :(
static void RegisterCapabilities(ServerCapabilities capabilities, ProgramFlags flags)
{
	bool visualstudio = (flags & ProgramFlags.VisualStudio) == ProgramFlags.VisualStudio;
	if (visualstudio)
	{
		capabilities.CompletionProvider = new CompletionRegistrationOptions.StaticOptions()
		{
			ResolveProvider = false,
			TriggerCharacters = new Container<string>("$", "^", "%", "~", "@")
		};
		capabilities.DefinitionProvider = new BooleanOr<DefinitionRegistrationOptions.StaticOptions>(new DefinitionRegistrationOptions.StaticOptions());
		capabilities.DocumentHighlightProvider = new BooleanOr<DocumentHighlightRegistrationOptions.StaticOptions>(new DocumentHighlightRegistrationOptions.StaticOptions());
		capabilities.TextDocumentSync = new TextDocumentSync(new TextDocumentSyncOptions
		{
			Change = TextDocumentSyncKind.Incremental,
			Save = new SaveOptions
			{
				IncludeText = false
			},
			OpenClose = true
		});
		capabilities.DocumentSymbolProvider = new BooleanOr<DocumentSymbolRegistrationOptions.StaticOptions>(new DocumentSymbolRegistrationOptions.StaticOptions());
		capabilities.HoverProvider = new BooleanOr<HoverRegistrationOptions.StaticOptions>(new HoverRegistrationOptions.StaticOptions());
		capabilities.ReferencesProvider = new BooleanOr<ReferenceRegistrationOptions.StaticOptions>(new ReferenceRegistrationOptions.StaticOptions());
		capabilities.RenameProvider = new BooleanOr<RenameRegistrationOptions.StaticOptions>(new RenameRegistrationOptions.StaticOptions()
		{
			PrepareProvider = true
		});
		capabilities.SemanticTokensProvider = new SemanticTokensRegistrationOptions.StaticOptions
		{
			Legend = SemanticTokensHandler.Legend,
			Full = true,
			Range = true
		};
		capabilities.WorkspaceSymbolProvider = new BooleanOr<WorkspaceSymbolRegistrationOptions.StaticOptions>(new WorkspaceSymbolRegistrationOptions.StaticOptions
		{
			ResolveProvider = true
		});
	}
}