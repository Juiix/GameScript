using System;
using GameScript.Bytecode;

namespace GameScript.DebugAdapter;

/// <summary>
/// Drop-in replacement for <see cref="ScriptRunner{TContext}"/> that adds breakpoint
/// checking and step execution. Does not modify <see cref="ScriptState{TContext}.Execution"/>.
/// </summary>
public sealed class DebugScriptRunner<TContext>(
    ScriptRunner<TContext> runner,
    ScriptDebugToken token,
    BreakpointIndex breakpointIndex,
    ScriptDebugHost host,
    int threadId)
    : IScriptRunner<TContext>
    where TContext : IScriptContext
{
    public ScriptExecution Run(ScriptState<TContext> state)
    {
        while (state.Execution == ScriptExecution.Running)
        {
            state.Next();

            var frame = state.CurrentFrameView;
            var (line, file) = GetLineAndFile(frame);

            var shouldPause = token.CheckAndPause(state.FrameDepth, line, out var reason);
            if (!shouldPause && line >= 0 && file != null && breakpointIndex.IsBreakpoint(file, line))
            {
                shouldPause = true;
                reason = PauseReason.Breakpoint;
            }

            if (shouldPause)
            {
                host.ScriptPaused?.Invoke(threadId, reason);
                token.WaitForResume();
                if (token.IsDisconnected) break;
            }

            var handler = runner.GetHandler(state.OpCode)
                ?? throw new NotImplementedException($"Operation not implemented for OpCode: {state.OpCode}");
            handler.Handle(state);
        }
        return state.Execution;
    }

    private (int line, string? file) GetLineAndFile(FrameView frame)
    {
        if (host.MethodMap == null || !host.MethodMap.TryGetValue(frame.Method, out var entry))
            return (-1, null);

        var lineNumbers = entry.meta.LineNumbers;
        if ((uint)frame.Ip >= (uint)lineNumbers.Length)
            return (-1, null);

        return (lineNumbers[frame.Ip], entry.meta.FilePath);
    }
}
