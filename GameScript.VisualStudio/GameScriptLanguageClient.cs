using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace GameScript.VisualStudio
{
	[ContentType("gamescript")]
	[Export(typeof(ILanguageClient))]
	internal sealed class GameScriptLanguageClient : ILanguageClient, IDisposable
	{
		private Process _process = null;
		
		public string Name => "GameScript Language Client";
		public IEnumerable<string> ConfigurationSections => null;
		public object InitializationOptions => null;
		public IEnumerable<string> FilesToWatch => null;
		public bool ShowNotificationOnInitializeFailed => true;

		public event AsyncEventHandler<EventArgs> StartAsync;
		public event AsyncEventHandler<EventArgs> StopAsync;

		public async Task<Connection> ActivateAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					/*
					var namedPipe = new NamedPipeClientStream(
						pipeName: "gamescript",
						direction: PipeDirection.InOut,
						serverName: ".",
						options: PipeOptions.Asynchronous);

					await namedPipe.ConnectAsync(token);
					return new Connection(namedPipe, namedPipe);
					*/

					await Task.Yield();

					// Adjust path to match your server location
					var exePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Server", "win-x64", "GameScript.LanguageServer.exe");
					var process = new Process
					{
						StartInfo = new ProcessStartInfo
						{
							FileName = exePath,
							Arguments = "",
							RedirectStandardInput = true,
							RedirectStandardOutput = true,
							UseShellExecute = false,
							CreateNoWindow = true
						}
					};

					if (!process.Start())
					{
						return null;
					}

					_process = process;
					var connection = new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
					return connection;
				}
				catch (Exception e)
				{
					await Task.Delay(5000);
				}
			}

			throw new OperationCanceledException();
		}

		public void Dispose()
		{
			if (_process?.HasExited == false)
			{
				// Ask for graceful exit first
				try { _process.StandardInput.Write("\n"); } catch { }
				if (!_process.WaitForExit(2000))
				{
					_process.Kill();
				}
			}
		}

		public async Task OnLoadedAsync()
		{
			await StartAsync.InvokeAsync(this, EventArgs.Empty);
		}
		public Task OnServerInitializedAsync() => Task.CompletedTask;
		public Task<InitializationFailureContext> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
		{
			var context = new InitializationFailureContext
			{
				FailureMessage = "Language server failed to initialize"
			};
			return Task<InitializationFailureContext>.FromResult(context);
		}
	}
}
