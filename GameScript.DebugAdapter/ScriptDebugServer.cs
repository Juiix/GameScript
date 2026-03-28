using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace GameScript.DebugAdapter;

/// <summary>
/// TCP server that speaks the Debug Adapter Protocol.
/// Embed in your game process and call <see cref="StartAsync"/> to enable debugging.
/// VS Code connects to it via a launch.json with <c>"request": "attach"</c>.
/// </summary>
public sealed class ScriptDebugServer(ScriptDebugHost host, BreakpointIndex breakpointIndex, int port = 4711)
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken externalCt = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();

        _ = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var sessionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var dapServer = await DebugAdapterServer.From(options =>
            {
                options.WithInput(stream).WithOutput(stream);
                options.WithUnhandledExceptionHandler(_ => sessionDone.TrySetResult());
                options.WithServices(services =>
                {
                    services.AddSingleton(host);
                    services.AddSingleton(breakpointIndex);
                    services.AddSingleton<GameScriptSession>();
                });
                options.AddHandler(typeof(GameScriptSession));
                options.OnInitialized((s, _, _, _) =>
                {
                    s.SendNotification(new InitializedEvent());
                    return Task.CompletedTask;
                });
            }, ct);

            // Wire pause callback after server is fully initialized
            var session = ((IServiceProvider)dapServer).GetService(typeof(GameScriptSession)) as GameScriptSession;
            session?.SetupPausedCallback();

            using var reg = ct.Register(() => sessionDone.TrySetResult());
            await sessionDone.Task;
        }
        catch (Exception) { /* client disconnected or server error */ }
        finally
        {
            await stream.DisposeAsync();
            client.Dispose();
        }
    }
}
