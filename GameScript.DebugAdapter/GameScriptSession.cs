using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameScript.Bytecode;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;

namespace GameScript.DebugAdapter;

internal sealed class GameScriptSession(
    ScriptDebugHost host,
    BreakpointIndex breakpointIndex,
    IDebugAdapterServerFacade server)
    : IAttachHandler,
      IDisconnectHandler,
      IConfigurationDoneHandler,
      ISetBreakpointsHandler,
      IThreadsHandler,
      IStackTraceHandler,
      IScopesHandler,
      IVariablesHandler,
      IContinueHandler,
      INextHandler,
      IStepInHandler,
      IStepOutHandler,
      IPauseHandler
{
    // Built at attach time for O(1) stack-trace line lookup
    private Dictionary<BytecodeMethod, (int index, BytecodeMethodMetadata meta)>? _methodMap;

    /// <summary>
    /// Called from the server's OnInitialized callback to wire up the pause notification.
    /// </summary>
    internal void SetupPausedCallback()
    {
        host.ScriptPaused = (threadId, reason) =>
        {
            var reasonStr = reason switch
            {
                PauseReason.Breakpoint => StoppedEventReason.Breakpoint,
                PauseReason.Step       => StoppedEventReason.Step,
                _                      => StoppedEventReason.Pause,
            };
            server.SendNotification(new StoppedEvent { ThreadId = threadId, Reason = reasonStr });
        };
    }

    public Task<AttachResponse> Handle(AttachRequestArguments request, CancellationToken ct)
    {
        if (host.Program != null && host.Metadata != null)
            _methodMap = BuildMethodMap(host.Program, host.Metadata);
        return Task.FromResult(new AttachResponse());
    }

    public Task<DisconnectResponse> Handle(DisconnectArguments request, CancellationToken ct)
    {
        host.DisconnectAll();
        breakpointIndex.Clear();
        _methodMap = null;
        return Task.FromResult(new DisconnectResponse());
    }

    public Task<ConfigurationDoneResponse> Handle(ConfigurationDoneArguments request, CancellationToken ct) =>
        Task.FromResult(new ConfigurationDoneResponse());

    public Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken ct)
    {
        var source = request.Source.Path ?? string.Empty;
        var lines = request.Breakpoints?.Select(b => b.Line).ToArray() ?? [];

        int[] verified = [];
        if (host.Program != null && host.Metadata != null)
            verified = breakpointIndex.SetBreakpoints(source, lines, host.Program, host.Metadata);

        var verifiedSet = new HashSet<int>(verified);
        var responseBreakpoints = lines.Select(line => new Breakpoint
        {
            Verified = verifiedSet.Contains(line),
            Line = line,
            Source = request.Source,
        }).ToArray();

        return Task.FromResult(new SetBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(responseBreakpoints),
        });
    }

    public Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken ct)
    {
        var threads = host.GetEntries()
            .Select(e => new OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread
            {
                Id = e.ThreadId,
                Name = e.Name,
            })
            .ToArray();
        return Task.FromResult(new ThreadsResponse
        {
            Threads = new Container<OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread>(threads),
        });
    }

    public Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken ct)
    {
        if (!host.TryGetEntry((int)request.ThreadId, out var entry))
            return Task.FromResult(new StackTraceResponse());

        var frameBuffer = new FrameView[64];
        var count = entry.State.CopyFrames(frameBuffer);

        var frames = new List<StackFrame>(count);
        for (int i = count - 1; i >= 0; i--)
        {
            var fv = frameBuffer[i];
            var (line, file) = GetLineAndFile(fv);
            frames.Add(new StackFrame
            {
                Id = (int)request.ThreadId * 10000 + i,
                Name = fv.Method.Name,
                Line = line >= 0 ? line : 0,
                Source = file != null ? new Source { Path = file } : null,
            });
        }

        return Task.FromResult(new StackTraceResponse
        {
            StackFrames = new Container<StackFrame>(frames),
            TotalFrames = count,
        });
    }

    public Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken ct)
    {
        var scope = new Scope
        {
            Name = "Locals",
            VariablesReference = request.FrameId,
            PresentationHint = "locals",
        };
        return Task.FromResult(new ScopesResponse { Scopes = new Container<Scope>(scope) });
    }

    public Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken ct)
    {
        var frameId = (int)request.VariablesReference;
        var threadId = frameId / 10000;
        var frameIndex = frameId % 10000;

        if (!host.TryGetEntry(threadId, out var entry))
            return Task.FromResult(new VariablesResponse());

        var frameBuffer = new FrameView[64];
        var count = entry.State.CopyFrames(frameBuffer);

        if (frameIndex >= count)
            return Task.FromResult(new VariablesResponse());

        var frame = frameBuffer[frameIndex];
        var localCount = frame.Method.ParamCount + frame.Method.LocalsCount;
        var variables = new List<Variable>(localCount);

        for (int i = 0; i < localCount; i++)
        {
            var value = entry.State.GetLocalInFrame(frameIndex, i);
            variables.Add(new Variable
            {
                Name = i < frame.Method.ParamCount
                    ? $"param_{i}"
                    : $"local_{i - frame.Method.ParamCount}",
                Value = FormatValue(value),
                VariablesReference = 0,
            });
        }

        return Task.FromResult(new VariablesResponse { Variables = new Container<Variable>(variables) });
    }

    public Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken ct)
    {
        if (host.TryGetEntry((int)request.ThreadId, out var entry))
            entry.Token.Resume();
        return Task.FromResult(new ContinueResponse { AllThreadsContinued = false });
    }

    public Task<NextResponse> Handle(NextArguments request, CancellationToken ct)
    {
        if (host.TryGetEntry((int)request.ThreadId, out var entry))
            entry.Token.Step(new StepContext(StepKind.Over, entry.State.FrameDepth, GetCurrentLine(entry.State)));
        return Task.FromResult(new NextResponse());
    }

    public Task<StepInResponse> Handle(StepInArguments request, CancellationToken ct)
    {
        if (host.TryGetEntry((int)request.ThreadId, out var entry))
            entry.Token.Step(new StepContext(StepKind.In, entry.State.FrameDepth, GetCurrentLine(entry.State)));
        return Task.FromResult(new StepInResponse());
    }

    public Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken ct)
    {
        if (host.TryGetEntry((int)request.ThreadId, out var entry))
            entry.Token.Step(new StepContext(StepKind.Out, entry.State.FrameDepth));
        return Task.FromResult(new StepOutResponse());
    }

    public Task<PauseResponse> Handle(PauseArguments request, CancellationToken ct)
    {
        if (host.TryGetEntry((int)request.ThreadId, out var entry))
            entry.Token.RequestPause(PauseReason.Pause);
        return Task.FromResult(new PauseResponse());
    }

    private int GetCurrentLine(IScriptState state)
    {
        var frames = new FrameView[1];
        return state.CopyFrames(frames) > 0 ? GetLineAndFile(frames[0]).line : -1;
    }

    private (int line, string? file) GetLineAndFile(FrameView frame)
    {
        if (_methodMap == null || !_methodMap.TryGetValue(frame.Method, out var entry))
            return (-1, null);

        var lineNumbers = entry.meta.LineNumbers;
        if ((uint)frame.Ip >= (uint)lineNumbers.Length)
            return (-1, entry.meta.FilePath);

        return (lineNumbers[frame.Ip], entry.meta.FilePath);
    }

    private static string FormatValue(Value value) => value.Type switch
    {
        GameScript.Bytecode.ValueType.Null   => "null",
        GameScript.Bytecode.ValueType.Int    => value.Int.ToString(),
        GameScript.Bytecode.ValueType.Bool   => value.Bool ? "true" : "false",
        GameScript.Bytecode.ValueType.String => $"\"{value.String}\"",
        _                                    => value.ToString() ?? "?",
    };

    private static Dictionary<BytecodeMethod, (int, BytecodeMethodMetadata)> BuildMethodMap(
        BytecodeProgram program, BytecodeProgramMetadata metadata)
    {
        var map = new Dictionary<BytecodeMethod, (int, BytecodeMethodMetadata)>(program.Methods.Length);
        for (int i = 0; i < program.Methods.Length; i++)
            map[program.Methods[i]] = (i, metadata.MethodMetadata[i]);
        return map;
    }
}
